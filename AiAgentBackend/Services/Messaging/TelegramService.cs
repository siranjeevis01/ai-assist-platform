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
    public class TelegramService : ITelegramService
    {
        private readonly HttpClient _httpClient;
        private readonly IOptions<MessagingOptions> _options;
        private readonly ApplicationDbContext _db;
        private readonly ICommandOrchestrator _orchestrator;
        private readonly ILogger<TelegramService> _logger;
        
        private bool _isInitialized = false;
        private string? _botUsername;

        public TelegramService(
            HttpClient httpClient,
            IOptions<MessagingOptions> options,
            ApplicationDbContext db,
            ICommandOrchestrator orchestrator,
            ILogger<TelegramService> logger)
        {
            _httpClient = httpClient;
            _options = options;
            _db = db;
            _orchestrator = orchestrator;
            _logger = logger;
        }

public async Task<MessagingPlatformStatus> GetStatusAsync()
{
    if (!_isInitialized)
    {
        await InitializeAsync();
    }

    var status = new MessagingPlatformStatus
    {
        IsConnected = _isInitialized,
        Status = _isInitialized ? "connected" : "disconnected",
        Username = _botUsername, 
        LastChecked = DateTime.UtcNow
    };

    if (!_isInitialized)
    {
        status.ErrorMessage = "Telegram bot not configured"; 
    }

    return status;
}

        public async Task<bool> InitializeAsync()
        {
            try
            {
                var botToken = _options.Value.Telegram.BotToken;
                if (string.IsNullOrEmpty(botToken) ||
                    botToken.Contains("YOUR_") ||
                    botToken.StartsWith("${"))
                {
                    _logger.LogWarning("Telegram bot token not configured");
                    return false;
                }

                // Test bot connection and get bot info
                var response = await _httpClient.GetAsync($"https://api.telegram.org/bot{_options.Value.Telegram.BotToken}/getMe");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var result = doc.RootElement.GetProperty("result");
                    _botUsername = result.GetProperty("username").GetString();
                    _isInitialized = true;

                    _logger.LogInformation("Telegram bot initialized successfully: @{BotUsername}", _botUsername);
                    return true;
                }
                else
                {
                    _logger.LogError("Failed to initialize Telegram bot: {StatusCode}", response.StatusCode);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing Telegram bot");
                return false;
            }
        }

        public async Task<SendMessageResult> SendMessageAsync(string chatId, string text)
        {
            try
            {
                if (!_isInitialized)
                {
                    return new SendMessageResult
                    {
                        Success = false,
                        Message = "Telegram bot not initialized",
                        Error = "NotInitialized",
                        Platform = "telegram"
                    };
                }

                var payload = new
                {
                    chat_id = chatId,
                    text = text,
                    parse_mode = "Markdown"
                };

                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"https://api.telegram.org/bot{_options.Value.Telegram.BotToken}/sendMessage", content);

                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(responseJson);
                    var messageId = doc.RootElement.GetProperty("result").GetProperty("message_id").GetInt32();

                    _logger.LogInformation("Telegram message sent to chat {ChatId}", chatId);

                    return new SendMessageResult
                    {
                        Success = true,
                        Message = "Telegram message sent successfully",
                        MessageId = messageId.ToString(),
                        Platform = "telegram"
                    };
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to send Telegram message: {Error}", error);
                    return new SendMessageResult
                    {
                        Success = false,
                        Message = "Failed to send Telegram message",
                        Error = error,
                        Platform = "telegram"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending Telegram message to chat {ChatId}", chatId);
                return new SendMessageResult
                {
                    Success = false,
                    Message = "Error sending Telegram message",
                    Error = ex.Message,
                    Platform = "telegram"
                };
            }
        }

        public async Task<SendMessageResult> SendMessageAsync(int userId, string text)
        {
            try
            {
                var chatId = await GetUserTelegramChatId(userId);
                if (string.IsNullOrEmpty(chatId))
                {
                    return new SendMessageResult
                    {
                        Success = false,
                        Message = "User has no Telegram chat associated",
                        Error = "NoChatId",
                        Platform = "telegram"
                    };
                }

                return await SendMessageAsync(chatId, text);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending Telegram message to user {UserId}", userId);
                return new SendMessageResult
                {
                    Success = false,
                    Message = "Error sending Telegram message",
                    Error = ex.Message,
                    Platform = "telegram"
                };
            }
        }

        public async Task<SendMessageResult> SendQuickActionsAsync(int userId, string message, string[] actions)
        {
            try
            {
                var chatId = await GetUserTelegramChatId(userId);
                if (string.IsNullOrEmpty(chatId))
                {
                    return new SendMessageResult
                    {
                        Success = false,
                        Message = "User has no Telegram chat associated",
                        Error = "NoChatId",
                        Platform = "telegram"
                    };
                }

                var keyboard = new
                {
                    inline_keyboard = actions.Select(action => new[]
                    {
                        new { text = action, callback_data = action.ToLower().Replace(" ", "_") }
                    }).ToArray()
                };

                var payload = new
                {
                    chat_id = chatId,
                    text = message,
                    parse_mode = "Markdown",
                    reply_markup = keyboard
                };

                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"https://api.telegram.org/bot{_options.Value.Telegram.BotToken}/sendMessage", content);

                if (response.IsSuccessStatusCode)
                {
                    return new SendMessageResult
                    {
                        Success = true,
                        Message = "Telegram quick actions sent successfully",
                        Platform = "telegram"
                    };
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to send Telegram quick actions: {Error}", error);
                    return new SendMessageResult
                    {
                        Success = false,
                        Message = "Failed to send quick actions",
                        Error = error,
                        Platform = "telegram"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending Telegram quick actions to user {UserId}", userId);
                return new SendMessageResult
                {
                    Success = false,
                    Message = "Error sending quick actions",
                    Error = ex.Message,
                    Platform = "telegram"
                };
            }
        }

        public async Task HandleUpdateAsync(TelegramUpdate update)
        {
            try
            {
                if (update.Message != null)
                {
                    await HandleMessageAsync(update.Message);
                }
                else if (update.CallbackQuery != null)
                {
                    await HandleCallbackQueryAsync(update.CallbackQuery);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling Telegram update");
            }
        }

        private async Task HandleMessageAsync(TelegramMessage message)
        {
            try
            {
                var userId = await GetOrCreateUserFromTelegram(message.From, message.Chat);
                
                if (!string.IsNullOrEmpty(message.Text))
                {
                    _logger.LogInformation("Received Telegram message from user {UserId}: {Text}", userId, message.Text);

                    // Process the message through orchestrator
                    var response = await _orchestrator.HandleAsync(userId, message.Text, "Telegram");
                    
                    // Send response back
                    await SendMessageAsync(message.Chat.Id.ToString(), response);

                    // Log the interaction
                    await LogMessageAsync(userId, message.Text, response, "Telegram");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling Telegram message");
                try
                {
                    await SendMessageAsync(message.Chat.Id.ToString(), "❌ Sorry, I encountered an error processing your message.");
                }
                catch (Exception sendEx)
                {
                    _logger.LogError(sendEx, "Failed to send error message to Telegram chat {ChatId}", message.Chat.Id);
                }
            }
        }

        private async Task HandleCallbackQueryAsync(TelegramCallbackQuery callbackQuery)
        {
            try
            {
                var userId = await GetOrCreateUserFromTelegram(callbackQuery.From, callbackQuery.Message.Chat);
                
                _logger.LogInformation("Received Telegram callback from user {UserId}: {Data}", userId, callbackQuery.Data);

                // Handle quick action responses
                var responseText = $"Action received: {callbackQuery.Data.Replace("_", " ")}";
                await SendMessageAsync(callbackQuery.Message.Chat.Id.ToString(), responseText);

                // Answer callback query to remove loading state
                var answerPayload = new { callback_query_id = callbackQuery.Id };
                var content = new StringContent(JsonSerializer.Serialize(answerPayload), Encoding.UTF8, "application/json");
                await _httpClient.PostAsync($"https://api.telegram.org/bot{_options.Value.Telegram.BotToken}/answerCallbackQuery", content);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling Telegram callback query");
            }
        }

        private async Task<int> GetOrCreateUserFromTelegram(TelegramUser from, TelegramChat chat)
        {
            // Find existing user preference with this Telegram user ID
            var preference = await _db.UserMessagingPreferences
                .Include(ump => ump.User)
                .FirstOrDefaultAsync(ump => ump.Platform == "telegram" && ump.PlatformUserId == from.Id.ToString());

            if (preference != null)
            {
                return preference.UserId;
            }

            // Create new user
            var user = new User
            {
                Email = $"{from.Id}@telegram.aiagent",
                Name = $"{from.FirstName} {from.Username ?? ""}".Trim(),
                PasswordHash = "telegram-auth", // Special password for Telegram users
                Role = "User",
                Timezone = "UTC",
                PhoneNumber = null,
                CreatedAt = DateTime.UtcNow
            };

            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            // Create messaging preference
            var userPreference = new UserMessagingPreference
            {
                UserId = user.Id,
                Platform = "telegram",
                PlatformUserId = from.Id.ToString(),
                ChatId = chat.Id.ToString(),
                PreferredPlatform = "telegram",
                UpdatedAt = DateTime.UtcNow
            };

            _db.UserMessagingPreferences.Add(userPreference);

            // Create default preferences
            var defaultPreference = new Preference
            {
                UserId = user.Id,
                WorkHours = "09:00-18:00",
                DefaultDurationMinutes = 30,
                DefaultBoard = "default",
                DefaultList = "To Do",
                ReminderPolicy = "30m-before"
            };
            _db.Preferences.Add(defaultPreference);

            await _db.SaveChangesAsync();

            _logger.LogInformation("Created new user from Telegram: {UserId} (@{Username})", user.Id, from.Username);

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

            await SendMessageAsync(chat.Id.ToString(), welcomeMessage);

            return user.Id;
        }

        private async Task<string?> GetUserTelegramChatId(int userId)
        {
            var preference = await _db.UserMessagingPreferences
                .FirstOrDefaultAsync(ump => ump.UserId == userId && ump.Platform == "telegram");

            return preference?.ChatId;
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
                _logger.LogWarning(ex, "Failed to log Telegram message for user {UserId}", userId);
            }
        }

public Task DisconnectAsync()
{
    _isInitialized = false;
    _botUsername = null;
    _logger.LogInformation("Telegram service disconnected");
    return Task.CompletedTask;
}
    }
}