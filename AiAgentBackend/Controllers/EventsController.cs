using AiAgentBackend.Data;
using AiAgentBackend.DTOs.Event;
using AiAgentBackend.Models;
using AiAgentBackend.Services.Integrations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;

namespace AiAgentBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class EventsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IGoogleCalendarService _google;
        private readonly ILogger<EventsController> _logger;

        public EventsController(ApplicationDbContext db, IGoogleCalendarService google, 
                              ILogger<EventsController> logger)
        { 
            _db = db; 
            _google = google; 
            _logger = logger;
        }

        private int GetUserId()
        {
            var userIdStr = User.FindFirstValue("uid") ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            return int.TryParse(userIdStr, out var userId) ? userId : 0;
        }

[HttpGet]
public async Task<ActionResult<IEnumerable<EventDto>>> List(
    [FromQuery] DateTime? from = null, 
    [FromQuery] DateTime? to = null)
{
    try
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();

        var query = _db.Events
            .Where(e => e.UserId == userId);
        
        if (from.HasValue)
            query = query.Where(e => e.StartUtc >= from.Value);
        
        if (to.HasValue)
            query = query.Where(e => e.EndUtc <= to.Value);
        
        var events = await query
            .OrderBy(e => e.StartUtc)
            .Select(e => new EventDto
            {
                Id = e.Id,
                UserId = e.UserId,
                Title = e.Title,
                Description = e.Description,
                StartUtc = e.StartUtc,
                EndUtc = e.EndUtc,
                Status = e.Status,
                ExternalId = e.ExternalId,
                AttendeesJson = e.AttendeesJson,
                Location = e.Location,
                Source = e.Source
            })
            .ToListAsync();
        
        return Ok(events);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error listing events");
        return StatusCode(500, new { error = "Internal server error" });
    }
}

[HttpPost]
public async Task<ActionResult<EventDto>> Create([FromBody] CreateEventRequest req)
{
    try
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();

        var user = await _db.Users.FindAsync(userId);
        var timezone = user?.Timezone ?? "UTC";

        DateTimeOffset startUtc;
        DateTimeOffset endUtc;

        // If user didn't provide StartUtc, pick random time
        if (req.StartUtc == DateTimeOffset.MinValue)
        {
            var random = new Random();
            int hour = random.Next(9, 18); // 9AM-5PM
            int minute = random.Next(0, 60);

            startUtc = new DateTimeOffset(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, hour, minute, 0, TimeSpan.Zero);
            endUtc = startUtc.AddHours(1);
        }
        else
        {
            // Convert user-provided time to UTC
            startUtc = ConvertToUtc(req.StartUtc, timezone);
            endUtc   = ConvertToUtc(req.EndUtc, timezone);
        }

        var evt = new Event
        { 
            UserId = userId,
            Title = req.Title,
            StartUtc = startUtc,
            EndUtc = endUtc,
            Location = req.Location,
            Description = req.Description,
            Status = "Scheduled",
            Source = "Dashboard"
        };

        // Handle attendees
        if (!string.IsNullOrWhiteSpace(req.AttendeesJson))
        {
            evt.AttendeesJson = req.AttendeesJson;
        }
        else if (!string.IsNullOrWhiteSpace(req.AttendeesCsv))
        {
            var attendees = req.AttendeesCsv.Split(',')
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrEmpty(x))
                .ToArray();
            evt.AttendeesJson = JsonSerializer.Serialize(attendees);
        }

        _db.Events.Add(evt);

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Entity = "Event",
            Action = "Create",
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        // Sync with Google Calendar
        try
        {
            var hasGoogle = await _db.ProviderTokens
                .AnyAsync(pt => pt.UserId == userId && pt.Provider == "Google");

            if (hasGoogle)
            {
                var created = await _google.CreateEventAsync(userId, evt);
                if (created != null)
                {
                    evt.ExternalId = created.ExternalId;
                    _db.Events.Update(evt);
                    await _db.SaveChangesAsync();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Google Calendar sync failed for event {EventId}", evt.Id);
        }

        var eventDto = new EventDto
        {
            Id = evt.Id,
            UserId = evt.UserId,
            Title = evt.Title,
            Description = evt.Description,
            StartUtc = ConvertToTimezone(evt.StartUtc, timezone),
            EndUtc = ConvertToTimezone(evt.EndUtc, timezone),
            Status = evt.Status,
            ExternalId = evt.ExternalId,
            AttendeesJson = evt.AttendeesJson,
            Location = evt.Location,
            Source = evt.Source
        };

        _logger.LogInformation("Event created: {EventId} by user {UserId}", evt.Id, userId);

        return CreatedAtAction(nameof(List), new { id = evt.Id }, eventDto);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error creating event: {Message}", ex.Message);
        return StatusCode(500, new { error = "Internal server error", details = ex.Message });
    }            
}

