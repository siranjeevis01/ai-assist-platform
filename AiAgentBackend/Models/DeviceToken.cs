using System.ComponentModel.DataAnnotations;

namespace AiAgentBackend.Models
{
    public class DeviceToken
    {
        public int Id { get; set; }
        
        [Required]
        public string UserId { get; set; } = string.Empty;
        
        [Required]
        public string Token { get; set; } = string.Empty;
        
        [MaxLength(20)]
        public string Platform { get; set; } = "web";
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;
    }
}
