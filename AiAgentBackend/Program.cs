// Program.cs 
using AiAgentBackend.Hubs;
using Hangfire.Dashboard;
using System.Text;
using AiAgentBackend.Configuration;
using AiAgentBackend.Middleware;
using AiAgentBackend.Data;
using AiAgentBackend.Services.Auth;
using AiAgentBackend.Services.Integrations;
using AiAgentBackend.Services.NLP;
using AiAgentBackend.Services.Orchestration;
using Hangfire;
using Hangfire.MySql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using AiAgentBackend.Jobs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.OpenApi.Models;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using AiAgentBackend.Services.Messaging;
using Microsoft.Extensions.Primitives; // Add this for StringValues

// Load .env file if present (for local development)
try { DotNetEnv.Env.Load(); } catch { }

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel for HTTP only on port 5000
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.AddServerHeader = false;
    serverOptions.ListenAnyIP(5000);
});

// Serilog
builder.Host.UseSerilog((context, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day);
});

// Get environment-specific connection string
var connectionString = EnvironmentHelper.GetConnectionString(builder.Configuration);

// DbContext (MySQL) - FIXED SYNTAX
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString), 
    mysqlOptions => 
    {
        mysqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(30), // FIXED: Use colon, not equals
            errorNumbersToAdd: null);
        mysqlOptions.CommandTimeout(60);
    });
    
    if (EnvironmentHelper.IsDevelopment)
    {
        options.EnableSensitiveDataLogging();
        options.EnableDetailedErrors();
    }
});

// Hangfire
builder.Services.AddHangfire(config =>
{
    config.UseSimpleAssemblyNameTypeSerializer()
          .UseRecommendedSerializerSettings()
          .UseStorage(new MySqlStorage(connectionString, new MySqlStorageOptions
          {
              QueuePollInterval = TimeSpan.FromSeconds(30),
              JobExpirationCheckInterval = TimeSpan.FromHours(1),
              CountersAggregateInterval = TimeSpan.FromMinutes(5),
              PrepareSchemaIfNecessary = true,
              DashboardJobListLimit = 50000,
              TransactionTimeout = TimeSpan.FromMinutes(1),
          }));
});

builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = Math.Max(Environment.ProcessorCount, 4);
    options.Queues = new[] { "critical", "default", "low" };
    options.SchedulePollingInterval = TimeSpan.FromSeconds(15);
    options.ServerCheckInterval = TimeSpan.FromSeconds(30);
    options.ServerTimeout = TimeSpan.FromMinutes(5);
    options.HeartbeatInterval = TimeSpan.FromSeconds(30);
});

// Options
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<MessagingOptions>(builder.Configuration.GetSection("Messaging"));
builder.Services.Configure<GoogleOptions>(builder.Configuration.GetSection("Google"));
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.Configure<TrelloOptions>(builder.Configuration.GetSection("Trello"));
builder.Services.Configure<OpenAIOptions>(builder.Configuration.GetSection("OpenAI"));

// Resolve ${VAR_NAME} placeholders from environment variables
builder.Services.PostConfigure<MessagingOptions>(options =>
{
    options.Telegram.BotToken = EnvironmentHelper.ResolveEnvPlaceholder(options.Telegram.BotToken);
    options.WhatsApp.AccessToken = EnvironmentHelper.ResolveEnvPlaceholder(options.WhatsApp.AccessToken);
    options.WhatsApp.PhoneNumberId = EnvironmentHelper.ResolveEnvPlaceholder(options.WhatsApp.PhoneNumberId);
    options.WhatsApp.VerifyToken = EnvironmentHelper.ResolveEnvPlaceholder(options.WhatsApp.VerifyToken);
    options.WhatsApp.BusinessAccountId = EnvironmentHelper.ResolveEnvPlaceholder(options.WhatsApp.BusinessAccountId);
});

builder.Services.PostConfigure<OpenAIOptions>(options =>
{
    options.ApiKey = EnvironmentHelper.ResolveEnvPlaceholder(options.ApiKey);
});

