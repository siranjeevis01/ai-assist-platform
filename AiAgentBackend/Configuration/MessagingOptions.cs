// Configuration/MessagingOptions.cs
namespace AiAgentBackend.Configuration
{
    public class MessagingOptions
    {
        public TelegramOptions Telegram { get; set; } = new();
        public WhatsAppOptions WhatsApp { get; set; } = new();
    }

    public class TelegramOptions
    {
        public string BotToken { get; set; } = string.Empty;
        public string WebhookUrl { get; set; } = "http://localhost:5000/api/telegram/webhook";
        public bool Enabled { get; set; } = true;
    }

    public class WhatsAppOptions
    {
        public bool Enabled { get; set; } = true;
        public string Provider { get; set; } = "Webhook"; // Webhook or Twilio
        public string WebhookUrl { get; set; } = "http://localhost:5000/api/whatsapp/webhook";
        public string VerifyToken { get; set; } = "ai-agent-verify-2024";
        public string AccessToken { get; set; } = string.Empty;
        public string PhoneNumberId { get; set; } = string.Empty;
        public string BusinessAccountId { get; set; } = string.Empty;
    }
}