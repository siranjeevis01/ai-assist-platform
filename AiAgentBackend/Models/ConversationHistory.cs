// Models/ConversationHistory.cs
using System.ComponentModel.DataAnnotations;

namespace AiAgentBackend.Models
{
    public class ConversationHistory
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string UserMessage { get; set; } = string.Empty;
        public string BotResponse { get; set; } = string.Empty;
        public string? Intent { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public virtual User User { get; set; } = null!;
    }
}
