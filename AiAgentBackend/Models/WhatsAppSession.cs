// Models/WhatsAppSession.cs
using System.ComponentModel.DataAnnotations;

namespace AiAgentBackend.Models
{
public class WhatsAppSession
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public string Status { get; set; } = "Active";
    
    public virtual User User { get; set; } = null!;
}
}