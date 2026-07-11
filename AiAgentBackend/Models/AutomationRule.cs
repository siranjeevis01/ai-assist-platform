using System.ComponentModel.DataAnnotations;

namespace AiAgentBackend.Models
{
    public class AutomationRule
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string TriggerType { get; set; } = string.Empty;
        public string TriggerConfig { get; set; } = "{}";
        public string ActionsJson { get; set; } = "[]";
        public bool IsActive { get; set; } = true;
        public int RunCount { get; set; }
        public DateTime? LastRunAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public virtual User User { get; set; } = null!;
    }

    public class AutomationTrigger
    {
        public string Type { get; set; } = string.Empty;
        public Dictionary<string, string> Config { get; set; } = new();
    }

    public class AutomationAction
    {
        public string Type { get; set; } = string.Empty;
        public Dictionary<string, string> Config { get; set; } = new();
        public int Order { get; set; }
        public bool DelayEnabled { get; set; }
        public int DelayMinutes { get; set; }
    }
}
