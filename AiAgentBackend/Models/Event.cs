// Models/Event.cs
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;

namespace AiAgentBackend.Models
{
    public class Event
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;
        
        [MaxLength(1000)]
        public string? Description { get; set; }
        
        public DateTimeOffset StartUtc { get; set; }
        public DateTimeOffset EndUtc { get; set; }
        
        [MaxLength(50)]
        public string Status { get; set; } = "Scheduled";
        
        [MaxLength(100)]
        public string? ExternalId { get; set; }
        
        public string? AttendeesJson { get; set; }
        
        [MaxLength(200)]
        public string? Location { get; set; }
        
        [MaxLength(50)]
        public string? Source { get; set; }

        // Navigation properties
        public virtual User User { get; set; } = null!;
    }
}