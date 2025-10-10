using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AiAgentBackend.Configuration;
using AiAgentBackend.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;

namespace AiAgentBackend.Services.Auth
{
    public class JwtTokenService : IJwtTokenService
    {
        private readonly JwtOptions _opts;
        private readonly ILogger<JwtTokenService> _logger;

        public JwtTokenService(IOptions<JwtOptions> opts, ILogger<JwtTokenService> logger)
        {
            _opts = opts.Value;
            _logger = logger;
        }

        public string CreateToken(User user)
        {
            try
            {
                var claims = new List<Claim>
                {
                    new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                    new Claim(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
                    new Claim("uid", user.Id.ToString()),
                    new Claim(ClaimTypes.Name, user.Name ?? string.Empty),
                    new Claim(ClaimTypes.Role, user.Role ?? "User"),
                    new Claim("timezone", user.Timezone ?? "UTC")
                };

                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opts.Key ?? throw new InvalidOperationException("JWT Key is not configured")));
                var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

                var token = new JwtSecurityToken(
                    issuer: _opts.Issuer,
                    audience: _opts.Audience,
                    claims: claims,
                    expires: DateTime.UtcNow.AddMinutes(_opts.AccessTokenMinutes > 0 ? _opts.AccessTokenMinutes : 60),
                    signingCredentials: creds
                );

                return new JwtSecurityTokenHandler().WriteToken(token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating JWT token");
                throw;
            }
        }

        public string HashPassword(string password)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(bytes);
        }

        public bool VerifyPassword(string password, string hash)
        {
            return HashPassword(password) == hash;
        }

        public ClaimsPrincipal? ValidateToken(string token)
        {
            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var key = Encoding.UTF8.GetBytes(_opts.Key ?? throw new InvalidOperationException("JWT Key is not configured"));
                
                var validationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = true,
                    ValidIssuer = _opts.Issuer,
                    ValidateAudience = true,
                    ValidAudience = _opts.Audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };

                return tokenHandler.ValidateToken(token, validationParameters, out _);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "JWT token validation failed");
                return null;
            }
        }
    }
}