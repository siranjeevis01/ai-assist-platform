// Services/Messaging/MessagingService.cs
using AiAgentBackend.Data;
using AiAgentBackend.Models;
using AiAgentBackend.Hubs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using AiAgentBackend.Configuration;
using Microsoft.AspNetCore.SignalR;

namespace AiAgentBackend.Services.Messaging
{
    public class MessagingService : IMessagingService
    {
        private readonly IOptions<MessagingOptions> _options;
        private readonly ApplicationDbContext _db;
        private readonly ILogger<MessagingService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IHubContext<UpdatesHub> _hubContext;

        public MessagingService(
            IOptions<MessagingOptions> options,
            ApplicationDbContext db,
            ILogger<MessagingService> logger,
            IServiceProvider serviceProvider,
            IHubContext<UpdatesHub> hubContext)
        {
            _options = options;
            _db = db;
            _logger = logger;
            _serviceProvider = serviceProvider;
            _hubContext = hubContext;
        }

        public async Task<MessagingStatus> GetStatusAsync()
        {
            var status = new MessagingStatus();
            
            try
            {
                using var scope = _serviceProvider.CreateScope();
                
                // Telegram status
                if (_options.Value.Telegram.Enabled)
                {
                    var telegramService = scope.ServiceProvider.GetService<ITelegramService>();
                    if (telegramService != null)
                    {
                        status.Telegram = await telegramService.GetStatusAsync();
                    }
                }

                // WhatsApp status
                if (_options.Value.WhatsApp.Enabled)
                {
                    var whatsAppService = scope.ServiceProvider.GetService<IWhatsAppCloudService>();
                    if (whatsAppService != null)
                    {
                        status.WhatsApp = await whatsAppService.GetStatusAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting messaging status");
            }

            return status;
        }

        public async Task<SendMessageResult> SendMessageAsync(int userId, string text)
        {
            try
            {
                var user = await _db.Users
                    .Include(u => u.Preference)
                    .FirstOrDefaultAsync(u => u.Id == userId);

                if (user == null)
                {
                    return new SendMessageResult
                    {
                        Success = false,
                        Message = "User not found",
                        Error = "UserNotFound"
                    };
                }

                // Get user's preferred platform
                var preferredPlatform = await GetUserMessagingPreferenceAsync(userId);
                
                // Fallback logic
                string platform;
                if (!string.IsNullOrEmpty(preferredPlatform))
                {
                    platform = preferredPlatform;
                }
                else if (_options.Value.Telegram.Enabled)
                {
                    platform = "telegram";
                }
                else if (_options.Value.WhatsApp.Enabled)
                {
                    platform = "whatsapp";
                }
                else
                {
                    return new SendMessageResult
                    {
                        Success = false,
                        Message = "No messaging platforms configured",
                        Error = "NoPlatforms"
                    };
                }

                // Find contact info
                string? contactInfo = null;
                if (platform == "telegram")
                {
                    var telegramPref = await _db.UserMessagingPreferences
                        .FirstOrDefaultAsync(ump => ump.UserId == userId && ump.Platform == "telegram");
                    contactInfo = telegramPref?.ChatId;
                }
                else if (platform == "whatsapp")
                {
                    if (!string.IsNullOrEmpty(user.PhoneNumber))
                    {
                        contactInfo = user.PhoneNumber;
                    }
                    else
                    {
                        // Try to find WhatsApp preference
                        var whatsappPref = await _db.UserMessagingPreferences
                            .FirstOrDefaultAsync(ump => ump.UserId == userId && ump.Platform == "whatsapp");
                        contactInfo = whatsappPref?.PlatformUserId;
                    }
                }

                if (string.IsNullOrEmpty(contactInfo))
                {
                    return new SendMessageResult
                    {
                        Success = false,
                        Message = $"No contact information found for {platform}",
                        Error = "NoContactInfo",
                        Platform = platform
                    };
                }

                var result = await SendMessageAsync(platform, contactInfo, text);
                
                // Log the message
                if (result.Success)
                {
                    await LogOutgoingMessage(userId, platform, text, result.MessageId);
                    await _hubContext.Clients.User(userId.ToString())
                        .SendAsync("MessageSent", new { Platform = platform, Message = text, MessageId = result.MessageId });
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending message to user {UserId}", userId);
                return new SendMessageResult
                {
                    Success = false,
                    Message = "Failed to send message",
                    Error = ex.Message
                };
            }
        }

        public async Task<SendMessageResult> SendMessageAsync(string platform, string to, string text)
        {
            try
            {
                if (platform.ToLower() == "telegram" && !_options.Value.Telegram.Enabled)
                {
                    return new SendMessageResult
                    {
                        Success = false,
                        Message = "Telegram integration is disabled",
                        Error = "Disabled",
                        Platform = platform
                    };
                }
                if (platform.ToLower() == "whatsapp" && !_options.Value.WhatsApp.Enabled)
                {
                    return new SendMessageResult
                    {
                        Success = false,
                        Message = "WhatsApp integration is disabled",
                        Error = "Disabled",
                        Platform = platform
                    };
                }

                using var scope = _serviceProvider.CreateScope();
                
                return platform.ToLower() switch
                {
                    "telegram" => await SendTelegramMessage(scope, to, text),
                    "whatsapp" => await SendWhatsAppMessage(scope, to, text),
                    _ => new SendMessageResult
                    {
                        Success = false,
                        Message = $"Unsupported platform: {platform}",
                        Error = "UnsupportedPlatform",
                        Platform = platform
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending {Platform} message to {To}", platform, to);
                return new SendMessageResult
                {
                    Success = false,
                    Message = $"Failed to send {platform} message",
                    Error = ex.Message,
                    Platform = platform
                };
            }
        }

        private async Task<SendMessageResult> SendTelegramMessage(IServiceScope scope, string chatId, string text)
        {
            var telegramService = scope.ServiceProvider.GetService<ITelegramService>();
            if (telegramService == null)
            {
                return new SendMessageResult
                {
                    Success = false,
                    Message = "Telegram service not available",
                    Error = "ServiceNotAvailable",
                    Platform = "telegram"
                };
            }

            return await telegramService.SendMessageAsync(chatId, text);
        }

        private async Task<SendMessageResult> SendWhatsAppMessage(IServiceScope scope, string phoneNumber, string text)
        {
            var whatsAppService = scope.ServiceProvider.GetService<IWhatsAppCloudService>();
            if (whatsAppService == null)
            {
                return new SendMessageResult
                {
                    Success = false,
                    Message = "WhatsApp service not available",
                    Error = "ServiceNotAvailable",
                    Platform = "whatsapp"
                };
            }

            return await whatsAppService.SendMessageAsync(phoneNumber, text);
        }

        public async Task<SendMessageResult> SendQuickActionsAsync(int userId, string message, string[] actions)
        {
            try
            {
                var platform = await GetUserMessagingPreferenceAsync(userId);
                
                if (platform == "telegram")
                {
                    using var scope = _serviceProvider.CreateScope();
                    var telegramService = scope.ServiceProvider.GetService<ITelegramService>();
                    if (telegramService != null)
                    {
                        return await telegramService.SendQuickActionsAsync(userId, message, actions);
                    }
                }
                
                // For WhatsApp, include actions in message text (since WhatsApp doesn't support inline keyboards like Telegram)
                var actionText = actions.Length > 0 
                    ? $"\n\n📋 Quick Actions:\n{string.Join("\n", actions.Select((a, i) => $"{i + 1}. {a}"))}"
                    : "";
                    
                return await SendMessageAsync(userId, message + actionText);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending quick actions to user {UserId}", userId);
                return new SendMessageResult
                {
                    Success = false,
                    Message = "Failed to send quick actions",
                    Error = ex.Message
                };
            }
        }

        public async Task<bool> SendReminderAsync(int userId, string type, string title, DateTime dueTime, string description)
        {
            try
            {
                var emoji = type.ToLower() switch
                {
                    "task" => "✅",
                    "event" => "📅",
                    "meeting" => "👥",
                    "email" => "📧",
                    _ => "⏰"
                };

                var timeUntil = dueTime - DateTime.UtcNow;
                var timeText = timeUntil.TotalHours < 1 
                    ? $"{timeUntil.Minutes} minutes" 
                    : $"{timeUntil.TotalHours:F1} hours";

                var message = $"{emoji} *{type.ToUpper()} REMINDER*\n\n" +
                             $"*{title}*\n" +
                             $"⏰ Due in: {timeText}\n" +
                             $"📅 Time: {dueTime:MMM dd, yyyy 'at' HH:mm}\n" +
                             (string.IsNullOrEmpty(description) ? "" : $"\n📝 Details: {description}");

                var result = await SendMessageAsync(userId, message);
                
                if (result.Success)
                {
                    _logger.LogInformation("Sent {Type} reminder to user {UserId}", type, userId);
                    await _hubContext.Clients.User(userId.ToString())
                        .SendAsync("ReminderSent", new { Type = type, Title = title, DueTime = dueTime });
                }

                return result.Success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send reminder to user {UserId}", userId);
                return false;
            }
        }

        public async Task<bool> InitializeTelegramAsync()
        {
            if (!_options.Value.Telegram.Enabled)
            {
                _logger.LogInformation("Telegram integration is disabled, skipping initialization");
                return false;
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var telegramService = scope.ServiceProvider.GetService<ITelegramService>();
                return telegramService != null && await telegramService.InitializeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Telegram");
                return false;
            }
        }

        public async Task<bool> InitializeWhatsAppAsync()
        {
            if (!_options.Value.WhatsApp.Enabled)
            {
                _logger.LogInformation("WhatsApp integration is disabled, skipping initialization");
                return false;
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var whatsAppService = scope.ServiceProvider.GetService<IWhatsAppCloudService>();
                return whatsAppService != null && await whatsAppService.InitializeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize WhatsApp");
                return false;
            }
        }

        public async Task DisconnectAsync(string platform)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                
                if (platform.ToLower() == "telegram")
                {
                    var telegramService = scope.ServiceProvider.GetService<ITelegramService>();
                    if (telegramService != null)
                    {
                        await telegramService.DisconnectAsync();
                    }
                }
                else if (platform.ToLower() == "whatsapp")
                {
                    var whatsAppService = scope.ServiceProvider.GetService<IWhatsAppCloudService>();
                    if (whatsAppService != null)
                    {
                        await whatsAppService.DisconnectAsync();
                    }
                }
                
                _logger.LogInformation("Disconnected {Platform} messaging service", platform);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disconnecting {Platform}", platform);
            }
        }

        public async Task SetUserMessagingPreferenceAsync(int userId, string preferredPlatform)
        {
            try
            {
                if (preferredPlatform != "telegram" && preferredPlatform != "whatsapp")
                {
                    throw new ArgumentException("Platform must be 'telegram' or 'whatsapp'");
                }

                var preference = await _db.UserMessagingPreferences
                    .FirstOrDefaultAsync(ump => ump.UserId == userId);

                if (preference == null)
                {
                    preference = new UserMessagingPreference
                    {
                        UserId = userId,
                        PreferredPlatform = preferredPlatform,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _db.UserMessagingPreferences.Add(preference);
                }
                else
                {
                    preference.PreferredPlatform = preferredPlatform;
                    preference.UpdatedAt = DateTime.UtcNow;
                    _db.UserMessagingPreferences.Update(preference);
                }

                await _db.SaveChangesAsync();
                _logger.LogInformation("User {UserId} messaging preference set to {Platform}", userId, preferredPlatform);
                
                await _hubContext.Clients.User(userId.ToString())
                    .SendAsync("PreferenceUpdated", new { Platform = preferredPlatform });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set messaging preference for user {UserId}", userId);
                throw;
            }
        }

        public async Task<string> GetUserMessagingPreferenceAsync(int userId)
        {
            try
            {
                var preference = await _db.UserMessagingPreferences
                    .FirstOrDefaultAsync(ump => ump.UserId == userId);

                if (preference != null)
                {
                    return preference.PreferredPlatform;
                }

                // Default fallback logic
                if (_options.Value.Telegram.Enabled)
                {
                    return "telegram";
                }
                else if (_options.Value.WhatsApp.Enabled)
                {
                    return "whatsapp";
                }

                return "telegram"; // Ultimate fallback
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting messaging preference for user {UserId}", userId);
                return "telegram"; // Safe fallback
            }
        }

        private async Task LogOutgoingMessage(int userId, string platform, string text, string? messageId = null)
        {
            try
            {
                var message = new Message
                {
                    UserId = userId,
                    Channel = platform,
                    Direction = "Outgoing",
                    Body = text,
                    MessageType = "Text",
                    CreatedAt = DateTime.UtcNow
                };

                _db.Messages.Add(message);
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to log outgoing message for user {UserId}", userId);
            }
        }

        public async Task<List<UserMessagingPreference>> GetUserPlatformsAsync(int userId)
        {
            return await _db.UserMessagingPreferences
                .Where(ump => ump.UserId == userId)
                .ToListAsync();
        }

        public async Task<bool> RegisterUserPlatformAsync(int userId, string platform, string platformUserId, string? chatId = null)
        {
            try
            {
                var existing = await _db.UserMessagingPreferences
                    .FirstOrDefaultAsync(ump => ump.UserId == userId && ump.Platform == platform);

                if (existing == null)
                {
                    existing = new UserMessagingPreference
                    {
                        UserId = userId,
                        Platform = platform,
                        PlatformUserId = platformUserId,
                        ChatId = chatId,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _db.UserMessagingPreferences.Add(existing);
                }
                else
                {
                    existing.PlatformUserId = platformUserId;
                    existing.ChatId = chatId;
                    existing.UpdatedAt = DateTime.UtcNow;
                    _db.UserMessagingPreferences.Update(existing);
                }

                await _db.SaveChangesAsync();
                _logger.LogInformation("Registered user {UserId} on platform {Platform}", userId, platform);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to register user {UserId} on platform {Platform}", userId, platform);
                return false;
            }
        }
    }
}