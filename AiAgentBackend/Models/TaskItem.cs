// Models/TaskItem.cs
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;

namespace AiAgentBackend.Models
{
    public class TaskItem
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;
        
        [MaxLength(50)]
        public string Status { get; set; } = "To Do";
        
        public DateTime? DueUtc { get; set; }
        
        [MaxLength(100)]
        public string? ExternalId { get; set; }
        
        public string? LabelsJson { get; set; }
        
        [MaxLength(1000)]
        public string? Description { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
        
        // Navigation properties
        public virtual User User { get; set; } = null!;
    }
}