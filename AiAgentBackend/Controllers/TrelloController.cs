using AiAgentBackend.Configuration;
using AiAgentBackend.Data;
using AiAgentBackend.Services.Integrations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace AiAgentBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class TrelloController : ControllerBase
    {
        private readonly TrelloOptions _options;
        private readonly ApplicationDbContext _db;
        private readonly ITrelloService _trello;
        private readonly ILogger<TrelloController> _logger;

        public TrelloController(IOptions<TrelloOptions> options, ApplicationDbContext db,
            ITrelloService trello, ILogger<TrelloController> logger)
        {
            _options = options.Value;
            _db = db;
            _trello = trello;
            _logger = logger;
        }

        private int GetUserId()
        {
            var userIdStr = User.FindFirstValue("uid") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(userIdStr, out var userId) ? userId : 0;
        }

        [HttpGet("status")]
        public IActionResult GetStatus()
        {
            var isConfigured = !string.IsNullOrEmpty(_options.ApiKey) && !string.IsNullOrEmpty(_options.AccessToken);
            return Ok(new {
                connected = isConfigured,
                configured = isConfigured,
                boardId = isConfigured ? _options.DefaultBoardId : null
            });
        }

        [HttpPost("sync")]
        public async Task<IActionResult> SyncTasks()
        {
            var userId = GetUserId();
            if (userId == 0) return Unauthorized();

            if (string.IsNullOrEmpty(_options.ApiKey) || string.IsNullOrEmpty(_options.AccessToken))
                return BadRequest(new { error = "Trello not configured. Set TRELLO_API_KEY and TRELLO_ACCESS_TOKEN environment variables." });

            try
            {
                await _trello.SyncUserTasks(userId);
                return Ok(new { message = "Tasks synced with Trello" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing tasks with Trello for user {UserId}", userId);
                return StatusCode(500, new { error = "Sync failed" });
            }
        }
    }
}
