// Models/AuditLog.cs
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;

namespace AiAgentBackend.Models
{
    public class AuditLog
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        
        [MaxLength(50)]
        public string Entity { get; set; } = string.Empty;
        
        [MaxLength(50)]
        public string Action { get; set; } = string.Empty;
        
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public virtual User User { get; set; } = null!;
    }
}