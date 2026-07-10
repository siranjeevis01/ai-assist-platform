using AiAgentBackend.Models;

namespace AiAgentBackend.Services.Messaging
{
    public interface IWhatsAppCloudService
    {
        Task<MessagingPlatformStatus> GetStatusAsync();
        Task<bool> InitializeAsync();
        Task<SendMessageResult> SendMessageAsync(string phoneNumber, string text);
        Task<SendMessageResult> SendMessageAsync(int userId, string text);
        Task HandleWebhookAsync(WhatsAppWebhookData webhookData);
        Task DisconnectAsync();
    }
}