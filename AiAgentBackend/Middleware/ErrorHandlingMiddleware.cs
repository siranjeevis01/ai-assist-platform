// Middleware/ErrorHandlingMiddleware.cs
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
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        private Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            _logger.LogError(exception, "Unhandled exception occurred for request: {Method} {Path}", 
                context.Request.Method, context.Request.Path);

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
            }

            // Don't expose stack traces in production
            var environment = context.RequestServices.GetRequiredService<IWebHostEnvironment>();
            if (environment.IsProduction())
            {
                details = null;
            }

            var result = JsonSerializer.Serialize(new { error = message, details });
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)code;

            return context.Response.WriteAsync(result);
        }
    }
}
