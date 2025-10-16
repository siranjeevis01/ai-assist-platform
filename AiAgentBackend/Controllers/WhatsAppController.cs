using AiAgentBackend.Services.Integrations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace AiAgentBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class WhatsAppController : ControllerBase
    {
        private readonly IWhatsAppService _whatsAppService;
        private readonly ILogger<WhatsAppController> _logger;
        private static readonly Dictionary<string, DateTime> _qrCache = new();

        public WhatsAppController(IWhatsAppService whatsAppService, ILogger<WhatsAppController> logger)
        {
            _whatsAppService = whatsAppService;
            _logger = logger;
        }

        [HttpGet("qr")]
        [Produces("application/json")]
        public async Task<IActionResult> GetQrCode()
        {
            try
            {
                var qr = await _whatsAppService.GetQrCodeAsync();
                
                // Enhanced response for Swagger
                var enhancedResponse = new
                {
                    qrCode = qr.QrCode,
                    qrImage = qr.QrImage,
                    message = qr.Message,
                    status = qr.Status,
                    expiresAt = qr.ExpiresAt,
                    instructions = "Scan this QR code with your WhatsApp mobile app",
                    nextSteps = new[] 
                    {
                        "Open WhatsApp on your phone",
                        "Tap Menu → Linked Devices → Link a Device",
                        "Point your phone at this QR code"
                    },
                    connectionStatus = await _whatsAppService.GetStatusAsync()
                };
                
                return Ok(enhancedResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting QR code");
                return StatusCode(500, new { 
                    error = ex.Message,
                    details = "Failed to retrieve QR code"
                });
            }
        }

        [HttpPost("connect")]
        [Produces("application/json")]
        public async Task<IActionResult> Connect()
        {
            try
            {
                _logger.LogInformation("WhatsApp connection initiated via API");
                
                var result = await _whatsAppService.InitializeConnectionAsync();
                
                if (result.Success)
                {
                    var response = new
                    {
                        success = true,
                        message = result.Message,
                        qrCode = result.QrCode,
                        nextStep = result.NextStep,
                        connectionId = Guid.NewGuid().ToString(),
                        timestamp = DateTime.UtcNow,
                        status = await _whatsAppService.GetStatusAsync(),
                        monitoringUrl = "/api/whatsapp/status", // For polling status
                        qrUrl = "/api/whatsapp/qr" // For getting QR code
                    };
                    
                    return Ok(response);
                }
                else
                {
                    return StatusCode(500, new 
                    { 
                        success = false, 
                        error = result.Error,
                        message = result.Message,
                        timestamp = DateTime.UtcNow
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error connecting WhatsApp");
                return StatusCode(500, new { 
                    error = ex.Message,
                    details = "Connection failed. Please try again."
                });
            }
        }

        [HttpGet("status")]
        [Produces("application/json")]
        public async Task<IActionResult> GetStatus()
        {
            try
            {
                var status = await _whatsAppService.GetStatusAsync();
                
                // Enhanced status response
                var enhancedStatus = new
                {
                    isConnected = status.IsConnected,
                    status = status.Status,
                    qrAvailable = status.QrAvailable,
                    timestamp = status.Timestamp,
                    isInitializing = status.IsInitializing,
                    qrGeneratedAt = status.QrGeneratedAt,
                    userFriendlyStatus = GetUserFriendlyStatus(status),
                    actions = GetAvailableActions(status),
                    lastUpdated = DateTime.UtcNow
                };
                
                return Ok(enhancedStatus);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting status");
                return StatusCode(500, new { 
                    error = ex.Message,
                    details = "Failed to retrieve status"
                });
            }
        }

        [HttpPost("send")]
        [Produces("application/json")]
        public async Task<IActionResult> SendMessage([FromBody] WhatsAppSendMessageRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.Text))
                    return BadRequest(new { 
                        error = "Message text is required",
                        field = "text"
                    });

                if (string.IsNullOrEmpty(request.To))
                    return BadRequest(new { 
                        error = "Recipient phone number is required",
                        field = "to",
                        format = "Include country code (e.g., +1234567890)"
                    });

                // Validate WhatsApp connection first
                var status = await _whatsAppService.GetStatusAsync();
                if (!status.IsConnected)
                {
                    return BadRequest(new
                    {
                        error = "WhatsApp is not connected",
                        solution = "Please initialize connection first using POST /api/whatsapp/connect",
                        currentStatus = status.Status
                    });
                }

                var result = await _whatsAppService.SendMessageAsync(request.To, request.Text);
                
                if (result.Success)
                {
                    return Ok(new 
                    { 
                        success = true,
                        message = "Message sent successfully",
                        messageId = result.MessageId,
                        recipient = request.To,
                        timestamp = DateTime.UtcNow,
                        text = request.Text
                    });
                }
                else
                {
                    return StatusCode(500, new 
                    { 
                        success = false,
                        error = result.Error,
                        message = result.Message,
                        recipient = request.To,
                        timestamp = DateTime.UtcNow
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending message to {To}", request.To);
                return StatusCode(500, new { 
                    error = ex.Message,
                    details = $"Failed to send message to {request.To}",
                    recipient = request.To
                });
            }
        }

        [HttpPost("send-to-user/{userId}")]
        [Produces("application/json")]
        public async Task<IActionResult> SendMessageToUser(int userId, [FromBody] WhatsAppSendToUserRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.Text))
                    return BadRequest(new { error = "Message text is required" });

                // Validate WhatsApp connection first
                var status = await _whatsAppService.GetStatusAsync();
                if (!status.IsConnected)
                {
                    return BadRequest(new
                    {
                        error = "WhatsApp is not connected",
                        solution = "Please initialize connection first using POST /api/whatsapp/connect"
                    });
                }

                var result = await _whatsAppService.SendMessageAsync(userId, request.Text);
                
                if (result.Success)
                {
                    return Ok(new 
                    { 
                        success = true,
                        message = "Message sent successfully to user",
                        messageId = result.MessageId,
                        userId = userId,
                        timestamp = DateTime.UtcNow
                    });
                }
                else
                {
                    return StatusCode(500, new 
                    { 
                        success = false,
                        error = result.Error,
                        message = result.Message,
                        userId = userId,
                        timestamp = DateTime.UtcNow
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending message to user {UserId}", userId);
                return StatusCode(500, new { 
                    error = ex.Message,
                    details = $"Failed to send message to user {userId}"
                });
            }
        }

        [HttpPost("disconnect")]
        [Produces("application/json")]
        public async Task<IActionResult> Disconnect()
        {
            try
            {
                await _whatsAppService.DisconnectAsync();
                
                return Ok(new
                {
                    success = true,
                    message = "WhatsApp disconnected successfully",
                    timestamp = DateTime.UtcNow,
                    status = "disconnected"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disconnecting WhatsApp");
                return StatusCode(500, new { 
                    error = ex.Message,
                    details = "Failed to disconnect WhatsApp"
                });
            }
        }

        [HttpGet("test-connection")]
        [Produces("application/json")]
        public async Task<IActionResult> TestConnection()
        {
            try
            {
                var status = await _whatsAppService.GetStatusAsync();
                var isConnected = await _whatsAppService.CheckConnectionStatusAsync();
                
                return Ok(new
                {
                    success = true,
                    status = status,
                    isConnected = isConnected,
                    health = isConnected ? "healthy" : "unhealthy",
                    timestamp = DateTime.UtcNow,
                    details = new
                    {
                        canSendMessages = isConnected,
                        qrAvailable = status.QrAvailable,
                        initializing = status.IsInitializing
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing WhatsApp connection");
                return StatusCode(500, new { 
                    error = ex.Message,
                    health = "unhealthy",
                    timestamp = DateTime.UtcNow
                });
            }
        }

        // Helper methods
        private string GetUserFriendlyStatus(WhatsAppStatus status)
        {
            return status.Status.ToLower() switch
            {
                "connected" => "✅ Connected to WhatsApp",
                "connecting" => "🔄 Connecting to WhatsApp...",
                "initializing" => "⚡ Initializing WhatsApp Web...",
                "disconnected" => "❌ Disconnected from WhatsApp",
                "qr_available" => "📱 QR Code Available - Scan to Connect",
                _ => "❓ Unknown Status"
            };
        }

        private string[] GetAvailableActions(WhatsAppStatus status)
        {
            var actions = new List<string>();
            
            if (status.Status == "disconnected")
            {
                actions.Add("POST /api/whatsapp/connect - Initialize connection");
            }
            
            if (status.QrAvailable)
            {
                actions.Add("GET /api/whatsapp/qr - Get QR code");
            }
            
            if (status.IsConnected)
            {
                actions.Add("POST /api/whatsapp/send - Send message");
                actions.Add("POST /api/whatsapp/disconnect - Disconnect");
            }
            
            actions.Add("GET /api/whatsapp/status - Check status");
            actions.Add("GET /api/whatsapp/test-connection - Test connection");
            
            return actions.ToArray();
        }
    }

    public class WhatsAppSendMessageRequest
    {
        public string To { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        
        [System.Text.Json.Serialization.JsonExtensionData]
        public Dictionary<string, object>? ExtensionData { get; set; }
    }

    public class WhatsAppSendToUserRequest
    {
        public string Text { get; set; } = string.Empty;
        
        [System.Text.Json.Serialization.JsonExtensionData]
        public Dictionary<string, object>? ExtensionData { get; set; }
    }
}