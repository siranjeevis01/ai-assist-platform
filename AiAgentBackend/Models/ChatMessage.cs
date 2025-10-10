// Models/ChatMessage.cs
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;

namespace AiAgentBackend.Models
{
    public class ChatMessage
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        
        [MaxLength(20)]
        public string Role { get; set; } = "user";
        
        public string Text { get; set; } = string.Empty;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public virtual User User { get; set; } = null!;
    }
}