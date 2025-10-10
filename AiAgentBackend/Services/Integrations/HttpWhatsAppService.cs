using System.Text.Json;
using Microsoft.Extensions.Options;
using AiAgentBackend.Configuration;
using AiAgentBackend.Data;
using AiAgentBackend.Models;
using AiAgentBackend.Services.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiAgentBackend.Services.Integrations
{
    public interface IHttpWhatsAppService
    {
        Task<string> GetQrCodeAsync(int userId);
        Task<bool> InitializeConnectionAsync(int userId);
        Task<WhatsAppStatus> GetStatusAsync(int userId);
        Task<bool> CheckConnectionStatusAsync(int userId);
        Task<bool> SendMessageAsync(int userId, string message);
        Task<bool> SendQuickActionsAsync(int userId, string message, string[] actions);
        Task<bool> SendQuickReplyOptionsAsync(int userId, string message, Dictionary<string, string> options);
        Task<bool> SendEventConfirmationAsync(int userId, Event evt, string[] actions);
        Task<bool> SendTaskConfirmationAsync(int userId, TaskItem task, string[] actions);
        Task<bool> SendReminderAsync(int userId, string type, string title, DateTime remindAt, string description);
        Task<bool> SendEmailAlertAsync(int userId, string subject, string from, string priority, string emailId);
        Task ProcessIncomingMessage(string phone, string text);
        Task<bool> SendQuickActionsAsync(int userId, string message, IEnumerable<string> actions);
    }

    public class WhatsAppStatus
    {
        public bool IsConnected { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime? LastSeen { get; set; }
        public string PhoneNumber { get; set; } = string.Empty;
        public string QrCode { get; set; } = string.Empty;
        public bool RequiresReconnect { get; set; }
    }

    public class HttpWhatsAppService : IHttpWhatsAppService
    {
        private readonly HttpClient _httpClient;
        private readonly ApplicationDbContext _db;
        private readonly ILogger<HttpWhatsAppService> _logger;
        private readonly WhatsAppOptions _options;
        protected readonly IServiceProvider _serviceProvider;

        public HttpWhatsAppService(
            HttpClient httpClient,
            ApplicationDbContext db,
            ILogger<HttpWhatsAppService> logger,
            IOptions<WhatsAppOptions> options,
            IServiceProvider serviceProvider)
        {
            _httpClient = httpClient;
            _db = db;
            _logger = logger;
            _options = options.Value;
            _serviceProvider = serviceProvider;

            _httpClient.BaseAddress = new Uri(_options.BotApiUrl);
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task<string> GetQrCodeAsync(int userId)
        {
            try
            {
                var response = await _httpClient.GetAsync("/qr");
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var qrData = JsonSerializer.Deserialize<QrResponse>(content);
                    return qrData?.QrCode ?? string.Empty;
                }
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting QR code for user {UserId}", userId);
                return string.Empty;
            }
        }

        public async Task<bool> InitializeConnectionAsync(int userId)
        {
            try
            {
                var response = await _httpClient.PostAsync("/connect", null);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing connection for user {UserId}", userId);
                return false;
            }
        }

        public async Task<WhatsAppStatus> GetStatusAsync(int userId)
        {
            try
            {
                var response = await _httpClient.GetAsync("/status");
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var status = JsonSerializer.Deserialize<WhatsAppStatus>(content);
                    return status ?? new WhatsAppStatus { Status = "Unknown" };
                }
                return new WhatsAppStatus { Status = "Unavailable" };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting status for user {UserId}", userId);
                return new WhatsAppStatus { Status = "Error" };
            }
        }

public async Task<bool> CheckConnectionStatusAsync(int userId)
{
    try
    {
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(10);
        
        var response = await httpClient.GetAsync($"{_options.BotApiUrl}/health");
        
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            
            return content.Contains("\"status\":\"connected\"") || 
                   content.Contains("\"status\":\"OK\"") ||
                   content.ToLower().Contains("connected");
        }
        
        return false;
    }
    catch (Exception ex)
    {
        _logger.LogWarning("WhatsApp bot health check failed: {Message}", ex.Message);
        return false;
    }
}

        public async Task<bool> SendMessageAsync(int userId, string body)
        {
            try
            {
                var user = await _db.Users.FindAsync(userId);
                if (user == null || string.IsNullOrEmpty(user.PhoneNumber))
                {
                    _logger.LogWarning("User {UserId} has no phone number", userId);
                    return false;
                }

                var formattedPhone = FormatPhoneNumber(user.PhoneNumber);

                var response = await _httpClient.PostAsJsonAsync("/send", new
                {
                    to = formattedPhone,
                    text = body
                });

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Message sent to user {UserId} at {Phone}", userId, formattedPhone);

                    _db.Messages.Add(new Message
                    {
                        UserId = userId,
                        Channel = "WhatsApp",
                        Direction = "Outgoing",
                        Body = body,
                        CreatedAt = DateTime.UtcNow
                    });
                    await _db.SaveChangesAsync();

                    return true;
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to send WhatsApp message: {StatusCode} - {Error}",
                        response.StatusCode, error);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send WhatsApp message to user {UserId}", userId);
                return false;
            }
        }

        public async Task<bool> SendQuickActionsAsync(int userId, string message, string[] actions)
        {
            try
            {
                var formattedMessage = $"{message}\n\n";
                for (int i = 0; i < actions.Length; i++)
                {
                    formattedMessage += $"{i + 1}. {actions[i]}\n";
                }
                formattedMessage += "\nReply with the number of your choice";

                // Store context for handling responses
                await _db.ChatMessages.AddAsync(new ChatMessage
                {
                    UserId = userId,
                    Role = "system",
                    Text = $"QUICK_REPLY_CONTEXT:ACTIONS:{string.Join(",", actions)}",
                    CreatedAt = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();

                return await SendMessageAsync(userId, formattedMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending quick actions");
                return false;
            }
        }

        public async Task<bool> SendQuickActionsAsync(int userId, string message, IEnumerable<string> actions)
        {
            return await SendQuickActionsAsync(userId, message, actions.ToArray());
        }

        public async Task<bool> SendQuickReplyOptionsAsync(int userId, string message, Dictionary<string, string> options)
        {
            try
            {
                var formattedMessage = $"{message}\n\n";
                var optionList = options.Select((kvp, index) => $"{index + 1}. {kvp.Key} - {kvp.Value}").ToArray();

                formattedMessage += string.Join("\n", optionList);
                formattedMessage += "\n\nReply with the number of your choice";

                // Store context for handling responses
                await _db.ChatMessages.AddAsync(new ChatMessage
                {
                    UserId = userId,
                    Role = "system",
                    Text = $"QUICK_REPLY_CONTEXT:OPTIONS:{JsonSerializer.Serialize(options)}",
                    CreatedAt = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();

                return await SendMessageAsync(userId, formattedMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending quick reply options");
                return false;
            }
        }

        public async Task ProcessIncomingMessage(string phone, string text)
        {
            try
            {
                var user = await _db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == phone);
                if (user == null)
                {
                    _logger.LogWarning("No user found for phone number: {Phone}", phone);
                    return;
                }

                // Store the incoming message
                _db.Messages.Add(new Message
                {
                    UserId = user.Id,
                    Channel = "WhatsApp",
                    Direction = "Incoming",
                    Body = text,
                    CreatedAt = DateTime.UtcNow
                });

                // Check if this is a response to a quick action
                var contextMessage = await _db.ChatMessages
                    .Where(cm => cm.UserId == user.Id && cm.Role == "system" && cm.Text.StartsWith("QUICK_REPLY_CONTEXT:"))
                    .OrderByDescending(cm => cm.CreatedAt)
                    .FirstOrDefaultAsync();

                if (contextMessage != null && int.TryParse(text.Trim(), out var optionNumber))
                {
                    await HandleQuickReplyAsync(user.Id, contextMessage.Text, optionNumber);
                }
                else
                {
                    // Process as regular command
                    var orchestrator = _serviceProvider.GetRequiredService<ICommandOrchestrator>();
                    await orchestrator.HandleAsync(user.Id, text, "WhatsApp");
                }

                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing incoming message from {Phone}", phone);
            }
        }

        private async Task HandleQuickReplyAsync(int userId, string contextText, int optionNumber)
        {
            try
            {
                var parts = contextText.Split(':');
                if (parts.Length >= 3)
                {
                    var contextType = parts[1];
                    var contextData = parts[2];

                    var orchestrator = _serviceProvider.GetRequiredService<ICommandOrchestrator>();

                    switch (contextType)
                    {
                        case "ACTIONS":
                            var actions = contextData.Split(',');
                            if (optionNumber > 0 && optionNumber <= actions.Length)
                            {
                                var selectedAction = actions[optionNumber - 1];
                                await SendMessageAsync(userId, $"You selected: {selectedAction}");
                                
                                // Handle specific actions
                                if (selectedAction.Contains("Confirm"))
                                {
                                    await orchestrator.HandleEventConfirmationResponse(userId, contextData, "confirm");
                                }
                                else if (selectedAction.Contains("Complete"))
                                {
                                    await orchestrator.HandleTaskConfirmationResponse(userId, contextData, "complete");
                                }
                            }
                            break;
                        case "OPTIONS":
                            var options = JsonSerializer.Deserialize<Dictionary<string, string>>(contextData);
                            if (options != null)
                            {
                                var optionKeys = options.Keys.ToArray();
                                if (optionNumber > 0 && optionNumber <= optionKeys.Length)
                                {
                                    var selectedKey = optionKeys[optionNumber - 1];
                                    await SendMessageAsync(userId, $"You selected: {selectedKey}");
                                    await orchestrator.HandleEmailActionResponse(userId, contextData, selectedKey);
                                }
                            }
                            break;
                    }
                }

                // Clear the context after handling
                var contextMessages = _db.ChatMessages
                    .Where(cm => cm.UserId == userId && cm.Role == "system" && cm.Text.StartsWith("QUICK_REPLY_CONTEXT:"));
                _db.ChatMessages.RemoveRange(contextMessages);
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling quick reply for user {UserId}", userId);
            }
        }

        public async Task<bool> SendEventConfirmationAsync(int userId, Event evt, string[] actions)
        {
            try
            {
                var user = await _db.Users.FindAsync(userId);
                if (user == null) return false;

                var message = $"📅 Event Created:\n" +
                              $"Title: {evt.Title}\n" +
                              $"Date: {evt.StartUtc.ToLocalTime():MMM dd, yyyy}\n" +
                              $"Time: {evt.StartUtc.ToLocalTime():HH:mm} - {evt.EndUtc.ToLocalTime():HH:mm}\n" +
                              $"Location: {evt.Location ?? "Not specified"}\n\n" +
                              $"Please confirm:";

                return await SendQuickActionsAsync(userId, message, actions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending event confirmation");
                return false;
            }
        }

        public async Task<bool> SendTaskConfirmationAsync(int userId, TaskItem task, string[] actions)
        {
            try
            {
                var user = await _db.Users.FindAsync(userId);
                if (user == null) return false;

                var dueInfo = task.DueUtc.HasValue
                    ? $"Due: {task.DueUtc.Value.ToLocalTime():MMM dd, yyyy HH:mm}"
                    : "No due date";

                var message = $"✅ Task Created:\n" +
                              $"Title: {task.Title}\n" +
                              $"{dueInfo}\n" +
                              $"Status: {task.Status}\n\n" +
                              $"What would you like to do?";

                return await SendQuickActionsAsync(userId, message, actions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending task confirmation");
                return false;
            }
        }

        public async Task<bool> SendReminderAsync(int userId, string type, string title, DateTime remindAt, string description = "")
        {
            try
            {
                var emoji = type.ToLower() switch
                {
                    "event" => "📅",
                    "task" => "✅",
                    "email" => "📧",
                    _ => "🔔"
                };

                var message = $"{emoji} Reminder:\n" +
                              $"{title}\n" +
                              $"Time: {remindAt:MMM dd, yyyy HH:mm}\n";

                if (!string.IsNullOrEmpty(description))
                    message += $"\nDetails: {description}";

                return await SendMessageAsync(userId, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending reminder");
                return false;
            }
        }

        public async Task<bool> SendEmailAlertAsync(int userId, string subject, string from, string priority, string emailId)
        {
            try
            {
                var emoji = priority.ToLower() switch
                {
                    "high" => "🚨",
                    "urgent" => "⚠️",
                    _ => "📧"
                };

                var message = $"{emoji} Email Alert:\n" +
                              $"From: {from}\n" +
                              $"Subject: {subject}\n" +
                              $"Priority: {priority}\n\n" +
                              $"Reply with 'read', 'reply', or 'ignore'";

                await _db.ChatMessages.AddAsync(new ChatMessage
                {
                    UserId = userId,
                    Role = "system",
                    Text = $"EMAIL_CONTEXT:{emailId}:{subject}:{from}",
                    CreatedAt = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();

                return await SendMessageAsync(userId, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending email alert");
                return false;
            }
        }

        private string FormatPhoneNumber(string phoneNumber)
        {
            var cleaned = new string(phoneNumber.Where(char.IsDigit).ToArray());

            if (cleaned.StartsWith("0"))
            {
                cleaned = "62" + cleaned.Substring(1); // Example country code
            }
            else if (!cleaned.StartsWith("+"))
            {
                cleaned = "+" + cleaned;
            }

            return cleaned;
        }

        public async Task ProcessBulkMessagesAsync(List<WhatsAppBulkMessage> messages)
        {
            foreach (var message in messages)
            {
                try
                {
                    await SendMessageAsync(message.UserId, message.Content);
                    await Task.Delay(500);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error sending bulk message to user {UserId}", message.UserId);
                }
            }
        }
    }

    // Helper classes
    public class QrResponse
    {
        public string QrCode { get; set; } = string.Empty;
    }

    public class WhatsAppBulkMessage
    {
        public int UserId { get; set; }
        public string Content { get; set; } = string.Empty;
        public DateTime ScheduledTime { get; set; }
        public bool IsUrgent { get; set; }
    }
}