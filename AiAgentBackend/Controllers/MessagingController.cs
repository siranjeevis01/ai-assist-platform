// Controllers/MessagingController.cs
using AiAgentBackend.Configuration;
using AiAgentBackend.Services.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AiAgentBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class MessagingController : ControllerBase
    {
        private readonly IMessagingService _messagingService;
        private readonly IOptions<MessagingOptions> _options;
        private readonly ILogger<MessagingController> _logger;

        public MessagingController(
            IMessagingService messagingService,
            IOptions<MessagingOptions> options,
            ILogger<MessagingController> logger)
        {
            _messagingService = messagingService;
            _options = options;
            _logger = logger;
        }

        [HttpGet("status")]
        public async Task<IActionResult> GetStatus()
        {
            try
            {
                var status = await _messagingService.GetStatusAsync();
                return Ok(status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting messaging status");
                return StatusCode(500, new { error = "Failed to get messaging status" });
            }
        }

        [HttpPost("telegram/initialize")]
        public async Task<IActionResult> InitializeTelegram()
        {
            try
            {
                if (!_options.Value.Telegram.Enabled)
                {
                    return BadRequest(new { error = "Telegram integration is disabled" });
                }

                if (string.IsNullOrEmpty(_options.Value.Telegram.BotToken) ||
                    _options.Value.Telegram.BotToken.Contains("YOUR_") ||
                    _options.Value.Telegram.BotToken.StartsWith("${"))
                {
                    return BadRequest(new { error = "Telegram bot token not configured" });
                }

                var success = await _messagingService.InitializeTelegramAsync();
                
                if (success)
                {
                    return Ok(new { message = "Telegram bot initialized successfully" });
                }
                else
                {
                    return StatusCode(500, new { error = "Failed to initialize Telegram bot" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing Telegram");
                return StatusCode(500, new { error = "Failed to initialize Telegram" });
            }
        }

        [HttpPost("whatsapp/initialize")]
        public async Task<IActionResult> InitializeWhatsApp()
        {
            try
            {
                if (!_options.Value.WhatsApp.Enabled)
                {
                    return BadRequest(new { error = "WhatsApp integration is disabled" });
                }

                if (string.IsNullOrEmpty(_options.Value.WhatsApp.AccessToken) ||
                    string.IsNullOrEmpty(_options.Value.WhatsApp.PhoneNumberId) ||
                    _options.Value.WhatsApp.AccessToken.Contains("YOUR_") ||
                    _options.Value.WhatsApp.AccessToken.StartsWith("${"))
                {
                    return BadRequest(new { 
                        error = "WhatsApp credentials not fully configured",
                        required = new[] { "AccessToken", "PhoneNumberId" }
                    });
                }

                var success = await _messagingService.InitializeWhatsAppAsync();
                
                if (success)
                {
                    return Ok(new { 
                        message = "WhatsApp Cloud API initialized successfully",
                        phoneNumberId = _options.Value.WhatsApp.PhoneNumberId
                    });
                }
                else
                {
                    return StatusCode(500, new { error = "Failed to initialize WhatsApp Cloud API" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing WhatsApp");
                return StatusCode(500, new { error = "Failed to initialize WhatsApp" });
            }
        }

        [HttpPost("preference")]
        public async Task<IActionResult> SetMessagingPreference([FromBody] SetPreferenceRequest request)
        {
            try
            {
                var userId = GetUserId();
                if (userId == 0) return Unauthorized();

                if (request.Platform != "telegram" && request.Platform != "whatsapp")
                {
                    return BadRequest(new { error = "Platform must be 'telegram' or 'whatsapp'" });
                }

                await _messagingService.SetUserMessagingPreferenceAsync(userId, request.Platform);
                
                return Ok(new { 
                    message = $"Messaging preference set to {request.Platform}",
                    platform = request.Platform,
                    userId = userId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting messaging preference");
                return StatusCode(500, new { error = "Failed to set messaging preference" });
            }
        }

        [HttpGet("preference")]
        public async Task<IActionResult> GetMessagingPreference()
        {
            try
            {
                var userId = GetUserId();
                if (userId == 0) return Unauthorized();

                var preference = await _messagingService.GetUserMessagingPreferenceAsync(userId);
                
                return Ok(new { 
                    platform = preference,
                    userId = userId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting messaging preference");
                return StatusCode(500, new { error = "Failed to get messaging preference" });
            }
        }

        [HttpPost("send-test")]
        public async Task<IActionResult> SendTestMessage([FromBody] SendTestMessageRequest request)
        {
            try
            {
                var userId = GetUserId();
                if (userId == 0) return Unauthorized();

                var result = await _messagingService.SendMessageAsync(userId, request.Message);
                
                if (result.Success)
                {
                    return Ok(new { 
                        message = "Test message sent successfully",
                        platform = result.Platform,
                        messageId = result.MessageId,
                        timestamp = DateTime.UtcNow
                    });
                }
                else
                {
                    return StatusCode(500, new { 
                        error = result.Error,
                        message = result.Message,
                        platform = result.Platform
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending test message");
                return StatusCode(500, new { error = "Failed to send test message" });
            }
        }

        [HttpPost("register-platform")]
        public async Task<IActionResult> RegisterPlatform([FromBody] RegisterPlatformRequest request)
        {
            try
            {
                var userId = GetUserId();
                if (userId == 0) return Unauthorized();

                // Safe cast with null check
                if (_messagingService is MessagingService enhancedService)
                {
                    var success = await enhancedService.RegisterUserPlatformAsync(
                        userId, request.Platform, request.PlatformUserId, request.ChatId);

                    if (success)
                    {
                        return Ok(new { 
                            message = $"Successfully registered on {request.Platform}",
                            platform = request.Platform,
                            platformUserId = request.PlatformUserId
                        });
                    }
                    else
                    {
                        return StatusCode(500, new { error = "Failed to register platform" });
                    }
                }
                else
                {
                    return StatusCode(500, new { error = "Enhanced messaging service not available" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering platform");
                return StatusCode(500, new { error = "Failed to register platform" });
            }
        }

        [HttpGet("user-platforms")]
        public async Task<IActionResult> GetUserPlatforms()
        {
            try
            {
                var userId = GetUserId();
                if (userId == 0) return Unauthorized();

                // Safe cast with null check
                if (_messagingService is MessagingService enhancedService)
                {
                    var platforms = await enhancedService.GetUserPlatformsAsync(userId);
                    
                    return Ok(new { 
                        platforms = platforms,
                        userId = userId
                    });
                }
                else
                {
                    return StatusCode(500, new { error = "Enhanced messaging service not available" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user platforms");
                return StatusCode(500, new { error = "Failed to get user platforms" });
            }
        }

        [HttpPost("disconnect/{platform}")]
        public async Task<IActionResult> DisconnectPlatform(string platform)
        {
            try
            {
                if (platform != "telegram" && platform != "whatsapp")
                    return BadRequest(new { error = "Platform must be 'telegram' or 'whatsapp'" });

                await _messagingService.DisconnectAsync(platform);
                return Ok(new { message = $"{platform} disconnected successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disconnecting {Platform}", platform);
                return StatusCode(500, new { error = $"Failed to disconnect {platform}" });
            }
        }

        private int GetUserId()
        {
            var userIdStr = User.FindFirst("uid")?.Value ?? User.FindFirst("sub")?.Value;
            return int.TryParse(userIdStr, out var userId) ? userId : 0;
        }
    }

    public class SetPreferenceRequest
    {
        public string Platform { get; set; } = string.Empty;
    }

    public class SendTestMessageRequest
    {
        public string Message { get; set; } = string.Empty;
    }

    public class RegisterPlatformRequest
    {
        public string Platform { get; set; } = string.Empty;
        public string PlatformUserId { get; set; } = string.Empty;
        public string? ChatId { get; set; }
    }
}