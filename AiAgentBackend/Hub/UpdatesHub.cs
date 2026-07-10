// Hub/UpdatesHub.cs
using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace AiAgentBackend.Hubs
{
    public class UpdatesHub : Hub
    {
        private readonly ILogger<UpdatesHub> _logger;

        public UpdatesHub(ILogger<UpdatesHub> logger)
        {
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            var userId = Context.User?.FindFirst("uid")?.Value ?? Context.User?.FindFirst("sub")?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"user-{userId}");
                _logger.LogInformation("User {UserId} connected to hub with connection {ConnectionId}", userId, Context.ConnectionId);
                
                // Send current status
                await Clients.Caller.SendAsync("ReceiveUpdate", new
                {
                    Type = "ConnectionEstablished",
                    Message = "Real-time updates connected",
                    Timestamp = DateTime.UtcNow
                });
            }
            
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = Context.User?.FindFirst("uid")?.Value ?? Context.User?.FindFirst("sub")?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user-{userId}");
                _logger.LogInformation("User {UserId} disconnected from hub", userId);
            }
            
            await base.OnDisconnectedAsync(exception);
        }

        public async Task SendUserUpdate(string userId, object data)
        {
            await Clients.Group($"user-{userId}").SendAsync("ReceiveUpdate", data);
        }

        public async Task SubscribeToUserUpdates()
        {
            var userId = Context.User?.FindFirst("uid")?.Value ?? Context.User?.FindFirst("sub")?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"user-{userId}");
                _logger.LogInformation("User {UserId} subscribed to real-time updates", userId);
                
                await Clients.Caller.SendAsync("ReceiveUpdate", new
                {
                    Type = "SubscriptionConfirmed",
                    Message = "Subscribed to real-time updates",
                    UserId = userId,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        // Enhanced event methods for SignalR real-time updates
        public async Task TaskUpdated(object task)
        {
            var userId = Context.User?.FindFirst("uid")?.Value ?? Context.User?.FindFirst("sub")?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                await Clients.Group($"user-{userId}").SendAsync("TaskUpdated", task);
            }
        }

        public async Task EventReminder(object reminder)
        {
            var userId = Context.User?.FindFirst("uid")?.Value ?? Context.User?.FindFirst("sub")?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                await Clients.Group($"user-{userId}").SendAsync("EventReminder", reminder);
            }
        }

        public async Task NewMessage(object message)
        {
            var userId = Context.User?.FindFirst("uid")?.Value ?? Context.User?.FindFirst("sub")?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                await Clients.Group($"user-{userId}").SendAsync("NewMessage", message);
            }
        }

        public async Task ProactiveNotification(object notification)
        {
            var userId = Context.User?.FindFirst("uid")?.Value ?? Context.User?.FindFirst("sub")?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                await Clients.Group($"user-{userId}").SendAsync("ProactiveNotification", notification);
            }
        }

        public async Task StatsUpdated(object stats)
        {
            var userId = Context.User?.FindFirst("uid")?.Value ?? Context.User?.FindFirst("sub")?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                await Clients.Group($"user-{userId}").SendAsync("StatsUpdated", stats);
            }
        }
    }
}