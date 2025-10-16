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
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using AiAgentBackend.Jobs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.OpenApi.Models;
using Microsoft.Extensions.Diagnostics.HealthChecks; 

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel for HTTP only on port 5000
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.AddServerHeader = false;
    serverOptions.ListenAnyIP(5000); // Only port 5000
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

// DbContext (MySQL)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString), 
    mysqlOptions => 
    {
        mysqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorNumbersToAdd: null);
        mysqlOptions.CommandTimeout(60);
    });
    options.EnableSensitiveDataLogging(builder.Environment.IsDevelopment());
    options.EnableDetailedErrors(builder.Environment.IsDevelopment());
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

// Options - Register all configuration sections
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<WhatsAppOptions>(builder.Configuration.GetSection("WhatsApp"));
builder.Services.Configure<GoogleOptions>(builder.Configuration.GetSection("Google"));
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.Configure<TrelloOptions>(builder.Configuration.GetSection("Trello"));
builder.Services.Configure<OpenAIOptions>(builder.Configuration.GetSection("OpenAI"));

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

// Core Services
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IPasswordService, PasswordService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<INlpService, NlpService>();

// Integration Services
builder.Services.AddScoped<IGoogleCalendarService, GoogleCalendarService>();
builder.Services.AddScoped<ITrelloService, TrelloService>();
builder.Services.AddScoped<IConversationStateService, ConversationStateService>();

// Gmail Service
builder.Services.AddScoped<IGmailService, GmailService>();

// WhatsApp Service configuration
builder.Services.AddSingleton<WhatsAppBackgroundService>();
builder.Services.AddScoped<IWhatsAppService, RealWhatsAppWebService>();

// Proactive Services
builder.Services.AddScoped<IProactiveNotificationService, ProactiveNotificationService>();

// HttpClient configuration
builder.Services.AddHttpClient("whatsapp", client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
    client.DefaultRequestHeaders.Add("User-Agent", "AiAgentBackend/1.0");
});

// Command Orchestrator
builder.Services.AddScoped<ICommandOrchestrator, CommandOrchestrator>();

// Background Jobs
builder.Services.AddScoped<ReminderJob>();
builder.Services.AddScoped<GmailPollingJob>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Enhanced Swagger configuration with localhost:5000
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "AiAgentBackend API",
        Version = "v1",
        Description = "AI Agent Backend Service with Integrated WhatsApp",
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
});

// CORS - Allow both frontend and backend ports
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:3000", "http://localhost:5000")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// Enhanced health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>()
    .AddCheck<WhatsAppHealthCheck>("whatsapp_connection");

var app = builder.Build();

