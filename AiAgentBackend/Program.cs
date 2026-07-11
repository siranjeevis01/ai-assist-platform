// Program.cs 
using AiAgentBackend.Hubs;
using Hangfire.Dashboard;
using System.Text;
using System.Security.Cryptography;
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
using AiAgentBackend.Services.Cache;
using AiAgentBackend.Services.PushNotification;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Primitives;
using StackExchange.Redis;

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
var hasDatabase = !string.IsNullOrWhiteSpace(connectionString) 
    && !connectionString.Contains("127.0.0.1") 
    && !connectionString.Contains("example.com")
    && !connectionString.Contains("localhost");

if (hasDatabase)
{
    // DbContext (MySQL) - FIXED SYNTAX
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
        
        if (EnvironmentHelper.IsDevelopment)
        {
            options.EnableSensitiveDataLogging();
            options.EnableDetailedErrors();
        }
    });

    // Hangfire with MySQL
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
    
    Console.WriteLine("✅ Database and Hangfire configured");
}
else
{
    Console.WriteLine("⚠️ No database configured — running without persistence (NLP/chat still works)");
    // Register a dummy DbContext so DI doesn't fail
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
    {
        options.UseInMemoryDatabase("AiAgentDb_NoDb");
    });
    
    // Use in-memory Hangfire storage as fallback
    builder.Services.AddHangfire(config =>
    {
        config.UseSimpleAssemblyNameTypeSerializer()
              .UseRecommendedSerializerSettings()
              .UseInMemoryStorage();
    });
    builder.Services.AddHangfireServer();
}

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

builder.Services.PostConfigure<GoogleOptions>(options =>
{
    options.ClientId = EnvironmentHelper.ResolveEnvPlaceholder(options.ClientId);
    options.ClientSecret = EnvironmentHelper.ResolveEnvPlaceholder(options.ClientSecret);
    options.RedirectUri = EnvironmentHelper.ResolveEnvPlaceholder(options.RedirectUri);
});

