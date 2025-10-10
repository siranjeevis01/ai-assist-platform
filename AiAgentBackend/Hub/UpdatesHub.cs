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

        public async Task SendUpdate(string userId, object data)
        {
            await Clients.Group($"user-{userId}").SendAsync("ReceiveUpdate", data);
        }

        public async Task SubscribeToUser()
        {
            var userId = Context.User?.FindFirst("uid")?.Value ?? Context.User?.FindFirst("sub")?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"user-{userId}");
                _logger.LogInformation("User {UserId} subscribed to updates", userId);
            }
        }

        public async Task UnsubscribeFromUser()
        {
            var userId = Context.User?.FindFirst("uid")?.Value ?? Context.User?.FindFirst("sub")?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user-{userId}");
                _logger.LogInformation("User {UserId} unsubscribed from updates", userId);
            }
        }
    }
}