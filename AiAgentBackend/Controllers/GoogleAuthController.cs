using AiAgentBackend.Configuration;
using AiAgentBackend.Data;
using AiAgentBackend.Models;
using AiAgentBackend.DTOs.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Caching.Memory;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Security.Claims;

namespace AiAgentBackend.Controllers
{
    [ApiController]
    [Route("api/google")]
    public class GoogleAuthController : ControllerBase
    {
        private readonly GoogleOptions _options;
        private readonly ApplicationDbContext _db;
        private readonly ILogger<GoogleAuthController> _logger;
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _cache;

        public GoogleAuthController(
            IOptions<GoogleOptions> options, 
            ApplicationDbContext db,
            ILogger<GoogleAuthController> logger,
            IConfiguration configuration,
            IMemoryCache cache)
        {
            _options = options.Value;
            _db = db;
            _logger = logger;
            _configuration = configuration;
            _cache = cache;
        }

        private string BuildGoogleAuthUrl(int userId)
        {
            var scopes = new[]
            {
                "https://www.googleapis.com/auth/calendar",
                "https://www.googleapis.com/auth/calendar.events",
                "https://www.googleapis.com/auth/gmail.readonly",
                "https://www.googleapis.com/auth/gmail.modify",
                "https://www.googleapis.com/auth/gmail.compose"
            };

            var redirectUri = _options.RedirectUri;
            if (string.IsNullOrEmpty(redirectUri))
            {
                redirectUri = _configuration["Google:RedirectUri"] ?? "http://localhost:5000/api/google/callback";
            }

            var nonce = Guid.NewGuid().ToString("N");
            var state = $"{userId}:{nonce}";
            _cache.Set($"google_oauth:{nonce}", userId, TimeSpan.FromMinutes(10));

            var url = $"https://accounts.google.com/o/oauth2/v2/auth" +
                      $"?client_id={Uri.EscapeDataString(_options.ClientId)}" +
                      $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                      $"&response_type=code" +
                      $"&scope={Uri.EscapeDataString(string.Join(" ", scopes))}" +
                      $"&access_type=offline" +
                      $"&prompt=consent" +
                      $"&state={Uri.EscapeDataString(state)}";

            return url;
        }

        [HttpGet("connect")]
        [Authorize]
        public IActionResult Connect()
        {
            try
            {
                var userIdClaim = User.FindFirst("uid")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userIdClaim))
                    return Unauthorized(new { error = "User not authenticated" });

                var userId = int.Parse(userIdClaim);
                var url = BuildGoogleAuthUrl(userId);

                _logger.LogInformation("Google OAuth initiated (redirect) for user {UserId}", userId);
                return Redirect(url);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initiating Google OAuth (connect redirect)");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        [HttpGet("connect-url")]
        [Authorize]
        public IActionResult ConnectUrl()
        {
            try
            {
                var userIdClaim = User.FindFirst("uid")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userIdClaim))
                    return Unauthorized(new { error = "User not authenticated" });

                var userId = int.Parse(userIdClaim);
                var url = BuildGoogleAuthUrl(userId);

                _logger.LogInformation("Google OAuth URL requested for user {UserId}", userId);
                return Ok(new { url });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating Google OAuth URL");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        [HttpGet("callback")]
        public async Task<IActionResult> Callback([FromQuery] string code, [FromQuery] string state)
        {
            try
            {
                if (string.IsNullOrEmpty(code)) 
                    return BadRequest(new { error = "Missing authorization code" });
                if (string.IsNullOrEmpty(state)) 
                    return BadRequest(new { error = "Missing state parameter" });

                var decodedState = Uri.UnescapeDataString(state);
                var stateParts = decodedState.Split(':');
                if (stateParts.Length != 2 || !int.TryParse(stateParts[0], out int userId))
                    return BadRequest(new { error = "Invalid state parameter" });

                var nonce = stateParts[1];
                var cacheKey = $"google_oauth:{nonce}";
                if (!_cache.TryGetValue(cacheKey, out int cachedUserId) || cachedUserId != userId)
                    return BadRequest(new { error = "Invalid or expired OAuth state" });
                _cache.Remove(cacheKey);

                var redirectUri = _options.RedirectUri;
                if (string.IsNullOrEmpty(redirectUri))
                {
                    redirectUri = _configuration["Google:RedirectUri"] ?? "http://localhost:5000/api/google/callback";
                }

                using var http = new HttpClient();
                var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    {"code", code},
                    {"client_id", _options.ClientId},
                    {"client_secret", _options.ClientSecret},
                    {"redirect_uri", redirectUri},
                    {"grant_type", "authorization_code"}
                });

