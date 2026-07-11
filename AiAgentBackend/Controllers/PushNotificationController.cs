using AiAgentBackend.Services.PushNotification;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiAgentBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class PushNotificationController : ControllerBase
    {
        private readonly IPushNotificationService _pushService;
        private readonly ILogger<PushNotificationController> _logger;

        public PushNotificationController(
            IPushNotificationService pushService,
            ILogger<PushNotificationController> logger)
        {
            _pushService = pushService;
            _logger = logger;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDeviceRequest request)
        {
            try
            {
                var userId = GetUserId();
                if (string.IsNullOrEmpty(userId)) return Unauthorized();

                await _pushService.RegisterDeviceAsync(userId, request.Token, request.Platform);

                return Ok(new
                {
                    message = "Device registered for push notifications",
                    platform = request.Platform
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering device for push notifications");
                return StatusCode(500, new { error = "Failed to register device" });
            }
        }

        [HttpPost("unregister")]
        public async Task<IActionResult> Unregister([FromBody] UnregisterDeviceRequest request)
        {
            try
            {
                var userId = GetUserId();
                if (string.IsNullOrEmpty(userId)) return Unauthorized();

                await _pushService.UnregisterDeviceAsync(request.Token);

                return Ok(new { message = "Device unregistered from push notifications" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unregistering device from push notifications");
                return StatusCode(500, new { error = "Failed to unregister device" });
            }
        }

        [HttpGet("status")]
        public IActionResult GetStatus()
        {
            try
            {
                var userId = GetUserId();
                if (string.IsNullOrEmpty(userId)) return Unauthorized();

                var firebaseInitialized = FirebaseAdmin.FirebaseApp.DefaultInstance != null;

                return Ok(new
                {
                    firebaseConfigured = firebaseInitialized,
                    userId = userId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting push notification status");
                return StatusCode(500, new { error = "Failed to get push notification status" });
            }
        }

        private string GetUserId()
        {
            return User.FindFirst("uid")?.Value
                   ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                   ?? string.Empty;
        }
    }

    public class RegisterDeviceRequest
    {
        public string Token { get; set; } = string.Empty;
        public string Platform { get; set; } = "web";
    }

    public class UnregisterDeviceRequest
    {
        public string Token { get; set; } = string.Empty;
    }
}
