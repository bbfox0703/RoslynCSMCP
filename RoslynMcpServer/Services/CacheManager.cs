using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace RoslynMcpServer.Services
{
    public interface IPersistentCache
    {
        Task<T?> GetAsync<T>(string key);
        Task SetAsync<T>(string key, T value, TimeSpan? expiry = null);
        Task RemoveAsync(string key);
    }

    public class FilePersistentCache : IPersistentCache
    {
        private readonly string _cacheDirectory;
        private readonly ILogger<FilePersistentCache>? _logger;

        public FilePersistentCache(string cacheDirectory = "cache", ILogger<FilePersistentCache>? logger = null)
        {
            _cacheDirectory = cacheDirectory;
            _logger = logger;
            Directory.CreateDirectory(_cacheDirectory);
        }

        public async Task<T?> GetAsync<T>(string key)
        {
            var filePath = GetFilePath(key);
            if (!File.Exists(filePath))
                return default;

            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                return JsonSerializer.Deserialize<T>(json);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to deserialize cache entry for key: {Key}", key);
                return default;
            }
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
        {
            var filePath = GetFilePath(key);
            var json = JsonSerializer.Serialize(value);
            await File.WriteAllTextAsync(filePath, json);
        }

        public Task RemoveAsync(string key)
        {
            var filePath = GetFilePath(key);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
            return Task.CompletedTask;
        }

        private string GetFilePath(string key)
        {
            var safeKey = string.Join("_", key.Split(Path.GetInvalidFileNameChars()));
            return Path.Combine(_cacheDirectory, $"{safeKey}.cache");
        }
    }

    public class MultiLevelCacheManager
    {
        private readonly IMemoryCache _l1Cache; // Hot data - in memory
        private readonly IDistributedCache? _l2Cache; // Warm data - Redis/SQL (optional)
        private readonly IPersistentCache _l3Cache; // Cold data - file system
        private readonly ILogger<MultiLevelCacheManager>? _logger;

        // Cache size limits (in bytes)
        private const long MaxL1CacheSize = 100 * 1024 * 1024; // 100 MB
        private const long WarningThreshold = 80 * 1024 * 1024; // 80 MB
        private long _currentCacheSize = 0;
        private readonly object _sizeLock = new object();

        public MultiLevelCacheManager(
            IMemoryCache memoryCache,
            IDistributedCache? distributedCache = null,
            IPersistentCache? persistentCache = null,
            ILogger<MultiLevelCacheManager>? logger = null)
        {
            _l1Cache = memoryCache;
            _l2Cache = distributedCache;
            _l3Cache = persistentCache ?? new FilePersistentCache();
            _logger = logger;
        }

        /// <summary>
        /// Gets the current L1 cache size estimate in bytes
        /// </summary>
        public long CurrentCacheSize
        {
            get
            {
                lock (_sizeLock)
                {
                    return _currentCacheSize;
                }
            }
        }
        
        public async Task<T?> GetOrComputeAsync<T>(
            string key,
            Func<Task<T>> computeFunc,
            TimeSpan? l1Expiry = null,
            TimeSpan? l2Expiry = null)
        {
            // L1 Cache check
            if (_l1Cache.TryGetValue(key, out T? value) && value != null)
            {
                _logger?.LogDebug("L1 cache hit: {Key}", key);
                return value;
            }

            // L2 Cache check (if available)
            if (_l2Cache != null)
            {
                var serializedValue = await _l2Cache.GetStringAsync(key);
                if (serializedValue != null)
                {
                    value = JsonSerializer.Deserialize<T>(serializedValue);
                    if (value != null)
                    {
                        _logger?.LogDebug("L2 cache hit: {Key}", key);

                        // Store in L1 with size tracking
                        CheckAndCompactCache();
                        long estimatedSize = EstimateObjectSize(value);
                        var cacheEntryOptions = new MemoryCacheEntryOptions()
                            .SetAbsoluteExpiration(l1Expiry ?? TimeSpan.FromMinutes(10))
                            .SetSize(estimatedSize)
                            .RegisterPostEvictionCallback((k, v, reason, state) =>
                            {
                                if (reason != EvictionReason.Replaced)
                                {
                                    lock (_sizeLock)
                                    {
                                        _currentCacheSize -= estimatedSize;
                                    }
                                }
                            });

                        _l1Cache.Set(key, value, cacheEntryOptions);
                        lock (_sizeLock)
                        {
                            _currentCacheSize += estimatedSize;
                        }

                        return value;
                    }
                }
            }

            // L3 Persistent cache check
            value = await _l3Cache.GetAsync<T>(key);
            if (value != null)
            {
                _logger?.LogDebug("L3 cache hit: {Key}", key);
                await StoreInUpperCaches(key, value, l1Expiry, l2Expiry);
                return value;
            }

            // Cache miss - compute and store at all levels
            _logger?.LogDebug("Cache miss: {Key}. Computing value...", key);
            value = await computeFunc();
            if (value != null)
            {
                await StoreInAllCaches(key, value, l1Expiry, l2Expiry);
            }

            return value;
        }
        
        private async Task StoreInUpperCaches<T>(string key, T value, TimeSpan? l1Expiry, TimeSpan? l2Expiry)
        {
            // Check if we need to compact cache before adding new item
            CheckAndCompactCache();

            // Estimate size and store in L1 with size tracking
            long estimatedSize = EstimateObjectSize(value);
            var cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(l1Expiry ?? TimeSpan.FromMinutes(10))
                .SetSize(estimatedSize)
                .RegisterPostEvictionCallback((k, v, reason, state) =>
                {
                    // Update size when item is evicted
                    if (reason != EvictionReason.Replaced)
                    {
                        lock (_sizeLock)
                        {
                            _currentCacheSize -= estimatedSize;
                        }
                        _logger?.LogDebug("Cache item evicted: {Key}, Reason: {Reason}, Size: {Size} bytes",
                            k, reason, estimatedSize);
                    }
                });

            _l1Cache.Set(key, value, cacheEntryOptions);

            // Track size
            lock (_sizeLock)
            {
                _currentCacheSize += estimatedSize;
            }

            _logger?.LogDebug("Added to L1 cache: {Key}, Size: {Size} bytes, Total: {Total} bytes",
                key, estimatedSize, CurrentCacheSize);

            if (_l2Cache != null)
            {
                var serializedValue = JsonSerializer.Serialize(value);
                await _l2Cache.SetStringAsync(key, serializedValue, new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = l2Expiry ?? TimeSpan.FromHours(1)
                });
            }
        }
        
        private async Task StoreInAllCaches<T>(string key, T value, TimeSpan? l1Expiry, TimeSpan? l2Expiry)
        {
            await StoreInUpperCaches(key, value, l1Expiry, l2Expiry);
            await _l3Cache.SetAsync(key, value, TimeSpan.FromDays(7));
        }
        
        public async Task InvalidateAsync(string keyPattern)
        {
            // For simplicity, this implementation removes exact keys
            // A more sophisticated implementation would support pattern matching
            _l1Cache.Remove(keyPattern);

            if (_l2Cache != null)
            {
                await _l2Cache.RemoveAsync(keyPattern);
            }

            await _l3Cache.RemoveAsync(keyPattern);
        }

        /// <summary>
        /// Estimates the size of an object in bytes using JSON serialization
        /// </summary>
        private long EstimateObjectSize<T>(T value)
        {
            try
            {
                // Serialize to estimate size
                var json = JsonSerializer.Serialize(value);
                // Each char is approximately 2 bytes (UTF-16), plus overhead
                return json.Length * 2 + 100; // Add 100 bytes for object overhead
            }
            catch
            {
                // Fallback: use a conservative estimate
                return 1024; // 1 KB default
            }
        }

        /// <summary>
        /// Checks cache size and compacts if necessary
        /// </summary>
        private void CheckAndCompactCache()
        {
            long currentSize;
            lock (_sizeLock)
            {
                currentSize = _currentCacheSize;
            }

            // Warning threshold check
            if (currentSize > WarningThreshold && currentSize <= MaxL1CacheSize)
            {
                _logger?.LogWarning("L1 cache size approaching limit: {CurrentSize} MB / {MaxSize} MB",
                    currentSize / 1024 / 1024, MaxL1CacheSize / 1024 / 1024);
            }

            // Compact if over limit
            if (currentSize > MaxL1CacheSize)
            {
                _logger?.LogWarning("L1 cache size exceeded limit: {CurrentSize} MB / {MaxSize} MB. Compacting...",
                    currentSize / 1024 / 1024, MaxL1CacheSize / 1024 / 1024);

                CompactCache(0.25); // Remove 25% of cache

                lock (_sizeLock)
                {
                    currentSize = _currentCacheSize;
                }

                _logger?.LogInformation("Cache compacted. New size: {CurrentSize} MB",
                    currentSize / 1024 / 1024);
            }
        }

        /// <summary>
        /// Compacts the cache by evicting a percentage of entries
        /// </summary>
        /// <param name="percentage">Percentage to compact (0.0 to 1.0)</param>
        private void CompactCache(double percentage)
        {
            if (_l1Cache is MemoryCache memoryCache)
            {
                // MemoryCache.Compact removes the specified percentage
                memoryCache.Compact(percentage);
            }
            else
            {
                _logger?.LogWarning("L1 cache does not support compaction. Consider using MemoryCache.");
            }
        }

        /// <summary>
        /// Gets cache statistics
        /// </summary>
        public CacheStatistics GetStatistics()
        {
            return new CacheStatistics
            {
                CurrentSizeBytes = CurrentCacheSize,
                CurrentSizeMB = CurrentCacheSize / 1024.0 / 1024.0,
                MaxSizeBytes = MaxL1CacheSize,
                MaxSizeMB = MaxL1CacheSize / 1024.0 / 1024.0,
                UsagePercentage = (double)CurrentCacheSize / MaxL1CacheSize * 100,
                IsNearLimit = CurrentCacheSize > WarningThreshold
            };
        }
    }

    /// <summary>
    /// Cache statistics information
    /// </summary>
    public class CacheStatistics
    {
        public long CurrentSizeBytes { get; set; }
        public double CurrentSizeMB { get; set; }
        public long MaxSizeBytes { get; set; }
        public double MaxSizeMB { get; set; }
        public double UsagePercentage { get; set; }
        public bool IsNearLimit { get; set; }
    }
}