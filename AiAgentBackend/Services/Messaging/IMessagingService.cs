using AiAgentBackend.Models;

namespace AiAgentBackend.Services.Messaging
{
    public interface IMessagingService
    {
        // Unified messaging interface
        Task<MessagingStatus> GetStatusAsync();
        Task<SendMessageResult> SendMessageAsync(int userId, string text);
        Task<SendMessageResult> SendMessageAsync(string platform, string to, string text);
        Task<SendMessageResult> SendQuickActionsAsync(int userId, string message, string[] actions);
        Task<bool> SendReminderAsync(int userId, string type, string title, DateTime dueTime, string description);
        
        // Platform management
        Task<bool> InitializeTelegramAsync();
        Task<bool> InitializeWhatsAppAsync();
        Task DisconnectAsync(string platform);
        
        // User preferences
        Task SetUserMessagingPreferenceAsync(int userId, string preferredPlatform);
        Task<string> GetUserMessagingPreferenceAsync(int userId);
    }

    public class MessagingStatus
    {
        public MessagingPlatformStatus Telegram { get; set; } = new();
        public MessagingPlatformStatus WhatsApp { get; set; } = new();
    }

    public class MessagingPlatformStatus
    {
        public bool IsConnected { get; set; }
        public string Status { get; set; } = "disconnected";
        public string? Username { get; set; }
        public DateTime LastChecked { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class SendMessageResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? MessageId { get; set; }
        public string? Error { get; set; }
        public string Platform { get; set; } = string.Empty;
    }
}