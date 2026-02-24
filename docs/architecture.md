# Architecture

## Layer Structure

1. **MCP Server Layer** (`Program.cs` in each module)
   - Handles MCP protocol communication via stdio transport
   - Registers required services with dependency injection
   - Configures logging to stderr (required for MCP protocol)

2. **Tools Layer** (`Tools/*.cs` in each module)
   - Exposes MCP tools decorated with `[McpServerTool]` attributes
   - Module-specific tools (e.g., `NavigationTools.cs`, `QualityTools.cs`)
   - All tool methods are static and receive `IServiceProvider` for dependency access
   - Tools handle validation, error handling, and result formatting

3. **Core Library** (`src/RoslynMcpServer.Core/`)
   - **Services**: All analysis services (`SymbolSearchService`, `CodeAnalysisService`, etc.)
   - **Models**: Data transfer objects for all tool results
   - **Configuration**: Tool profile configuration
   - **Utilities**: `SecurityValidator`, `DiagnosticLogger`, `CacheManager`

4. **Models** (`Core/Models/`)
   - `SearchModels.cs` — `SymbolSearchResult`, `ReferenceResult`, `SymbolInfo`, etc.
   - `PaginationModels.cs` — `PaginatedResult<T>`, `PaginationCursor`, `PaginationRequest`
   - `McpErrorModels.cs` — `McpError`, `McpErrorCodes`, `McpResult<T>`, `McpException`

5. **Authentication** (`Core/Authentication/`)
   - `OAuthConfiguration.cs` — OAuth 2.0 settings and validation
   - `OAuthTokenManager.cs` — Token acquisition, refresh, and validation
   - `OAuthAuthenticationService.cs` — Authorization flow management
   - `TokenStorage.cs` — Secure token storage (DPAPI/AES)

---

## Key Architectural Patterns

### Roslyn Workspace Management
- Solutions are loaded via MSBuild workspace APIs
- Each tool call loads the solution fresh (stateless design)
- Compilation objects are cached at the `IncrementalAnalyzer` level

### Multi-Level Caching Strategy
- **L1 (Memory)**: Hot data, 10-minute expiry
- **L2 (Optional Redis)**: Warm data, 1-hour expiry
- **L3 (File system)**: Cold data, 7-day expiry
- Cache keys are based on solution path and search parameters

### Security Model (`Core/Services/SecurityValidator.cs`)
- Path traversal prevention (blocks `..`, `~`, null bytes, URL-encoded sequences)
- Allowed file extensions: `.sln`, `.csproj` only
- Cross-platform path validation:
  - Windows: `^[a-zA-Z]:[\\/][^<>:|?*]+$`
  - Unix/macOS: `^/[^<>:|?*\x00]+$`
- Platform detection via `OperatingSystem.IsWindows()`
- WSL compatibility (accepts both formats on Windows)
- Search pattern sanitization

### Concurrency & Performance
- `IncrementalAnalyzer` uses `SemaphoreSlim` for throttling (max concurrent = CPU count)
- Symbol searches run across projects in parallel via `Task.WhenAll`
- Batch processing with periodic GC collection for large codebases
- File-level caching based on `LastWriteTimeUtc`

---

## Tool Implementation Details

### SearchSymbols
- Converts wildcard patterns (`*` and `?`) to regex
- Filters symbols by kind (class, interface, method, property, field, event)
- Calculates relevance scores (exact match > prefix match > accessibility)
- Supports cursor-based pagination via `pageSize` and `cursor` parameters
- Uses `CancellationContext` for cancellable operations

### FindReferences
- Uses Roslyn's `SymbolFinder.FindReferencesAsync`
- Distinguishes definitions from references by comparing source spans
- Provides 5-line context around each reference
- Deduplicates by `DocumentPath:LineNumber`
- Supports cursor-based pagination with query hash validation

### AnalyzeCodeComplexity
- Calculates cyclomatic complexity for methods
- Complexity = 1 + decision points (`if`/`while`/`for`/`foreach`/`switch`/`catch`) + logical operators (`&&`/`||`)
- Default threshold is 5, configurable via tool parameter

### CodeAnalysisService (`Services/CodeAnalysisService.cs`)
- Manages `MSBuildWorkspace` instances and solution loading
- Provides `GetSolutionAsync` with 5-minute memory caching
- Implements `AnalyzeDependenciesAsync` for dependency graph analysis
- Handles workspace lifecycle and disposal
- One workspace per solution path, reused across calls

---

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

---

## MCP Specification Compliance

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
