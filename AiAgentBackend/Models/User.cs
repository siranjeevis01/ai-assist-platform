// Models/User.cs
using System.ComponentModel.DataAnnotations;

namespace AiAgentBackend.Models
{
    public class User
    {
        public int Id { get; set; }

        [Required, EmailAddress, MaxLength(255)]
        public string Email { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(20)]
        public string Role { get; set; } = "User";

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(50)]
        public string Timezone { get; set; } = "UTC";

        [Phone, MaxLength(100)]
        public string? PhoneNumber { get; set; }

        [MaxLength(128)]
        public string? PasswordResetToken { get; set; }
        
        public DateTime? PasswordResetExpiry { get; set; }

        public bool ExternalAuthOnly { get; set; } = false;

        // Navigation properties
        public virtual ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();
        public virtual ICollection<Event> Events { get; set; } = new List<Event>();
        public virtual ICollection<ProviderToken> ProviderTokens { get; set; } = new List<ProviderToken>();
        public virtual ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
        public virtual ICollection<Message> Messages { get; set; } = new List<Message>();
        public virtual ICollection<ChatMessage> ChatMessages { get; set; } = new List<ChatMessage>();
        public virtual ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
        public virtual Preference? Preference { get; set; }
    }
}
