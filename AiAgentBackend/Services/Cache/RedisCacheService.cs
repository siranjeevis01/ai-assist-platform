using StackExchange.Redis;
using System.Text.Json;

namespace AiAgentBackend.Services.Cache
{
    public class RedisCacheService : ICacheService, IAsyncDisposable
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly IDatabase _db;
        private readonly ILogger<RedisCacheService> _logger;
        private const string KeyPrefix = "aia:";

        public RedisCacheService(IConnectionMultiplexer redis, ILogger<RedisCacheService> logger)
        {
            _redis = redis;
            _db = redis.GetDatabase();
            _logger = logger;
        }

        public T? Get<T>(string key)
        {
            try
            {
                var val = _db.StringGet(KeyPrefix + key);
                if (val.HasValue)
                {
                    _logger.LogDebug("Redis CACHE HIT for key: {Key}", key);
                    return JsonSerializer.Deserialize<T>(val!);
                }
                _logger.LogDebug("Redis CACHE MISS for key: {Key}", key);
                return default;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting Redis cache key: {Key}", key);
                return default;
            }
        }

        public void Set<T>(string key, T value, TimeSpan? expiry = null)
        {
            try
            {
                var json = JsonSerializer.Serialize(value);
                _db.StringSet(KeyPrefix + key, json, expiry ?? TimeSpan.FromMinutes(5));
                _logger.LogDebug("Redis CACHE SET key: {Key}, expires in {Expiry}", key, expiry ?? TimeSpan.FromMinutes(5));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error setting Redis cache key: {Key}", key);
            }
        }

        public void Remove(string key)
        {
            try
            {
                _db.KeyDelete(KeyPrefix + key);
                _logger.LogDebug("Redis CACHE REMOVED key: {Key}", key);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error removing Redis cache key: {Key}", key);
            }
        }

        public bool Exists(string key)
        {
            try
            {
                return _db.KeyExists(KeyPrefix + key);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error checking Redis cache key: {Key}", key);
                return false;
            }
        }

        public void Clear()
        {
            try
            {
                var server = _redis.GetServer(_redis.GetEndPoints().First());
                var keys = server.Keys(pattern: KeyPrefix + "*");
                foreach (var key in keys)
                {
                    _db.KeyDelete(key);
                }
                _logger.LogInformation("Redis CACHE CLEARED - all keys removed");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error clearing Redis cache");
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_redis.IsConnected)
            {
                await _redis.CloseAsync();
            }
        }
    }
}
