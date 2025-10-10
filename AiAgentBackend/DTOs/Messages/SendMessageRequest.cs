// DTOs/Messages/SendMessageRequest.cs
using System.ComponentModel.DataAnnotations;

namespace AiAgentBackend.DTOs.Messages
{
    public class SendMessageRequest
    {
        [Required]
        public string Text { get; set; } = string.Empty;
    }
}