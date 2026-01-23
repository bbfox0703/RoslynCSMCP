using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoslynMcpServer.Core.Models
{
    /// <summary>
    /// Standard JSON-RPC 2.0 error codes as defined in the MCP specification.
    /// https://modelcontextprotocol.io/specification
    /// </summary>
    public static class McpErrorCodes
    {
        // Standard JSON-RPC 2.0 error codes

        /// <summary>Invalid JSON was received by the server</summary>
        public const int ParseError = -32700;

        /// <summary>The JSON sent is not a valid Request object</summary>
        public const int InvalidRequest = -32600;

        /// <summary>The method does not exist / is not available</summary>
        public const int MethodNotFound = -32601;

        /// <summary>Invalid method parameter(s)</summary>
        public const int InvalidParams = -32602;

        /// <summary>Internal JSON-RPC error</summary>
        public const int InternalError = -32603;

        // MCP Server-defined error codes (-32000 to -32099)

        /// <summary>Solution file not found or inaccessible</summary>
        public const int SolutionNotFound = -32001;

        /// <summary>Invalid or potentially malicious path provided</summary>
        public const int InvalidPath = -32002;

        /// <summary>Symbol not found in the solution</summary>
        public const int SymbolNotFound = -32003;

        /// <summary>Operation timed out</summary>
        public const int OperationTimeout = -32004;

        /// <summary>Access denied due to security restrictions</summary>
        public const int AccessDenied = -32005;

        /// <summary>Service not available or not initialized</summary>
        public const int ServiceUnavailable = -32006;

        /// <summary>MSBuild or Roslyn compilation error</summary>
        public const int CompilationError = -32007;

        /// <summary>Invalid cursor or pagination state</summary>
        public const int InvalidCursor = -32008;

        /// <summary>Resource limit exceeded (e.g., too many results)</summary>
        public const int ResourceLimitExceeded = -32009;

        /// <summary>Operation cancelled by user or system</summary>
        public const int OperationCancelled = -32010;

        /// <summary>
        /// Gets the standard message for an error code
        /// </summary>
        public static string GetMessage(int code) => code switch
        {
            ParseError => "Parse error",
            InvalidRequest => "Invalid Request",
            MethodNotFound => "Method not found",
            InvalidParams => "Invalid params",
            InternalError => "Internal error",
            SolutionNotFound => "Solution not found",
            InvalidPath => "Invalid path",
            SymbolNotFound => "Symbol not found",
            OperationTimeout => "Operation timed out",
            AccessDenied => "Access denied",
            ServiceUnavailable => "Service unavailable",
            CompilationError => "Compilation error",
            InvalidCursor => "Invalid cursor",
            ResourceLimitExceeded => "Resource limit exceeded",
            OperationCancelled => "Operation cancelled",
            _ => "Unknown error"
        };
    }

    /// <summary>
    /// JSON-RPC 2.0 Error object following MCP specification.
    /// </summary>
    public class McpError
    {
        /// <summary>
        /// A Number that indicates the error type that occurred.
        /// </summary>
        [JsonPropertyName("code")]
        public int Code { get; set; }

        /// <summary>
        /// A String providing a short description of the error.
        /// </summary>
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// A Primitive or Structured value that contains additional information about the error.
        /// This may be omitted.
        /// </summary>
        [JsonPropertyName("data")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public McpErrorData? Data { get; set; }

        /// <summary>
        /// Creates a new MCP error with the specified code and message
        /// </summary>
        public static McpError Create(int code, string? message = null, McpErrorData? data = null)
        {
            return new McpError
            {
                Code = code,
                Message = message ?? McpErrorCodes.GetMessage(code),
                Data = data
            };
        }

        /// <summary>
        /// Creates an InvalidParams error
        /// </summary>
        public static McpError InvalidParams(string parameterName, string reason)
        {
            return Create(McpErrorCodes.InvalidParams, $"Invalid parameter: {parameterName}", new McpErrorData
            {
                Parameter = parameterName,
                Reason = reason
            });
        }

        /// <summary>
        /// Creates a SolutionNotFound error
        /// </summary>
        public static McpError SolutionNotFound(string solutionPath)
        {
            return Create(McpErrorCodes.SolutionNotFound, $"Solution not found: {solutionPath}", new McpErrorData
            {
                Path = solutionPath
            });
        }

        /// <summary>
        /// Creates an InvalidPath error
        /// </summary>
        public static McpError InvalidPath(string path, string reason)
        {
            return Create(McpErrorCodes.InvalidPath, $"Invalid path: {reason}", new McpErrorData
            {
                Path = path,
                Reason = reason
            });
        }

        /// <summary>
        /// Creates a SymbolNotFound error
        /// </summary>
        public static McpError SymbolNotFound(string symbolName, string? solutionPath = null)
        {
            return Create(McpErrorCodes.SymbolNotFound, $"Symbol not found: {symbolName}", new McpErrorData
            {
                Symbol = symbolName,
                Path = solutionPath
            });
        }

        /// <summary>
        /// Creates an OperationTimeout error
        /// </summary>
        public static McpError OperationTimeout(string operation, TimeSpan? elapsed = null)
        {
            return Create(McpErrorCodes.OperationTimeout, $"Operation timed out: {operation}", new McpErrorData
            {
                Operation = operation,
                ElapsedMs = elapsed?.TotalMilliseconds
            });
        }

        /// <summary>
        /// Creates an AccessDenied error
        /// </summary>
        public static McpError AccessDenied(string resource, string reason)
        {
            return Create(McpErrorCodes.AccessDenied, $"Access denied: {reason}", new McpErrorData
            {
                Path = resource,
                Reason = reason
            });
        }

        /// <summary>
        /// Creates a ServiceUnavailable error
        /// </summary>
        public static McpError ServiceUnavailable(string serviceName)
        {
            return Create(McpErrorCodes.ServiceUnavailable, $"Service unavailable: {serviceName}", new McpErrorData
            {
                Service = serviceName
            });
        }

        /// <summary>
        /// Creates an InternalError from an exception
        /// </summary>
        public static McpError FromException(Exception ex, bool includeStackTrace = false)
        {
            var data = new McpErrorData
            {
                ExceptionType = ex.GetType().Name,
                Reason = ex.Message
            };

            if (includeStackTrace && ex.StackTrace != null)
            {
                data.StackTrace = ex.StackTrace;
            }

            return Create(McpErrorCodes.InternalError, $"Internal error: {ex.Message}", data);
        }

        /// <summary>
        /// Serializes the error to JSON string
        /// </summary>
        public string ToJson(bool indented = false)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = indented,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            return JsonSerializer.Serialize(this, options);
        }

        /// <summary>
        /// Formats the error as a human-readable string for tool responses
        /// </summary>
        public string ToToolResponse()
        {
            var response = $"Error [{Code}]: {Message}";

            if (Data != null)
            {
                if (!string.IsNullOrEmpty(Data.Reason))
                    response += $"\nReason: {Data.Reason}";
                if (!string.IsNullOrEmpty(Data.Path))
                    response += $"\nPath: {Data.Path}";
                if (!string.IsNullOrEmpty(Data.Parameter))
                    response += $"\nParameter: {Data.Parameter}";
                if (!string.IsNullOrEmpty(Data.Symbol))
                    response += $"\nSymbol: {Data.Symbol}";
            }

            return response;
        }
    }

    /// <summary>
    /// Additional error data following MCP conventions
    /// </summary>
    public class McpErrorData
    {
        /// <summary>Path that caused the error</summary>
        [JsonPropertyName("path")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Path { get; set; }

        /// <summary>Parameter name that was invalid</summary>
        [JsonPropertyName("parameter")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Parameter { get; set; }

        /// <summary>Symbol name involved in the error</summary>
        [JsonPropertyName("symbol")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Symbol { get; set; }

        /// <summary>Detailed reason for the error</summary>
        [JsonPropertyName("reason")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Reason { get; set; }

        /// <summary>Operation that failed</summary>
        [JsonPropertyName("operation")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Operation { get; set; }

        /// <summary>Service name that is unavailable</summary>
        [JsonPropertyName("service")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Service { get; set; }

        /// <summary>Exception type for internal errors</summary>
        [JsonPropertyName("exceptionType")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ExceptionType { get; set; }

        /// <summary>Elapsed time in milliseconds for timeout errors</summary>
        [JsonPropertyName("elapsedMs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? ElapsedMs { get; set; }

        /// <summary>Stack trace for debugging (only in development)</summary>
        [JsonPropertyName("stackTrace")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? StackTrace { get; set; }

        /// <summary>Additional context-specific data</summary>
        [JsonPropertyName("context")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, object>? Context { get; set; }
    }

    /// <summary>
    /// Result wrapper that can contain either a successful result or an error
    /// </summary>
    /// <typeparam name="T">Type of the successful result</typeparam>
    public class McpResult<T>
    {
        /// <summary>
        /// The successful result value (null if error)
        /// </summary>
        public T? Value { get; private set; }

        /// <summary>
        /// The error (null if success)
        /// </summary>
        public McpError? Error { get; private set; }

        /// <summary>
        /// Whether the operation was successful
        /// </summary>
        public bool IsSuccess => Error == null;

        /// <summary>
        /// Whether the operation failed
        /// </summary>
        public bool IsError => Error != null;

        private McpResult() { }

        /// <summary>
        /// Creates a successful result
        /// </summary>
        public static McpResult<T> Success(T value)
        {
            return new McpResult<T> { Value = value };
        }

        /// <summary>
        /// Creates an error result
        /// </summary>
        public static McpResult<T> Failure(McpError error)
        {
            return new McpResult<T> { Error = error };
        }

        /// <summary>
        /// Creates an error result from an error code
        /// </summary>
        public static McpResult<T> Failure(int code, string? message = null, McpErrorData? data = null)
        {
            return new McpResult<T> { Error = McpError.Create(code, message, data) };
        }

        /// <summary>
        /// Maps the result to a different type
        /// </summary>
        public McpResult<TNew> Map<TNew>(Func<T, TNew> mapper)
        {
            if (IsError)
                return McpResult<TNew>.Failure(Error!);

            return McpResult<TNew>.Success(mapper(Value!));
        }

        /// <summary>
        /// Gets the value or throws if error
        /// </summary>
        public T GetValueOrThrow()
        {
            if (IsError)
                throw new McpException(Error!);

            return Value!;
        }

        /// <summary>
        /// Gets the value or returns a default
        /// </summary>
        public T? GetValueOrDefault(T? defaultValue = default)
        {
            return IsSuccess ? Value : defaultValue;
        }
    }

    /// <summary>
    /// Exception that wraps an MCP error for throwing in exceptional cases
    /// </summary>
    public class McpException : Exception
    {
        /// <summary>
        /// The MCP error associated with this exception
        /// </summary>
        public McpError McpError { get; }

        /// <summary>
        /// The error code
        /// </summary>
        public int Code => McpError.Code;

        public McpException(McpError error)
            : base(error.Message)
        {
            McpError = error;
        }

        public McpException(int code, string? message = null, McpErrorData? data = null)
            : this(McpError.Create(code, message, data))
        {
        }

        public McpException(int code, string message, Exception innerException)
            : base(message, innerException)
        {
            McpError = McpError.Create(code, message, new McpErrorData
            {
                ExceptionType = innerException.GetType().Name,
                Reason = innerException.Message
            });
        }
    }
}