                var response = await http.PostAsync("https://oauth2.googleapis.com/token", content);
                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Google token exchange failed: {Error}", error);
                    return BadRequest(new { error = "Failed to exchange authorization code" });
                }

                var json = await response.Content.ReadAsStringAsync();
                GoogleTokenResponse? tokenResponse = null;

                try 
                {
                    tokenResponse = JsonSerializer.Deserialize<GoogleTokenResponse>(json);
                    if (tokenResponse?.AccessToken == null)
                    {
                        _logger.LogError("Invalid token response from Google");
                        return BadRequest(new { error = "Invalid token response from Google" });
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Failed to parse Google token response");
                    return BadRequest(new { error = "Invalid response format from Google" });
                }

                var providerToken = await _db.ProviderTokens
                    .FirstOrDefaultAsync(t => t.UserId == userId && t.Provider == "Google");

                if (providerToken == null)
                {
                    providerToken = new ProviderToken 
                    { 
                        UserId = userId, 
                        Provider = "Google",
                        CreatedAt = DateTime.UtcNow
                    };
                    _db.ProviderTokens.Add(providerToken);
                }

                providerToken.EncryptedAccessToken = tokenResponse.AccessToken;
                providerToken.RefreshToken = tokenResponse.RefreshToken ?? providerToken.RefreshToken;
                providerToken.Scope = tokenResponse.Scope;
                providerToken.ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);

                // Log audit
                _db.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    Entity = "Integration",
                    Action = "GoogleConnect",
                    Timestamp = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();

                _logger.LogInformation("Google connected successfully for user {UserId}", userId);

                var frontendUrl = Environment.GetEnvironmentVariable("CORS_ORIGINS")?.Split(',')?.FirstOrDefault()?.Trim() ?? "http://localhost:4200";
                return Redirect($"{frontendUrl}/integrations?googleConnected=true&message=Google%20integration%20successful");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Google OAuth callback");
                var frontendUrl = Environment.GetEnvironmentVariable("CORS_ORIGINS")?.Split(',')?.FirstOrDefault()?.Trim() ?? "http://localhost:4200";
                return Redirect($"{frontendUrl}/integrations?googleConnected=false&error=OAuth%20failed");
            }
        }

        [HttpDelete("disconnect")]
        [Authorize]
        public async Task<IActionResult> Disconnect()
        {
            try
            {
                var userIdClaim = User.FindFirst("uid")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userIdClaim)) 
                    return Unauthorized();
                var userId = int.Parse(userIdClaim);

                var tokens = await _db.ProviderTokens
                    .Where(t => t.UserId == userId && t.Provider == "Google")
                    .ToListAsync();

                if (tokens.Any())
                {
                    _db.ProviderTokens.RemoveRange(tokens);
                    _db.AuditLogs.Add(new AuditLog
                    {
                        UserId = userId,
                        Entity = "Integration",
                        Action = "GoogleDisconnect",
                        Timestamp = DateTime.UtcNow
                    });

                    await _db.SaveChangesAsync();
                }

                _logger.LogInformation("Google disconnected for user {UserId}", userId);
                return Ok(new { message = "Google disconnected successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disconnecting Google");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        [HttpGet("status")]
        [Authorize]
        public async Task<IActionResult> Status()
        {
            try
            {
                var userIdClaim = User.FindFirst("uid")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userIdClaim)) 
                    return Unauthorized();
                var userId = int.Parse(userIdClaim);

                var cacheKey = $"google_status:{userId}";
                if (_cache.TryGetValue(cacheKey, out object? cached))
                    return Ok(cached);

                var token = await _db.ProviderTokens
                    .FirstOrDefaultAsync(t => t.UserId == userId && t.Provider == "Google");

                if (token == null) 
                    return Ok(new { connected = false });

                var isExpired = token.ExpiresAt.HasValue && token.ExpiresAt <= DateTime.UtcNow;

                var result = new
                {
                    connected = !isExpired,
                    expiresAt = token.ExpiresAt,
                    scopes = token.Scope?.Split(' ')
                };

                _cache.Set(cacheKey, result, TimeSpan.FromSeconds(30));
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking Google status");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }
    }
}