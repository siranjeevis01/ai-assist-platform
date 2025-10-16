using AiAgentBackend.Models;

namespace AiAgentBackend.Services.Integrations
{
    public interface IWhatsAppService
    {
        Task<WhatsAppStatus> GetStatusAsync();
        Task<QrResponse> GetQrCodeAsync();
        Task<InitializeConnectionResult> InitializeConnectionAsync();
        Task<SendMessageResult> SendMessageAsync(int userId, string text);
        Task<SendMessageResult> SendMessageAsync(string to, string text);
        Task<SendMessageResult> SendQuickActionsAsync(int userId, string message, string[] actions);
        Task<bool> SendReminderAsync(int userId, string type, string title, DateTime dueTime, string description);
        Task HandleIncomingMessageAsync(string from, string message, string messageId);
        Task<bool> CheckConnectionStatusAsync();
        Task DisconnectAsync();
        Task CleanupAsync();
        void RegisterMessageHandler(Func<string, string, string, Task> handler);
        event Func<string, string, string, Task>? OnMessageReceived;
    }

    // Response Models
    public class WhatsAppStatus
    {
        public bool IsConnected { get; set; }
        public string Status { get; set; } = string.Empty;
        public bool QrAvailable { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsInitializing { get; set; }
        public DateTime? QrGeneratedAt { get; set; }
    }

    public class QrResponse
    {
        public string QrCode { get; set; } = string.Empty;
        public string QrImage { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime? ExpiresAt { get; set; }
    }

    public class InitializeConnectionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? QrCode { get; set; }
        public string? QrImage { get; set; }
        public string? NextStep { get; set; }
        public string? Error { get; set; }
    }

    public class SendMessageResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? MessageId { get; set; }
        public string? Error { get; set; }
    }
}