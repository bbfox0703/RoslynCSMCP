using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;

namespace RoslynMcpServer.Tests.Helpers;

/// <summary>
/// Helper class to create mock service providers for testing
/// </summary>
public class MockServiceProvider
{
    private readonly Dictionary<Type, object> _services = new();

    public MockServiceProvider()
    {
        // Register default mocks
        RegisterDefaultMocks();
    }

    private void RegisterDefaultMocks()
    {
        // Register a mock logger
        var mockLogger = new Mock<ILogger<object>>();
        Register(typeof(ILogger<>), mockLogger.Object);

        // Register a real memory cache
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        Register<IMemoryCache>(memoryCache);

        // Register a mock distributed cache
        var mockDistributedCache = new Mock<IDistributedCache>();
        Register<IDistributedCache>(mockDistributedCache.Object);
    }

    /// <summary>
    /// Registers a service
    /// </summary>
    public void Register<T>(T service) where T : class
    {
        _services[typeof(T)] = service;
    }

    /// <summary>
    /// Registers a service by type
    /// </summary>
    public void Register(Type serviceType, object service)
    {
        _services[serviceType] = service;
    }

    /// <summary>
    /// Gets a service
    /// </summary>
    public T? GetService<T>() where T : class
    {
        if (_services.TryGetValue(typeof(T), out var service))
        {
            return service as T;
        }

        return null;
    }

    /// <summary>
    /// Gets a service by type
    /// </summary>
    public object? GetService(Type serviceType)
    {
        // Handle generic logger types
        if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(ILogger<>))
        {
            var mockLoggerType = typeof(Mock<>).MakeGenericType(serviceType);
            var mockLogger = Activator.CreateInstance(mockLoggerType);
            var objectProperty = mockLoggerType.GetProperty("Object");
            return objectProperty?.GetValue(mockLogger);
        }

        if (_services.TryGetValue(serviceType, out var service))
        {
            return service;
        }

        return null;
    }

    /// <summary>
    /// Creates a mock IServiceProvider
    /// </summary>
    public IServiceProvider Build()
    {
        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider.Setup(sp => sp.GetService(It.IsAny<Type>()))
            .Returns<Type>(GetService);

        return mockServiceProvider.Object;
    }

    /// <summary>
    /// Creates a mock logger
    /// </summary>
    public static Mock<ILogger<T>> CreateMockLogger<T>()
    {
        return new Mock<ILogger<T>>();
    }

    /// <summary>
    /// Creates a real memory cache for testing
    /// </summary>
    public static IMemoryCache CreateMemoryCache(long? sizeLimit = null)
    {
        var options = new MemoryCacheOptions();
        if (sizeLimit.HasValue)
        {
            options.SizeLimit = sizeLimit.Value;
        }

        return new MemoryCache(options);
    }

    /// <summary>
    /// Creates a mock distributed cache
    /// </summary>
    public static Mock<IDistributedCache> CreateMockDistributedCache()
    {
        return new Mock<IDistributedCache>();
    }
}
