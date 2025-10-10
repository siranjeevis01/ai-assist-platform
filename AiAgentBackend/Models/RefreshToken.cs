// Models/RefreshToken.cs
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;

namespace AiAgentBackend.Models
{
    public class RefreshToken
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        
        [MaxLength(100)]
        public string Token { get; set; } = string.Empty;
        
        public DateTime ExpiresAt { get; set; }
        
        public bool IsRevoked { get; set; } = false;

        // Navigation properties
        public virtual User User { get; set; } = null!;
    }
}