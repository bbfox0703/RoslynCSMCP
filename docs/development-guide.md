# Development Guide

## Adding New MCP Tools

### Choose the Appropriate Module

| Module | Tool Category |
|--------|---------------|
| `RoslynMcpServer.Navigation` | Symbol search, references, file outline |
| `RoslynMcpServer.Quality` | Code smells, complexity, naming conventions |
| `RoslynMcpServer.Security` | Security issues, thread safety, exception handling |
| `RoslynMcpServer.Dependencies` | Dependency analysis, package analysis, DI container |
| `RoslynMcpServer.Refactoring` | Rename, extract interface, change impact |
| `RoslynMcpServer.Testing` | Test discovery, coverage analysis |
| `RoslynMcpServer.Metrics` | Code metrics, file statistics, documentation coverage |
| `RoslynMcpServer.Advanced` | Batch queries, call hierarchy, performance issues |

### Adding a Tool (Modular Server)

1. Add the method to the module's Tools file (e.g., `NavigationTools.cs`):

```csharp
[McpServerTool, Description("Tool description")]
public static async Task<string> MyNewTool(
    [Description("Parameter description")] string param1,
    IServiceProvider? serviceProvider = null)
{
    // Implementation
}
```

2. If adding a new service, place it in `RoslynMcpServer.Core/Services/` and register it in the module's `Program.cs`.

### Adding a Tool (Full Version)

Add the method to the appropriate tools file in `RoslynMcpServer/Tools/`.

### Tool Implementation Checklist

- [ ] Decorate with `[McpServerTool]` and `[Description]` attributes
- [ ] Add `[Description]` to all parameters
- [ ] Include `IServiceProvider? serviceProvider = null` as last parameter
- [ ] Use `SecurityValidator` for path/input validation
- [ ] Use `McpError` for standardized error responses
- [ ] Use `CancellationContext` for long-running operations
- [ ] Add `pageSize` and `cursor` parameters if results can be large
- [ ] Return formatted strings (markdown supported)

### Full Example: Tool with Pagination & Cancellation

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
        if (string.IsNullOrWhiteSpace(pattern))
            return McpError.InvalidParams("pattern", "Cannot be empty").ToToolResponse();

        var validator = serviceProvider?.GetService<SecurityValidator>();
        var pathError = validator?.ValidateSolutionPath(solutionPath, errorHandler);
        if (pathError != null) return pathError;

        ctx.Token.ThrowIfCancellationRequested();

        var results = await DoSearchAsync(pattern, solutionPath);
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

---

## Logging

### Dual Logging Strategy (Serilog)

1. **stderr output** (required for MCP protocol)
   - All log levels go to stderr; stdout is reserved for MCP messages
   - Configured via `standardErrorFromLevel: Verbose`

2. **File logging** (debugging and diagnostics)

   | Mode | Log Level | Location (Windows) | Retention |
   |------|-----------|-------------------|-----------|
   | Development | Verbose | `%TEMP%\RoslynCSMCP\logs\debug-YYYYMMDD.log` | 7 days |
   | Production | Warning+ | `%TEMP%\RoslynCSMCP\logs\roslyn-mcp-YYYYMMDD.log` | 30 days |

   Linux/macOS: replace `%TEMP%` with `/tmp`.

### Enable Development Mode

Set `DOTNET_ENVIRONMENT=Development` in env or `appsettings.json`:
```json
"env": { "DOTNET_ENVIRONMENT": "Development" }
```

### Heartbeat Service

Background service logs health metrics every 20 minutes (configurable via `HEARTBEAT_INTERVAL_MINUTES`, range: 5–60):
```
💓 HEARTBEAT #1 | Uptime: 0d 0h 20m | Memory: 125.50 MB | Threads: 18 | Time: 2026-01-12 09:30:00 UTC
```

### Viewing Logs in Real-time

```powershell
# Windows PowerShell
Get-Content "$env:TEMP\RoslynCSMCP\logs\debug-$(Get-Date -Format yyyyMMdd).log" -Wait -Tail 50
```
```bash
# Linux/macOS
tail -f /tmp/RoslynCSMCP/logs/debug-$(date +%Y%m%d).log
```

---

## Working with Roslyn

- Always check `Project.SupportsCompilation` before calling `GetCompilationAsync`
- Use semantic model for symbol information; syntax tree for structural analysis
- Dispose workspace resources when using `MSBuildWorkspace` directly
- Always null-check: compilation, syntax trees, semantic models

---

## Performance Considerations

- Default result limit: 20 per category for searches
- Use `IncrementalAnalyzer` for repeated analysis of the same solution
- Batch document processing in groups matching CPU count
- Invalidate cache when solution files change (based on `LastWriteTimeUtc`)
