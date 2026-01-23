using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace RoslynMcpServer.Core.Services
{
    /// <summary>
    /// Manages cancellation tokens for MCP requests following the MCP specification.
    /// Supports the `notifications/cancelled` notification pattern.
    ///
    /// MCP Cancellation Spec: https://modelcontextprotocol.io/specification/2025-06-18/basic/utilities/cancellation
    /// </summary>
    public class CancellationManager : IDisposable
    {
        private readonly ConcurrentDictionary<string, CancellationEntry> _cancellationTokens = new();
        private readonly ILogger<CancellationManager> _logger;
        private readonly TimeSpan _defaultTimeout;
        private readonly Timer _cleanupTimer;
        private bool _disposed;

        /// <summary>
        /// Event raised when a request is cancelled
        /// </summary>
        public event EventHandler<RequestCancelledEventArgs>? RequestCancelled;

        public CancellationManager(ILogger<CancellationManager> logger)
        {
            _logger = logger;
            _defaultTimeout = TimeSpan.FromMinutes(5);

            // Cleanup expired entries every minute
            _cleanupTimer = new Timer(CleanupExpiredEntries, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        /// <summary>
        /// Registers a new request and returns a CancellationToken for it.
        /// </summary>
        /// <param name="requestId">Unique identifier for the request</param>
        /// <param name="timeout">Optional timeout for the request (defaults to 5 minutes)</param>
        /// <returns>A CancellationToken that will be triggered on cancellation or timeout</returns>
        public CancellationToken RegisterRequest(string requestId, TimeSpan? timeout = null)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                throw new ArgumentNullException(nameof(requestId));
            }

            var effectiveTimeout = timeout ?? _defaultTimeout;
            var cts = new CancellationTokenSource(effectiveTimeout);
            var entry = new CancellationEntry
            {
                TokenSource = cts,
                CreatedAt = DateTime.UtcNow,
                Timeout = effectiveTimeout,
                State = RequestState.Running
            };

            if (!_cancellationTokens.TryAdd(requestId, entry))
            {
                // Request ID already exists, cancel the old one and register new
                if (_cancellationTokens.TryRemove(requestId, out var oldEntry))
                {
                    oldEntry.TokenSource.Cancel();
                    oldEntry.TokenSource.Dispose();
                }
                _cancellationTokens.TryAdd(requestId, entry);
            }

            _logger.LogDebug("Registered request {RequestId} with timeout {Timeout}s", requestId, effectiveTimeout.TotalSeconds);
            return cts.Token;
        }

        /// <summary>
        /// Creates a linked cancellation token that combines request cancellation with an external token.
        /// </summary>
        public CancellationToken RegisterRequest(string requestId, CancellationToken externalToken, TimeSpan? timeout = null)
        {
            var requestToken = RegisterRequest(requestId, timeout);

            // Create a linked token source that cancels if either token is triggered
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(requestToken, externalToken);

            // Update the entry with the linked token source
            if (_cancellationTokens.TryGetValue(requestId, out var entry))
            {
                var oldCts = entry.TokenSource;
                entry.TokenSource = linkedCts;
                // Don't dispose old CTS yet as it's still linked
            }

            return linkedCts.Token;
        }

        /// <summary>
        /// Cancels a request by its ID. Called when receiving `notifications/cancelled`.
        /// </summary>
        /// <param name="requestId">The request ID to cancel</param>
        /// <param name="reason">Optional reason for cancellation</param>
        /// <returns>True if the request was found and cancelled, false otherwise</returns>
        public bool CancelRequest(string requestId, string? reason = null)
        {
            if (_cancellationTokens.TryGetValue(requestId, out var entry))
            {
                if (entry.State == RequestState.Running)
                {
                    entry.State = RequestState.Cancelled;
                    entry.CancellationReason = reason;

                    try
                    {
                        entry.TokenSource.Cancel();
                        _logger.LogInformation("Cancelled request {RequestId}. Reason: {Reason}",
                            requestId, reason ?? "No reason provided");

                        // Raise event
                        RequestCancelled?.Invoke(this, new RequestCancelledEventArgs(requestId, reason));
                        return true;
                    }
                    catch (ObjectDisposedException)
                    {
                        // Token source already disposed, request completed
                        _logger.LogDebug("Request {RequestId} already completed (token disposed)", requestId);
                    }
                }
                else
                {
                    _logger.LogDebug("Request {RequestId} already in state {State}, ignoring cancellation",
                        requestId, entry.State);
                }
            }
            else
            {
                // Per MCP spec: Invalid cancellation notifications SHOULD be ignored
                _logger.LogDebug("Received cancellation for unknown request {RequestId}, ignoring", requestId);
            }

            return false;
        }

        /// <summary>
        /// Marks a request as completed and cleans up resources.
        /// </summary>
        /// <param name="requestId">The request ID to complete</param>
        public void CompleteRequest(string requestId)
        {
            if (_cancellationTokens.TryRemove(requestId, out var entry))
            {
                entry.State = RequestState.Completed;
                entry.TokenSource.Dispose();
                _logger.LogDebug("Completed and cleaned up request {RequestId}", requestId);
            }
        }

        /// <summary>
        /// Marks a request as failed and cleans up resources.
        /// </summary>
        public void FailRequest(string requestId, string? error = null)
        {
            if (_cancellationTokens.TryRemove(requestId, out var entry))
            {
                entry.State = RequestState.Failed;
                entry.TokenSource.Dispose();
                _logger.LogDebug("Failed and cleaned up request {RequestId}: {Error}", requestId, error ?? "Unknown error");
            }
        }

        /// <summary>
        /// Gets the current state of a request.
        /// </summary>
        public RequestState? GetRequestState(string requestId)
        {
            if (_cancellationTokens.TryGetValue(requestId, out var entry))
            {
                return entry.State;
            }
            return null;
        }

        /// <summary>
        /// Checks if a request has been cancelled.
        /// </summary>
        public bool IsCancelled(string requestId)
        {
            if (_cancellationTokens.TryGetValue(requestId, out var entry))
            {
                return entry.State == RequestState.Cancelled || entry.TokenSource.IsCancellationRequested;
            }
            return false;
        }

        /// <summary>
        /// Gets statistics about tracked requests.
        /// </summary>
        public CancellationManagerStats GetStats()
        {
            var entries = _cancellationTokens.Values.ToList();
            return new CancellationManagerStats
            {
                TotalTracked = entries.Count,
                Running = entries.Count(e => e.State == RequestState.Running),
                Cancelled = entries.Count(e => e.State == RequestState.Cancelled),
                Completed = entries.Count(e => e.State == RequestState.Completed),
                Failed = entries.Count(e => e.State == RequestState.Failed)
            };
        }

        /// <summary>
        /// Executes an async operation with automatic request tracking and cancellation support.
        /// </summary>
        public async Task<T> ExecuteWithCancellationAsync<T>(
            string requestId,
            Func<CancellationToken, Task<T>> operation,
            TimeSpan? timeout = null)
        {
            var ct = RegisterRequest(requestId, timeout);

            try
            {
                var result = await operation(ct);
                CompleteRequest(requestId);
                return result;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                var state = GetRequestState(requestId);
                if (state == RequestState.Cancelled)
                {
                    _logger.LogInformation("Request {RequestId} was cancelled by client", requestId);
                }
                else
                {
                    _logger.LogWarning("Request {RequestId} timed out", requestId);
                }
                throw;
            }
            catch (Exception ex)
            {
                FailRequest(requestId, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Executes an async operation with automatic request tracking and cancellation support.
        /// </summary>
        public async Task ExecuteWithCancellationAsync(
            string requestId,
            Func<CancellationToken, Task> operation,
            TimeSpan? timeout = null)
        {
            await ExecuteWithCancellationAsync(requestId, async ct =>
            {
                await operation(ct);
                return true;
            }, timeout);
        }

        private void CleanupExpiredEntries(object? state)
        {
            var expiredIds = new List<string>();
            var now = DateTime.UtcNow;

            foreach (var kvp in _cancellationTokens)
            {
                var entry = kvp.Value;
                var age = now - entry.CreatedAt;

                // Clean up entries that are:
                // 1. Older than 2x their timeout (stale)
                // 2. Already completed/cancelled/failed and older than 1 minute
                if (age > entry.Timeout * 2 ||
                    (entry.State != RequestState.Running && age > TimeSpan.FromMinutes(1)))
                {
                    expiredIds.Add(kvp.Key);
                }
            }

            foreach (var id in expiredIds)
            {
                if (_cancellationTokens.TryRemove(id, out var entry))
                {
                    entry.TokenSource.Dispose();
                    _logger.LogDebug("Cleaned up stale request entry {RequestId}", id);
                }
            }

            if (expiredIds.Count > 0)
            {
                _logger.LogDebug("Cleaned up {Count} stale request entries", expiredIds.Count);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _cleanupTimer.Dispose();

            foreach (var entry in _cancellationTokens.Values)
            {
                entry.TokenSource.Dispose();
            }
            _cancellationTokens.Clear();
        }

        private class CancellationEntry
        {
            public CancellationTokenSource TokenSource { get; set; } = null!;
            public DateTime CreatedAt { get; set; }
            public TimeSpan Timeout { get; set; }
            public RequestState State { get; set; }
            public string? CancellationReason { get; set; }
        }
    }

    /// <summary>
    /// Represents the state of a tracked request.
    /// </summary>
    public enum RequestState
    {
        /// <summary>Request is currently being processed</summary>
        Running,

        /// <summary>Request was cancelled by client</summary>
        Cancelled,

        /// <summary>Request completed successfully</summary>
        Completed,

        /// <summary>Request failed with an error</summary>
        Failed
    }

    /// <summary>
    /// Event args for request cancellation events.
    /// </summary>
    public class RequestCancelledEventArgs : EventArgs
    {
        public string RequestId { get; }
        public string? Reason { get; }

        public RequestCancelledEventArgs(string requestId, string? reason)
        {
            RequestId = requestId;
            Reason = reason;
        }
    }

    /// <summary>
    /// Statistics about tracked requests.
    /// </summary>
    public class CancellationManagerStats
    {
        public int TotalTracked { get; set; }
        public int Running { get; set; }
        public int Cancelled { get; set; }
        public int Completed { get; set; }
        public int Failed { get; set; }
    }
}
