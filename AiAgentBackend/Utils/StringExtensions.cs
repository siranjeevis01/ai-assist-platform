// Utils/StringExtensions.cs
namespace AiAgentBackend.Utils
{
    public static class StringExtensions
    {
        public static string Truncate(this string value, int maxLength, string suffix = "...")
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + suffix;
        }

        public static string ToCamelCase(this string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return char.ToLowerInvariant(value[0]) + value.Substring(1);
        }

        public static string ToSnakeCase(this string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            
            return System.Text.RegularExpressions.Regex.Replace(
                value,
                "(?<=[a-z0-9])[A-Z]",
                m => "_" + m.Value.ToLowerInvariant()
            ).ToLowerInvariant();
        }

        public static string MaskEmail(this string email)
        {
            if (string.IsNullOrEmpty(email) || !email.Contains('@')) return email;
            
            var parts = email.Split('@');
            if (parts[0].Length <= 2) return email;
            
            return parts[0].Substring(0, 2) + new string('*', parts[0].Length - 2) + "@" + parts[1];
        }

        public static string MaskPhone(this string phone)
        {
            if (string.IsNullOrEmpty(phone) || phone.Length <= 4) return phone;
            
            return new string('*', phone.Length - 4) + phone.Substring(phone.Length - 4);
        }

        public static bool IsValidEmail(this string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return false;
            
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }

        public static string GenerateRandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, length)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }
    }
}