// Configuration/WhatsAppOptions.cs
namespace AiAgentBackend.Configuration
{
    public class WhatsAppOptions
    {
        public string BotApiUrl { get; set; } = "http://localhost:5000";
        public string WebhookSecret { get; set; } = "whatsapp-webhook-secret-2024";
        public int ConnectionTimeoutSeconds { get; set; } = 60;
        public int MaxRetries { get; set; } = 5;
        public int RetryDelayMs { get; set; } = 2000;
        public int QrCodeExpiryMinutes { get; set; } = 5;
        public bool WelcomeMessageEnabled { get; set; } = true;
    }
}