// Models/Message.cs
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;

namespace AiAgentBackend.Models
{
    public class Message
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        
        [MaxLength(50)]
        public string Channel { get; set; } = string.Empty;
        
        [MaxLength(20)]
        public string Direction { get; set; } = string.Empty;
        
        public string Body { get; set; } = string.Empty;
        
        [MaxLength(50)]
        public string? Intent { get; set; }
        
        public string? EntitiesJson { get; set; }
        
        [MaxLength(100)]
        public string? CorrelationId { get; set; }

        public string? MessageType { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public virtual User User { get; set; } = null!;
    }
}