// Auth / JWT
var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
var jwtKey = jwtOptions.Key ?? "super-secure-32-char-key-1234567890123456";
var jwtIssuer = jwtOptions.Issuer ?? "AiAgentBackend";
var jwtAudience = jwtOptions.Audience ?? "AiAgentUsers";

builder.Services.AddAuthentication(opt =>
{
    opt.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    opt.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(5)
    };

    // SignalR JWT support
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hub"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});

// HttpClient support
builder.Services.AddHttpClient();

// Rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("api", limiterOptions =>
    {
        limiterOptions.PermitLimit = 60;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueLimit = 10;
    });
    options.AddFixedWindowLimiter("auth", limiterOptions =>
    {
        limiterOptions.PermitLimit = 10;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueLimit = 0;
    });
});

// Environment-specific CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        if (EnvironmentHelper.IsDevelopment)
        {
            policy.WithOrigins("http://localhost:3000", "http://localhost:5000", "https://localhost:5001", "http://localhost:4200")
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        }
        else
        {
            var allowedOrigins = Environment.GetEnvironmentVariable("CORS_ORIGINS") 
                ?? "https://ai-agent-frontend.onrender.com,https://aiagent-siranjeevis01.web.app,https://aiagent-siranjeevis01.firebaseapp.com";
            policy.WithOrigins(allowedOrigins.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        }
    });
});

// Core Services
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IPasswordService, PasswordService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<INlpService, IntelligentNlpService>();
builder.Services.AddScoped<NlpService>(); // Keep as fallback

// Integration Services
builder.Services.AddScoped<IGoogleCalendarService, GoogleCalendarService>();
builder.Services.AddScoped<ITrelloService, TrelloService>();
builder.Services.AddScoped<IConversationStateService, ConversationStateService>();
builder.Services.AddScoped<IGmailService, GmailService>();
builder.Services.AddScoped<IProactiveNotificationService, ProactiveNotificationService>();

// Messaging Services (platform-specific)
builder.Services.AddScoped<ITelegramService, TelegramService>();
builder.Services.AddScoped<IWhatsAppCloudService, WhatsAppCloudService>();

// Unified Messaging Service (depends on platform services)
builder.Services.AddScoped<IMessagingService, MessagingService>();

// Command Orchestrator (depends on messaging)
builder.Services.AddScoped<ICommandOrchestrator, CommandOrchestrator>();

// Background Jobs
builder.Services.AddScoped<ReminderJob>();
builder.Services.AddScoped<GmailPollingJob>();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });
builder.Services.AddEndpointsApiExplorer();

// Enhanced Swagger configuration
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "AiAgentBackend API",
        Version = "v1",
        Description = "AI Agent Backend Service with Integrated WhatsApp & Telegram",
        Contact = new OpenApiContact
        {
            Name = "AI Agent Team",
            Email = "support@aiagent.com"
        }
    });

    // JWT Bearer
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter 'Bearer {your token}'"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.MaximumReceiveMessageSize = 1024 * 1024;
})
.AddJsonProtocol(options =>
{
    options.PayloadSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
});

// Health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>()
    .AddCheck<MessagingHealthCheck>("messaging_services");

var app = builder.Build();

