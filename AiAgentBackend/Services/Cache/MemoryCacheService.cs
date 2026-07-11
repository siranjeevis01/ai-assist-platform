using Microsoft.Extensions.Caching.Memory;

namespace AiAgentBackend.Services.Cache
{
    public interface ICacheService
    {
        T? Get<T>(string key);
        void Set<T>(string key, T value, TimeSpan? expiry = null);
        void Remove(string key);
        bool Exists(string key);
        void Clear();
    }

    public class MemoryCacheService : ICacheService
    {
        private readonly IMemoryCache _cache;
        private readonly ILogger<MemoryCacheService> _logger;
        private readonly HashSet<string> _keys = new();
        private readonly object _lock = new();

        public MemoryCacheService(IMemoryCache cache, ILogger<MemoryCacheService> logger)
        {
            _cache = cache;
            _logger = logger;
        }

        public T? Get<T>(string key)
        {
            try
            {
                if (_cache.TryGetValue(key, out T? value))
                {
                    _logger.LogDebug("Cache HIT for key: {Key}", key);
                    return value;
                }
                _logger.LogDebug("Cache MISS for key: {Key}", key);
                return default;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting cache key: {Key}", key);
                return default;
            }
        }

        public void Set<T>(string key, T value, TimeSpan? expiry = null)
        {
            try
            {
                var options = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = expiry ?? TimeSpan.FromMinutes(5),
                    SlidingExpiration = TimeSpan.FromMinutes(2),
                    Priority = CacheItemPriority.Normal
                };

                options.RegisterPostEvictionCallback((evictedKey, evictedValue, reason, state) =>
                {
                    lock (_lock)
                    {
                        _keys.Remove(evictedKey.ToString()!);
                    }
                    _logger.LogDebug("Cache EVICTED key: {Key}, reason: {Reason}", evictedKey, reason);
                });

                _cache.Set(key, value, options);
                lock (_lock)
                {
                    _keys.Add(key);
                }
                _logger.LogDebug("Cache SET key: {Key}, expires in {Expiry}", key, expiry ?? TimeSpan.FromMinutes(5));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error setting cache key: {Key}", key);
            }
        }

        public void Remove(string key)
        {
            try
            {
                _cache.Remove(key);
                lock (_lock)
                {
                    _keys.Remove(key);
                }
                _logger.LogDebug("Cache REMOVED key: {Key}", key);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error removing cache key: {Key}", key);
            }
        }

        public bool Exists(string key)
        {
            return _cache.TryGetValue(key, out _);
        }

        public void Clear()
        {
            try
            {
                lock (_lock)
                {
                    foreach (var key in _keys)
                    {
                        _cache.Remove(key);
                    }
                    _keys.Clear();
                }
                _logger.LogInformation("Cache CLEARED - all entries removed");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error clearing cache");
            }
        }
    }
}
