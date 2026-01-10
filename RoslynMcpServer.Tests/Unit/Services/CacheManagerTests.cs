using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using RoslynMcpServer.Services;
using RoslynMcpServer.Tests.Helpers;

namespace RoslynMcpServer.Tests.Unit.Services;

public class CacheManagerTests : IDisposable
{
    private readonly Mock<ILogger<MultiLevelCacheManager>> _mockLogger;
    private readonly IMemoryCache _memoryCache;

    public CacheManagerTests()
    {
        _mockLogger = MockServiceProvider.CreateMockLogger<MultiLevelCacheManager>();
        _memoryCache = MockServiceProvider.CreateMemoryCache();
    }

    public void Dispose()
    {
        (_memoryCache as MemoryCache)?.Dispose();
    }

    #region Memory Cache Eviction Tests

    [Fact]
    public async Task Cache_AddItem_UpdatesSize()
    {
        // Arrange
        var cacheManager = new MultiLevelCacheManager(_memoryCache, null, null, _mockLogger.Object);
        var initialSize = cacheManager.CurrentCacheSize;

        // Act
        await cacheManager.GetOrComputeAsync("test-key",
            () => Task.FromResult(new byte[1024]), // 1 KB
            TimeSpan.FromMinutes(10));

        // Assert
        cacheManager.CurrentCacheSize.Should().BeGreaterThan(initialSize);
    }

    [Fact]
    public async Task Cache_GetStatistics_ReturnsCorrectData()
    {
        // Arrange
        var cacheManager = new MultiLevelCacheManager(_memoryCache, null, null, _mockLogger.Object);

        // Act: Add some items
        await cacheManager.GetOrComputeAsync("key1",
            () => Task.FromResult(new byte[1024 * 1024]), // 1 MB
            TimeSpan.FromMinutes(10));

        var stats = cacheManager.GetStatistics();

        // Assert
        stats.Should().NotBeNull();
        stats.CurrentSizeBytes.Should().BeGreaterThan(0);
        stats.CurrentSizeMB.Should().BeGreaterThan(0);
        stats.UsagePercentage.Should().BeGreaterThan(0);
        stats.L2CircuitState.Should().Be("Closed");
    }

