using Microsoft.AspNetCore.Mvc;

namespace AiAgentBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AnalyticsController : ControllerBase
    {
        private readonly ILogger<AnalyticsController> _logger;

        public AnalyticsController(ILogger<AnalyticsController> logger)
        {
            _logger = logger;
        }

        [HttpPost("track")]
        public IActionResult Track([FromBody] AnalyticsEvent evt)
        {
            _logger.LogInformation("AB Test Event: {Test} - {Event} - Variant: {Variant} - User: {UserId}",
                evt.Test, evt.Event, evt.Variant, evt.UserId);
            return Ok(new { tracked = true });
        }
    }

    public class AnalyticsEvent
    {
        public string Test { get; set; } = "";
        public string Event { get; set; } = "";
        public string Variant { get; set; } = "";
        public string UserId { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public string? Url { get; set; }
        public string? UserAgent { get; set; }
    }
}
