using AiAgentBackend.Models;

namespace AiAgentBackend.Services.Messaging
{
    public interface ITelegramService
    {
        Task<MessagingPlatformStatus> GetStatusAsync();
        Task<bool> InitializeAsync();
        Task<SendMessageResult> SendMessageAsync(string chatId, string text);
        Task<SendMessageResult> SendMessageAsync(int userId, string text);
        Task<SendMessageResult> SendQuickActionsAsync(int userId, string message, string[] actions);
        Task HandleUpdateAsync(TelegramUpdate update);
        Task DisconnectAsync();
    }
}