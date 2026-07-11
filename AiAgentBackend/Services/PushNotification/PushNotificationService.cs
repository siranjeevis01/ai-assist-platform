using AiAgentBackend.Data;
using AiAgentBackend.Models;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.EntityFrameworkCore;

namespace AiAgentBackend.Services.PushNotification
{
    public class PushNotificationService : IPushNotificationService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<PushNotificationService> _logger;
        private static bool _firebaseInitialized;
        private static readonly object _lock = new();

        public PushNotificationService(
            ApplicationDbContext db,
            ILogger<PushNotificationService> logger)
        {
            _db = db;
            _logger = logger;
            EnsureFirebaseInitialized();
        }

        private static void EnsureFirebaseInitialized()
        {
            if (_firebaseInitialized) return;
            lock (_lock)
            {
                if (_firebaseInitialized) return;
                try
                {
                    if (FirebaseApp.DefaultInstance == null)
                    {
                        var configPath = Environment.GetEnvironmentVariable("FIREBASE_CONFIG")
                                         ?? Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS");
                        if (!string.IsNullOrEmpty(configPath))
                        {
                            FirebaseApp.Create(new AppOptions
                            {
                                Credential = GoogleCredential.FromFile(configPath)
                            });
                            _firebaseInitialized = true;
                        }
                    }
                    else
                    {
                        _firebaseInitialized = true;
                    }
                }
                catch
                {
                    // Firebase init is non-critical; we'll log warnings on send attempts instead
                }
            }
        }

        public async Task RegisterDeviceAsync(string userId, string token, string platform)
        {
            try
            {
                var existing = await _db.DeviceTokens
                    .FirstOrDefaultAsync(dt => dt.Token == token);

                if (existing != null)
                {
                    existing.UserId = userId;
                    existing.Platform = platform;
                    existing.LastUsedAt = DateTime.UtcNow;
                    existing.IsActive = true;
                }
                else
                {
                    _db.DeviceTokens.Add(new DeviceToken
                    {
                        UserId = userId,
                        Token = token,
                        Platform = platform,
                        CreatedAt = DateTime.UtcNow,
                        LastUsedAt = DateTime.UtcNow,
                        IsActive = true
                    });
                }

                await _db.SaveChangesAsync();
                _logger.LogInformation("Device token registered for user {UserId} on {Platform}", userId, platform);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to register device token");
                throw;
            }
        }

        public async Task UnregisterDeviceAsync(string token)
        {
            try
            {
                var device = await _db.DeviceTokens
                    .FirstOrDefaultAsync(dt => dt.Token == token);

                if (device != null)
                {
                    device.IsActive = false;
                    await _db.SaveChangesAsync();
                    _logger.LogInformation("Device token unregistered");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to unregister device token");
                throw;
            }
        }

        public async Task SendPushAsync(string userId, string title, string body, Dictionary<string, string>? data = null)
        {
            if (!_firebaseInitialized)
            {
                _logger.LogWarning("Firebase not initialized — cannot send push notifications");
                return;
            }

            try
            {
                var tokens = await _db.DeviceTokens
                    .Where(dt => dt.UserId == userId && dt.IsActive)
                    .Select(dt => dt.Token)
                    .ToListAsync();

                if (tokens.Count == 0)
                {
                    _logger.LogDebug("No active device tokens for user {UserId}", userId);
                    return;
                }

                var message = new MulticastMessage
                {
                    Tokens = tokens,
                    Notification = new Notification
                    {
                        Title = title,
                        Body = body
                    },
                    Data = data?.ToDictionary(kv => kv.Key, kv => kv.Value) ?? new Dictionary<string, string>()
                };

                var response = await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(message);

                _logger.LogInformation(
                    "Push sent to user {UserId}: {SuccessCount} succeeded, {FailureCount} failed",
                    userId, response.SuccessCount, response.FailureCount);

                // Clean up invalid tokens
                if (response.FailureCount > 0)
                {
                    for (int i = 0; i < response.Responses.Count; i++)
                    {
                        if (!response.Responses[i].IsSuccess &&
                            IsInvalidTokenError(response.Responses[i].Exception))
                        {
                            var invalidToken = tokens[i];
                            var device = await _db.DeviceTokens
                                .FirstOrDefaultAsync(dt => dt.Token == invalidToken);
                            if (device != null)
                            {
                                device.IsActive = false;
                            }
                        }
                    }
                    await _db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send push notification to user {UserId}", userId);
            }
        }

        public async Task SendPushToAllAsync(string title, string body, Dictionary<string, string>? data = null)
        {
            if (!_firebaseInitialized)
            {
                _logger.LogWarning("Firebase not initialized — cannot send push notifications");
                return;
            }

            try
            {
                var tokens = await _db.DeviceTokens
                    .Where(dt => dt.IsActive)
                    .Select(dt => dt.Token)
                    .ToListAsync();

                if (tokens.Count == 0)
                {
                    _logger.LogDebug("No active device tokens");
                    return;
                }

                var message = new MulticastMessage
                {
                    Tokens = tokens,
                    Notification = new Notification
                    {
                        Title = title,
                        Body = body
                    },
                    Data = data?.ToDictionary(kv => kv.Key, kv => kv.Value) ?? new Dictionary<string, string>()
                };

                var response = await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(message);

                _logger.LogInformation(
                    "Broadcast push: {SuccessCount} succeeded, {FailureCount} failed",
                    response.SuccessCount, response.FailureCount);

                if (response.FailureCount > 0)
                {
                    for (int i = 0; i < response.Responses.Count; i++)
                    {
                        if (!response.Responses[i].IsSuccess &&
                            IsInvalidTokenError(response.Responses[i].Exception))
                        {
                            var invalidToken = tokens[i];
                            var device = await _db.DeviceTokens
                                .FirstOrDefaultAsync(dt => dt.Token == invalidToken);
                            if (device != null)
                            {
                                device.IsActive = false;
                            }
                        }
                    }
                    await _db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send broadcast push notification");
            }
        }

        private static bool IsInvalidTokenError(Exception? exception)
        {
            if (exception == null) return false;
            var message = exception.Message.ToLowerInvariant();
            return message.Contains("registration-token-not-registered") ||
                   message.Contains("invalid-argument") ||
                   message.Contains("not-a-registered-fcm-token");
        }
    }
}
