// Middleware/ErrorHandlingMiddleware.cs
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace AiAgentBackend.Middleware
{
    public class ErrorHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ErrorHandlingMiddleware> _logger;

        public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            var correlationId = Guid.NewGuid().ToString("N")[..8];
            var stopwatch = Stopwatch.StartNew();

            context.Items["CorrelationId"] = correlationId;
            context.Response.Headers["X-Correlation-Id"] = correlationId;

            try
            {
                await _next(context);
                stopwatch.Stop();

                var elapsed = stopwatch.ElapsedMilliseconds;
                var logLevel = elapsed > 5000 ? LogLevel.Warning : LogLevel.Debug;

                _logger.Log(logLevel, "[{CorrelationId}] {Method} {Path} responded {StatusCode} in {Elapsed}ms",
                    correlationId, context.Request.Method, context.Request.Path,
                    context.Response.StatusCode, elapsed);

                if (elapsed > 10000)
                {
                    _logger.LogWarning("[{CorrelationId}] SLOW REQUEST: {Method} {Path} took {Elapsed}ms",
                        correlationId, context.Request.Method, context.Request.Path, elapsed);
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                await HandleExceptionAsync(context, ex, correlationId, stopwatch.ElapsedMilliseconds);
            }
        }

        private Task HandleExceptionAsync(HttpContext context, Exception exception, string correlationId, long elapsedMs)
        {
            _logger.LogError(exception, "[{CorrelationId}] Unhandled exception for {Method} {Path} after {Elapsed}ms",
                correlationId, context.Request.Method, context.Request.Path, elapsedMs);

            var code = HttpStatusCode.InternalServerError;
            var message = "An unexpected error occurred";
            var details = exception.Message;

            switch (exception)
            {
                case UnauthorizedAccessException:
                    code = HttpStatusCode.Unauthorized;
                    message = "Unauthorized access";
                    break;
                case ArgumentException:
                case InvalidOperationException:
                    code = HttpStatusCode.BadRequest;
                    message = exception.Message;
                    break;
                case KeyNotFoundException:
                    code = HttpStatusCode.NotFound;
                    message = "Resource not found";
                    break;
                case DbUpdateException:
                    code = HttpStatusCode.BadRequest;
                    message = "Database update failed";
                    details = "There was an error saving data to the database";
                    break;
                case TimeoutException:
                    code = HttpStatusCode.RequestTimeout;
                    message = "Request timeout";
                    break;
                case OperationCanceledException:
                    code = HttpStatusCode.ServiceUnavailable;
                    message = "Operation was cancelled";
                    break;
            }

            var environment = context.RequestServices.GetRequiredService<IWebHostEnvironment>();
            var result = JsonSerializer.Serialize(new
            {
                error = message,
                details = environment.IsProduction() ? null : details,
                correlationId,
                timestamp = DateTime.UtcNow,
                elapsedMs
            });

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)code;

            return context.Response.WriteAsync(result);
        }
    }
}
