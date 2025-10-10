// DTOs/Event/CreateEventRequest.cs
namespace AiAgentBackend.DTOs.Event
{
    public class CreateEventRequest
    {
        public string Title { get; set; } = string.Empty;
        public DateTimeOffset StartUtc { get; set; }
        public DateTimeOffset EndUtc { get; set; }
        public string? Location { get; set; }
        public string? Description { get; set; }
        public string? AttendeesCsv { get; set; }
        public string? AttendeesJson { get; set; }  
    }
}
