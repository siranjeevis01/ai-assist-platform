using AiAgentBackend.Data;
using AiAgentBackend.DTOs.Gmail;
using AiAgentBackend.Services.Integrations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

        public GmailController(ApplicationDbContext db, IGmailService gmail, ILogger<GmailController> logger)
        {
            _db = db;
            _gmail = gmail;
            _logger = logger;
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

            var hasGoogle = await _db.ProviderTokens
                .AnyAsync(t => t.UserId == userId && t.Provider == "Google");

            return Ok(new {
                connected = hasGoogle,
                provider = hasGoogle ? "Gmail" : "None"
            });
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
                return Ok(new { message = "Email sent", id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending email for user {UserId}", userId);
                return StatusCode(500, new { error = "Failed to send email" });
            }
        }
    }
}
