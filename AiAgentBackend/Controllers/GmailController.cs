using AiAgentBackend.Data;
using AiAgentBackend.DTOs.Gmail;
using AiAgentBackend.Services.Integrations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;

namespace AiAgentBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class GmailController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IGmailService _gmail;
        private readonly ILogger<GmailController> _logger;
        private readonly IMemoryCache _cache;

        private static readonly Dictionary<string, (string To, string Subject, string Body)> _pendingSends = new();

        public GmailController(ApplicationDbContext db, IGmailService gmail, ILogger<GmailController> logger, IMemoryCache cache)
        {
            _db = db;
            _gmail = gmail;
            _logger = logger;
            _cache = cache;
        }

        private int GetUserId()
        {
            var userIdStr = User.FindFirstValue("uid") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(userIdStr, out var userId) ? userId : 0;
        }

        [HttpGet("status")]
        public async Task<IActionResult> GetStatus()
        {
            var userId = GetUserId();
            if (userId == 0) return Unauthorized();

            var cacheKey = $"gmail_status:{userId}";
            if (_cache.TryGetValue(cacheKey, out object? cached))
                return Ok(cached);

            var hasGoogle = await _db.ProviderTokens
                .AnyAsync(t => t.UserId == userId && t.Provider == "Google");

            var result = new {
                connected = hasGoogle,
                provider = hasGoogle ? "Gmail" : "None"
            };

            _cache.Set(cacheKey, result, TimeSpan.FromSeconds(30));
            return Ok(result);
        }

        [HttpGet("emails")]
        public async Task<IActionResult> GetEmails([FromQuery] string query = "", [FromQuery] int maxResults = 20)
        {
            var userId = GetUserId();
            if (userId == 0) return Unauthorized();

            try
            {
                var emails = await _gmail.GetEmailsAsync(userId, query, maxResults);
                return Ok(emails);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching emails for user {UserId}", userId);
                return StatusCode(500, new { error = "Failed to fetch emails" });
            }
        }

        [HttpGet("labels")]
        public async Task<IActionResult> GetLabels()
        {
            var userId = GetUserId();
            if (userId == 0) return Unauthorized();

            try
            {
                var labels = await _gmail.GetLabelsAsync(userId);
                return Ok(labels);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching labels for user {UserId}", userId);
                return StatusCode(500, new { error = "Failed to fetch labels" });
            }
        }

        [HttpPost("send")]
        public async Task<IActionResult> SendEmail([FromBody] SendEmailRequest request)
        {
            var userId = GetUserId();
            if (userId == 0) return Unauthorized();

            try
            {
                var id = await _gmail.SendEmailAsync(userId, request.To, request.Subject, request.Body);
                var undoId = Guid.NewGuid().ToString("N")[..8];
                _pendingSends[undoId] = (request.To, request.Subject, request.Body);

                _ = Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ => _pendingSends.Remove(undoId));

                return Ok(new { message = "Email sent", id, undoId, undoWindowSeconds = 30 });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending email for user {UserId}", userId);
                return StatusCode(500, new { error = "Failed to send email" });
            }
        }

        [HttpPost("undo/{undoId}")]
        public IActionResult UndoSend(string undoId)
        {
            if (_pendingSends.TryGetValue(undoId, out var email))
            {
                _pendingSends.Remove(undoId);
                return Ok(new { message = "Undo noted. Note: Gmail may have already sent the email. Use Gmail's Undo Send feature for guaranteed recall.", email });
            }
            return BadRequest(new { error = "Undo window expired or invalid ID" });
        }
    }
}
