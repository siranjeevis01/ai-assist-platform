// Models/GmailWebhook.cs
using System.ComponentModel.DataAnnotations;

namespace AiAgentBackend.Models
{
public class GmailWebhook
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string WebhookId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public string ResourceId { get; set; } = string.Empty;
    
    public virtual User User { get; set; } = null!;
}
}