[HttpPatch("{id:int}")]
public async Task<IActionResult> Update(int id, [FromBody] UpdateEventRequest req)
{
    try
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();

        var evt = await _db.Events.FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId);
        if (evt is null) return NotFound();

        var user = await _db.Users.FindAsync(userId);
        var timezone = user?.Timezone ?? "UTC";

        if (!string.IsNullOrWhiteSpace(req.Title))
            evt.Title = req.Title;

        DateTimeOffset startUtc;
        DateTimeOffset endUtc;

        if (req.StartUtc.HasValue)
        {
            // Convert user-provided time to UTC
            startUtc = ConvertToUtc(req.StartUtc.Value, timezone);
            endUtc = req.EndUtc.HasValue ? ConvertToUtc(req.EndUtc.Value, timezone) : startUtc.AddHours(1);
        }
        else
        {
            // Pick random time if not provided
            var random = new Random();
            int hour = random.Next(9, 18); // 9AM-5PM
            int minute = random.Next(0, 60);

            startUtc = new DateTimeOffset(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, hour, minute, 0, TimeSpan.Zero);
            endUtc = startUtc.AddHours(1);
        }

        evt.StartUtc = startUtc;
        evt.EndUtc = endUtc;

        if (!string.IsNullOrWhiteSpace(req.Location))
            evt.Location = req.Location;

        if (!string.IsNullOrWhiteSpace(req.Description))
            evt.Description = req.Description;

        if (!string.IsNullOrWhiteSpace(req.Status))
            evt.Status = req.Status;

        // Handle attendees
        if (!string.IsNullOrWhiteSpace(req.AttendeesCsv))
        {
            var attendees = req.AttendeesCsv.Split(',')
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrEmpty(x))
                .ToArray();
            evt.AttendeesJson = JsonSerializer.Serialize(attendees);
        }
        else if (!string.IsNullOrWhiteSpace(req.AttendeesJson))
        {
            try
            {
                JsonDocument.Parse(req.AttendeesJson);
                evt.AttendeesJson = req.AttendeesJson;
            }
            catch
            {
                return BadRequest(new { error = "Invalid AttendeesJson format" });
            }
        }

        _db.Events.Update(evt);

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Entity = "Event",
            Action = "Update",
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        // Google sync
        try
        {
            var hasGoogle = await _db.ProviderTokens
                .AnyAsync(pt => pt.UserId == userId && pt.Provider == "Google");

            if (hasGoogle && !string.IsNullOrEmpty(evt.ExternalId))
            {
                await _google.UpdateEventAsync(userId, evt);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Google Calendar update failed for event {EventId}", evt.Id);
        }

        var dto = new EventDto
        {
            Id = evt.Id,
            UserId = evt.UserId,
            Title = evt.Title,
            Description = evt.Description,
            StartUtc = ConvertToTimezone(evt.StartUtc, timezone),
            EndUtc = ConvertToTimezone(evt.EndUtc, timezone),
            Status = evt.Status,
            ExternalId = evt.ExternalId,
            AttendeesJson = evt.AttendeesJson,
            Location = evt.Location,
            Source = evt.Source
        };

        _logger.LogInformation("Event updated: {EventId} by user {UserId}", evt.Id, userId);

        return Ok(dto);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error updating event {EventId}", id);
        return StatusCode(500, new { error = "Internal server error" });
    }
}

[HttpDelete("{id:int}")]
public async Task<IActionResult> Delete(int id)
{
    try
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();

        var evt = await _db.Events.FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId);
        if (evt is null) return NotFound();

        // Delete from Google Calendar first if connected
        try
        {
            var hasGoogle = await _db.ProviderTokens
                .AnyAsync(pt => pt.UserId == userId && pt.Provider == "Google");
                
            if (hasGoogle && !string.IsNullOrEmpty(evt.ExternalId))
            {
                await _google.DeleteEventAsync(userId, evt.ExternalId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Google Calendar delete failed for event {EventId}", evt.Id);
        }

        _db.Events.Remove(evt);
        
        // Log the event deletion
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Entity = "Event",
            Action = "Delete",
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        _logger.LogInformation("Event deleted: {EventId} by user {UserId}", evt.Id, userId);

        return Ok(new { message = $"Event with ID {id} has been successfully deleted" });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error deleting event {EventId}", id);
        return StatusCode(500, new { error = "Internal server error" });
    }
}

[HttpPost("{id:int}/sync")]
public async Task<IActionResult> SyncWithGoogle(int id)
{
    try
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();

        var evt = await _db.Events.FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId);
        if (evt is null) return NotFound();

        var hasGoogle = await _db.ProviderTokens
            .AnyAsync(pt => pt.UserId == userId && pt.Provider == "Google");
            
        if (!hasGoogle)
            return BadRequest(new { error = "Google Calendar not connected" });

        Event syncedEvent;
        if (string.IsNullOrEmpty(evt.ExternalId))
        {
            syncedEvent = await _google.CreateEventAsync(userId, evt);
        }
        else
        {
            syncedEvent = await _google.UpdateEventAsync(userId, evt);
        }

        if (syncedEvent != null)
        {
            evt.ExternalId = syncedEvent.ExternalId;
            _db.Events.Update(evt);
            await _db.SaveChangesAsync();
        }

        // Log the sync action
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Entity = "Event",
            Action = "SyncWithGoogle",
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        _logger.LogInformation("Event synced with Google: {EventId} by user {UserId}", evt.Id, userId);

        return Ok(new { message = "Event synced successfully", externalId = evt.ExternalId });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error syncing event {EventId} with Google", id);
        return StatusCode(500, new { error = "Internal server error during sync" });
    }
}

        private DateTimeOffset ConvertToUtc(DateTimeOffset dateTime, string timezone)
        {
            try
            {
                var timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(timezone);
                return TimeZoneInfo.ConvertTimeToUtc(dateTime.DateTime, timeZoneInfo);
            }
            catch
            {
                return dateTime.ToUniversalTime();
            }
        }

        private DateTimeOffset ConvertToTimezone(DateTimeOffset dateTime, string timezone)
        {
            try
            {
                var timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(timezone);
                return TimeZoneInfo.ConvertTimeFromUtc(dateTime.UtcDateTime, timeZoneInfo);
            }
            catch
            {
                return dateTime.ToLocalTime();
            }
        }
    }
}