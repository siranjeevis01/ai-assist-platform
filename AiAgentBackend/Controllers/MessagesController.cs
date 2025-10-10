using AiAgentBackend.Data;
using AiAgentBackend.Models;
using AiAgentBackend.Services.Orchestration;
using Microsoft.AspNetCore.Authorization;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace AiAgentBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class MessagesController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly ICommandOrchestrator _orchestrator;
        private readonly ILogger<MessagesController> _logger;

        public MessagesController(ApplicationDbContext db, ICommandOrchestrator orchestrator,
                                ILogger<MessagesController> logger)
        {
            _db = db; 
            _orchestrator = orchestrator;
            _logger = logger;
        }

        private int GetUserId()
        {
            var userIdStr = User.FindFirstValue("uid") ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            return int.TryParse(userIdStr, out var userId) ? userId : 0;
        }

        [HttpPost("send")]
        public async Task<IActionResult> Send([FromBody] SendMessageRequest req)
        {
            try
            {
                var userId = GetUserId();
                if (userId == 0) return Unauthorized();

                if (string.IsNullOrWhiteSpace(req.Text))
                    return BadRequest(new { error = "Message text is required" });

                var response = await _orchestrator.HandleAsync(userId, req.Text, "Dashboard");

                // Store the message
                _db.Messages.Add(new Message
                {
                    UserId = userId,
                    Channel = "Dashboard",
                    Direction = "Incoming",
                    Body = req.Text,
                    Intent = "UserMessage",
                    CreatedAt = DateTime.UtcNow
                });

                // Store the response
                _db.Messages.Add(new Message
                {
                    UserId = userId,
                    Channel = "Dashboard",
                    Direction = "Outgoing",
                    Body = response,
                    Intent = "AIResponse",
                    CreatedAt = DateTime.UtcNow
                });

                // Also store in chat history
                _db.ChatMessages.Add(new ChatMessage
                {
                    UserId = userId,
                    Role = "user",
                    Text = req.Text,
                    CreatedAt = DateTime.UtcNow
                });

                _db.ChatMessages.Add(new ChatMessage
                {
                    UserId = userId,
                    Role = "assistant",
                    Text = response,
                    CreatedAt = DateTime.UtcNow
                });

                // Log the message
                _db.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    Entity = "Message",
                    Action = "Send",
                    Timestamp = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();

                _logger.LogInformation("Message processed for user {UserId}", userId);

                return Ok(new { result = response });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        [HttpGet("history")]
        public async Task<IActionResult> GetHistory([FromQuery] int limit = 50)
        {
            try
            {
                var userId = GetUserId();
                if (userId == 0) return Unauthorized();

                var messages = await _db.ChatMessages
                    .Where(cm => cm.UserId == userId)
                    .OrderByDescending(cm => cm.CreatedAt)
                    .Take(limit)
                    .OrderBy(cm => cm.CreatedAt)
                    .ToListAsync();

                return Ok(messages);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving message history");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        [HttpDelete("history")]
        public async Task<IActionResult> ClearHistory()
        {
            try
            {
                var userId = GetUserId();
                if (userId == 0) return Unauthorized();

                var messages = await _db.ChatMessages
                    .Where(cm => cm.UserId == userId)
                    .ToListAsync();

                _db.ChatMessages.RemoveRange(messages);
                
                // Log the history clearance
                _db.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    Entity = "Message",
                    Action = "ClearHistory",
                    Timestamp = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();

                _logger.LogInformation("Chat history cleared for user {UserId}", userId);

                return Ok(new { message = "Chat history cleared" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing chat history");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }
    }

    public class SendMessageRequest
    {
        [Required]
        public string Text { get; set; } = string.Empty;
    }
}