// Utils/DateTimeExtensions.cs
namespace AiAgentBackend.Utils
{
    public static class DateTimeExtensions
    {
        public static DateTime ToUtc(this DateTime dt)
        {
            return dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        }

        public static DateTime ToUtc(this DateTime dt, string timezone)
        {
            try
            {
                var timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(timezone);
                return TimeZoneInfo.ConvertTimeToUtc(dt, timeZoneInfo);
            }
            catch
            {
                return dt.ToUtc();
            }
        }

        public static DateTime ToLocalTime(this DateTime dateTime, string timezone = "UTC")
        {
            try
            {
                if (timezone == "UTC") 
                    return dateTime.ToLocalTime();
                
                var timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(timezone);
                return TimeZoneInfo.ConvertTimeFromUtc(dateTime, timeZoneInfo);
            }
            catch
            {
                return dateTime.ToLocalTime();
            }
        }
        
        public static DateTime FromUtc(this DateTime dt, string timezone)
        {
            try
            {
                var timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(timezone);
                return TimeZoneInfo.ConvertTimeFromUtc(dt, timeZoneInfo);
            }
            catch
            {
                return dt.ToLocalTime();
            }
        }

        public static DateTime StartOfWeek(this DateTime dt, DayOfWeek startOfWeek = DayOfWeek.Monday)
        {
            int diff = (7 + (dt.DayOfWeek - startOfWeek)) % 7;
            return dt.AddDays(-1 * diff).Date;
        }

        public static DateTime EndOfWeek(this DateTime dt, DayOfWeek startOfWeek = DayOfWeek.Monday)
        {
            return dt.StartOfWeek(startOfWeek).AddDays(6).AddHours(23).AddMinutes(59).AddSeconds(59);
        }

        public static DateTime StartOfMonth(this DateTime dt)
        {
            return new DateTime(dt.Year, dt.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        }

        public static DateTime EndOfMonth(this DateTime dt)
        {
            return new DateTime(dt.Year, dt.Month, DateTime.DaysInMonth(dt.Year, dt.Month), 23, 59, 59, DateTimeKind.Utc);
        }

        public static string ToRelativeTime(this DateTime dt)
        {
            var span = DateTime.UtcNow - dt;
            
            if (span.TotalSeconds < 60) return "just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} minutes ago";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours} hours ago";
            if (span.TotalDays < 30) return $"{(int)span.TotalDays} days ago";
            
            return dt.ToString("MMM dd, yyyy");
        }
    }
}