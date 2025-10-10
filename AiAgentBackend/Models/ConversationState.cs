// Models/ConversationState.cs
using System.ComponentModel.DataAnnotations;

namespace AiAgentBackend.Models
{
    public class ConversationState
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Intent { get; set; } = string.Empty;
        public string CurrentStep { get; set; } = string.Empty;
        public string ContextData { get; set; } = string.Empty; // JSON serialized data
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(1);
        
        public virtual User User { get; set; } = null!;
    }
    
    public class EventCreationData
    {
        public string Title { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public List<string> Attendees { get; set; } = new List<string>();
        public string Location { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}