builder.Services.PostConfigure<TrelloOptions>(options =>
{
    options.ApiKey = EnvironmentHelper.ResolveEnvPlaceholder(options.ApiKey);
    options.AccessToken = EnvironmentHelper.ResolveEnvPlaceholder(options.AccessToken);
    options.DefaultBoardId = EnvironmentHelper.ResolveEnvPlaceholder(options.DefaultBoardId);
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

// Caching - Redis if REDIS_CONNECTION is set, otherwise in-memory
builder.Services.AddMemoryCache();
var redisConnection = Environment.GetEnvironmentVariable("REDIS_CONNECTION");
var useRedis = !string.IsNullOrWhiteSpace(redisConnection);
if (useRedis)
{
    try
    {
        // Parse Redis URL or redis-cli command into StackExchange.Redis connection string
        var redisConnStr = EnvironmentHelper.ParseRedisConnectionString(redisConnection!);
        var redis = ConnectionMultiplexer.Connect(redisConnStr);
        builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
        Console.WriteLine("✅ Redis caching configured");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"⚠️ Redis connection failed ({ex.Message}), falling back to in-memory cache");
        useRedis = false;
    }
}
if (!useRedis)
{
    Console.WriteLine("ℹ️ Using in-memory cache (set REDIS_CONNECTION env var to enable Redis)");
}

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
if (useRedis)
    builder.Services.AddSingleton<ICacheService, RedisCacheService>();
else
    builder.Services.AddSingleton<ICacheService, MemoryCacheService>();

// Integration Services
builder.Services.AddScoped<IGoogleCalendarService, GoogleCalendarService>();
builder.Services.AddScoped<ITrelloService, TrelloService>();
builder.Services.AddScoped<IConversationStateService, ConversationStateService>();
builder.Services.AddScoped<IGmailService, GmailService>();
builder.Services.AddScoped<IProactiveNotificationService, ProactiveNotificationService>();

// Automation, Document, Voice, RBAC Services
builder.Services.AddScoped<AiAgentBackend.Services.Automation.IAutomationService, AiAgentBackend.Services.Automation.AutomationService>();
builder.Services.AddScoped<AiAgentBackend.Services.Documents.IDocumentService, AiAgentBackend.Services.Documents.DocumentService>();
builder.Services.AddScoped<AiAgentBackend.Services.Voice.IVoiceService, AiAgentBackend.Services.Voice.VoiceService>();
builder.Services.AddScoped<AiAgentBackend.Services.RBAC.ITeamService, AiAgentBackend.Services.RBAC.TeamService>();

// Messaging Services (platform-specific)
builder.Services.AddScoped<ITelegramService, TelegramService>();
builder.Services.AddScoped<IWhatsAppCloudService, WhatsAppCloudService>();

// Unified Messaging Service (depends on platform services)
builder.Services.AddScoped<IMessagingService, MessagingService>();

// Command Orchestrator (depends on messaging)
builder.Services.AddScoped<ICommandOrchestrator, CommandOrchestrator>();

// Push Notification Service
builder.Services.AddScoped<IPushNotificationService, PushNotificationService>();

// Background Jobs
builder.Services.AddScoped<ReminderJob>();
builder.Services.AddScoped<GmailPollingJob>();
builder.Services.AddScoped<AiAgentBackend.Jobs.SmartReminderService>();
builder.Services.AddScoped<AiAgentBackend.Jobs.SmartReminderHangfireJob>();

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
if (hasDatabase)
{
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
                try
                {
                    var pendingMigrations = await db.Database.GetPendingMigrationsAsync();
                    var pendingList = pendingMigrations.ToList();
                    if (pendingList.Any())
                    {
                        Console.WriteLine($"📦 Applying {pendingList.Count} pending migration(s): {string.Join(", ", pendingList)}");
                        await db.Database.MigrateAsync();
                        Console.WriteLine("✅ All migrations applied successfully");
                    }
                    else
                    {
                        Console.WriteLine("✅ Database is up to date - no pending migrations");
                    }
                }
                catch (Exception migrateEx)
                {
                    Console.WriteLine($"⚠️ MigrateAsync failed ({migrateEx.Message}), applying schema manually");
                }

                // Ensure all tables exist (handles DB created by EnsureCreatedAsync before new models were added)
                await EnsureAllTablesExistAsync(db);
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Database initialization failed: {ex.Message}");
    }
}
else
{
    Console.WriteLine("⚠️ No database configured — skipping DB initialization");
}

// Firebase initialization (non-critical — skip gracefully if not configured)
try
{
    var firebaseConfigPath = Environment.GetEnvironmentVariable("FIREBASE_CONFIG")
                            ?? Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS");
    if (!string.IsNullOrEmpty(firebaseConfigPath) && FirebaseApp.DefaultInstance == null)
    {
        FirebaseApp.Create(new AppOptions
        {
            Credential = GoogleCredential.FromFile(firebaseConfigPath)
        });
        Console.WriteLine("✅ Firebase initialized for push notifications");
    }
    else if (string.IsNullOrEmpty(firebaseConfigPath))
    {
        Console.WriteLine("ℹ️ Firebase not configured — push notifications disabled (set FIREBASE_CONFIG or GOOGLE_APPLICATION_CREDENTIALS)");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"⚠️ Firebase initialization failed ({ex.Message}) — push notifications disabled");
}

// Middleware pipeline - CORRECT ORDER IS CRITICAL
app.UseMiddleware<ErrorHandlingMiddleware>();

// CORS must come before other middleware
app.UseCors("AllowFrontend");

// Rate limiting
app.UseRateLimiter();
app.UseMiddleware<UserRateLimitMiddleware>();

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
if (hasDatabase)
{
    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = new[] { new HangfireAuthorizationFilter() },
        DashboardTitle = "AI Agent Jobs",
        AppPath = "/hangfire"
    });
}

