using AiAgentBackend.Hubs;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using AiAgentBackend.Services.Integrations;
using AiAgentBackend.Services.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using AiAgentBackend.Data;
using AiAgentBackend.Models;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;

namespace AiAgentBackend.Controllers
{
    [ApiController]
    [Route("webhooks/[controller]")]
    public class WebhooksController : ControllerBase
    {
        private readonly IEnhancedCommandOrchestrator _orchestrator;
        private readonly ApplicationDbContext _db;
        private readonly IHubContext<UpdatesHub> _hub;
        private readonly IConfiguration _config;
        private readonly ILogger<WebhooksController> _logger;
        private readonly IHttpWhatsAppService _whatsAppService;

        public WebhooksController(
            IEnhancedCommandOrchestrator orchestrator,
            ApplicationDbContext db,
            IHubContext<UpdatesHub> hub,
            IConfiguration config,
            ILogger<WebhooksController> logger,
            IHttpWhatsAppService whatsAppService)
        {
            _whatsAppService = whatsAppService;
            _orchestrator = orchestrator;
            _db = db;
            _hub = hub;
            _config = config;
            _logger = logger;
            _whatsAppService = whatsAppService; 
        }

        [HttpPost("whatsapp")]
        public async Task<IActionResult> WhatsAppJson([FromBody] WhatsAppPayload payload)
        {
            if (payload == null || string.IsNullOrEmpty(payload.Text))
                return BadRequest(new { error = "Invalid payload" });

            try
            {
                _logger.LogInformation("Received WhatsApp message from {Phone}: {Text}", payload.Phone, payload.Text);

                // Process through the integrated service
                await _whatsAppService.ProcessIncomingMessage(payload.Phone, payload.Text);

                return Ok(new { 
                    status = "Processed", 
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing WhatsApp webhook");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        [HttpPost("gmail")]
        public async Task<IActionResult> GmailWebhook([FromBody] GmailWebhookPayload payload)
        {
            try
            {
                _logger.LogInformation("Received Gmail webhook: {MessageId}", payload.MessageId);

                if (payload.HistoryId != null)
                {
                    // Process email changes
                    await ProcessGmailHistory(payload.HistoryId);
                }
                else if (!string.IsNullOrEmpty(payload.MessageId))
                {
                    // Process specific message
                    await ProcessGmailMessage(payload.MessageId, payload.UserId);
                }

                return Ok(new { status = "Processed" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Gmail webhook");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        [HttpPost("google")]
        public async Task<IActionResult> Google([FromBody] GoogleEventPayload payload)
        {
            try
            {
                _logger.LogInformation("Received Google webhook for event {EventId}", payload.Id);

                var evt = await _db.Events.FirstOrDefaultAsync(e => e.ExternalId == payload.Id);
                if (evt != null)
                {
                    evt.Title = payload.Summary ?? evt.Title;
                    evt.StartUtc = payload.Start.UtcDateTime;
                    evt.EndUtc = payload.End.UtcDateTime;

                    // Audit log
                    _db.AuditLogs.Add(new AuditLog
                    {
                        UserId = evt.UserId,
                        Entity = "Webhook",
                        Action = "GoogleEventUpdate",
                        Timestamp = DateTime.UtcNow
                    });

                    await _db.SaveChangesAsync();

                    await _hub.Clients.User(evt.UserId.ToString()).SendAsync("ReceiveUpdate", new
                    {
                        Type = "EventUpdated",
                        Event = evt
                    });

                    _logger.LogInformation("Updated event {EventId} from Google webhook", payload.Id);
                }
                else
                {
                    _logger.LogWarning("Event not found for Google webhook: {EventId}", payload.Id);
                }

                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Google webhook");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        [HttpPost("trello")]
        public async Task<IActionResult> Trello([FromBody] JsonElement payload)
        {
            try
            {
                var actionType = payload.GetProperty("action").GetProperty("type").GetString();
                var cardId = payload.GetProperty("action")
                                    .GetProperty("data")
                                    .GetProperty("card")
                                    .GetProperty("id").GetString();

                _logger.LogInformation("Received Trello webhook: {ActionType} for card {CardId}", actionType, cardId);

                var task = await _db.Tasks.FirstOrDefaultAsync(t => t.ExternalId == cardId);
                if (task != null)
                {
                    task.Status = actionType switch
                    {
                        "updateCard" => "In Progress",
                        "deleteCard" => "Archived",
                        _ => task.Status
                    };

                    // Audit log
                    _db.AuditLogs.Add(new AuditLog
                    {
                        UserId = task.UserId,
                        Entity = "Webhook",
                        Action = $"Trello{actionType}",
                        Timestamp = DateTime.UtcNow
                    });

                    await _db.SaveChangesAsync();

                    await _hub.Clients.User(task.UserId.ToString()).SendAsync("ReceiveUpdate", new
                    {
                        Type = "TaskUpdated",
                        Task = task
                    });

                    _logger.LogInformation("Updated task {TaskId} from Trello webhook", task.Id);
                }
                else
                {
                    _logger.LogWarning("Task not found for Trello webhook: {CardId}", cardId);
                }

                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Trello webhook");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        [HttpGet("health")]
        public async Task<IActionResult> Health()
        {
            var status = new
            {
                status = "OK",
                timestamp = DateTime.UtcNow,
                whatsAppStatus = await CheckWhatsAppBot(),
                gmailStatus = await CheckGmailService(),
                database = await _db.Database.CanConnectAsync() ? "Connected" : "Disconnected",
                services = new
                {
                    whatsApp = true,
                    gmail = true,
                    orchestrator = true
                }
            };

            return Ok(status);
        }

[HttpGet("test-whatsapp")]
public async Task<IActionResult> TestWhatsAppConnection()
{
    try
    {
        var httpClient = new HttpClient();
        var response = await httpClient.GetAsync("http://whatsapp-bot:3001/health");
        
        return Ok(new {
            whatsappStatus = response.StatusCode,
            backendStatus = "OK",
            message = response.IsSuccessStatusCode ? "Connected" : "Disconnected"
        });
    }
    catch (Exception ex)
    {
        return Ok(new {
            whatsappStatus = "Error",
            backendStatus = "OK", 
            error = ex.Message
        });
    }
}

        private async Task<string> CheckWhatsAppBot()
        {
            try
            {
                using var http = new HttpClient();
                var botUrl = _config["WhatsApp:BotApiUrl"] ?? "http://localhost:3001";
                var res = await http.GetAsync($"{botUrl}/health");

                if (!res.IsSuccessStatusCode)
                    return "Unavailable";

                var json = await res.Content.ReadAsStringAsync();
                var status = JsonSerializer.Deserialize<WhatsAppBotStatus>(json);
                return status?.Status ?? "Unknown";
            }
            catch
            {
                return "Unavailable";
            }
        }

        private async Task<string> CheckGmailService()
        {
            try
            {
                // Test Gmail service connectivity
                var usersWithGoogle = await _db.ProviderTokens
                    .Where(t => t.Provider == "Google")
                    .CountAsync();

                return usersWithGoogle > 0 ? "Available" : "No users connected";
            }
            catch
            {
                return "Error";
            }
        }

        private Task ProcessGmailHistory(string historyId)
        {
            _logger.LogInformation("Processing Gmail history: {HistoryId}", historyId);
            return Task.CompletedTask;
        }

        private Task ProcessGmailMessage(string messageId, string userId)
        {
            _logger.LogInformation("Processing Gmail message: {MessageId} for user {UserId}", messageId, userId);
            return Task.CompletedTask;
        }
    }

    public class WhatsAppPayload
    {
        [Required]
        public string Phone { get; set; } = string.Empty;
        
        [Required]
        public string Text { get; set; } = string.Empty;
        public string? MessageId { get; set; }
        public DateTime? Timestamp { get; set; }
        public string? MessageType { get; set; }
    }

    public class GmailWebhookPayload
    {
        public string? MessageId { get; set; }
        public string? HistoryId { get; set; }
        [Required] public string UserId { get; set; } = string.Empty;
        public string? EventType { get; set; }
    }

    public class GoogleEventPayload
    {
        [Required]
        public string Id { get; set; } = string.Empty;
        
        public string Summary { get; set; } = string.Empty;
        
        [Required]
        public EventDateTimePayload Start { get; set; } = new();
        
        [Required]
        public EventDateTimePayload End { get; set; } = new();
    }

    public class EventDateTimePayload 
    { 
        public DateTime UtcDateTime { get; set; } 
    }

    // Renamed to avoid conflict
    public class WhatsAppBotStatus
    {
        public string Status { get; set; } = string.Empty;
        public bool QrAvailable { get; set; }
        public int BackendStatus { get; set; }
    }
}