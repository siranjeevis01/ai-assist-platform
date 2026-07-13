using AiAgentBackend.Configuration;
using AiAgentBackend.Data;
using AiAgentBackend.Models;
using AiAgentBackend.Services.Integrations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;

namespace AiAgentBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class TrelloController : ControllerBase
    {
        private readonly TrelloOptions _options;
        private readonly ApplicationDbContext _db;
        private readonly ITrelloService _trello;
        private readonly ILogger<TrelloController> _logger;
        private readonly HttpClient _http = new();

        public TrelloController(IOptions<TrelloOptions> options, ApplicationDbContext db,
            ITrelloService trello, ILogger<TrelloController> logger)
        {
            _options = options.Value;
            _db = db;
            _trello = trello;
            _logger = logger;
        }

        private int GetUserId()
        {
            var userIdStr = User.FindFirstValue("uid") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(userIdStr, out var userId) ? userId : 0;
        }

        private string GetConsumerKey() =>
            !string.IsNullOrEmpty(_options.ConsumerKey) ? _options.ConsumerKey : _options.ApiKey;

        private string GetConsumerSecret() =>
            !string.IsNullOrEmpty(_options.ConsumerSecret) ? _options.ConsumerSecret : _options.AccessToken;

        private string GenerateOAuthSignature(string method, string url, Dictionary<string, string> parameters, string consumerSecret, string? tokenSecret = null)
        {
            var sortedParams = parameters.OrderBy(p => p.Key, StringComparer.Ordinal)
                .ToDictionary(p => p.Key, p => p.Value);

            var paramParts = sortedParams.Select(p =>
                $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}");
            var baseString = $"{method.ToUpperInvariant()}&{Uri.EscapeDataString(url)}&{Uri.EscapeDataString(string.Join("&", paramParts))}";

            var key = $"{Uri.EscapeDataString(consumerSecret)}&{Uri.EscapeDataString(tokenSecret ?? "")}";
            using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(key));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(baseString));
            return Convert.ToBase64String(hash);
        }

        [HttpGet("connect-url")]
        public async Task<IActionResult> ConnectUrl()
        {
            var userId = GetUserId();
            if (userId == 0) return Unauthorized();

            var consumerKey = GetConsumerKey();
            var consumerSecret = GetConsumerSecret();
            if (string.IsNullOrEmpty(consumerKey) || string.IsNullOrEmpty(consumerSecret))
                return BadRequest(new { error = "Trello OAuth not configured" });

            var redirectUri = _options.RedirectUri;
            if (string.IsNullOrEmpty(redirectUri))
                return BadRequest(new { error = "Trello redirect URI not configured" });

            var oauthParams = new Dictionary<string, string>
            {
                { "oauth_callback", redirectUri },
                { "oauth_consumer_key", consumerKey },
                { "oauth_nonce", Guid.NewGuid().ToString("N") },
                { "oauth_signature_method", "HMAC-SHA1" },
                { "oauth_timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() },
                { "oauth_version", "1.0" }
            };

            var signature = GenerateOAuthSignature("POST", "https://trello.com/1/OAuthGetRequestToken",
                oauthParams, consumerSecret);
            oauthParams.Add("oauth_signature", signature);

            var authHeader = "OAuth " + string.Join(", ",
                oauthParams.Select(p => $"{Uri.EscapeDataString(p.Key)}=\"{Uri.EscapeDataString(p.Value)}\""));

            var request = new HttpRequestMessage(HttpMethod.Post, "https://trello.com/1/OAuthGetRequestToken");
            request.Headers.Add("Authorization", authHeader);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "oauth_callback", redirectUri }
            });

            var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to get Trello request token: {Error}", err);
                return StatusCode(502, new { error = "Failed to initiate Trello OAuth" });
            }

            var body = await response.Content.ReadAsStringAsync();
            var responseParams = HttpUtility.ParseQueryString(body);
            var requestToken = responseParams["oauth_token"];
            var requestTokenSecret = responseParams["oauth_token_secret"];

            if (string.IsNullOrEmpty(requestToken) || string.IsNullOrEmpty(requestTokenSecret))
                return StatusCode(502, new { error = "Invalid response from Trello" });

            // Store the request token secret temporarily in ProviderTokens for the callback
            var existingPending = await _db.ProviderTokens
                .FirstOrDefaultAsync(t => t.UserId == userId && t.Provider == "TrelloPending");
            if (existingPending != null)
                _db.ProviderTokens.Remove(existingPending);

            _db.ProviderTokens.Add(new ProviderToken
            {
                UserId = userId,
                Provider = "TrelloPending",
                EncryptedAccessToken = requestTokenSecret,
                Scope = requestToken,
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            var authorizeUrl = $"https://trello.com/1/OAuthAuthorizeToken?oauth_token={requestToken}";

            return Ok(new { url = authorizeUrl });
        }

        [HttpGet("callback")]
        [AllowAnonymous]
        public async Task<IActionResult> Callback([FromQuery] string oauth_token, [FromQuery] string oauth_verifier)
        {
            try
            {
                if (string.IsNullOrEmpty(oauth_token) || string.IsNullOrEmpty(oauth_verifier))
                    return BadRequest(new { error = "Missing OAuth parameters" });

                var consumerKey = GetConsumerKey();
                var consumerSecret = GetConsumerSecret();

                // Find the pending token entry
                var pendingToken = await _db.ProviderTokens
                    .FirstOrDefaultAsync(t => t.Provider == "TrelloPending" && t.Scope == oauth_token);

                if (pendingToken == null)
                    return BadRequest(new { error = "Invalid or expired OAuth request" });

                var requestTokenSecret = pendingToken.EncryptedAccessToken;
                var userId = pendingToken.UserId;

                // Exchange verifier for access token
                var tokenParams = new Dictionary<string, string>
                {
                    { "oauth_consumer_key", consumerKey },
                    { "oauth_nonce", Guid.NewGuid().ToString("N") },
                    { "oauth_signature_method", "HMAC-SHA1" },
                    { "oauth_token", oauth_token },
                    { "oauth_timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() },
                    { "oauth_verifier", oauth_verifier },
                    { "oauth_version", "1.0" }
                };

                var signature = GenerateOAuthSignature("POST", "https://trello.com/1/OAuthGetAccessToken",
                    tokenParams, consumerSecret, requestTokenSecret);
                tokenParams.Add("oauth_signature", signature);

                var authHeader = "OAuth " + string.Join(", ",
                    tokenParams.Select(p => $"{Uri.EscapeDataString(p.Key)}=\"{Uri.EscapeDataString(p.Value)}\""));

                var request = new HttpRequestMessage(HttpMethod.Post, "https://trello.com/1/OAuthGetAccessToken");
                request.Headers.Add("Authorization", authHeader);

                var response = await _http.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Failed to exchange Trello verifier for access token");
                    return BadRequest(new { error = "Failed to complete Trello OAuth" });
                }

                var body = await response.Content.ReadAsStringAsync();
                var accessParams = HttpUtility.ParseQueryString(body);
                var accessToken = accessParams["oauth_token"];
                var accessSecret = accessParams["oauth_token_secret"];
                var trelloUsername = accessParams["username"];

                if (string.IsNullOrEmpty(accessToken))
                    return BadRequest(new { error = "Invalid access token response" });

                // Remove pending token
                _db.ProviderTokens.Remove(pendingToken);

                // Store or update the user's Trello token
                var existingToken = await _db.ProviderTokens
                    .FirstOrDefaultAsync(t => t.UserId == userId && t.Provider == "Trello");

                if (existingToken != null)
                {
                    existingToken.EncryptedAccessToken = accessToken;
                    existingToken.RefreshToken = accessSecret;
                    existingToken.Scope = trelloUsername ?? "";
                    existingToken.ExpiresAt = null;
                }
                else
                {
                    _db.ProviderTokens.Add(new ProviderToken
                    {
                        UserId = userId,
                        Provider = "Trello",
                        EncryptedAccessToken = accessToken,
                        RefreshToken = accessSecret,
                        Scope = trelloUsername ?? "",
                        CreatedAt = DateTime.UtcNow
                    });
                }

                _db.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    Entity = "Integration",
                    Action = "TrelloConnect",
                    Timestamp = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();

                _logger.LogInformation("Trello connected for user {UserId}", userId);

                var frontendUrl = Environment.GetEnvironmentVariable("CORS_ORIGINS")?.Split(',')?.FirstOrDefault()?.Trim() ?? "http://localhost:4200";
                return Redirect($"{frontendUrl}/integrations?trelloConnected=true&message=Trello%20integration%20successful");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Trello OAuth callback");
                var frontendUrl = Environment.GetEnvironmentVariable("CORS_ORIGINS")?.Split(',')?.FirstOrDefault()?.Trim() ?? "http://localhost:4200";
                return Redirect($"{frontendUrl}/integrations?trelloConnected=false&error=OAuth%20failed");
            }
        }

        [HttpGet("status")]
        public async Task<IActionResult> GetStatus()
        {
            var userId = GetUserId();
            if (userId == 0) return Unauthorized();

            var userToken = await _db.ProviderTokens
                .FirstOrDefaultAsync(t => t.UserId == userId && t.Provider == "Trello");

            var hasShared = !string.IsNullOrEmpty(_options.ApiKey) && !string.IsNullOrEmpty(_options.AccessToken);
            var connected = userToken != null;

            return Ok(new
            {
                connected,
                configured = connected || hasShared,
                boardId = _options.DefaultBoardId,
                username = userToken?.Scope
            });
        }

        [HttpDelete("disconnect")]
        public async Task<IActionResult> Disconnect()
        {
            var userId = GetUserId();
            if (userId == 0) return Unauthorized();

            var tokens = await _db.ProviderTokens
                .Where(t => t.UserId == userId && t.Provider == "Trello")
                .ToListAsync();

            if (tokens.Any())
            {
                _db.ProviderTokens.RemoveRange(tokens);

                _db.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    Entity = "Integration",
                    Action = "TrelloDisconnect",
                    Timestamp = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();
            }

            _logger.LogInformation("Trello disconnected for user {UserId}", userId);
            return Ok(new { message = "Trello disconnected successfully" });
        }

        [HttpPost("sync")]
        public async Task<IActionResult> SyncTasks()
        {
            var userId = GetUserId();
            if (userId == 0) return Unauthorized();

            try
            {
                await _trello.SyncUserTasks(userId);
                return Ok(new { message = "Tasks synced with Trello" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing tasks with Trello for user {UserId}", userId);
                return StatusCode(500, new { error = "Sync failed" });
            }
        }
    }
}
