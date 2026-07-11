using Microsoft.Extensions.Caching.Memory;

namespace AiAgentBackend.Middleware
{
    public class UserRateLimitMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IMemoryCache _cache;
        private readonly ILogger<UserRateLimitMiddleware> _logger;

        private static readonly Dictionary<string, (int Limit, int WindowSeconds)> Limits = new()
        {
            ["/api/Messages/send"] = (20, 60),
            ["/api/Gmail"] = (30, 60),
            ["/api/Google"] = (60, 60),
            ["/api/Calendar"] = (30, 60),
            ["/api/Trello"] = (30, 60),
            ["/api/Voice"] = (10, 60),
            ["/api/Messaging/status"] = (30, 60),
            ["default"] = (60, 60)
        };

        public UserRateLimitMiddleware(RequestDelegate next, IMemoryCache cache, ILogger<UserRateLimitMiddleware> logger)
        {
            _next = next;
            _cache = cache;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (!context.Request.Path.StartsWithSegments("/api"))
            {
                await _next(context);
                return;
            }

            var userId = context.User?.FindFirst("uid")?.Value
                      ?? context.User?.FindFirst("sub")?.Value
                      ?? context.Connection.RemoteIpAddress?.ToString()
                      ?? "anonymous";

            var path = context.Request.Path.Value ?? "";
            var (limit, windowSeconds) = GetLimit(path);
            var cacheKey = $"rl:{userId}:{GetPathGroup(path)}";

            var entry = _cache.GetOrCreate(cacheKey, e =>
            {
                e.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(windowSeconds);
                return new RateLimitEntry { Count = 0, WindowStart = DateTime.UtcNow };
            });

            lock (entry)
            {
                entry.Count++;
                if (entry.Count > limit)
                {
                    _logger.LogWarning("Rate limit exceeded for user {UserId} on {Path}", userId, path);
                    context.Response.StatusCode = 429;
                    context.Response.Headers["Retry-After"] = windowSeconds.ToString();
                    context.Response.Headers["X-RateLimit-Limit"] = limit.ToString();
                    context.Response.Headers["X-RateLimit-Remaining"] = "0";
                    context.Response.ContentType = "application/json";
                    context.Response.WriteAsync($"{{\"error\":\"Rate limit exceeded\",\"retryAfter\":{windowSeconds},\"limit\":{limit}}}");
                    return;
                }
            }

            context.Response.Headers["X-RateLimit-Limit"] = limit.ToString();
            context.Response.Headers["X-RateLimit-Remaining"] = (limit - entry.Count).ToString();

            await _next(context);
        }

        private (int Limit, int WindowSeconds) GetLimit(string path)
        {
            foreach (var kvp in Limits)
            {
                if (kvp.Key != "default" && path.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
            }
            return Limits["default"];
        }

        private static string GetPathGroup(string path)
        {
            if (path.StartsWith("/api/Messages")) return "messages";
            if (path.StartsWith("/api/Gmail")) return "gmail";
            if (path.StartsWith("/api/Google")) return "google";
            if (path.StartsWith("/api/Voice")) return "voice";
            return "general";
        }
    }

    internal class RateLimitEntry
    {
        public int Count { get; set; }
        public DateTime WindowStart { get; set; }
    }
}
