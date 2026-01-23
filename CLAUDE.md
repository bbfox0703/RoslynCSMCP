# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

RoslynCSMCP is a C# MCP (Model Context Protocol) server that integrates with Microsoft's Roslyn compiler platform to provide Claude Desktop with code analysis and navigation capabilities for C# codebases. It exposes tools for symbol search, reference tracking, dependency analysis, and code complexity analysis.

## MCP Specification Compliance

This server implements the [MCP Specification (2025-11-25)](https://modelcontextprotocol.io/specification) with the following features:

| Feature | Status | Implementation |
|---------|--------|----------------|
| JSON-RPC 2.0 Transport | ✅ | stdio via `WithStdioServerTransport()` |
| Tool Registration | ✅ | `[McpServerTool]` + `[Description]` attributes |
| Cursor-based Pagination | ✅ | `PaginatedResult<T>`, `PaginationCursor` |
| JSON-RPC Error Codes | ✅ | `McpError`, `McpErrorCodes` |
| Request Cancellation | ✅ | `CancellationManager`, `CancellableOperation` |
| OAuth 2.0 + PKCE | ✅ | `OAuthAuthenticationService` (optional) |
| Resource Indicators (RFC 8707) | ✅ | `resource` parameter in OAuth |
| Cross-platform Paths | ✅ | Windows + Unix path validation |

## Build & Development Commands

### Building the Project
```bash
# Restore NuGet packages
dotnet restore

# Build in debug mode
dotnet build

# Build in release mode
dotnet build -c Release
```

### Running the Server
```bash
# Run directly (for development/testing)
dotnet run

# Run without rebuild
dotnet run --no-build

# Run with MCP Inspector for testing
npx @modelcontextprotocol/inspector dotnet run --project ./RoslynMcpServer
```

### Testing
The project uses automated setup and test scripts:
- **Windows**: `.\install\setup-claude-desktop.ps1` (builds, configures, and tests the server)
- **Linux/macOS**: `./install/setup-claude-desktop.sh` and `./RoslynMcpServer/test-installation.sh`

### Important: MSBuild Registration
The server requires MSBuild to be registered before any Roslyn workspace operations. This happens automatically in Program.cs:12-33 via `MSBuildLocator.RegisterDefaults()`. This MUST occur before creating any Roslyn workspaces.

## Architecture

### Modular Project Structure

The codebase follows a **modular architecture** for optimized token usage:

```
RoslynCSMCP.sln
├── src/
│   ├── RoslynMcpServer.Core/           # Shared library (Class Library)
│   │   ├── Services/                    # All analysis services
│   │   ├── Models/                      # Data models
│   │   └── Configuration/               # Tool profile config
│   │
│   ├── RoslynMcpServer.Navigation/     # Navigation MCP (6 tools, ~1,050 tokens)
│   ├── RoslynMcpServer.Quality/        # Quality MCP (6 tools, ~1,050 tokens)
│   ├── RoslynMcpServer.Security/       # Security MCP (3 tools, ~525 tokens)
│   ├── RoslynMcpServer.Dependencies/   # Dependencies MCP (5 tools, ~875 tokens)
│   ├── RoslynMcpServer.Refactoring/    # Refactoring MCP (4 tools, ~700 tokens)
│   ├── RoslynMcpServer.Testing/        # Testing MCP (2 tools, ~350 tokens)
│   ├── RoslynMcpServer.Metrics/        # Metrics MCP (3 tools, ~525 tokens)
│   └── RoslynMcpServer.Advanced/       # Advanced MCP (13 tools, ~2,275 tokens)
│
├── RoslynMcpServer/                    # Full version (42 tools, ~7,350 tokens)
└── RoslynMcpServer.Tests/              # Unit & integration tests
```

### Layer Structure

1. **MCP Server Layer** (`Program.cs` in each module)
   - Handles MCP protocol communication via stdio transport
   - Registers required services with dependency injection
   - Configures logging to stderr (required for MCP protocol)

2. **Tools Layer** (`Tools/*.cs` in each module)
   - Exposes MCP tools decorated with `[McpServerTool]` attributes
   - Module-specific tools (e.g., NavigationTools.cs, QualityTools.cs)
   - All tool methods are static and receive IServiceProvider for dependency access
   - Tools handle validation, error handling, and result formatting

3. **Core Library** (`src/RoslynMcpServer.Core/`)
   - **Services**: All analysis services (SymbolSearchService, CodeAnalysisService, etc.)
   - **Models**: Data transfer objects for all tool results
   - **Configuration**: Tool profile configuration
   - **Utilities**: SecurityValidator, DiagnosticLogger, CacheManager

4. **Models** (`Core/Models/`)
   - `SearchModels.cs` - SymbolSearchResult, ReferenceResult, SymbolInfo, etc.
   - `PaginationModels.cs` - PaginatedResult<T>, PaginationCursor, PaginationRequest
   - `McpErrorModels.cs` - McpError, McpErrorCodes, McpResult<T>, McpException

5. **Authentication** (`Core/Authentication/`)
   - `OAuthConfiguration.cs` - OAuth 2.0 settings and validation
   - `OAuthTokenManager.cs` - Token acquisition, refresh, and validation
   - `OAuthAuthenticationService.cs` - Authorization flow management
   - `TokenStorage.cs` - Secure token storage (DPAPI/AES)

### Key Architectural Patterns

**Roslyn Workspace Management**
- Solutions are loaded via MSBuild workspace APIs
- Each tool call loads the solution fresh (stateless design)
- Compilation objects are cached at the IncrementalAnalyzer level

**Multi-Level Caching Strategy**
- L1 (Memory): Hot data, 10-minute expiry
- L2 (Optional Redis): Warm data, 1-hour expiry
- L3 (File system): Cold data, 7-day expiry
- Cache keys are based on solution path and search parameters

**Security Model** (`Core/Services/SecurityValidator.cs`)
- SecurityValidator enforces:
  - Path traversal prevention (blocks "..", "~", null bytes, URL-encoded sequences)
  - Allowed file extensions (.sln, .csproj only)
  - Cross-platform path validation:
    - Windows: `^[a-zA-Z]:[\\/][^<>:|?*]+$`
    - Unix/macOS: `^/[^<>:|?*\x00]+$`
  - Platform detection via `OperatingSystem.IsWindows()`
  - WSL compatibility (accepts both formats on Windows)
  - Search pattern sanitization

**Concurrency & Performance**
- IncrementalAnalyzer uses SemaphoreSlim for throttling (max concurrent = CPU count)
- Symbol searches run across projects in parallel via Task.WhenAll
- Batch processing with periodic GC collection for large codebases
- File-level caching based on LastWriteTimeUtc

### Tool Implementation Details

**SearchSymbols**
- Converts wildcard patterns (* and ?) to regex
- Filters symbols by kind (class, interface, method, property, field, event)
- Calculates relevance scores (exact match > prefix match > accessibility)
- Supports cursor-based pagination via `pageSize` and `cursor` parameters
- Uses `CancellationContext` for cancellable operations

**FindReferences**
- Uses Roslyn's SymbolFinder.FindReferencesAsync
- Distinguishes definitions from references by comparing source spans
- Provides 5-line context around each reference
- Deduplicates by DocumentPath:LineNumber
- Supports cursor-based pagination with query hash validation

**AnalyzeCodeComplexity**
- Calculates cyclomatic complexity for methods
- Complexity = 1 + decision points (if/while/for/foreach/switch/catch) + logical operators (&&/||)
- Default threshold is 5, configurable via tool parameter

**CodeAnalysisService** (Services/CodeAnalysisService.cs)
- Manages MSBuildWorkspace instances and solution loading
- Provides `GetSolutionAsync` with 5-minute memory caching
- Implements `AnalyzeDependenciesAsync` for dependency graph analysis
- Handles workspace lifecycle and disposal
- One workspace per solution path, reused across calls

## Development Guidelines

### Adding New MCP Tools

**For Modular MCP Servers** (recommended):

1. **Choose the appropriate module** based on tool category:
   - Navigation: Symbol search, references, file outline
   - Quality: Code smells, complexity, naming conventions
   - Security: Security issues, thread safety, exception handling
   - Dependencies: Dependency analysis, package analysis, DI container
   - Refactoring: Rename, extract interface, change impact
   - Testing: Test discovery, coverage analysis
   - Metrics: Code metrics, file statistics, documentation coverage
   - Advanced: Batch queries, call hierarchy, performance issues

2. **Add the tool to the module's Tools file** (e.g., `NavigationTools.cs`):
   ```csharp
   [McpServerTool, Description("Tool description")]
   public static async Task<string> MyNewTool(
       [Description("Parameter description")] string param1,
       IServiceProvider? serviceProvider = null)
   {
       // Implementation
   }
   ```

3. **If adding a new service**, add it to `RoslynMcpServer.Core/Services/` and register it in the module's `Program.cs`.

**For Full Version** (RoslynMcpServer):

1. Add the method to the appropriate tools file in `RoslynMcpServer/Tools/`
2. Follow the same patterns as modular tools

**Tool Implementation Guidelines**:
- Decorate with `[McpServerTool]` and `[Description]` attributes
- Add `[Description]` attributes to all parameters
- Include `IServiceProvider? serviceProvider = null` as the last parameter
- Use SecurityValidator for path/input validation
- Use `McpError` for standardized error responses
- Use `CancellationContext` for cancellable long-running operations
- Add `pageSize` and `cursor` parameters for paginated results
- Return formatted strings (markdown supported)

**Example Tool with Full MCP Support**:
```csharp
[McpServerTool, Description("Search for symbols with pagination and cancellation")]
public static async Task<string> SearchSymbols(
    [Description("Search pattern")] string pattern,
    [Description("Solution path")] string solutionPath,
    [Description("Page size (default: 20, max: 100)")] int pageSize = 20,
    [Description("Pagination cursor")] string? cursor = null,
    IServiceProvider? serviceProvider = null)
{
    var errorHandler = serviceProvider?.GetService<McpErrorHandler>();
    using var ctx = CancellationContext.Create(serviceProvider, "SearchSymbols");

    try
    {
        // Validate parameters
        if (string.IsNullOrWhiteSpace(pattern))
            return McpError.InvalidParams("pattern", "Cannot be empty").ToToolResponse();

        var validator = serviceProvider?.GetService<SecurityValidator>();
        var pathError = validator?.ValidateSolutionPath(solutionPath, errorHandler);
        if (pathError != null) return pathError;

        // Check cancellation
        ctx.Token.ThrowIfCancellationRequested();

        // Execute operation
        var results = await DoSearchAsync(pattern, solutionPath);

        // Apply pagination
        var paginated = PaginatedResult<Result>.FromCursor(results, cursor, pageSize);

        ctx.Complete();
        return FormatResults(paginated);
    }
    catch (OperationCanceledException) when (ctx.Token.IsCancellationRequested)
    {
        return ctx.IsCancelled
            ? McpError.Create(McpErrorCodes.OperationCancelled, "Cancelled").ToToolResponse()
            : McpError.OperationTimeout("SearchSymbols").ToToolResponse();
    }
    catch (Exception ex)
    {
        ctx.Fail(ex.Message);
        return errorHandler?.HandleException(ex, "SearchSymbols")
            ?? McpError.FromException(ex).ToToolResponse();
    }
}
```

### Logging Requirements

**Dual Logging Strategy (Serilog)**

The server uses Serilog for structured logging with dual output:

1. **stderr Output** (Required for MCP protocol)
   - All log levels output to stderr (stdout reserved for MCP messages)
   - Configured via `standardErrorFromLevel: Verbose`
   - Ensures Claude Desktop/CLI receives all diagnostic information

2. **File Logging** (For debugging and diagnostics)
   - **Development Mode**: Verbose logging to help developers debug
     - Location: `%TEMP%\RoslynCSMCP\logs\debug-YYYYMMDD.log` (Windows)
     - Location: `/tmp/RoslynCSMCP/logs/debug-YYYYMMDD.log` (Linux/macOS)
     - Log Level: Verbose (all events)
     - Retention: 7 days
     - Includes detailed properties and operation context

   - **Production Mode**: Warning+ logging for issue diagnosis
     - Location: `%TEMP%\RoslynCSMCP\logs\roslyn-mcp-YYYYMMDD.log`
     - Log Level: Warning and above
     - Retention: 30 days
     - Minimal overhead, only captures problems

**Environment Detection**:
- Set `DOTNET_ENVIRONMENT=Development` for debug logging
- Defaults to Production mode if not specified

**Configuration**:
- `appsettings.json` - Production settings
- `appsettings.Development.json` - Development settings
- Logs are written asynchronously with automatic daily rolling

**Heartbeat Service**:
- Background service logs periodic heartbeat messages every 15-30 minutes (default: 20 minutes)
- Confirms the MCP server is running and provides health metrics:
  - Uptime since server start
  - Memory usage (working set in MB)
  - Thread count
  - Current timestamp
- Configure interval via environment variable: `HEARTBEAT_INTERVAL_MINUTES` (5-60 minutes)
- Heartbeat logs include:
  ```
  💓 HEARTBEAT #1 | Uptime: 0d 0h 20m | Memory: 125.50 MB | Threads: 18 | Time: 2026-01-12 09:30:00 UTC
  ```

### Working with Roslyn

When analyzing code:
- Always check if Project.SupportsCompilation before calling GetCompilationAsync
- Use semantic model for symbol information, syntax tree for structure analysis
- Dispose of workspace resources when using MSBuildWorkspace directly
- Check for null: compilation, syntax trees, semantic models

### Performance Considerations

- Limit result sets (default: 20 per category for searches)
- Use IncrementalAnalyzer for repeated analysis of same solution
- Batch document processing in groups matching CPU count
- Consider cache invalidation when solution files change

## Configuration

MCP server configuration is added to Claude Desktop config:
- **Windows**: `%APPDATA%\Claude\claude_desktop_config.json`
- **macOS**: `~/Library/Application Support/Claude/claude_desktop_config.json`

### Modular Configuration (Recommended)

Choose only the modules you need to minimize token usage:

```json
{
  "mcpServers": {
    "roslyn-nav": {
      "command": "dotnet",
      "args": ["run", "--project", "D:/RoslynCSMCP/src/RoslynMcpServer.Navigation"]
    },
    "roslyn-quality": {
      "command": "dotnet",
      "args": ["run", "--project", "D:/RoslynCSMCP/src/RoslynMcpServer.Quality"]
    }
  }
}
```

### Full Version Configuration

For all 42 tools in a single server:

```json
{
  "mcpServers": {
    "roslyn": {
      "command": "dotnet",
      "args": ["run", "--project", "D:/RoslynCSMCP/RoslynMcpServer"]
    }
  }
}
```

### Environment Variables

- `DOTNET_ENVIRONMENT`: Production (default) or Development
- `LOG_LEVEL`: Information (deprecated, now using Serilog configuration)

### Debugging with File Logs

When debugging or developing RoslynCSMCP:

1. **Enable Development Mode**:
   ```json
   "env": {
     "DOTNET_ENVIRONMENT": "Development"
   }
   ```

2. **Locate Log Files**:
   - **Windows**: `%TEMP%\RoslynCSMCP\logs\debug-YYYYMMDD.log`
   - **Linux/macOS**: `/tmp/RoslynCSMCP/logs/debug-YYYYMMDD.log`
   - **Startup logs**: `startup-YYYYMMDD.log` (early initialization)

3. **Log File Contents**:
   - All tool invocations with parameters
   - Operation timing (from DiagnosticLogger)
   - MSBuild workspace loading events
   - Symbol search and analysis details
   - Cache hits/misses
   - Errors with full stack traces

4. **Viewing Logs in Real-time**:

   When the server starts in Development mode, it logs the PowerShell command to tail debug logs:
   ```
   [HH:mm:ss INF] To tail debug log (PowerShell): Get-Content "$env:TEMP\RoslynCSMCP\logs\debug-$(Get-Date -Format yyyyMMdd).log" -Wait -Tail 20
   ```

   Manual commands:
   ```bash
   # Windows PowerShell
   Get-Content "$env:TEMP\RoslynCSMCP\logs\debug-$(Get-Date -Format yyyyMMdd).log" -Wait -Tail 50

   # Linux/macOS (PowerShell)
   Get-Content "/tmp/RoslynCSMCP/logs/debug-$(date +%Y%m%d).log" -Wait -Tail 50

   # Linux/macOS (bash)
   tail -f /tmp/RoslynCSMCP/logs/debug-$(date +%Y%m%d).log
   ```

## MCP Protocol Features

### JSON-RPC Error Handling

The server uses standardized JSON-RPC 2.0 error codes (`Core/Models/McpErrorModels.cs`):

| Code | Constant | Description |
|------|----------|-------------|
| -32700 | `ParseError` | Invalid JSON |
| -32600 | `InvalidRequest` | Invalid request object |
| -32601 | `MethodNotFound` | Method not found |
| -32602 | `InvalidParams` | Invalid parameters |
| -32603 | `InternalError` | Internal error |
| -32001 | `SolutionNotFound` | Solution file not found |
| -32002 | `InvalidPath` | Path validation failed |
| -32003 | `SymbolNotFound` | Symbol not found |
| -32004 | `OperationTimeout` | Operation timed out |
| -32005 | `AccessDenied` | Access denied |
| -32006 | `ServiceUnavailable` | Service unavailable |
| -32010 | `OperationCancelled` | Cancelled by client |

**Error Response Format**:
```
Error [-32003]: Symbol not found: MyClass
Reason: No matching symbol in solution
Path: C:\Projects\MySolution.sln
```

### Cursor-based Pagination

Tools support pagination via `pageSize` and `cursor` parameters (`Core/Models/PaginationModels.cs`):

```csharp
// First request
SearchSymbols(pattern: "*Service", solutionPath: "...", pageSize: 20)

// Response includes nextCursor
// Showing 20 of 150 total items (Page 1/8)
// nextCursor: eyJPZmZzZXQiOjIwLC...

// Next page request
SearchSymbols(pattern: "*Service", solutionPath: "...", cursor: "eyJPZmZzZXQiOjIwLC...")
```

**Cursor Features**:
- Base64-encoded JSON containing offset and query hash
- 24-hour expiration
- Query hash validation prevents cursor reuse with different parameters

### Request Cancellation

Operations support cancellation via `CancellationManager` (`Core/Services/CancellationManager.cs`):

- Tracks in-progress requests with unique IDs
- Supports MCP `notifications/cancelled` pattern
- Automatic timeout (default: 5 minutes)
- Proper cleanup of stale entries

**Request States**: `Running` → `Completed` | `Cancelled` | `Failed`

### OAuth 2.0 Authentication (Optional)

For remote deployments, OAuth 2.0 is available (`Core/Authentication/`):

**Configuration** (`appsettings.json`):
```json
{
  "OAuth": {
    "Enabled": true,
    "Issuer": "https://auth.example.com",
    "AuthorizationEndpoint": "https://auth.example.com/authorize",
    "TokenEndpoint": "https://auth.example.com/token",
    "ClientId": "roslyn-mcp-server",
    "ResourceIdentifier": "mcp://roslyn-mcp-server",
    "Scopes": ["mcp.read", "mcp.write"],
    "Pkce": {
      "Required": true,
      "CodeChallengeMethod": "S256"
    }
  }
}
```

**Features**:
- OAuth 2.1 Authorization Code Flow
- PKCE (Proof Key for Code Exchange) - mandatory per MCP spec
- Resource Indicators (RFC 8707)
- Protected Resource Metadata (RFC 9728)
- Secure token storage (DPAPI on Windows, AES on other platforms)
- Automatic token refresh

**Token Storage Options**:
- `InMemoryTokenStorage` - Development/testing
- `EncryptedFileTokenStorage` - Production (default)
- `EnvironmentTokenStorage` - Container environments

## Registered Services

Services registered in `Program.cs` for each module:

```csharp
// Core services
builder.Services.AddSingleton<CodeAnalysisService>();
builder.Services.AddSingleton<SymbolSearchService>();
builder.Services.AddSingleton<SecurityValidator>();

// MCP protocol services
builder.Services.AddSingleton<McpErrorHandler>();        // Error handling
builder.Services.AddSingleton<CancellationManager>();    // Cancellation
builder.Services.AddSingleton<CancellableOperation>();   // Cancellable ops

// Caching
builder.Services.AddSingleton<MultiLevelCacheManager>();
builder.Services.AddMemoryCache();
```
