using AiAgentBackend.Data;
using AiAgentBackend.Services.Integrations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;

namespace AiAgentBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class CalendarController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<CalendarController> _logger;
        private readonly IMemoryCache _cache;

        public CalendarController(ApplicationDbContext db, ILogger<CalendarController> logger, IMemoryCache cache)
        {
            _db = db;
            _logger = logger;
            _cache = cache;
        }

        private int GetUserId()
        {
            var userIdStr = User.FindFirstValue("uid") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(userIdStr, out var userId) ? userId : 0;
        }

        [HttpGet("status")]
        public async Task<IActionResult> GetStatus()
        {
            var userId = GetUserId();
            if (userId == 0) return Unauthorized();

            var cacheKey = $"cal_status:{userId}";
            if (_cache.TryGetValue(cacheKey, out object? cached))
                return Ok(cached);

            var hasGoogle = await _db.ProviderTokens
                .AnyAsync(t => t.UserId == userId && t.Provider == "Google");

            var result = new {
                connected = hasGoogle,
                provider = hasGoogle ? "Google Calendar" : "Local"
            };

            _cache.Set(cacheKey, result, TimeSpan.FromSeconds(30));
            return Ok(result);
        }

        [HttpGet("sync")]
        public async Task<IActionResult> SyncCalendar()
        {
            var userId = GetUserId();
            if (userId == 0) return Unauthorized();

            try
            {
                var hasGoogle = await _db.ProviderTokens
                    .AnyAsync(t => t.UserId == userId && t.Provider == "Google");
                if (!hasGoogle)
                    return BadRequest(new { error = "Google Calendar not connected. Connect it in Integrations." });

                return Ok(new { message = "Calendar sync initiated", provider = "Google Calendar" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing calendar for user {UserId}", userId);
                return StatusCode(500, new { error = "Sync failed" });
            }
        }
    }
}
