// Configuration/WhatsAppOptions.cs
namespace AiAgentBackend.Configuration
{
    public class WhatsAppOptions
    {
        public string BotApiUrl { get; set; } = "http://localhost:3001";
        public string WebhookSecret { get; set; } = string.Empty;
        public int ConnectionTimeoutSeconds { get; set; } = 30;
    }
}