// Database initialization
try
{
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

// Middleware
app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseCors("AllowFrontend");

// Swagger - Configure for localhost:5000
app.UseSwagger(c =>
{
    c.RouteTemplate = "swagger/{documentName}/swagger.json";
    c.PreSerializeFilters.Add((swaggerDoc, httpReq) =>
    {
        swaggerDoc.Servers = new List<OpenApiServer>
        {
            new OpenApiServer { Url = "http://localhost:5000" } // Only localhost:5000
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
});

app.UseSerilogRequestLogging();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

// Hangfire dashboard
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
    var whatsAppService = scope.ServiceProvider.GetRequiredService<IWhatsAppService>();
    var backgroundJobClient = scope.ServiceProvider.GetRequiredService<IBackgroundJobClient>();
    
    try
    {
        var whatsAppStatus = await whatsAppService.GetStatusAsync();
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
        
        // Check WhatsApp connectivity
        var whatsAppConnected = await whatsAppService.CheckConnectionStatusAsync();
        
        var overallStatus = dbConnected && hangfireConnected ? "Healthy" : "Degraded";
        if (!dbConnected) overallStatus = "Unhealthy";
        
        return Results.Json(new 
        {
            status = overallStatus,
            timestamp = DateTime.UtcNow,
            services = new
            {
                database = dbConnected ? "Connected" : "Disconnected",
                whatsapp = whatsAppConnected ? "Connected" : "Disconnected",
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
            whatsapp_details = new
            {
                connected = whatsAppStatus.IsConnected,
                qr_available = whatsAppStatus.QrAvailable,
                initializing = whatsAppStatus.IsInitializing,
                status = whatsAppStatus.Status,
                last_checked = whatsAppStatus.Timestamp
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
            details = ex.StackTrace,
            inner_exception = ex.InnerException?.Message
        }, statusCode: 503);
    }
});

app.MapGet("/whatsapp-status", async (IWhatsAppService whatsAppService) =>
{
    var status = await whatsAppService.GetStatusAsync();
    
    var html = $@"
    <!DOCTYPE html>
    <html>
    <head>
        <title>WhatsApp Status - AI Agent</title>
        <style>
            body {{ font-family: Arial, sans-serif; margin: 40px; text-align: center; }}
            .container {{ max-width: 600px; margin: 0 auto; }}
            .connected {{ background: #d4edda; padding: 20px; border-radius: 10px; }}
            .disconnected {{ background: #f8d7da; padding: 20px; border-radius: 10px; }}
            .qr-available {{ background: #fff3cd; padding: 20px; border-radius: 10px; }}
        </style>
    </head>
    <body>
        <div class='container'>
            <h1>🤖 AI Agent - WhatsApp Status</h1>
            <div class='{GetStatusClass(status)}'>
                <h2>{GetStatusTitle(status)}</h2>
                <p>{GetStatusMessage(status)}</p>
                <p><strong>API Usage:</strong> Use Swagger UI at <a href='/swagger'>/swagger</a> for full control</p>
            </div>
            <p><a href='/swagger'>Go to Swagger UI</a> | <a href='/api/whatsapp/status'>Raw Status JSON</a></p>
        </div>
    </body>
    </html>";
    
    return Results.Content(html, "text/html");
    
    string GetStatusClass(WhatsAppStatus status) => status.Status.ToLower() switch
    {
        "connected" => "connected",
        "qr_available" => "qr-available",
        _ => "disconnected"
    };
    
    string GetStatusTitle(WhatsAppStatus status) => status.Status.ToLower() switch
    {
        "connected" => "✅ WhatsApp Connected",
        "qr_available" => "📱 QR Code Available",
        _ => "❌ WhatsApp Disconnected"
    };
    
    string GetStatusMessage(WhatsAppStatus status) => status.Status.ToLower() switch
    {
        "connected" => "Your AI Agent is connected to WhatsApp and ready to process messages.",
        "qr_available" => "Scan the QR code using the API endpoints to connect WhatsApp.",
        _ => "Initialize the connection using the WhatsApp API endpoints."
    };
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
        var whatsAppService = scope.ServiceProvider.GetRequiredService<IWhatsAppService>();

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
            "whatsapp-health",
            () => whatsAppService.CheckConnectionStatusAsync(),
            "*/2 * * * *"
        );

        recurringJobManager.AddOrUpdate(
            "proactive-reminders",
            () => scope.ServiceProvider.GetRequiredService<IProactiveNotificationService>().CheckAndSendRemindersAsync(),
            Cron.Minutely
        );

        recurringJobManager.AddOrUpdate(
            "event-reminders", 
            () => scope.ServiceProvider.GetRequiredService<IProactiveNotificationService>().CheckAndSendEventRemindersAsync(),
            Cron.Minutely
        );

        recurringJobManager.AddOrUpdate(
            "email-alerts",
            () => scope.ServiceProvider.GetRequiredService<IProactiveNotificationService>().CheckAndSendEmailAlertsAsync(),
            "*/5 * * * *"
        );

        recurringJobManager.AddOrUpdate(
            "deadline-warnings",
            () => scope.ServiceProvider.GetRequiredService<IProactiveNotificationService>().CheckAndSendTaskDeadlineWarningsAsync(),
            "0 */6 * * *"
        );

        Console.WriteLine("✅ Background jobs scheduled successfully");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Failed to schedule background jobs: {ex.Message}");
}

// Update startup message (removed webhooks reference)
Console.WriteLine($"🚀 AI Agent Backend starting on port 5000...");
Console.WriteLine($"🌐 Server URL: http://localhost:5000");
Console.WriteLine($"📚 Swagger UI: http://localhost:5000/swagger");
Console.WriteLine($"📊 Hangfire Dashboard: http://localhost:5000/hangfire");
Console.WriteLine($"📱 WhatsApp API: http://localhost:5000/api/whatsapp/*");
Console.WriteLine($"❤️ Health Check: http://localhost:5000/health");
Console.WriteLine($"⏰ Reminder: Make sure MySQL is running on localhost:3306");

app.Run();

// Hangfire authorization filter
public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        return httpContext.User.Identity?.IsAuthenticated == true;
    }
}

// WhatsApp Health Check Implementation (moved outside top-level statements)
public class WhatsAppHealthCheck : IHealthCheck
{
    private readonly IServiceProvider _serviceProvider;

    public WhatsAppHealthCheck(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var whatsAppService = scope.ServiceProvider.GetRequiredService<IWhatsAppService>();
            var status = await whatsAppService.GetStatusAsync();
            
            return status.IsConnected 
                ? HealthCheckResult.Healthy("WhatsApp is connected")
                : HealthCheckResult.Degraded("WhatsApp is disconnected");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("WhatsApp health check failed", ex);
        }
    }
}