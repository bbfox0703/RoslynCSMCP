using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Core.Models;

namespace RoslynMcpServer.Core.Services
{
    /// <summary>
    /// Provides cancellation support for MCP tool operations.
    /// Wraps operations with proper cancellation token handling and error reporting.
    /// </summary>
    public class CancellableOperation
    {
        private readonly CancellationManager _cancellationManager;
        private readonly McpErrorHandler _errorHandler;
        private readonly ILogger<CancellableOperation> _logger;

        public CancellableOperation(
            CancellationManager cancellationManager,
            McpErrorHandler errorHandler,
            ILogger<CancellableOperation> logger)
        {
            _cancellationManager = cancellationManager;
            _errorHandler = errorHandler;
            _logger = logger;
        }

        /// <summary>
        /// Executes a tool operation with cancellation support.
        /// </summary>
        /// <param name="operationName">Name of the operation for logging/errors</param>
        /// <param name="operation">The async operation to execute</param>
        /// <param name="timeout">Optional timeout (default: 5 minutes)</param>
        /// <returns>The operation result or an error message</returns>
        public async Task<string> ExecuteAsync(
            string operationName,
            Func<CancellationToken, Task<string>> operation,
            TimeSpan? timeout = null)
        {
            var requestId = GenerateRequestId(operationName);
            var ct = _cancellationManager.RegisterRequest(requestId, timeout ?? TimeSpan.FromMinutes(5));

            try
            {
                _logger.LogDebug("Starting cancellable operation {OperationName} with request ID {RequestId}",
                    operationName, requestId);

                var result = await operation(ct);

                _cancellationManager.CompleteRequest(requestId);
                return result;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                var state = _cancellationManager.GetRequestState(requestId);
                if (state == RequestState.Cancelled)
                {
                    _logger.LogInformation("Operation {OperationName} was cancelled by request", operationName);
                    return McpError.Create(
                        McpErrorCodes.OperationCancelled,
                        $"Operation '{operationName}' was cancelled",
                        new McpErrorData { Operation = operationName, Reason = "Cancelled by client request" }
                    ).ToToolResponse();
                }
                else
                {
                    _logger.LogWarning("Operation {OperationName} timed out", operationName);
                    return McpError.OperationTimeout(operationName, timeout).ToToolResponse();
                }
            }
            catch (Exception ex)
            {
                _cancellationManager.FailRequest(requestId, ex.Message);
                return _errorHandler.HandleException(ex, operationName);
            }
        }

        /// <summary>
        /// Executes a tool operation with cancellation support and returns a typed result.
        /// </summary>
        public async Task<McpResult<T>> ExecuteAsync<T>(
            string operationName,
            Func<CancellationToken, Task<T>> operation,
            TimeSpan? timeout = null)
        {
            var requestId = GenerateRequestId(operationName);
            var ct = _cancellationManager.RegisterRequest(requestId, timeout ?? TimeSpan.FromMinutes(5));

            try
            {
                _logger.LogDebug("Starting cancellable operation {OperationName} with request ID {RequestId}",
                    operationName, requestId);

                var result = await operation(ct);

                _cancellationManager.CompleteRequest(requestId);
                return McpResult<T>.Success(result);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                var state = _cancellationManager.GetRequestState(requestId);
                var error = state == RequestState.Cancelled
                    ? McpError.Create(McpErrorCodes.OperationCancelled, $"Operation '{operationName}' was cancelled")
                    : McpError.OperationTimeout(operationName, timeout);

                return McpResult<T>.Failure(error);
            }
            catch (Exception ex)
            {
                _cancellationManager.FailRequest(requestId, ex.Message);
                return _errorHandler.HandleException<T>(ex, operationName);
            }
        }

        /// <summary>
        /// Gets a cancellation token for the specified request ID.
        /// Creates a new registration if the request doesn't exist.
        /// </summary>
        public CancellationToken GetOrCreateToken(string requestId, TimeSpan? timeout = null)
        {
            return _cancellationManager.RegisterRequest(requestId, timeout ?? TimeSpan.FromMinutes(5));
        }

        /// <summary>
        /// Cancels a request by its ID.
        /// </summary>
        public bool Cancel(string requestId, string? reason = null)
        {
            return _cancellationManager.CancelRequest(requestId, reason);
        }

        /// <summary>
        /// Checks if a request has been cancelled.
        /// </summary>
        public bool IsCancelled(string requestId)
        {
            return _cancellationManager.IsCancelled(requestId);
        }

        private static string GenerateRequestId(string operationName)
        {
            return $"{operationName}-{Guid.NewGuid():N}";
        }
    }

