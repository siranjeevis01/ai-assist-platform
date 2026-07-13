// Controllers/AuthController.cs
using Microsoft.AspNetCore.Authorization;
using AiAgentBackend.Data;
using AiAgentBackend.DTOs.Auth;
using AiAgentBackend.Models;
using AiAgentBackend.Services.Auth;
using AiAgentBackend.Services.Integrations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;

namespace AiAgentBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IJwtTokenService _jwt;
        private readonly IPasswordService _passwordService;
        private readonly IEmailService _emailService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            ApplicationDbContext db, 
            IJwtTokenService jwt,
            IPasswordService passwordService,
            IEmailService emailService, 
            ILogger<AuthController> logger)
        {
            _db = db;
            _jwt = jwt;
            _passwordService = passwordService;
            _emailService = emailService;
            _logger = logger;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest req)
        {
            try
            {
                // Validate password
                var passwordValidation = _passwordService.ValidatePassword(req.Password);
                if (!passwordValidation.IsValid)
                {
                    return BadRequest(new { 
                        error = "Password validation failed", 
                        details = passwordValidation.Errors 
                    });
                }

                if (await _db.Users.AnyAsync(u => u.Email == req.Email))
                    return Conflict(new { error = "Email already registered" });

                var user = new User
                {
                    Email = req.Email,
                    Name = req.Name,
                    PasswordHash = _passwordService.HashPassword(req.Password),
                    CreatedAt = DateTime.UtcNow,
                    Timezone = "UTC",
                    Role = "User"
                };

                _db.Users.Add(user);
                await _db.SaveChangesAsync();

                // Create default preferences
                var preference = new Preference
                {
                    UserId = user.Id,
                    WorkHours = "09:00-18:00",
                    DefaultDurationMinutes = 30,
                    DefaultBoard = "default",
                    DefaultList = "To Do",
                    ReminderPolicy = "30m-before"
                };
                _db.Preferences.Add(preference);
                
                // Log the registration
                _db.AuditLogs.Add(new AuditLog
                {
                    UserId = user.Id,
                    Entity = "User",
                    Action = "Register",
                    Timestamp = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();

                var token = _jwt.CreateToken(user);
                
                var refreshToken = Guid.NewGuid().ToString();
                _db.RefreshTokens.Add(new RefreshToken
                {
                    UserId = user.Id,
                    Token = refreshToken,
                    ExpiresAt = DateTime.UtcNow.AddDays(7)
                });
                await _db.SaveChangesAsync();

                _logger.LogInformation("User registered: {Email}", user.Email);

                return Ok(new AuthResponse 
                { 
                    Token = token, 
                    RefreshToken = refreshToken,
                    Email = user.Email, 
                    Name = user.Name 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during user registration");
                return StatusCode(500, new { error = "Internal server error during registration" });
            }
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest req)
        {
            try
            {
                var user = await _db.Users
                    .Include(u => u.Preference)
                    .FirstOrDefaultAsync(u => u.Email == req.Email);
                    
                if (user == null || user.ExternalAuthOnly || !_passwordService.VerifyPassword(req.Password, user.PasswordHash))
                {
                    _logger.LogWarning("Failed login attempt for email: {Email}", req.Email);
                    return Unauthorized(new { error = "Invalid credentials" });
                }

                var jwt = _jwt.CreateToken(user);

                var refreshToken = Guid.NewGuid().ToString();
                _db.RefreshTokens.Add(new RefreshToken
                {
                    UserId = user.Id,
                    Token = refreshToken,
                    ExpiresAt = DateTime.UtcNow.AddDays(7)
                });
                
                // Log the login
                _db.AuditLogs.Add(new AuditLog
                {
                    UserId = user.Id,
                    Entity = "User",
                    Action = "Login",
                    Timestamp = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();

                _logger.LogInformation("User logged in: {Email}", user.Email);

                return Ok(new 
                {
                    Token = jwt,
                    RefreshToken = refreshToken,
                    Email = user.Email,
                    Name = user.Name,
                    HasGoogle = await _db.ProviderTokens.AnyAsync(pt => pt.UserId == user.Id && pt.Provider == "Google"),
                    HasTrello = await _db.ProviderTokens.AnyAsync(pt => pt.UserId == user.Id && pt.Provider == "Trello")
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during user login");
                return StatusCode(500, new { error = "Internal server error during login" });
            }
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest req)
        {
            try
            {
                var tokenEntry = await _db.RefreshTokens
                    .Include(t => t.User)
                    .FirstOrDefaultAsync(t => t.Token == req.RefreshToken && 
                                            !t.IsRevoked && 
                                            t.ExpiresAt > DateTime.UtcNow);

                if (tokenEntry == null) 
                    return Unauthorized(new { error = "Invalid or expired refresh token" });

                var newJwt = _jwt.CreateToken(tokenEntry.User!);

                // Revoke old refresh token
                tokenEntry.IsRevoked = true;

                // Create new refresh token
                var newRefreshToken = Guid.NewGuid().ToString();
                _db.RefreshTokens.Add(new RefreshToken
                {
                    UserId = tokenEntry.UserId,
                    Token = newRefreshToken,
                    ExpiresAt = DateTime.UtcNow.AddDays(7)
                });
                
                _db.AuditLogs.Add(new AuditLog
                {
                    UserId = tokenEntry.UserId,
                    Entity = "Auth",
                    Action = "RefreshToken",
                    Timestamp = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();

                return Ok(new { Token = newJwt, RefreshToken = newRefreshToken });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during token refresh");
                return StatusCode(500, new { error = "Internal server error during token refresh" });
            }
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest req)
        {
            try
            {
                var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == req.Email);
                if (user == null) 
                {
                    // Don't reveal whether email exists or not
                    return Ok(new { message = "If the email exists, a reset link has been sent" });
                }

                var otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
                user.PasswordResetToken = otp;
                user.PasswordResetExpiry = DateTime.UtcNow.AddHours(1);
                
                // Log the password reset request
                _db.AuditLogs.Add(new AuditLog
                {
                    UserId = user.Id,
                    Entity = "Auth",
                    Action = "ForgotPassword",
                    Timestamp = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();

                try
                {
                    await _emailService.SendResetPasswordAsync(user.Email, otp);
                    _logger.LogInformation("Password reset OTP sent to: {Email}", user.Email);
                }
                catch (Exception emailEx)
                {
                    _logger.LogError(emailEx, "Failed to send password reset email to {Email} — OTP is still valid", user.Email);
                }

                return Ok(new { message = "If the email exists, a reset link has been sent" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during forgot password process");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest req)
        {
            try
            {
                var user = await _db.Users.FirstOrDefaultAsync(u =>
                    u.Email == req.Email &&
                    u.PasswordResetToken == req.Token &&
                    u.PasswordResetExpiry > DateTime.UtcNow);

                if (user == null) 
                    return BadRequest(new { error = "Invalid or expired token" });

                user.PasswordHash = _passwordService.HashPassword(req.NewPassword);
                user.PasswordResetToken = null;
                user.PasswordResetExpiry = null;
                
                // Revoke all refresh tokens for security
                var refreshTokens = await _db.RefreshTokens
                    .Where(rt => rt.UserId == user.Id)
                    .ToListAsync();
                    
                foreach (var token in refreshTokens)
                {
                    token.IsRevoked = true;
                }
                
                // Log the password reset
                _db.AuditLogs.Add(new AuditLog
                {
                    UserId = user.Id,
                    Entity = "Auth",
                    Action = "ResetPassword",
                    Timestamp = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();

                _logger.LogInformation("Password reset successfully for: {Email}", user.Email);

                return Ok(new { message = "Password reset successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during password reset");
                return StatusCode(500, new { error = "Internal server error during password reset" });
            }
        }

        [HttpPost("logout")]
        [Authorize]
        public async Task<IActionResult> Logout()
        {
            try
            {
                var userIdClaim = User.FindFirst("uid")?.Value ?? User.FindFirst("sub")?.Value;
                if (string.IsNullOrEmpty(userIdClaim))
                {
                    return Unauthorized(new { error = "User ID claim is missing" });
                }
                
                var userId = int.Parse(userIdClaim);
                
                // Revoke all refresh tokens
                var refreshTokens = await _db.RefreshTokens
                    .Where(rt => rt.UserId == userId && !rt.IsRevoked)
                    .ToListAsync();
                    
                foreach (var token in refreshTokens)
                {
                    token.IsRevoked = true;
                }
                
                // Log the logout
                _db.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    Entity = "Auth",
                    Action = "Logout",
                    Timestamp = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();

                _logger.LogInformation("User logged out: {UserId}", userId);

                return Ok(new { message = "Logged out successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during logout");
                return StatusCode(500, new { error = "Internal server error during logout" });
            }
        }
    }

    public class RefreshTokenRequest
    {
        public string RefreshToken { get; set; } = string.Empty;
    }

    public class ForgotPasswordRequest
    {
        public string Email { get; set; } = string.Empty;
    }
}