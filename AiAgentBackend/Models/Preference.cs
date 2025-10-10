// Models/Preference.cs
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization; 

namespace AiAgentBackend.Models
{
    public class Preference
    {
        public int Id { get; set; }
        public int UserId { get; set; }

        [JsonIgnore]   
        public virtual User? User { get; set; }

        [MaxLength(50)]
        public string WorkHours { get; set; } = "09:00-18:00";
        
        public int DefaultDurationMinutes { get; set; } = 30;
        
        [MaxLength(100)]
        public string DefaultBoard { get; set; } = "default";
        
        [MaxLength(100)]
        public string DefaultList { get; set; } = "To Do";
        
        [MaxLength(50)]
        public string ReminderPolicy { get; set; } = "30m-before";
    }
}
