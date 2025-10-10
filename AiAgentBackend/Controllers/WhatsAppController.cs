using AiAgentBackend.Services.Integrations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AiAgentBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class WhatsAppController : ControllerBase
    {
        private readonly IHttpWhatsAppService _whatsAppService;
        private readonly ILogger<WhatsAppController> _logger;

        public WhatsAppController(IHttpWhatsAppService whatsAppService, ILogger<WhatsAppController> logger)
        {
            _whatsAppService = whatsAppService;
            _logger = logger;
        }

        private int GetUserId()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(userIdStr, out var userId) ? userId : 0;
        }

        [HttpGet("qr")]
        public async Task<IActionResult> GetQrCode()
        {
            try
            {
                var userId = GetUserId();
                if (userId == 0) return Unauthorized();

                var qrCode = await _whatsAppService.GetQrCodeAsync(userId);
                if (string.IsNullOrEmpty(qrCode))
                {
                    return Ok(new { message = "No QR code available. Bot may be connected or starting up." });
                }

                return Ok(new { qrCode = $"data:image/png;base64,{qrCode}" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating QR code");
                return StatusCode(500, new { error = "Failed to generate QR code" });
            }
        }

        [HttpPost("connect")]
        public async Task<IActionResult> Connect()
        {
            try
            {
                var userId = GetUserId();
                if (userId == 0) return Unauthorized();

                var success = await _whatsAppService.InitializeConnectionAsync(userId);
                return Ok(new { success, message = "Connection initialized" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing WhatsApp connection");
                return StatusCode(500, new { error = "Failed to initialize connection" });
            }
        }

        [HttpGet("status")]
        public async Task<IActionResult> GetStatus()
        {
            try
            {
                var userId = GetUserId();
                if (userId == 0) return Unauthorized();

                var status = await _whatsAppService.GetStatusAsync(userId);
                return Ok(status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking WhatsApp status");
                return StatusCode(500, new { error = "Failed to check status" });
            }
        }

        [HttpPost("send")]
        public async Task<IActionResult> SendMessage([FromBody] WhatsAppSendMessageRequest request)
        {
            try
            {
                var userId = GetUserId();
                if (userId == 0) return Unauthorized();

                if (string.IsNullOrEmpty(request.Text))
                    return BadRequest(new { error = "Message is required" });

                var success = await _whatsAppService.SendMessageAsync(userId, request.Text);
                
                return Ok(new 
                { 
                    success,
                    message = success ? "Message sent" : "Failed to send message"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending WhatsApp message");
                return StatusCode(500, new { error = "Failed to send message" });
            }
        }

        [HttpGet("test")]
        public async Task<IActionResult> TestConnection()
        {
            try
            {
                var userId = GetUserId();
                if (userId == 0) return Unauthorized();

                var isConnected = await _whatsAppService.CheckConnectionStatusAsync(userId);
                
                if (isConnected)
                {
                    var success = await _whatsAppService.SendMessageAsync(userId, 
                        "🔗 Connection test successful! Your WhatsApp is properly connected to AI Agent.");
                    
                    return Ok(new 
                    { 
                        success,
                        message = "Test message sent successfully"
                    });
                }
                else
                {
                    return BadRequest(new 
                    { 
                        connected = false,
                        message = "WhatsApp not connected. Please scan the QR code first."
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing WhatsApp connection");
                return StatusCode(500, new { error = "Failed to test connection" });
            }
        }
    }

    // Renamed to avoid conflict
    public class WhatsAppSendMessageRequest
    {
        public string Text { get; set; } = string.Empty;
    }
}