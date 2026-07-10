using System.Text;
using System.Text.Json;
using AiAgentBackend.Data;
using AiAgentBackend.Models;
using AiAgentBackend.Services.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using AiAgentBackend.Configuration;

namespace AiAgentBackend.Services.Messaging
{
    public class WhatsAppCloudService : IWhatsAppCloudService
    {
        private readonly HttpClient _httpClient;
        private readonly IOptions<MessagingOptions> _options;
        private readonly ApplicationDbContext _db;
        private readonly ICommandOrchestrator _orchestrator;
        private readonly ILogger<WhatsAppCloudService> _logger;
        
        private bool _isInitialized = false;

        public WhatsAppCloudService(
            HttpClient httpClient,
            IOptions<MessagingOptions> options,
            ApplicationDbContext db,
            ICommandOrchestrator orchestrator,
            ILogger<WhatsAppCloudService> logger)
        {
            _httpClient = httpClient;
            _options = options;
            _db = db;
            _orchestrator = orchestrator;
            _logger = logger;
            
            // Set up HttpClient for WhatsApp Cloud API
            _httpClient.BaseAddress = new Uri("https://graph.facebook.com/v17.0/");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public Task<MessagingPlatformStatus> GetStatusAsync()
        {
            var status = new MessagingPlatformStatus
            {
                IsConnected = _isInitialized,
                Status = _isInitialized ? "connected" : "disconnected",
                LastChecked = DateTime.UtcNow
            };

            if (!_isInitialized)
            {
                status.ErrorMessage = "WhatsApp Cloud API not configured";
            }

            return Task.FromResult(status);
        }

        public async Task<bool> InitializeAsync()
        {
            try
            {
                var accessToken = _options.Value.WhatsApp.AccessToken;
                if (string.IsNullOrEmpty(accessToken) ||
                    string.IsNullOrEmpty(_options.Value.WhatsApp.PhoneNumberId) ||
                    accessToken.Contains("YOUR_") ||
                    accessToken.StartsWith("${"))
                {
                    _logger.LogWarning("WhatsApp Cloud API credentials not fully configured");
                    _isInitialized = false;
                    return false;
                }

                // Test connection by getting business profile
                var response = await _httpClient.GetAsync(
                    $"{_options.Value.WhatsApp.PhoneNumberId}?fields=verified_name&access_token={_options.Value.WhatsApp.AccessToken}");

                if (response.IsSuccessStatusCode)
                {
                    _isInitialized = true;
                    _logger.LogInformation("WhatsApp Cloud API initialized successfully for phone number: {PhoneNumberId}", 
                        _options.Value.WhatsApp.PhoneNumberId);
                    return true;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to initialize WhatsApp Cloud API. Status: {StatusCode}, Error: {Error}", 
                        response.StatusCode, errorContent);
                    
                    // Try to parse the error for better messaging
                    try
                    {
                        var errorDoc = JsonDocument.Parse(errorContent);
                        if (errorDoc.RootElement.TryGetProperty("error", out var errorObj))
                        {
                            var errorMessage = errorObj.GetProperty("message").GetString();
                            var errorType = errorObj.GetProperty("type").GetString();
                            _logger.LogError("WhatsApp API Error: {Type} - {Message}", errorType, errorMessage);
                        }
                    }
                    catch (JsonException)
                    {
                        _logger.LogError("Raw error response: {Error}", errorContent);
                    }
                    
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing WhatsApp Cloud API");
                return false;
            }
        }

        public async Task<SendMessageResult> SendMessageAsync(string phoneNumber, string text)
        {
            try
            {
                if (!_isInitialized)
                {
                    var initResult = await InitializeAsync();
                    if (!initResult)
                    {
                        return new SendMessageResult
                        {
                            Success = false,
                            Message = "WhatsApp Cloud API not initialized",
                            Error = "NotInitialized",
                            Platform = "whatsapp"
                        };
                    }
                }

                // Format phone number (remove any non-digit characters)
                var formattedPhone = new string(phoneNumber.Where(char.IsDigit).ToArray());
                
                // Ensure proper international format
                if (formattedPhone.StartsWith("0"))
                {
                    formattedPhone = "62" + formattedPhone.Substring(1); // Indonesia format example
                }
                else if (!formattedPhone.StartsWith("1") && !formattedPhone.StartsWith("44") && !formattedPhone.StartsWith("91"))
                {
                    // Add country code if missing (default to 1 for US)
                    formattedPhone = "1" + formattedPhone;
                }

                var payload = new
                {
                    messaging_product = "whatsapp",
                    recipient_type = "individual",
                    to = formattedPhone,
                    type = "text",
                    text = new { body = text }
                };

                var jsonPayload = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                
                var url = $"{_options.Value.WhatsApp.PhoneNumberId}/messages?access_token={_options.Value.WhatsApp.AccessToken}";
                
                _logger.LogDebug("Sending WhatsApp message to {PhoneNumber}: {Text}", formattedPhone, text);
                
                var response = await _httpClient.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(responseJson);
                    var messageId = doc.RootElement.GetProperty("messages")[0].GetProperty("id").GetString();

                    _logger.LogInformation("WhatsApp message sent successfully to {PhoneNumber}, Message ID: {MessageId}", 
                        formattedPhone, messageId);

                    return new SendMessageResult
                    {
                        Success = true,
                        Message = "WhatsApp message sent successfully",
                        MessageId = messageId,
                        Platform = "whatsapp"
                    };
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to send WhatsApp message. Status: {StatusCode}, Error: {Error}", 
                        response.StatusCode, errorContent);
                    
                    string errorMessage = "Failed to send WhatsApp message";
                    try
                    {
                        var errorDoc = JsonDocument.Parse(errorContent);
                        if (errorDoc.RootElement.TryGetProperty("error", out var errorObj))
                        {
                            errorMessage = errorObj.GetProperty("message").GetString() ?? errorMessage;
                        }
                    }
                    catch (JsonException) { }

                    return new SendMessageResult
                    {
                        Success = false,
                        Message = errorMessage,
                        Error = errorContent,
                        Platform = "whatsapp"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending WhatsApp message to {PhoneNumber}", phoneNumber);
                return new SendMessageResult
                {
                    Success = false,
                    Message = "Error sending WhatsApp message",
                    Error = ex.Message,
                    Platform = "whatsapp"
                };
            }
        }

        public async Task<SendMessageResult> SendMessageAsync(int userId, string text)
        {
            try
            {
                var user = await _db.Users.FindAsync(userId);
                if (user == null || string.IsNullOrEmpty(user.PhoneNumber))
                {
                    return new SendMessageResult
                    {
                        Success = false,
                        Message = "User not found or phone number not set",
                        Error = "UserNotFound",
                        Platform = "whatsapp"
                    };
                }

                return await SendMessageAsync(user.PhoneNumber, text);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending WhatsApp message to user {UserId}", userId);
                return new SendMessageResult
                {
                    Success = false,
                    Message = "Error sending WhatsApp message",
                    Error = ex.Message,
                    Platform = "whatsapp"
                };
            }
        }

        public async Task HandleWebhookAsync(WhatsAppWebhookData webhookData)
        {
            try
            {
                foreach (var entry in webhookData.Entry)
                {
                    foreach (var change in entry.Changes)
                    {
                        if (change.Value.Messages != null)
                        {
                            foreach (var message in change.Value.Messages)
                            {
                                await HandleIncomingMessageAsync(message);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling WhatsApp webhook");
            }
        }

        private async Task HandleIncomingMessageAsync(WhatsAppMessage message)
        {
            try
            {
                if (message.Type == "text" && message.Text != null)
                {
                    var phoneNumber = message.From;
                    var messageText = message.Text.Body;

                    _logger.LogInformation("Received WhatsApp message from {PhoneNumber}: {Text}", phoneNumber, messageText);

                    var userId = await GetOrCreateUserFromWhatsApp(phoneNumber);
                    
                    // Process the message through orchestrator
                    var response = await _orchestrator.HandleAsync(userId, messageText, "WhatsApp");
                    
                    // Send response back
                    await SendMessageAsync(phoneNumber, response);

                    // Log the interaction
                    await LogMessageAsync(userId, messageText, response, "WhatsApp");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling WhatsApp message");
                try
                {
                    await SendMessageAsync(message.From, "❌ Sorry, I encountered an error processing your message.");
                }
                catch (Exception sendEx)
                {
                    _logger.LogError(sendEx, "Failed to send error message to WhatsApp {PhoneNumber}", message.From);
                }
            }
        }

        private async Task<int> GetOrCreateUserFromWhatsApp(string phoneNumber)
        {
            // Find existing user with this phone number
            var user = await _db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == phoneNumber);
            if (user != null)
            {
                return user.Id;
            }

            // Create new user
            user = new User
            {
                Email = $"{phoneNumber}@whatsapp.aiagent",
                Name = $"WhatsApp User {phoneNumber}",
                PasswordHash = "whatsapp-auth", // Special password for WhatsApp users
                Role = "User",
                Timezone = "UTC",
                PhoneNumber = phoneNumber,
                CreatedAt = DateTime.UtcNow
            };

            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            // Create default preferences
            var preference = new Preference
            {
                UserId = user.Id,
                WorkHours = "09:00-18:00",
                DefaultDurationMinutes = 30,
                DefaultBoard = "default",
                DefaultList = "To Do",
                ReminderPolicy = "30m-before"
            };
            _db.Preferences.Add(preference);

            // Create messaging preference
            var userPreference = new UserMessagingPreference
            {
                UserId = user.Id,
                Platform = "whatsapp",
                PlatformUserId = phoneNumber,
                PreferredPlatform = "whatsapp",
                UpdatedAt = DateTime.UtcNow
            };
            _db.UserMessagingPreferences.Add(userPreference);

            await _db.SaveChangesAsync();

            _logger.LogInformation("Created new user from WhatsApp: {UserId} ({PhoneNumber})", user.Id, phoneNumber);

            // Send welcome message
            var welcomeMessage = "👋 Welcome to AI Agent!\n\n" +
                               "I can help you with:\n" +
                               "• 📅 Scheduling meetings and events\n" +
                               "• ✅ Creating and managing tasks\n" +
                               "• ⏰ Setting reminders\n" +
                               "• 📧 Email alerts and management\n\n" +
                               "Try commands like:\n" +
                               "\"Schedule meeting tomorrow at 3 PM\"\n" +
                               "\"Create task for project report\"\n" +
                               "\"Remind me to call John at 5 PM\"";

            await SendMessageAsync(phoneNumber, welcomeMessage);

            return user.Id;
        }

        private async Task LogMessageAsync(int userId, string incoming, string outgoing, string platform)
        {
            try
            {
                // Store incoming message
                _db.Messages.Add(new Message
                {
                    UserId = userId,
                    Channel = platform,
                    Direction = "Incoming",
                    Body = incoming,
                    CreatedAt = DateTime.UtcNow
                });

                // Store outgoing response
                _db.Messages.Add(new Message
                {
                    UserId = userId,
                    Channel = platform,
                    Direction = "Outgoing",
                    Body = outgoing,
                    CreatedAt = DateTime.UtcNow
                });

                // Store in chat history
                _db.ChatMessages.Add(new ChatMessage
                {
                    UserId = userId,
                    Role = "user",
                    Text = incoming,
                    CreatedAt = DateTime.UtcNow
                });

                _db.ChatMessages.Add(new ChatMessage
                {
                    UserId = userId,
                    Role = "assistant",
                    Text = outgoing,
                    CreatedAt = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to log WhatsApp message for user {UserId}", userId);
            }
        }

public Task DisconnectAsync()
{
    _isInitialized = false;
    _logger.LogInformation("WhatsApp Cloud API service disconnected");
    return Task.CompletedTask;
}
    }
}