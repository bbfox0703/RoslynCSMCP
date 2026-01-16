using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace RoslynMcpServer.Core.Services
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

        // Circuit breaker for L2 cache (Redis)
        private CircuitBreakerState _l2CircuitState = CircuitBreakerState.Closed;
        private int _l2FailureCount = 0;
        private DateTime _l2LastFailureTime = DateTime.MinValue;
        private DateTime _l2CircuitOpenedTime = DateTime.MinValue;
        private readonly object _circuitLock = new object();

        // Circuit breaker configuration
        private const int FailureThreshold = 3; // Open circuit after 3 failures
        private static readonly TimeSpan CircuitOpenDuration = TimeSpan.FromMinutes(5); // Keep circuit open for 5 minutes
        private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(1); // Count failures within 1 minute

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

            // L2 Cache check (if available, with circuit breaker protection)
            value = await TryGetFromL2CacheAsync<T>(key);
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

            // Try to store in L2 cache with circuit breaker protection
            await TrySetToL2CacheAsync(key, value, l2Expiry);
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
        /// Checks if L2 cache should be attempted based on circuit breaker state
        /// </summary>
        private bool ShouldAttemptL2Cache()
        {
            lock (_circuitLock)
            {
                switch (_l2CircuitState)
                {
                    case CircuitBreakerState.Closed:
                        return true;

                    case CircuitBreakerState.Open:
                        // Check if we should transition to half-open
                        if (DateTime.UtcNow - _l2CircuitOpenedTime >= CircuitOpenDuration)
                        {
                            _l2CircuitState = CircuitBreakerState.HalfOpen;
                            _logger?.LogInformation("L2 cache circuit breaker transitioning to HalfOpen state");
                            return true;
                        }
                        return false;

                    case CircuitBreakerState.HalfOpen:
                        // Allow one attempt in half-open state
                        return true;

                    default:
                        return false;
                }
            }
        }

        /// <summary>
        /// Records a successful L2 cache operation
        /// </summary>
        private void RecordL2Success()
        {
            lock (_circuitLock)
            {
                if (_l2CircuitState == CircuitBreakerState.HalfOpen)
                {
                    _l2CircuitState = CircuitBreakerState.Closed;
                    _l2FailureCount = 0;
                    _logger?.LogInformation("L2 cache circuit breaker closed after successful recovery");
                }
                else if (_l2CircuitState == CircuitBreakerState.Closed)
                {
                    // Reset failure count on success
                    _l2FailureCount = 0;
                }
            }
        }

        /// <summary>
        /// Records a failed L2 cache operation
        /// </summary>
        private void RecordL2Failure(Exception ex)
        {
            lock (_circuitLock)
            {
                var now = DateTime.UtcNow;

                // Reset failure count if outside failure window
                if (now - _l2LastFailureTime > FailureWindow)
                {
                    _l2FailureCount = 0;
                }

                _l2FailureCount++;
                _l2LastFailureTime = now;

                _logger?.LogWarning(ex, "L2 cache operation failed. Failure count: {FailureCount}", _l2FailureCount);

                // Check if we should open the circuit
                if (_l2CircuitState == CircuitBreakerState.Closed && _l2FailureCount >= FailureThreshold)
                {
                    _l2CircuitState = CircuitBreakerState.Open;
                    _l2CircuitOpenedTime = now;
                    _logger?.LogError("L2 cache circuit breaker opened after {FailureCount} failures. Will retry after {Duration}",
                        _l2FailureCount, CircuitOpenDuration);
                }
                else if (_l2CircuitState == CircuitBreakerState.HalfOpen)
                {
                    // Failure in half-open state, reopen the circuit
                    _l2CircuitState = CircuitBreakerState.Open;
                    _l2CircuitOpenedTime = now;
                    _logger?.LogWarning("L2 cache circuit breaker reopened after failure in HalfOpen state");
                }
            }
        }

        /// <summary>
        /// Tries to get a value from L2 cache with circuit breaker protection
        /// </summary>
        private async Task<T?> TryGetFromL2CacheAsync<T>(string key)
        {
            if (_l2Cache == null || !ShouldAttemptL2Cache())
            {
                return default;
            }

            try
            {
                var serializedValue = await _l2Cache.GetStringAsync(key);
                if (serializedValue != null)
                {
                    var value = JsonSerializer.Deserialize<T>(serializedValue);
                    RecordL2Success();
                    return value;
                }
                RecordL2Success();
                return default;
            }
            catch (Exception ex)
            {
                RecordL2Failure(ex);
                return default;
            }
        }

        /// <summary>
        /// Tries to set a value to L2 cache with circuit breaker protection
        /// </summary>
        private async Task<bool> TrySetToL2CacheAsync<T>(string key, T value, TimeSpan? expiry)
        {
            if (_l2Cache == null || !ShouldAttemptL2Cache())
            {
                return false;
            }

            try
            {
                var serializedValue = JsonSerializer.Serialize(value);
                await _l2Cache.SetStringAsync(key, serializedValue, new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = expiry ?? TimeSpan.FromHours(1)
                });
                RecordL2Success();
                return true;
            }
            catch (Exception ex)
            {
                RecordL2Failure(ex);
                return false;
            }
        }

        /// <summary>
        /// Gets cache statistics
        /// </summary>
        public CacheStatistics GetStatistics()
        {
            lock (_circuitLock)
            {
                return new CacheStatistics
                {
                    CurrentSizeBytes = CurrentCacheSize,
                    CurrentSizeMB = CurrentCacheSize / 1024.0 / 1024.0,
                    MaxSizeBytes = MaxL1CacheSize,
                    MaxSizeMB = MaxL1CacheSize / 1024.0 / 1024.0,
                    UsagePercentage = (double)CurrentCacheSize / MaxL1CacheSize * 100,
                    IsNearLimit = CurrentCacheSize > WarningThreshold,
                    L2CircuitState = _l2CircuitState.ToString(),
                    L2FailureCount = _l2FailureCount,
                    L2LastFailureTime = _l2LastFailureTime == DateTime.MinValue ? null : _l2LastFailureTime
                };
            }
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
        public string L2CircuitState { get; set; } = string.Empty;
        public int L2FailureCount { get; set; }
        public DateTime? L2LastFailureTime { get; set; }
    }

    /// <summary>
    /// Circuit breaker state for distributed cache
    /// </summary>
    public enum CircuitBreakerState
    {
        Closed,    // Normal operation, L2 cache is being used
        Open,      // Too many failures, L2 cache is bypassed
        HalfOpen   // Testing if L2 cache has recovered
    }
}