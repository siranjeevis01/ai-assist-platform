// Models/GmailWebhook.cs
using System.ComponentModel.DataAnnotations;

namespace AiAgentBackend.Models
{
    public class GmailWebhook
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        
        [MaxLength(100)]
        public string WebhookId { get; set; } = string.Empty;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ExpiresAt { get; set; }
        
        [MaxLength(100)]
        public string ResourceId { get; set; } = string.Empty;
        
        // Navigation properties
        public virtual User User { get; set; } = null!;
    }
}