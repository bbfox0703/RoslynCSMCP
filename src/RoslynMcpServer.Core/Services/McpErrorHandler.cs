using Microsoft.Extensions.Logging;
using RoslynMcpServer.Core.Models;

namespace RoslynMcpServer.Core.Services
{
    /// <summary>
    /// Provides centralized error handling for MCP tools following JSON-RPC 2.0 specification.
    /// </summary>
    public class McpErrorHandler
    {
        private readonly ILogger<McpErrorHandler> _logger;
        private readonly bool _isDevelopment;

        public McpErrorHandler(ILogger<McpErrorHandler> logger)
        {
            _logger = logger;
            _isDevelopment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") == "Development";
        }

        /// <summary>
        /// Handles an exception and returns a formatted error response
        /// </summary>
        public string HandleException(Exception ex, string operation)
        {
            var error = ClassifyException(ex, operation);
            LogError(error, ex);
            return error.ToToolResponse();
        }

        /// <summary>
        /// Handles an exception and returns an McpResult
        /// </summary>
        public McpResult<T> HandleException<T>(Exception ex, string operation)
        {
            var error = ClassifyException(ex, operation);
            LogError(error, ex);
            return McpResult<T>.Failure(error);
        }

        /// <summary>
        /// Creates a validation error for invalid parameters
        /// </summary>
        public string ValidationError(string parameterName, string reason)
        {
            var error = McpError.InvalidParams(parameterName, reason);
            _logger.LogWarning("Validation error for parameter {Parameter}: {Reason}", parameterName, reason);
            return error.ToToolResponse();
        }

        /// <summary>
        /// Creates a not found error for solutions
        /// </summary>
        public string SolutionNotFoundError(string solutionPath)
        {
            var error = McpError.SolutionNotFound(solutionPath);
            _logger.LogWarning("Solution not found: {Path}", solutionPath);
            return error.ToToolResponse();
        }

        /// <summary>
        /// Creates a not found error for symbols
        /// </summary>
        public string SymbolNotFoundError(string symbolName, string? solutionPath = null)
        {
            var error = McpError.SymbolNotFound(symbolName, solutionPath);
            _logger.LogWarning("Symbol not found: {Symbol} in {Path}", symbolName, solutionPath ?? "unknown");
            return error.ToToolResponse();
        }

        /// <summary>
        /// Creates an invalid path error
        /// </summary>
        public string InvalidPathError(string path, string reason)
        {
            var error = McpError.InvalidPath(path, reason);
            _logger.LogWarning("Invalid path {Path}: {Reason}", path, reason);
            return error.ToToolResponse();
        }

        /// <summary>
        /// Creates a service unavailable error
        /// </summary>
        public string ServiceUnavailableError(string serviceName)
        {
            var error = McpError.ServiceUnavailable(serviceName);
            _logger.LogError("Service unavailable: {Service}", serviceName);
            return error.ToToolResponse();
        }

        /// <summary>
        /// Creates a timeout error
        /// </summary>
        public string TimeoutError(string operation, TimeSpan? elapsed = null)
        {
            var error = McpError.OperationTimeout(operation, elapsed);
            _logger.LogWarning("Operation timed out: {Operation}, Elapsed: {Elapsed}ms",
                operation, elapsed?.TotalMilliseconds ?? 0);
            return error.ToToolResponse();
        }

        /// <summary>
        /// Creates an access denied error
        /// </summary>
        public string AccessDeniedError(string resource, string reason)
        {
            var error = McpError.AccessDenied(resource, reason);
            _logger.LogWarning("Access denied to {Resource}: {Reason}", resource, reason);
            return error.ToToolResponse();
        }

        /// <summary>
        /// Classifies an exception and creates the appropriate MCP error
        /// </summary>
        private McpError ClassifyException(Exception ex, string operation)
        {
            return ex switch
            {
                OperationCanceledException => McpError.Create(
                    McpErrorCodes.OperationCancelled,
                    $"Operation cancelled: {operation}",
                    new McpErrorData { Operation = operation }),

                TimeoutException => McpError.OperationTimeout(operation),

                FileNotFoundException fnfEx => McpError.Create(
                    McpErrorCodes.SolutionNotFound,
                    $"File not found: {fnfEx.FileName ?? "unknown"}",
                    new McpErrorData { Path = fnfEx.FileName }),

                DirectoryNotFoundException => McpError.Create(
                    McpErrorCodes.SolutionNotFound,
                    "Directory not found",
                    new McpErrorData { Reason = ex.Message }),

                UnauthorizedAccessException => McpError.AccessDenied(
                    operation,
                    "Access denied. Please check file permissions."),

                ArgumentNullException argEx => McpError.InvalidParams(
                    argEx.ParamName ?? "unknown",
                    "Parameter cannot be null"),

                ArgumentException argEx => McpError.InvalidParams(
                    argEx.ParamName ?? "unknown",
                    argEx.Message),

                InvalidOperationException => McpError.Create(
                    McpErrorCodes.InternalError,
                    $"Invalid operation: {ex.Message}",
                    CreateErrorData(ex)),

                NotSupportedException => McpError.Create(
                    McpErrorCodes.InvalidRequest,
                    $"Operation not supported: {ex.Message}",
                    CreateErrorData(ex)),

                _ => McpError.FromException(ex, _isDevelopment)
            };
        }

