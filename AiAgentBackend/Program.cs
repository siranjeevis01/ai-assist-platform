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

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel for HTTP only in production
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.AddServerHeader = false;
    
    // HTTP only - remove HTTPS for containerized environment
    serverOptions.ListenAnyIP(5000, listenOptions =>
    {
        listenOptions.UseConnectionLogging();
    });
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
var conn = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(conn, ServerVersion.AutoDetect(conn),
    sqlOptions =>
    {
        sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(30), // FIXED: Changed = to :
            errorNumbersToAdd: null);
    }));

// Hangfire
builder.Services.AddHangfire(config =>
{
    config.UseSimpleAssemblyNameTypeSerializer()
          .UseRecommendedSerializerSettings()
          .UseStorage(new MySqlStorage(conn, new MySqlStorageOptions
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
    options.WorkerCount = Math.Max(Environment.ProcessorCount, 2);
    options.Queues = new[] { "default", "critical", "low" };
});

// Options
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<WhatsAppOptions>(builder.Configuration.GetSection("WhatsApp"));
builder.Services.Configure<GoogleOptions>(builder.Configuration.GetSection("Google"));
builder.Services.Configure<TrelloOptions>(builder.Configuration.GetSection("Trello"));

// Auth / JWT
var jwtKey = builder.Configuration["Jwt:Key"] ?? "fallback-dev-key-change-in-production-with-32-char-secret";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "AiAgentBackend";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "AiAgentUsers";

if (jwtKey.Length < 32)
{
    throw new InvalidOperationException("JWT Key must be at least 32 characters long for production");
}

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

// DI: Services
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddHttpClient<INlpService, OpenAiNlpService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IGoogleCalendarService, GoogleCalendarService>();
builder.Services.AddScoped<ITrelloService, TrelloService>();
builder.Services.AddScoped<ReminderJob>();
builder.Services.AddScoped<GmailPollingJob>();

// Gmail Services
builder.Services.AddScoped<IGmailService, EnhancedGmailService>();
builder.Services.AddScoped<IEnhancedGmailService, EnhancedGmailService>();

// WhatsApp Services
builder.Services.Configure<WhatsAppOptions>(builder.Configuration.GetSection("WhatsApp"));
builder.Services.AddHttpClient<IHttpWhatsAppService, HttpWhatsAppService>();

// Other enhanced services
builder.Services.AddScoped<IProactiveNotificationService, ProactiveNotificationService>();
builder.Services.AddScoped<IConversationStateService, ConversationStateService>();

// Command Orchestrators
builder.Services.AddScoped<ICommandOrchestrator, CommandOrchestrator>();
builder.Services.AddScoped<IEnhancedCommandOrchestrator, EnhancedCommandOrchestrator>();

// Free NLP Service as fallback
// builder.Services.AddHttpClient<INlpService, FreeNlpService>();

// NLP Services with fallback chain
builder.Services.AddHttpClient<OpenAiNlpService>();
builder.Services.AddHttpClient<FreeNlpService>();
builder.Services.AddScoped<INlpService, EnhancedNlpService>();

// HTTP Client for WhatsApp services with retry policy
builder.Services.AddHttpClient("WhatsAppBot", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("User-Agent", "AiAgentBackend/1.0");
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Enhanced Swagger configuration - ALWAYS enable Swagger
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "AiAgentBackend API",
        Version = "v1",
        Description = "AI Agent Backend Service",
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

    // Include XML comments if available
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.MaximumReceiveMessageSize = 1024 * 1024; // 1MB
});

// CORS policies
builder.Services.AddCors(options =>
{
    options.AddPolicy("DevelopmentPolicy", policy =>
    {
        policy.WithOrigins("http://localhost:5000", "http://localhost:3000", "http://localhost:3001")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });

    options.AddPolicy("ProductionPolicy", policy =>
    {
        policy.WithOrigins("http://backend:5000", "http://whatsapp-bot:3001")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });

    options.AddPolicy("DockerNetworkPolicy", policy =>
    {
        policy.SetIsOriginAllowed(origin => true)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// SIMPLE Health Check
builder.Services.AddHealthChecks();

var app = builder.Build();

// Middleware
app.UseMiddleware<ErrorHandlingMiddleware>();

// Apply CORS based on environment
if (app.Environment.IsDevelopment())
{
    app.UseCors("DevelopmentPolicy");
    Console.WriteLine("🔧 Running in DEVELOPMENT mode");
}
else
{
    app.UseCors("DockerNetworkPolicy");
    Console.WriteLine("🚀 Running in PRODUCTION mode");
}

// ALWAYS enable Swagger in all environments
app.UseSwagger(c =>
{
    c.RouteTemplate = "swagger/{documentName}/swagger.json";
    c.PreSerializeFilters.Add((swaggerDoc, httpReq) =>
    {
        swaggerDoc.Servers = new List<OpenApiServer>
        {
            new OpenApiServer { Url = $"{httpReq.Scheme}://{httpReq.Host.Value}" }
        };
    });
});

app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "AiAgentBackend v1");
    c.RoutePrefix = "swagger"; // Always use /swagger route
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
app.MapGet("/health", () => 
{
    return Results.Json(new { 
        status = "Healthy",
        timestamp = DateTime.UtcNow,
        service = "AiAgentBackend"
    });
});

// Map controllers and hubs
app.MapControllers();
app.MapHub<UpdatesHub>("/hub");

app.MapControllerRoute(
    name: "webhooks",
    pattern: "webhooks/{controller=Webhooks}/{action=Index}/{id?}");

// Add .well-known endpoint for Chrome DevTools
app.MapGet("/.well-known/appspecific/com.chrome.devtools.json", () =>
{
    return Results.Json(new
    {
        name = "AI Agent Backend",
        version = "1.0.0",
        description = "AI Agent Backend API",
        environment = app.Environment.EnvironmentName
    });
});

// API info endpoint
app.MapGet("/api/info", () => Results.Json(new
{
    service = "AI Agent Backend",
    version = "1.0.0",
    status = "Running",
    timestamp = DateTime.UtcNow,
    environment = app.Environment.EnvironmentName,
    endpoints = new
    {
        swagger = "/swagger",
        hangfire = "/hangfire",
        health = "/health"
    }
}));

// Root endpoint
app.MapGet("/", () => Results.Json(new
{
    service = "AI Agent Backend",
    version = "1.0.0",
    status = "Running",
    timestamp = DateTime.UtcNow,
    environment = app.Environment.EnvironmentName,
    documentation = "Visit /swagger for API documentation"
}));

// Schedule background jobs
using (var scope = app.Services.CreateScope())
{
    var recurringJobManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
    var reminderJob = scope.ServiceProvider.GetRequiredService<ReminderJob>();
    var gmailJob = scope.ServiceProvider.GetRequiredService<GmailPollingJob>();

    // Schedule jobs with proper error handling
    try
    {
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
            "calendar-sync",
            () => scope.ServiceProvider.GetService<IGoogleCalendarService>()!.SyncAllUserCalendars(),
            "*/5 * * * *"
        );

        recurringJobManager.AddOrUpdate(
            "trello-sync",
            () => scope.ServiceProvider.GetService<ITrelloService>()!.SyncAllUserTasks(),
            "*/5 * * * *"
        );

        recurringJobManager.AddOrUpdate(
            "proactive-reminders",
            () => scope.ServiceProvider.GetService<IProactiveNotificationService>()!.CheckAndSendRemindersAsync(),
            Cron.Minutely
        );

        recurringJobManager.AddOrUpdate(
            "proactive-event-reminders",
            () => scope.ServiceProvider.GetService<IProactiveNotificationService>()!.CheckAndSendEventRemindersAsync(),
            Cron.Minutely
        );

        recurringJobManager.AddOrUpdate(
            "proactive-email-alerts",
            () => scope.ServiceProvider.GetService<IProactiveNotificationService>()!.CheckAndSendEmailAlertsAsync(),
            "*/5 * * * *"
        );

        recurringJobManager.AddOrUpdate(
            "proactive-task-warnings",
            () => scope.ServiceProvider.GetService<IProactiveNotificationService>()!.CheckAndSendTaskDeadlineWarningsAsync(),
            "0 * * * *"
        );

        Console.WriteLine("✅ Background jobs scheduled successfully");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Failed to schedule background jobs: {ex.Message}");
    }
}

// Global exception handler for the application
AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
{
    Console.WriteLine($"🛑 CRITICAL: Unhandled exception: {e.ExceptionObject}");
};

TaskScheduler.UnobservedTaskException += (sender, e) =>
{
    Console.WriteLine($"🛑 CRITICAL: Unobserved task exception: {e.Exception}");
    e.SetObserved();
};

Console.WriteLine($"🚀 AI Agent Backend starting in {app.Environment.EnvironmentName} environment...");
Console.WriteLine($"🌐 Server URLs: {string.Join(", ", builder.Configuration["ASPNETCORE_URLS"]?.Split(';') ?? new[] { "http://+:5000" })}");
Console.WriteLine($"📚 Swagger UI: http://localhost:5000/swagger");
Console.WriteLine($"📊 Hangfire Dashboard: http://localhost:5000/hangfire");
Console.WriteLine($"❤️ Health Check: http://localhost:5000/health");

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