    /// <summary>
    /// Extension methods for creating cancellable operations in tools.
    /// </summary>
    public static class CancellableOperationExtensions
    {
        /// <summary>
        /// Gets or creates a CancellableOperation from the service provider.
        /// </summary>
        public static CancellableOperation? GetCancellableOperation(this IServiceProvider? serviceProvider)
        {
            return serviceProvider?.GetService<CancellableOperation>();
        }

        /// <summary>
        /// Creates a cancellation token with the specified timeout.
        /// Falls back to creating a local CancellationTokenSource if service is unavailable.
        /// </summary>
        public static (CancellationToken Token, IDisposable? Disposable) CreateCancellationToken(
            this IServiceProvider? serviceProvider,
            string operationName,
            TimeSpan? timeout = null)
        {
            var cancellationManager = serviceProvider?.GetService<CancellationManager>();

            if (cancellationManager != null)
            {
                var requestId = $"{operationName}-{Guid.NewGuid():N}";
                var token = cancellationManager.RegisterRequest(requestId, timeout ?? TimeSpan.FromMinutes(5));
                return (token, new CancellationCleanup(cancellationManager, requestId));
            }

            // Fallback to local CTS if service unavailable
            var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(5));
            return (cts.Token, cts);
        }

        /// <summary>
        /// Executes an operation with cancellation support.
        /// Uses CancellableOperation service if available, otherwise executes directly with local CTS.
        /// </summary>
        public static async Task<string> ExecuteCancellableAsync(
            this IServiceProvider? serviceProvider,
            string operationName,
            Func<CancellationToken, Task<string>> operation,
            TimeSpan? timeout = null)
        {
            var cancellableOp = serviceProvider?.GetService<CancellableOperation>();

            if (cancellableOp != null)
            {
                return await cancellableOp.ExecuteAsync(operationName, operation, timeout);
            }

            // Fallback
            using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(5));
            try
            {
                return await operation(cts.Token);
            }
            catch (OperationCanceledException)
            {
                return McpError.OperationTimeout(operationName, timeout).ToToolResponse();
            }
            catch (Exception ex)
            {
                return McpError.FromException(ex).ToToolResponse();
            }
        }

        private class CancellationCleanup : IDisposable
        {
            private readonly CancellationManager _manager;
            private readonly string _requestId;
            private bool _disposed;

            public CancellationCleanup(CancellationManager manager, string requestId)
            {
                _manager = manager;
                _requestId = requestId;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _manager.CompleteRequest(_requestId);
            }
        }
    }

    /// <summary>
    /// Context for a cancellable operation, providing cancellation token and cleanup.
    /// </summary>
    public class CancellationContext : IDisposable
    {
        private readonly CancellationManager? _manager;
        private readonly CancellationTokenSource? _localCts;
        private readonly string _requestId;
        private bool _disposed;

        /// <summary>
        /// The cancellation token for this operation.
        /// </summary>
        public CancellationToken Token { get; }

        /// <summary>
        /// The request ID for this operation.
        /// </summary>
        public string RequestId => _requestId;

        private CancellationContext(
            CancellationToken token,
            string requestId,
            CancellationManager? manager = null,
            CancellationTokenSource? localCts = null)
        {
            Token = token;
            _requestId = requestId;
            _manager = manager;
            _localCts = localCts;
        }

        /// <summary>
        /// Creates a cancellation context from a service provider.
        /// </summary>
        public static CancellationContext Create(
            IServiceProvider? serviceProvider,
            string operationName,
            TimeSpan? timeout = null)
        {
            var manager = serviceProvider?.GetService<CancellationManager>();
            var effectiveTimeout = timeout ?? TimeSpan.FromMinutes(5);
            var requestId = $"{operationName}-{Guid.NewGuid():N}";

            if (manager != null)
            {
                var token = manager.RegisterRequest(requestId, effectiveTimeout);
                return new CancellationContext(token, requestId, manager);
            }

            // Fallback to local CTS
            var cts = new CancellationTokenSource(effectiveTimeout);
            return new CancellationContext(cts.Token, requestId, localCts: cts);
        }

        /// <summary>
        /// Marks the operation as completed.
        /// </summary>
        public void Complete()
        {
            _manager?.CompleteRequest(_requestId);
        }

        /// <summary>
        /// Marks the operation as failed.
        /// </summary>
        public void Fail(string? error = null)
        {
            _manager?.FailRequest(_requestId, error);
        }

        /// <summary>
        /// Checks if the operation was cancelled.
        /// </summary>
        public bool IsCancelled => _manager?.IsCancelled(_requestId) ?? Token.IsCancellationRequested;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _localCts?.Dispose();
            // Don't auto-complete on dispose - let the caller decide the final state
        }
    }
}