        /// <summary>
        /// Creates error data from an exception
        /// </summary>
        private McpErrorData CreateErrorData(Exception ex)
        {
            var data = new McpErrorData
            {
                ExceptionType = ex.GetType().Name,
                Reason = ex.Message
            };

            if (_isDevelopment && ex.StackTrace != null)
            {
                data.StackTrace = ex.StackTrace;
            }

            return data;
        }

        /// <summary>
        /// Logs the error appropriately based on severity
        /// </summary>
        private void LogError(McpError error, Exception? ex = null)
        {
            var logLevel = error.Code switch
            {
                McpErrorCodes.InvalidParams => LogLevel.Warning,
                McpErrorCodes.SymbolNotFound => LogLevel.Information,
                McpErrorCodes.SolutionNotFound => LogLevel.Warning,
                McpErrorCodes.InvalidPath => LogLevel.Warning,
                McpErrorCodes.OperationTimeout => LogLevel.Warning,
                McpErrorCodes.OperationCancelled => LogLevel.Information,
                McpErrorCodes.AccessDenied => LogLevel.Warning,
                McpErrorCodes.ServiceUnavailable => LogLevel.Error,
                McpErrorCodes.InternalError => LogLevel.Error,
                _ => LogLevel.Error
            };

            if (ex != null)
            {
                _logger.Log(logLevel, ex, "MCP Error [{Code}]: {Message}", error.Code, error.Message);
            }
            else
            {
                _logger.Log(logLevel, "MCP Error [{Code}]: {Message}", error.Code, error.Message);
            }
        }
    }

    /// <summary>
    /// Extension methods for easier error handling in tools
    /// </summary>
    public static class McpErrorExtensions
    {
        /// <summary>
        /// Wraps a task execution with standardized error handling
        /// </summary>
        public static async Task<string> ExecuteWithErrorHandling(
            this McpErrorHandler errorHandler,
            string operation,
            Func<Task<string>> action)
        {
            try
            {
                return await action();
            }
            catch (Exception ex)
            {
                return errorHandler.HandleException(ex, operation);
            }
        }

        /// <summary>
        /// Wraps a task execution with standardized error handling and result type
        /// </summary>
        public static async Task<McpResult<T>> ExecuteWithErrorHandling<T>(
            this McpErrorHandler errorHandler,
            string operation,
            Func<Task<T>> action)
        {
            try
            {
                var result = await action();
                return McpResult<T>.Success(result);
            }
            catch (Exception ex)
            {
                return errorHandler.HandleException<T>(ex, operation);
            }
        }

        /// <summary>
        /// Validates a solution path and returns an error string if invalid
        /// </summary>
        public static string? ValidateSolutionPath(
            this SecurityValidator validator,
            string solutionPath,
            McpErrorHandler errorHandler)
        {
            if (string.IsNullOrWhiteSpace(solutionPath))
            {
                return errorHandler.ValidationError("solutionPath", "Solution path cannot be empty");
            }

            if (!validator.ValidateSolutionPath(solutionPath))
            {
                if (!File.Exists(solutionPath))
                {
                    return errorHandler.SolutionNotFoundError(solutionPath);
                }
                return errorHandler.InvalidPathError(solutionPath, "Path validation failed - potential security risk");
            }

            return null; // Valid
        }

        /// <summary>
        /// Validates a symbol name and returns an error string if invalid
        /// </summary>
        public static string? ValidateSymbolName(
            this McpErrorHandler errorHandler,
            string symbolName)
        {
            if (string.IsNullOrWhiteSpace(symbolName))
            {
                return errorHandler.ValidationError("symbolName", "Symbol name cannot be empty");
            }

            if (symbolName.Length > 500)
            {
                return errorHandler.ValidationError("symbolName", "Symbol name is too long (max 500 characters)");
            }

            return null; // Valid
        }
    }
}
