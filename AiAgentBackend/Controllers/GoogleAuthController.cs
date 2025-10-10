using AiAgentBackend.Configuration;
using AiAgentBackend.Data;
using AiAgentBackend.Models;
using AiAgentBackend.DTOs.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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

        public GoogleAuthController(IOptions<GoogleOptions> options, ApplicationDbContext db,
                                  ILogger<GoogleAuthController> logger)
        {
            _options = options.Value;
            _db = db;
            _logger = logger;
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

            var url = $"https://accounts.google.com/o/oauth2/v2/auth" +
                      $"?client_id={_options.ClientId}" +
                      $"&redirect_uri={Uri.EscapeDataString(_options.RedirectUri)}" +
                      $"&response_type=code" +
                      $"&scope={Uri.EscapeDataString(string.Join(" ", scopes))}" +
                      $"&access_type=offline" +
                      $"&prompt=consent" +
                      $"&state={userId}";

            return url;
        }

        // --- Google Connect ---
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

        // NEW: Return the Google OAuth URL as JSON (for fetch + Authorization header usage)
        // Frontend will call this with the Authorization header set.
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

        // --- Google Callback ---
        [HttpGet("callback")]
        public async Task<IActionResult> Callback([FromQuery] string code, [FromQuery] string state)
        {
            try
            {
                if (string.IsNullOrEmpty(code)) return BadRequest(new { error = "Missing authorization code" });
                if (string.IsNullOrEmpty(state)) return BadRequest(new { error = "Missing state parameter" });

                var userId = int.Parse(state);

                using var http = new HttpClient();
                var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    {"code", code},
                    {"client_id", _options.ClientId},
                    {"client_secret", _options.ClientSecret},
                    {"redirect_uri", _options.RedirectUri},
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
                var tokenResponse = JsonSerializer.Deserialize<GoogleTokenResponse>(json);

                if (tokenResponse == null)
                    return BadRequest(new { error = "Invalid token response" });

                var providerToken = await _db.ProviderTokens
                    .FirstOrDefaultAsync(t => t.UserId == userId && t.Provider == "Google");

                if (providerToken == null)
                {
                    providerToken = new ProviderToken { UserId = userId, Provider = "Google" };
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

                // Redirect back to dashboard
                // return Redirect($"/dashboard?googleConnected=true");
                return Redirect($"/dashboard.html?googleConnected=true");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Google OAuth callback");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        // --- Disconnect Google ---
        [HttpDelete("disconnect")]
        [Authorize]
        public async Task<IActionResult> Disconnect()
        {
            try
            {
                var userIdClaim = User.FindFirst("uid")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userIdClaim)) return Unauthorized();
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

        // --- Check Google Status ---
        [HttpGet("status")]
        [Authorize]
        public async Task<IActionResult> Status()
        {
            try
            {
                var userIdClaim = User.FindFirst("uid")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userIdClaim)) return Unauthorized();
                var userId = int.Parse(userIdClaim);

                var token = await _db.ProviderTokens
                    .FirstOrDefaultAsync(t => t.UserId == userId && t.Provider == "Google");

                if (token == null) return Ok(new { connected = false });

                var isExpired = token.ExpiresAt.HasValue && token.ExpiresAt <= DateTime.UtcNow;

                return Ok(new
                {
                    connected = !isExpired,
                    expiresAt = token.ExpiresAt,
                    scopes = token.Scope?.Split(' ')
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking Google status");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }
    }
}