// Health check endpoint
app.MapGet("/health/real-time", async (IServiceProvider serviceProvider) =>
{
    using var scope = serviceProvider.CreateScope();
    var messagingService = scope.ServiceProvider.GetRequiredService<IMessagingService>();
    
    try
    {
        var messagingStatus = await messagingService.GetStatusAsync();
        
        var result = new 
        {
            status = "Healthy",
            timestamp = DateTime.UtcNow,
            services = new
            {
                database = hasDatabase ? "Configured" : "Not configured (in-memory)",
                telegram = messagingStatus.Telegram.IsConnected ? "Connected" : "Disconnected",
                whatsapp = messagingStatus.WhatsApp.IsConnected ? "Connected" : "Disconnected",
                hangfire = hasDatabase ? "Running" : "Disabled (no DB)",
                signalr = "Active"
            }
        };
        
        if (hasDatabase)
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var usersCount = await db.Users.CountAsync();
            var activeTasks = await db.Tasks.CountAsync(t => t.Status != "Done");
            var pendingEvents = await db.Events.CountAsync(e => e.StartUtc >= DateTime.UtcNow);
            var unreadMessages = await db.Messages.CountAsync(m => m.Direction == "Incoming");
            
            return Results.Json(new 
            {
                result.status,
                result.timestamp,
                result.services,
                metrics = new
                {
                    users = usersCount,
                    active_tasks = activeTasks,
                    pending_events = pendingEvents,
                    unread_messages = unreadMessages
                }
            });
        }
        
        return Results.Json(result);
    }
    catch (Exception ex)
    {
        return Results.Json(new 
        {
            status = "Unhealthy",
            timestamp = DateTime.UtcNow,
            error = ex.Message
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

// Webhook endpoints with signature verification
app.MapPost("/api/telegram/webhook", async (HttpContext context, ITelegramService telegramService, IConfiguration config) =>
{
    try
    {
        var secretToken = config["Messaging:Telegram:SecretToken"];
        if (!string.IsNullOrEmpty(secretToken))
        {
            if (!context.Request.Headers.TryGetValue("X-Telegram-Bot-Api-Secret-Token", out var receivedToken)
                || receivedToken != secretToken)
            {
                Console.WriteLine("Telegram webhook: invalid secret token");
                return Results.StatusCode(403);
            }
        }

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

app.MapPost("/api/whatsapp/webhook", async (HttpContext context, IWhatsAppCloudService whatsAppService, IConfiguration config) =>
{
    try
    {
        var appSecret = config["Messaging:WhatsApp:AppSecret"];
        if (!string.IsNullOrEmpty(appSecret))
        {
            if (!context.Request.Headers.TryGetValue("X-Hub-Signature-256", out var signature)
                || string.IsNullOrEmpty(signature))
            {
                Console.WriteLine("WhatsApp webhook: missing signature");
                return Results.StatusCode(403);
            }

            using var ms = new MemoryStream();
            await context.Request.Body.CopyToAsync(ms);
            var bodyBytes = ms.ToArray();
            var bodyForVerify = System.Text.Encoding.UTF8.GetString(bodyBytes);

            using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(appSecret));
            var hash = hmac.ComputeHash(bodyBytes);
            var expected = "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();

            if (signature != expected)
            {
                Console.WriteLine("WhatsApp webhook: invalid signature");
                return Results.StatusCode(403);
            }

            context.Request.Body = new MemoryStream(bodyBytes);
        }

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

// Health check endpoint
app.MapHealthChecks("/health");

// Schedule background jobs
if (hasDatabase)
{
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

            // Schedule smart reminders for all users every hour
            var smartReminderJob = scope.ServiceProvider.GetRequiredService<AiAgentBackend.Jobs.SmartReminderHangfireJob>();
            recurringJobManager.AddOrUpdate(
                "smart-reminders",
                () => smartReminderJob.RunAsync(),
                Cron.Hourly
            );

            Console.WriteLine("✅ Background jobs scheduled successfully");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Failed to schedule background jobs: {ex.Message}");
    }
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

// Ensures all tables from the current DbContext model exist in MySQL
static async Task EnsureAllTablesExistAsync(ApplicationDbContext db)
{
    var dropStatements = new[]
    {
        "DROP TABLE IF EXISTS `TeamMembers`",
        "DROP TABLE IF EXISTS `AuditEntries`",
        "DROP TABLE IF EXISTS `Teams`",
        "DROP TABLE IF EXISTS `ConversationHistory`",
        "DROP TABLE IF EXISTS `DocumentChunks`",
        "DROP TABLE IF EXISTS `Documents`",
        "DROP TABLE IF EXISTS `AutomationRules`",
        "DROP TABLE IF EXISTS `DeviceTokens`"
    };
    var createStatements = new[]
    {
        @"CREATE TABLE IF NOT EXISTS `Teams` (`Id` INT NOT NULL AUTO_INCREMENT, `OwnerId` INT NOT NULL, `Name` VARCHAR(100) NOT NULL, `Description` VARCHAR(500) NULL, `CreatedAt` DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6), PRIMARY KEY (`Id`), INDEX `IX_Teams_OwnerId` (`OwnerId`), CONSTRAINT `FK_Teams_Users_OwnerId` FOREIGN KEY (`OwnerId`) REFERENCES `Users`(`Id`) ON DELETE RESTRICT) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci",
        @"CREATE TABLE IF NOT EXISTS `TeamMembers` (`Id` INT NOT NULL AUTO_INCREMENT, `TeamId` INT NOT NULL, `UserId` INT NOT NULL, `Role` VARCHAR(20) NOT NULL DEFAULT 'Member', `JoinedAt` DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6), PRIMARY KEY (`Id`), UNIQUE INDEX `IX_TeamMembers_TeamId_UserId` (`TeamId`, `UserId`), INDEX `IX_TeamMembers_UserId` (`UserId`), CONSTRAINT `FK_TeamMembers_Teams_TeamId` FOREIGN KEY (`TeamId`) REFERENCES `Teams`(`Id`) ON DELETE CASCADE, CONSTRAINT `FK_TeamMembers_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users`(`Id`) ON DELETE CASCADE) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci",
        @"CREATE TABLE IF NOT EXISTS `AuditEntries` (`Id` INT NOT NULL AUTO_INCREMENT, `UserId` INT NOT NULL, `TeamId` INT NULL, `EntityType` VARCHAR(100) NOT NULL, `EntityId` INT NULL, `Action` VARCHAR(50) NOT NULL, `Details` TEXT NULL, `IpAddress` VARCHAR(50) NULL, `CreatedAt` DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6), PRIMARY KEY (`Id`), INDEX `IX_AuditEntries_UserId` (`UserId`), INDEX `IX_AuditEntries_CreatedAt` (`CreatedAt`), CONSTRAINT `FK_AuditEntries_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users`(`Id`) ON DELETE CASCADE) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci",
        @"CREATE TABLE IF NOT EXISTS `ConversationHistory` (`Id` INT NOT NULL AUTO_INCREMENT, `UserId` INT NOT NULL, `UserMessage` LONGTEXT NOT NULL, `BotResponse` LONGTEXT NOT NULL, `Intent` VARCHAR(50) NULL, `CreatedAt` DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6), PRIMARY KEY (`Id`), INDEX `IX_ConversationHistory_UserId` (`UserId`), INDEX `IX_ConversationHistory_CreatedAt` (`CreatedAt`), INDEX `IX_ConversationHistory_UserId_CreatedAt` (`UserId`, `CreatedAt`), CONSTRAINT `FK_ConversationHistory_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users`(`Id`) ON DELETE CASCADE) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci",
        @"CREATE TABLE IF NOT EXISTS `AutomationRules` (`Id` INT NOT NULL AUTO_INCREMENT, `UserId` INT NOT NULL, `Name` VARCHAR(100) NOT NULL, `Description` VARCHAR(500) NULL, `TriggerType` VARCHAR(50) NOT NULL, `TriggerConfig` TEXT NOT NULL, `ActionsJson` TEXT NOT NULL, `IsActive` TINYINT(1) NOT NULL DEFAULT 1, `RunCount` INT NOT NULL DEFAULT 0, `LastRunAt` DATETIME(6) NULL, `CreatedAt` DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6), `UpdatedAt` DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6), PRIMARY KEY (`Id`), INDEX `IX_AutomationRules_UserId` (`UserId`), INDEX `IX_AutomationRules_IsActive` (`IsActive`), CONSTRAINT `FK_AutomationRules_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users`(`Id`) ON DELETE CASCADE) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci",
        @"CREATE TABLE IF NOT EXISTS `Documents` (`Id` INT NOT NULL AUTO_INCREMENT, `UserId` INT NOT NULL, `FileName` VARCHAR(255) NOT NULL, `ContentType` VARCHAR(100) NOT NULL, `SizeBytes` BIGINT NOT NULL, `StoragePath` VARCHAR(500) NOT NULL, `ExtractedText` LONGTEXT NULL, `Summary` TEXT NULL, `EmbeddingStatus` VARCHAR(20) NOT NULL DEFAULT 'pending', `CreatedAt` DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6), `UpdatedAt` DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6), PRIMARY KEY (`Id`), INDEX `IX_Documents_UserId` (`UserId`), INDEX `IX_Documents_CreatedAt` (`CreatedAt`), CONSTRAINT `FK_Documents_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users`(`Id`) ON DELETE CASCADE) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci",
        @"CREATE TABLE IF NOT EXISTS `DocumentChunks` (`Id` INT NOT NULL AUTO_INCREMENT, `DocumentId` INT NOT NULL, `ChunkIndex` INT NOT NULL, `Content` TEXT NOT NULL, `EmbeddingVector` TEXT NULL, `TokenCount` INT NOT NULL, `CreatedAt` DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6), PRIMARY KEY (`Id`), INDEX `IX_DocumentChunks_DocumentId` (`DocumentId`), CONSTRAINT `FK_DocumentChunks_Documents_DocumentId` FOREIGN KEY (`DocumentId`) REFERENCES `Documents`(`Id`) ON DELETE CASCADE) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci",
        @"CREATE TABLE IF NOT EXISTS `DeviceTokens` (`Id` INT NOT NULL AUTO_INCREMENT, `UserId` VARCHAR(255) NOT NULL, `Token` LONGTEXT NOT NULL, `Platform` VARCHAR(20) NOT NULL DEFAULT 'web', `CreatedAt` DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6), `LastUsedAt` DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6), `IsActive` TINYINT(1) NOT NULL DEFAULT 1, PRIMARY KEY (`Id`), INDEX `IX_DeviceTokens_UserId` (`UserId`), INDEX `IX_DeviceTokens_IsActive` (`IsActive`)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci"
    };
    var count = 0;
    foreach (var sql in dropStatements)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); } catch { }
    }
    foreach (var sql in createStatements)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); count++; } catch { }
    }
    if (count > 0) Console.WriteLine($"✅ Ensured {count} table(s) exist");
}

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
        
        // Check for env var override first (works for both dev and prod)
        var envConnStr = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (!string.IsNullOrWhiteSpace(envConnStr)) return envConnStr;
        
        return env switch
        {
            "Production" => configuration.GetConnectionString("ProductionConnection") 
                         ?? configuration.GetConnectionString("DefaultConnection")
                         ?? "",
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

    public static string ParseRedisConnectionString(string input)
    {
        // Already a StackExchange.Redis connection string (contains comma-separated key=value pairs)
        if (input.Contains('=') && !input.StartsWith("redis://") && !input.StartsWith("redis-cli"))
            return input;

        // Handle "redis-cli -u redis://..." format
        if (input.StartsWith("redis-cli"))
        {
            var uriMatch = System.Text.RegularExpressions.Regex.Match(input, @"redis://(\S+)");
            if (uriMatch.Success) input = uriMatch.Groups[1].Value;
        }

        // Handle "redis://user:pass@host:port" format
        if (input.StartsWith("default@") || input.Contains('@'))
        {
            var userInfo = input.Split('@')[0];
            var hostPart = input.Split('@')[1];
            var password = userInfo.Replace("default:", "").Trim();
            var host = hostPart.TrimEnd('/');
            return $"{host},password={password},ssl=True,abortConnect=False";
        }

        return input;
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