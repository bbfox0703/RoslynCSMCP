using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using RoslynMcpServer.Core.Services;
using RoslynMcpServer.Tests.Helpers;
using System.Text;
using System.Text.Json;

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
        var mockL3Cache = new Mock<IPersistentCache>();
        mockL3Cache.Setup(c => c.GetAsync<byte[]>(It.IsAny<string>()))
            .Returns(Task.FromResult<byte[]?>(null));
        mockL3Cache.Setup(c => c.GetAsync<string>(It.IsAny<string>()))
            .Returns(Task.FromResult<string?>(null));

        var cacheManager = new MultiLevelCacheManager(_memoryCache, null, mockL3Cache.Object, _mockLogger.Object);
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
        var mockL3Cache = new Mock<IPersistentCache>();
        mockL3Cache.Setup(c => c.GetAsync<byte[]>(It.IsAny<string>()))
            .Returns(Task.FromResult<byte[]?>(null));
        mockL3Cache.Setup(c => c.GetAsync<string>(It.IsAny<string>()))
            .Returns(Task.FromResult<string?>(null));

        var cacheManager = new MultiLevelCacheManager(_memoryCache, null, mockL3Cache.Object, _mockLogger.Object);

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
        var mockL3Cache = new Mock<IPersistentCache>();
        mockL3Cache.Setup(c => c.GetAsync<byte[]>(It.IsAny<string>()))
            .Returns(Task.FromResult<byte[]?>(null));
        mockL3Cache.Setup(c => c.GetAsync<string>(It.IsAny<string>()))
            .Returns(Task.FromResult<string?>(null));

        var cacheManager = new MultiLevelCacheManager(_memoryCache, null, mockL3Cache.Object, _mockLogger.Object);

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
        var mockL3Cache = new Mock<IPersistentCache>();

        var cacheManager = new MultiLevelCacheManager(
            _memoryCache,
            mockL2Cache.Object,
            mockL3Cache.Object,
            _mockLogger.Object);

        // Act
        var stats = cacheManager.GetStatistics();

        // Assert
        stats.L2CircuitState.Should().Be("Closed");
        stats.L2FailureCount.Should().Be(0);
    }

    [Fact]
    public async Task Circuit_Open_SkipsL2Cache()
    {
        // Arrange
        var mockL2Cache = MockServiceProvider.CreateMockDistributedCache();
        var mockL3Cache = new Mock<IPersistentCache>();
        mockL3Cache.Setup(c => c.GetAsync<byte[]>(It.IsAny<string>()))
            .Returns(Task.FromResult<byte[]?>(null));
        mockL3Cache.Setup(c => c.GetAsync<string>(It.IsAny<string>()))
            .Returns(Task.FromResult<string?>(null));

        int getCallCount = 0;
        int setCallCount = 0;

        // Setup to fail first 3 calls
        mockL2Cache.Setup(c => c.GetAsync(It.IsAny<string>(), default))
            .ReturnsAsync(() =>
            {
                getCallCount++;
                if (getCallCount <= 3)
                {
                    throw new Exception("Redis connection failed");
                }
                return (byte[]?)null;
            });

        mockL2Cache.Setup(c => c.SetAsync(
            It.IsAny<string>(),
            It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(),
            default))
            .Returns(() =>
            {
                setCallCount++;
                if (setCallCount <= 3)
                {
                    throw new Exception("Redis connection failed");
                }
                return Task.CompletedTask;
            });

        var cacheManager = new MultiLevelCacheManager(
            _memoryCache,
            mockL2Cache.Object,
            mockL3Cache.Object,
            _mockLogger.Object);

        // Open the circuit by causing 3 failures
        for (int i = 0; i < 3; i++)
        {
            await cacheManager.GetOrComputeAsync($"fail{i}",
                () => Task.FromResult($"value{i}"),
                TimeSpan.FromMinutes(1));
        }

        getCallCount = 0; // Reset counter
        setCallCount = 0;

        // Act: Try to access cache with circuit open
        await cacheManager.GetOrComputeAsync("normal-key",
            () => Task.FromResult("value"),
            TimeSpan.FromMinutes(1));

        // Assert: L2 cache should not be called when circuit is open
        // The circuit is open, so it should skip L2 entirely
        getCallCount.Should().Be(0);
        setCallCount.Should().Be(0);

        var stats = cacheManager.GetStatistics();
        stats.L2CircuitState.Should().Be("Open");
    }

    [Fact]
    public async Task Circuit_SuccessResetsFailureCount()
    {
        // Arrange
        var mockL2Cache = MockServiceProvider.CreateMockDistributedCache();
        var mockL3Cache = new Mock<IPersistentCache>();
        mockL3Cache.Setup(c => c.GetAsync<byte[]>(It.IsAny<string>()))
            .Returns(Task.FromResult<byte[]?>(null));
        mockL3Cache.Setup(c => c.GetAsync<string>(It.IsAny<string>()))
            .Returns(Task.FromResult<string?>(null));

        var callCount = 0;

        mockL2Cache.Setup(c => c.GetAsync(It.IsAny<string>(), default))
            .ReturnsAsync(() =>
            {
                callCount++;
                // First 2 calls fail, third succeeds
                if (callCount <= 2)
                {
                    throw new Exception("Redis connection failed");
                }
                return (byte[]?)null; // Success (no value found)
            });

        var cacheManager = new MultiLevelCacheManager(
            _memoryCache,
            mockL2Cache.Object,
            mockL3Cache.Object,
            _mockLogger.Object);

        // Act: Trigger 2 failures, then 1 success
        for (int i = 0; i < 3; i++)
        {
            await cacheManager.GetOrComputeAsync($"key{i}",
                () => Task.FromResult($"value{i}"),
                TimeSpan.FromMinutes(1));
        }

        // Assert: Success should reset failure count
        var stats = cacheManager.GetStatistics();
        stats.L2CircuitState.Should().Be("Closed");
        stats.L2FailureCount.Should().Be(0); // Reset after success
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task Cache_L1Hit_ReturnsValueImmediately()
    {
        // Arrange
        var mockL3Cache = new Mock<IPersistentCache>();
        mockL3Cache.Setup(c => c.GetAsync<byte[]>(It.IsAny<string>()))
            .Returns(Task.FromResult<byte[]?>(null));
        mockL3Cache.Setup(c => c.GetAsync<string>(It.IsAny<string>()))
            .Returns(Task.FromResult<string?>(null));

        var cacheManager = new MultiLevelCacheManager(_memoryCache, null, mockL3Cache.Object, _mockLogger.Object);
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
        var mockL3Cache = new Mock<IPersistentCache>();
        mockL3Cache.Setup(c => c.GetAsync<byte[]>(It.IsAny<string>()))
            .Returns(Task.FromResult<byte[]?>(null));
        mockL3Cache.Setup(c => c.GetAsync<string>(It.IsAny<string>()))
            .Returns(Task.FromResult<string?>(null));

        var cacheManager = new MultiLevelCacheManager(_memoryCache, null, mockL3Cache.Object, _mockLogger.Object);

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
