// DTOs/Event/EventDto.cs
namespace AiAgentBackend.DTOs.Event
{
    public class EventDto
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTimeOffset StartUtc { get; set; }
        public DateTimeOffset EndUtc { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? ExternalId { get; set; }
        public string? AttendeesJson { get; set; }
        public string? Location { get; set; }
        public string? Source { get; set; }
    }
}    