using System.ComponentModel.DataAnnotations;

namespace AiAgentBackend.Models
{
    public class UserMessagingPreference
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        
        [MaxLength(20)]
        public string Platform { get; set; } = string.Empty; // telegram, whatsapp
        
        [MaxLength(100)]
        public string? PlatformUserId { get; set; } // Telegram user ID, WhatsApp phone number
        
        [MaxLength(100)]
        public string? ChatId { get; set; } // Telegram chat ID
        
        [MaxLength(20)]
        public string PreferredPlatform { get; set; } = "telegram";
        
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public virtual User User { get; set; } = null!;
    }
}