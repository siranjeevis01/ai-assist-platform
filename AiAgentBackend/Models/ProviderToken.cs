// Models/ProviderToken.cs
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;

namespace AiAgentBackend.Models
{
    public class ProviderToken
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        
        [MaxLength(50)]
        public string Provider { get; set; } = string.Empty;
        
        public string EncryptedAccessToken { get; set; } = string.Empty;
        
        [MaxLength(500)]
        public string? RefreshToken { get; set; }
        
        [MaxLength(500)]
        public string Scope { get; set; } = string.Empty;
        
        public DateTime? ExpiresAt { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public virtual User User { get; set; } = null!;
    }
}