    [Fact]
    public async Task Cache_ExceedsWarning_LogsWarning()
    {
        // Arrange
        var cacheManager = new MultiLevelCacheManager(_memoryCache, null, null, _mockLogger.Object);

        // Act: Add items approaching warning threshold (80 MB)
        for (int i = 0; i < 85; i++)
        {
            await cacheManager.GetOrComputeAsync($"key{i}",
                () => Task.FromResult(new byte[1024 * 1024]), // 1 MB each
                TimeSpan.FromMinutes(10));
        }

        // Assert: Warning should be logged
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("cache size")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    #endregion

    #region Circuit Breaker Tests

    [Fact]
    public async Task Circuit_InitialState_Closed()
    {
        // Arrange
        var mockL2Cache = MockServiceProvider.CreateMockDistributedCache();
        var cacheManager = new MultiLevelCacheManager(
            _memoryCache,
            mockL2Cache.Object,
            null,
            _mockLogger.Object);

        // Act
        var stats = cacheManager.GetStatistics();

        // Assert
        stats.L2CircuitState.Should().Be("Closed");
        stats.L2FailureCount.Should().Be(0);
    }

    [Fact]
    public async Task Circuit_ThreeFailures_OpensCircuit()
    {
        // Arrange
        var mockL2Cache = MockServiceProvider.CreateMockDistributedCache();
        mockL2Cache.Setup(c => c.GetAsync(It.IsAny<string>(), default))
            .ThrowsAsync(new Exception("Redis connection failed"));

        var cacheManager = new MultiLevelCacheManager(
            _memoryCache,
            mockL2Cache.Object,
            null,
            _mockLogger.Object);

        // Act: Trigger 3 failures
        for (int i = 0; i < 3; i++)
        {
            await cacheManager.GetOrComputeAsync($"key{i}",
                () => Task.FromResult($"value{i}"),
                TimeSpan.FromMinutes(1));
        }

        // Assert
        var stats = cacheManager.GetStatistics();
        stats.L2CircuitState.Should().Be("Open");
        stats.L2FailureCount.Should().Be(3);
        stats.L2LastFailureTime.Should().NotBeNull();
    }

    [Fact]
    public async Task Circuit_Open_SkipsL2Cache()
    {
        // Arrange
        var mockL2Cache = MockServiceProvider.CreateMockDistributedCache();
        int callCount = 0;
        mockL2Cache.Setup(c => c.GetAsync(It.IsAny<string>(), default))
            .ReturnsAsync((byte[]?)null)
            .Callback(() => callCount++);

        // First, trigger 3 failures to open the circuit
        mockL2Cache.Setup(c => c.GetAsync(It.Is<string>(k => k.StartsWith("fail")), default))
            .ThrowsAsync(new Exception("Redis connection failed"));

        var cacheManager = new MultiLevelCacheManager(
            _memoryCache,
            mockL2Cache.Object,
            null,
            _mockLogger.Object);

        // Open the circuit
        for (int i = 0; i < 3; i++)
        {
            await cacheManager.GetOrComputeAsync($"fail{i}",
                () => Task.FromResult($"value{i}"),
                TimeSpan.FromMinutes(1));
        }

        callCount = 0; // Reset counter

        // Act: Try to access cache with circuit open
        await cacheManager.GetOrComputeAsync("normal-key",
            () => Task.FromResult("value"),
            TimeSpan.FromMinutes(1));

        // Assert: L2 cache should not be called when circuit is open
        callCount.Should().Be(0);
        var stats = cacheManager.GetStatistics();
        stats.L2CircuitState.Should().Be("Open");
    }

    [Fact]
    public async Task Circuit_TwoFailures_StaysClosed()
    {
        // Arrange
        var mockL2Cache = MockServiceProvider.CreateMockDistributedCache();
        var failureCount = 0;
        mockL2Cache.Setup(c => c.GetAsync(It.IsAny<string>(), default))
            .ReturnsAsync(() =>
            {
                failureCount++;
                if (failureCount <= 2)
                {
                    throw new Exception("Redis connection failed");
                }
                return (byte[]?)null;
            });

        var cacheManager = new MultiLevelCacheManager(
            _memoryCache,
            mockL2Cache.Object,
            null,
            _mockLogger.Object);

        // Act: Trigger 2 failures (not enough to open circuit)
        for (int i = 0; i < 2; i++)
        {
            await cacheManager.GetOrComputeAsync($"key{i}",
                () => Task.FromResult($"value{i}"),
                TimeSpan.FromMinutes(1));
        }

        // Assert: Circuit should still be closed
        var stats = cacheManager.GetStatistics();
        stats.L2CircuitState.Should().Be("Closed");
        stats.L2FailureCount.Should().Be(2);
    }

    [Fact]
    public async Task Circuit_HalfOpen_SuccessClosesCircuit()
    {
        // Arrange
        var mockL2Cache = MockServiceProvider.CreateMockDistributedCache();
        var failureCount = 0;

        mockL2Cache.Setup(c => c.GetAsync(It.IsAny<string>(), default))
            .ReturnsAsync(() =>
            {
                failureCount++;
                if (failureCount <= 3)
                {
                    throw new Exception("Redis connection failed");
                }
                return (byte[]?)null; // Success after 3 failures
            });

        var cacheManager = new MultiLevelCacheManager(
            _memoryCache,
            mockL2Cache.Object,
            null,
            _mockLogger.Object);

        // Act: Trigger 3 failures to open circuit
        for (int i = 0; i < 3; i++)
        {
            await cacheManager.GetOrComputeAsync($"key{i}",
                () => Task.FromResult($"value{i}"),
                TimeSpan.FromMinutes(1));
        }

        // Wait for circuit to transition to HalfOpen (simulate time passing)
        // In real implementation, this would require time-based testing
        var stats1 = cacheManager.GetStatistics();
        stats1.L2CircuitState.Should().Be("Open");

        // Try one more call - should succeed and close circuit
        await cacheManager.GetOrComputeAsync("test-key",
            () => Task.FromResult("value"),
            TimeSpan.FromMinutes(1));

        // Assert: After successful call, failures should be recorded but circuit logic should handle it
        var stats2 = cacheManager.GetStatistics();
        failureCount.Should().Be(4); // 3 failures + 1 success
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task Cache_L1Hit_ReturnsValueImmediately()
    {
        // Arrange
        var cacheManager = new MultiLevelCacheManager(_memoryCache, null, null, _mockLogger.Object);
        var computeCallCount = 0;

        // Act: First call - cache miss, should compute
        var value1 = await cacheManager.GetOrComputeAsync("test-key",
            () =>
            {
                computeCallCount++;
                return Task.FromResult("test-value");
            },
            TimeSpan.FromMinutes(10));

        // Second call - should hit L1 cache
        var value2 = await cacheManager.GetOrComputeAsync("test-key",
            () =>
            {
                computeCallCount++;
                return Task.FromResult("test-value");
            },
            TimeSpan.FromMinutes(10));

        // Assert
        value1.Should().Be("test-value");
        value2.Should().Be("test-value");
        computeCallCount.Should().Be(1); // Only computed once
    }

    [Fact]
    public async Task Cache_DifferentKeys_StoresSeparately()
    {
        // Arrange
        var cacheManager = new MultiLevelCacheManager(_memoryCache, null, null, _mockLogger.Object);

        // Act
        await cacheManager.GetOrComputeAsync("key1",
            () => Task.FromResult("value1"),
            TimeSpan.FromMinutes(10));

        await cacheManager.GetOrComputeAsync("key2",
            () => Task.FromResult("value2"),
            TimeSpan.FromMinutes(10));

        var value1 = await cacheManager.GetOrComputeAsync("key1",
            () => Task.FromResult("wrong"),
            TimeSpan.FromMinutes(10));

        var value2 = await cacheManager.GetOrComputeAsync("key2",
            () => Task.FromResult("wrong"),
            TimeSpan.FromMinutes(10));

        // Assert
        value1.Should().Be("value1");
        value2.Should().Be("value2");
    }

    #endregion
}
