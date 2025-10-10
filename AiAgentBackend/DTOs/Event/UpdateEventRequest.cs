// DTOs/Event/UpdateEventRequest.cs
namespace AiAgentBackend.DTOs.Event
{
    public class UpdateEventRequest
{
    public string? Title { get; set; }
    public DateTimeOffset? StartUtc { get; set; }
    public DateTimeOffset? EndUtc { get; set; }
    public string? Location { get; set; }
    public string? Description { get; set; }
    public string? Status { get; set; }
    public string? AttendeesCsv { get; set; }
    public string? AttendeesJson { get; set; }
}
}