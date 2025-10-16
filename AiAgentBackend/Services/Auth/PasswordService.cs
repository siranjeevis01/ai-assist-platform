// Services/Auth/PasswordService.cs
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.RegularExpressions;

namespace AiAgentBackend.Services.Auth
{
    public interface IPasswordService
    {
        string HashPassword(string password);
        bool VerifyPassword(string password, string hash);
        PasswordValidationResult ValidatePassword(string password);
    }

    public class PasswordService : IPasswordService
    {
        private readonly PasswordOptions _options;
        private readonly ILogger<PasswordService> _logger;

        public PasswordService(IOptions<PasswordOptions> options, ILogger<PasswordService> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public string HashPassword(string password)
        {
            using var algorithm = new Rfc2898DeriveBytes(
                password,
                _options.SaltSize,
                _options.Iterations,
                HashAlgorithmName.SHA256);
            
            var salt = Convert.ToBase64String(algorithm.Salt);
            var hash = Convert.ToBase64String(algorithm.GetBytes(_options.KeySize));
            
            return $"{_options.Iterations}.{salt}.{hash}";
        }

        public bool VerifyPassword(string password, string hash)
        {
            try
            {
                var parts = hash.Split('.', 3);
                if (parts.Length != 3)
                    return false;

                var iterations = int.Parse(parts[0]);
                var salt = Convert.FromBase64String(parts[1]);
                var key = Convert.FromBase64String(parts[2]);

                using var algorithm = new Rfc2898DeriveBytes(
                    password,
                    salt,
                    iterations,
                    HashAlgorithmName.SHA256);
                
                var keyToCheck = algorithm.GetBytes(_options.KeySize);
                return keyToCheck.SequenceEqual(key);
            }
            catch
            {
                return false;
            }
        }

        public PasswordValidationResult ValidatePassword(string password)
        {
            var result = new PasswordValidationResult { IsValid = true };

            if (string.IsNullOrWhiteSpace(password))
            {
                result.IsValid = false;
                result.Errors.Add("Password cannot be empty");
                return result;
            }

            // Development mode: Allow simpler passwords - skip common password check
            if (_options.DevelopmentMode)
            {
                if (password.Length < 6)
                {
                    result.IsValid = false;
                    result.Errors.Add("Password must be at least 6 characters in development mode");
                }
                return result;
            }

            // Production mode: Strict validation
            if (password.Length < _options.MinimumLength)
            {
                result.IsValid = false;
                result.Errors.Add($"Password must be at least {_options.MinimumLength} characters");
            }

            if (!Regex.IsMatch(password, @"[A-Z]") && _options.RequireUppercase)
            {
                result.IsValid = false;
                result.Errors.Add("Password must contain at least one uppercase letter");
            }

            if (!Regex.IsMatch(password, @"[a-z]") && _options.RequireLowercase)
            {
                result.IsValid = false;
                result.Errors.Add("Password must contain at least one lowercase letter");
            }

            if (!Regex.IsMatch(password, @"[0-9]") && _options.RequireDigit)
            {
                result.IsValid = false;
                result.Errors.Add("Password must contain at least one number");
            }

            if (!Regex.IsMatch(password, @"[!@#$%^&*()_+\-=\[\]{};':""\\|,.<>\/?]") && _options.RequireSpecialCharacter)
            {
                result.IsValid = false;
                result.Errors.Add("Password must contain at least one special character");
            }

            // Check for common patterns (only in production)
            if (!_options.DevelopmentMode && IsCommonPassword(password))
            {
                result.IsValid = false;
                result.Errors.Add("This password is too common. Please choose a more unique password.");
            }

            return result;
        }

        private bool IsCommonPassword(string password)
        {
            var commonPasswords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "password", "123456", "password123", "admin", "qwerty",
                "siran123", "siran@123", "siran#123", "siran_123"
            };

            return commonPasswords.Contains(password) || 
                   commonPasswords.Any(cp => password.ToLower().Contains(cp));
        }
    }

    public class PasswordValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    public class PasswordOptions
    {
        public int SaltSize { get; set; } = 16;
        public int KeySize { get; set; } = 32;
        public int Iterations { get; set; } = 10000;
        public int MinimumLength { get; set; } = 8;
        public bool RequireUppercase { get; set; } = true;
        public bool RequireLowercase { get; set; } = true;
        public bool RequireDigit { get; set; } = true;
        public bool RequireSpecialCharacter { get; set; } = true;
        public bool DevelopmentMode { get; set; } = false;
    }
}