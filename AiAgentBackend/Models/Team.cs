using System.ComponentModel.DataAnnotations;

namespace AiAgentBackend.Models
{
    public enum RoleType
    {
        Owner = 0,
        Admin = 1,
        Member = 2,
        Viewer = 3
    }

    public class Team
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int OwnerId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public virtual User Owner { get; set; } = null!;
        public virtual ICollection<TeamMember> Members { get; set; } = new List<TeamMember>();
    }

    public class TeamMember
    {
        public int Id { get; set; }
        public int TeamId { get; set; }
        public int UserId { get; set; }
        public string Role { get; set; } = nameof(RoleType.Member);
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

        public virtual Team Team { get; set; } = null!;
        public virtual User User { get; set; } = null!;
    }

    public class AuditEntry
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int? TeamId { get; set; }
        public string Action { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
        public int? EntityId { get; set; }
        public string? Details { get; set; }
        public string? IpAddress { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public virtual User User { get; set; } = null!;
    }
}
