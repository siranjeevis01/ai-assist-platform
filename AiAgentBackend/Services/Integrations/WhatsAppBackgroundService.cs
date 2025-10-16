using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiAgentBackend.Services.Integrations
{
    public class WhatsAppBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<WhatsAppBackgroundService> _logger;
        private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(2);

        public WhatsAppBackgroundService(IServiceProvider serviceProvider, ILogger<WhatsAppBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("WhatsApp Background Service started");

            // Initial delay to allow app to start up
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var whatsAppService = scope.ServiceProvider.GetRequiredService<IWhatsAppService>();
                    
                    var status = await whatsAppService.CheckConnectionStatusAsync();
                    
                    if (!status)
                    {
                        _logger.LogWarning("WhatsApp is disconnected. Consider reinitializing connection.");
                        
                        // Optionally attempt to reconnect if disconnected for too long
                        var whatsAppStatus = await whatsAppService.GetStatusAsync();
                        if (!whatsAppStatus.IsInitializing)
                        {
                            _logger.LogInformation("WhatsApp is not initializing, connection may need manual intervention");
                        }
                    }
                    else
                    {
                        _logger.LogDebug("WhatsApp connection is active");
                    }
                    
                    await Task.Delay(_checkInterval, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Error in WhatsApp background service");
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                }
            }

            _logger.LogInformation("WhatsApp Background Service stopped");
        }
    }
}