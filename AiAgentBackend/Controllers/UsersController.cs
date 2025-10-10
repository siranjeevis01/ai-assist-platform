using AiAgentBackend.Data;
using AiAgentBackend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace AiAgentBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class UsersController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<UsersController> _logger;

        public UsersController(ApplicationDbContext db, ILogger<UsersController> logger)
        { 
            _db = db; 
            _logger = logger;
        }

        private int GetUserId()
        {
            var userIdStr = User.FindFirstValue("uid") ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            return int.TryParse(userIdStr, out var userId) ? userId : 0;
        }

        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            try
            {
                var userId = GetUserId();
                if (userId == 0) return Unauthorized();

                var user = await _db.Users
                    .Include(u => u.Preference)
                    .FirstOrDefaultAsync(u => u.Id == userId);

                if (user == null) return NotFound();

                var hasGoogle = await _db.ProviderTokens
                    .AnyAsync(pt => pt.UserId == userId && pt.Provider == "Google");
                    
                var hasTrello = await _db.ProviderTokens
                    .AnyAsync(pt => pt.UserId == userId && pt.Provider == "Trello");

return Ok(new
{
    Id = user.Id,
    Email = user.Email,
    Name = user.Name,
    Role = user.Role,
    Timezone = user.Timezone,
    PhoneNumber = user.PhoneNumber,
    CreatedAt = user.CreatedAt,
    Preference = user.Preference == null ? null : new
    {
        user.Preference.WorkHours,
        user.Preference.DefaultDurationMinutes,
        user.Preference.DefaultBoard,
        user.Preference.DefaultList,
        user.Preference.ReminderPolicy
    },
    Integrations = new
    {
        Google = hasGoogle,
        Trello = hasTrello
    }
});
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user profile");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

[HttpPut("me")]
public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest req)
{
    try
    {
        var userId = GetUserId();
        if (userId == 0) return Unauthorized();

        var user = await _db.Users
            .Include(u => u.Preference)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null) return NotFound();

        // Update user fields
        if (!string.IsNullOrWhiteSpace(req.Name))
            user.Name = req.Name;

        if (!string.IsNullOrWhiteSpace(req.Timezone))
            user.Timezone = req.Timezone;

        if (!string.IsNullOrWhiteSpace(req.PhoneNumber))
            user.PhoneNumber = req.PhoneNumber;

        // Update or create preferences
        if (req.Preference != null)
        {
            if (user.Preference == null)
            {
                user.Preference = new Preference { UserId = userId };
                _db.Preferences.Add(user.Preference);
            }

            if (!string.IsNullOrWhiteSpace(req.Preference.WorkHours))
                user.Preference.WorkHours = req.Preference.WorkHours;

            if (req.Preference.DefaultDurationMinutes > 0)
                user.Preference.DefaultDurationMinutes = req.Preference.DefaultDurationMinutes;

            if (!string.IsNullOrWhiteSpace(req.Preference.DefaultBoard))
                user.Preference.DefaultBoard = req.Preference.DefaultBoard;

            if (!string.IsNullOrWhiteSpace(req.Preference.DefaultList))
                user.Preference.DefaultList = req.Preference.DefaultList;

            if (!string.IsNullOrWhiteSpace(req.Preference.ReminderPolicy))
                user.Preference.ReminderPolicy = req.Preference.ReminderPolicy;
        }

        // No need to call _db.Users.Update(user) since EF Core tracks changes automatically

        // Audit log
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Entity = "User",
            Action = "UpdateProfile",
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        _logger.LogInformation("Profile updated for user {UserId}", userId);

        return Ok(new { message = "Profile updated successfully" });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error updating user profile");
        return StatusCode(500, new { error = "Internal server error" });
    }
}

        [HttpPost("me/timezone")]
        public async Task<IActionResult> UpdateTimezone([FromBody] string timezone)
        {
            try
            {
                var userId = GetUserId();
                if (userId == 0) return Unauthorized();

                var user = await _db.Users.FindAsync(userId);
                if (user == null) return NotFound();

                user.Timezone = timezone;
                _db.Users.Update(user);
                
                // Log the timezone update
                _db.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    Entity = "User",
                    Action = "UpdateTimezone",
                    Timestamp = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();

                _logger.LogInformation("Timezone updated to {Timezone} for user {UserId}", timezone, userId);

                return Ok(new { message = "Timezone updated successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating timezone");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        [HttpGet("stats")]
        public async Task<IActionResult> GetStats()
        {
            try
            {
                var userId = GetUserId();
                if (userId == 0) return Unauthorized();

                var now = DateTime.UtcNow;
                var startOfWeek = now.AddDays(-(int)now.DayOfWeek);
                var startOfMonth = new DateTime(now.Year, now.Month, 1);

                var taskStats = new
                {
                    Total = await _db.Tasks.CountAsync(t => t.UserId == userId),
                    Completed = await _db.Tasks.CountAsync(t => t.UserId == userId && t.Status == "Done"),
                    ThisWeek = await _db.Tasks.CountAsync(t => t.UserId == userId && t.CreatedAt >= startOfWeek),
                    ThisMonth = await _db.Tasks.CountAsync(t => t.UserId == userId && t.CreatedAt >= startOfMonth)
                };

                var eventStats = new
                {
                    Total = await _db.Events.CountAsync(e => e.UserId == userId),
                    Upcoming = await _db.Events.CountAsync(e => e.UserId == userId && e.StartUtc >= now),
                    ThisWeek = await _db.Events.CountAsync(e => e.UserId == userId && e.StartUtc >= startOfWeek),
                    ThisMonth = await _db.Events.CountAsync(e => e.UserId == userId && e.StartUtc >= startOfMonth)
                };

                return Ok(new
                {
                    Tasks = taskStats,
                    Events = eventStats
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user stats");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }
    }

    public class UpdateProfileRequest
    {
        public string? Name { get; set; }
        public string? Timezone { get; set; }
        public string? PhoneNumber { get; set; }
        public PreferenceUpdate? Preference { get; set; }
    }

    public class PreferenceUpdate
    {
        public string? WorkHours { get; set; }
        public int DefaultDurationMinutes { get; set; }
        public string? DefaultBoard { get; set; }
        public string? DefaultList { get; set; }
        public string? ReminderPolicy { get; set; }
    }
}