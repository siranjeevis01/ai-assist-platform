using System.ComponentModel.DataAnnotations;

namespace AiAgentBackend.Models
{
    public class WhatsAppSession
    {
        [Key]
        public int Id { get; set; }
        
        public bool IsConnected { get; set; }
        
        public DateTime? ConnectedAt { get; set; }
        
        public DateTime LastCheckedAt { get; set; }
        
        public string? SessionData { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}