// Database initialization
try
{
    // Auto-create the database if it doesn't exist on the MySQL server
    try
    {
        var csb = new MySqlConnector.MySqlConnectionStringBuilder(connectionString);
        var databaseName = csb.Database;
        csb.Database = "mysql";
        using var tempConn = new MySqlConnector.MySqlConnection(csb.ConnectionString);
        await tempConn.OpenAsync();
        using var cmd = tempConn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE IF NOT EXISTS `{databaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci";
        await cmd.ExecuteNonQueryAsync();
        Console.WriteLine($"✅ Database '{databaseName}' verified/created");
    }
    catch (Exception dbEx)
    {
        Console.WriteLine($"⚠️ Could not auto-create database: {dbEx.Message}");
    }
    
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var canConnect = await db.Database.CanConnectAsync();
        Console.WriteLine($"✅ Database connection: {(canConnect ? "Success" : "Failed")}");
        
        if (canConnect)
        {
            await db.Database.EnsureCreatedAsync();
            Console.WriteLine("✅ Database tables verified");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Database initialization failed: {ex.Message}");
}

// Middleware pipeline - CORRECT ORDER IS CRITICAL
app.UseMiddleware<ErrorHandlingMiddleware>();

// CORS must come before other middleware
app.UseCors("AllowFrontend");

// Rate limiting
app.UseRateLimiter();

// Swagger (enabled in both Development and Production for API docs)
app.UseSwagger(c =>
{
    c.RouteTemplate = "swagger/{documentName}/swagger.json";
    c.PreSerializeFilters.Add((swaggerDoc, httpReq) =>
    {
        var baseUrl = EnvironmentHelper.GetBaseUrl(builder.Configuration);
        swaggerDoc.Servers = new List<OpenApiServer>
        {
            new OpenApiServer { Url = baseUrl }
        };
    });
});

app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "AiAgentBackend v1");
    c.RoutePrefix = "swagger";
    c.DisplayRequestDuration();
    c.EnableDeepLinking();
    c.EnableFilter();
    c.DocumentTitle = "AI Agent Backend API";
    c.EnablePersistAuthorization();
});

app.UseSerilogRequestLogging();
app.UseStaticFiles();

// Serve Angular SPA from dist folder (eliminates need for ng serve)
var angularDistPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "ai-agent-frontend", "dist", "ai-agent-frontend", "browser");
if (Directory.Exists(angularDistPath))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(angularDistPath),
        RequestPath = ""
    });
    // SPA fallback for client-side routing
    app.MapFallbackToFile("index.html", new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(angularDistPath)
    });
    Console.WriteLine($"✅ Serving Angular SPA from: {angularDistPath}");
}
else
{
    Console.WriteLine($"ℹ️ Angular SPA dist not found at: {angularDistPath} — run `npm run build` in ai-agent-frontend");
}

// Authentication & Authorization
app.UseAuthentication();
app.UseAuthorization();

// Hangfire dashboard (limited access in production)
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireAuthorizationFilter() },
    DashboardTitle = "AI Agent Jobs",
    AppPath = "/hangfire"
});

// Health check endpoint
app.MapGet("/health/real-time", async (IServiceProvider serviceProvider) =>
{
    using var scope = serviceProvider.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var messagingService = scope.ServiceProvider.GetRequiredService<IMessagingService>();
    
    try
    {
        var messagingStatus = await messagingService.GetStatusAsync();
        var usersCount = await db.Users.CountAsync();
        var activeTasks = await db.Tasks.CountAsync(t => t.Status != "Done");
        var pendingEvents = await db.Events.CountAsync(e => e.StartUtc >= DateTime.UtcNow);
        var unreadMessages = await db.Messages.CountAsync(m => m.Direction == "Incoming");
        
        var monitoringApi = JobStorage.Current.GetMonitoringApi();
        var servers = monitoringApi.Servers();
        var jobs = servers.Count;
        
        // Check database connectivity
        var dbConnected = await db.Database.CanConnectAsync();
        
        // Check Hangfire connectivity
        var hangfireConnected = jobs > 0;
        
        var overallStatus = dbConnected && hangfireConnected ? "Healthy" : "Degraded";
        if (!dbConnected) overallStatus = "Unhealthy";
        
        return Results.Json(new 
        {
            status = overallStatus,
            timestamp = DateTime.UtcNow,
            services = new
            {
                database = dbConnected ? "Connected" : "Disconnected",
                telegram = messagingStatus.Telegram.IsConnected ? "Connected" : "Disconnected",
                whatsapp = messagingStatus.WhatsApp.IsConnected ? "Connected" : "Disconnected",
                hangfire = hangfireConnected ? "Running" : "Stopped",
                signalr = "Active"
            },
            metrics = new
            {
                users = usersCount,
                active_tasks = activeTasks,
                pending_events = pendingEvents,
                unread_messages = unreadMessages,
                background_jobs = jobs,
                hangfire_servers = servers.Count
            },
            messaging_details = new
            {
                telegram = new {
                    connected = messagingStatus.Telegram.IsConnected,
                    username = messagingStatus.Telegram.Username ?? string.Empty,
                    last_checked = messagingStatus.Telegram.LastChecked
                },
                whatsapp = new {
                    connected = messagingStatus.WhatsApp.IsConnected,
                    status = messagingStatus.WhatsApp.Status ?? "disconnected",
                    last_checked = messagingStatus.WhatsApp.LastChecked
                }
            },
            connections = new
            {
                database_connection_string = dbConnected ? "Valid" : "Invalid",
                hangfire_storage = hangfireConnected ? "Accessible" : "Inaccessible"
            }
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new 
        {
            status = "Unhealthy",
            timestamp = DateTime.UtcNow,
            error = ex.Message,
            details = ex.StackTrace
        }, statusCode: 503);
    }
});

// Messaging status page
app.MapGet("/messaging-status", async (IMessagingService messagingService) =>
{
    var status = await messagingService.GetStatusAsync();
    
    var html = $@"
    <!DOCTYPE html>
    <html>
    <head>
        <title>Messaging Status - AI Agent</title>
        <style>
            body {{ font-family: Arial, sans-serif; margin: 40px; text-align: center; }}
            .container {{ max-width: 800px; margin: 0 auto; }}
            .status-box {{ padding: 20px; border-radius: 10px; margin: 20px 0; }}
            .connected {{ background: #d4edda; }}
            .disconnected {{ background: #f8d7da; }}
            .qr-available {{ background: #fff3cd; }}
            .row {{ display: flex; gap: 20px; }}
            .col {{ flex: 1; }}
        </style>
    </head>
    <body>
        <div class='container'>
            <h1>🤖 AI Agent - Messaging Status</h1>
            <div class='row'>
                <div class='col'>
                    <div class='status-box {(status.Telegram.IsConnected ? "connected" : "disconnected")}'>
                        <h2>📱 Telegram</h2>
                        <h3>{(status.Telegram.IsConnected ? "✅ Connected" : "❌ Disconnected")}</h3>
                        <p>{(status.Telegram.IsConnected ? "Ready to receive and send messages." : "Configure in settings to enable.")}</p>
                        {(status.Telegram.IsConnected && !string.IsNullOrEmpty(status.Telegram.Username) ? $"<p><strong>Bot:</strong> @{status.Telegram.Username}</p>" : "")}
                    </div>
                </div>
                <div class='col'>
                    <div class='status-box {(status.WhatsApp.IsConnected ? "connected" : "disconnected")}'>
                        <h2>💬 WhatsApp</h2>
                        <h3>{(status.WhatsApp.IsConnected ? "✅ Connected" : "❌ Disconnected")}</h3>
                        <p>{(status.WhatsApp.IsConnected ? "Ready to receive and send messages." : "Configure in settings to enable.")}</p>
                    </div>
                </div>
            </div>
            <p><strong>API Usage:</strong> Use Swagger UI at <a href='/swagger'>/swagger</a> for full control</p>
            <p><a href='/swagger'>Go to Swagger UI</a> | <a href='/api/messaging/status'>Raw Status JSON</a></p>
        </div>
    </body>
    </html>";
    
    return Results.Content(html, "text/html");
});

// Webhook endpoints
app.MapPost("/api/telegram/webhook", async (HttpContext context, ITelegramService telegramService) =>
{
    try
    {
        // Read the request body
        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync();
        
        if (!string.IsNullOrEmpty(body))
        {
            var update = System.Text.Json.JsonSerializer.Deserialize<TelegramUpdate>(body, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            
            if (update != null)
            {
                await telegramService.HandleUpdateAsync(update);
                return Results.Ok();
            }
        }
        return Results.BadRequest("Invalid Telegram webhook data");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Telegram webhook error: {ex.Message}");
        return Results.StatusCode(500);
    }
});

app.MapPost("/api/whatsapp/webhook", async (HttpContext context, IWhatsAppCloudService whatsAppService) =>
{
    try
    {
        // Read the request body
        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync();
        
        if (!string.IsNullOrEmpty(body))
        {
            var webhookData = System.Text.Json.JsonSerializer.Deserialize<WhatsAppWebhookData>(body, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            
            if (webhookData != null)
            {
                await whatsAppService.HandleWebhookAsync(webhookData);
                return Results.Ok();
            }
        }
        return Results.BadRequest("Invalid WhatsApp webhook data");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"WhatsApp webhook error: {ex.Message}");
        return Results.StatusCode(500);
    }
});

// FIXED: Proper StringValues handling
app.MapGet("/api/whatsapp/webhook", (HttpContext context, IConfiguration config) =>
{
    var mode = context.Request.Query["hub.mode"].ToString();
    var token = context.Request.Query["hub.verify_token"].ToString();
    var challenge = context.Request.Query["hub.challenge"].ToString();
    
    var verifyToken = config["Messaging:WhatsApp:VerifyToken"] ?? string.Empty;
    
    if (mode == "subscribe" && token == verifyToken)
    {
        return Results.Text(string.IsNullOrEmpty(challenge) ? string.Empty : challenge);
    }
    
    return Results.StatusCode(403);
});

// Map controllers and hubs
app.MapControllers();
app.MapHub<UpdatesHub>("/hub");

// Schedule background jobs
try
{
    using (var scope = app.Services.CreateScope())
    {
        var recurringJobManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
        var reminderJob = scope.ServiceProvider.GetRequiredService<ReminderJob>();
        var gmailJob = scope.ServiceProvider.GetRequiredService<GmailPollingJob>();
        var proactiveService = scope.ServiceProvider.GetRequiredService<IProactiveNotificationService>();

        recurringJobManager.AddOrUpdate(
            "task-reminders",
            () => reminderJob.RunAsync(),
            Cron.Minutely
        );

        recurringJobManager.AddOrUpdate(
            "gmail-polling",
            () => gmailJob.RunAsync(),
            "*/5 * * * *"
        );

        recurringJobManager.AddOrUpdate(
            "proactive-reminders",
            () => proactiveService.CheckAndSendRemindersAsync(),
            Cron.Minutely
        );

        recurringJobManager.AddOrUpdate(
            "event-reminders", 
            () => proactiveService.CheckAndSendEventRemindersAsync(),
            Cron.Minutely
        );

        recurringJobManager.AddOrUpdate(
            "email-alerts",
            () => proactiveService.CheckAndSendEmailAlertsAsync(),
            "*/5 * * * *"
        );

        recurringJobManager.AddOrUpdate(
            "deadline-warnings",
            () => proactiveService.CheckAndSendTaskDeadlineWarningsAsync(),
            "0 */6 * * *"
        );

        Console.WriteLine("✅ Background jobs scheduled successfully");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Failed to schedule background jobs: {ex.Message}");
}

// Display startup information
var baseUrl = EnvironmentHelper.GetBaseUrl(builder.Configuration);
Console.WriteLine($"🚀 AI Agent Backend starting...");
Console.WriteLine($"🌐 Environment: {app.Environment.EnvironmentName}");
Console.WriteLine($"🌐 Server URL: {baseUrl}");
Console.WriteLine($"📚 Swagger UI: {baseUrl}/swagger");
Console.WriteLine($"📊 Hangfire Dashboard: {baseUrl}/hangfire");
Console.WriteLine($"🤖 Telegram Webhook: {baseUrl}/api/telegram/webhook");
Console.WriteLine($"💬 WhatsApp Webhook: {baseUrl}/api/whatsapp/webhook");
Console.WriteLine($"❤️ Health Check: {baseUrl}/health");

app.Run();

// ============ SUPPORTING CLASSES ============

// Environment Helper Class (FIXED NULL REFERENCE ISSUES)
public static class EnvironmentHelper
{
    public static bool IsDevelopment => 
        (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? string.Empty) == "Development";
        
    public static bool IsProduction => 
        (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? string.Empty) == "Production";
        
    public static bool IsStaging => 
        (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? string.Empty) == "Staging";

    public static string GetConnectionString(IConfiguration configuration)
    {
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        return env switch
        {
            "Production" => configuration.GetConnectionString("ProductionConnection") 
                         ?? configuration.GetConnectionString("DefaultConnection")
                         ?? "Server=127.0.0.1;Port=3306;Database=AiAgentDb;User=root;Password=;",
            _ => configuration.GetConnectionString("DefaultConnection")
                         ?? "Server=127.0.0.1;Port=3306;Database=AiAgentDb;User=root;Password=;"
        };
    }

    public static string GetBaseUrl(IConfiguration configuration)
    {
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        var envUrl = Environment.GetEnvironmentVariable("AI_AGENT_BASE_URL");
        if (!string.IsNullOrEmpty(envUrl)) return envUrl;
        return env switch
        {
            "Production" => configuration["AiAgent:BaseUrl"] ?? "https://yourappdomain.com",
            _ => configuration["AiAgent:BaseUrl"] ?? "http://localhost:5000"
        };
    }

    public static string ResolveEnvPlaceholder(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (!value.StartsWith("${") || !value.EndsWith("}")) return value;
        var varName = value[2..^1];
        var envValue = Environment.GetEnvironmentVariable(varName);
        return !string.IsNullOrEmpty(envValue) ? envValue : value;
    }
}

// Hangfire authorization filter
public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        return httpContext.User.Identity?.IsAuthenticated == true;
    }
}

// Health Check Implementation
public class MessagingHealthCheck : IHealthCheck
{
    private readonly IServiceProvider _serviceProvider;

    public MessagingHealthCheck(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var messagingService = scope.ServiceProvider.GetRequiredService<IMessagingService>();
            var status = await messagingService.GetStatusAsync();
            
            var platforms = new List<string>();
            if (status.Telegram.IsConnected) platforms.Add("Telegram");
            if (status.WhatsApp.IsConnected) platforms.Add("WhatsApp");
            
            return platforms.Count > 0 
                ? HealthCheckResult.Healthy($"Connected to: {string.Join(", ", platforms)}")
                : HealthCheckResult.Degraded("No messaging platforms connected");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Messaging health check failed", ex);
        }
    }
}

// Webhook models
public class TelegramUpdate
{
    public long UpdateId { get; set; }
    public TelegramMessage? Message { get; set; }
    public TelegramCallbackQuery? CallbackQuery { get; set; }
}

public class TelegramMessage
{
    public long MessageId { get; set; }
    public TelegramUser From { get; set; } = new();
    public TelegramChat Chat { get; set; } = new();
    public string Text { get; set; } = string.Empty;
    public DateTime Date { get; set; }
}

public class TelegramUser
{
    public long Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string? Username { get; set; }
}

public class TelegramChat
{
    public long Id { get; set; }
    public string Type { get; set; } = string.Empty;
}

public class TelegramCallbackQuery
{
    public string Id { get; set; } = string.Empty;
    public TelegramUser From { get; set; } = new();
    public TelegramMessage Message { get; set; } = new();
    public string Data { get; set; } = string.Empty;
}

public class WhatsAppWebhookData
{
    public string Object { get; set; } = string.Empty;
    public List<WhatsAppEntry> Entry { get; set; } = new();
}

public class WhatsAppEntry
{
    public string Id { get; set; } = string.Empty;
    public List<WhatsAppChange> Changes { get; set; } = new();
}

public class WhatsAppChange
{
    public WhatsAppValue Value { get; set; } = new();
    public string Field { get; set; } = string.Empty;
}

public class WhatsAppValue
{
    public string MessagingProduct { get; set; } = string.Empty;
    public WhatsAppMetadata Metadata { get; set; } = new();
    public List<WhatsAppMessage> Messages { get; set; } = new();
}

public class WhatsAppMetadata
{
    public string DisplayPhoneNumber { get; set; } = string.Empty;
    public string PhoneNumberId { get; set; } = string.Empty;
}

public class WhatsAppMessage
{
    public string From { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public WhatsAppText? Text { get; set; }
    public string Type { get; set; } = string.Empty;
}

public class WhatsAppText
{
    public string Body { get; set; } = string.Empty;
}