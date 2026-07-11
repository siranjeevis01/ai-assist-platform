namespace AiAgentBackend.Services.PushNotification
{
    public interface IPushNotificationService
    {
        Task RegisterDeviceAsync(string userId, string token, string platform);
        Task UnregisterDeviceAsync(string token);
        Task SendPushAsync(string userId, string title, string body, Dictionary<string, string>? data = null);
        Task SendPushToAllAsync(string title, string body, Dictionary<string, string>? data = null);
    }
}
