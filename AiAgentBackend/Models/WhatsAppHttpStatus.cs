namespace AiAgentBackend.Models
{
    public class WhatsAppHttpStatus
    {
        public bool IsConnected { get; set; }
        public string ConnectionId { get; set; } = string.Empty;
        public string Status { get; set; } = "Disconnected";
        public DateTime LastChecked { get; set; } = DateTime.UtcNow;
        public string? ErrorMessage { get; set; }
    }
}
