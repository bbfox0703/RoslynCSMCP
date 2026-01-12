# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

RoslynCSMCP is a C# MCP (Model Context Protocol) server that integrates with Microsoft's Roslyn compiler platform to provide Claude Desktop with code analysis and navigation capabilities for C# codebases. It exposes tools for symbol search, reference tracking, dependency analysis, and code complexity analysis.

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

### Layer Structure

The codebase follows a layered architecture:

1. **MCP Server Layer** (Program.cs)
   - Handles MCP protocol communication via stdio transport
   - Registers all services with dependency injection
   - Configures logging to stderr (required for MCP protocol)

2. **Tools Layer** (Tools/CodeNavigationTools.cs)
   - Exposes MCP tools decorated with `[McpServerTool]` attributes
   - Five main tools: SearchSymbols, FindReferences, GetSymbolInfo, AnalyzeDependencies, AnalyzeCodeComplexity
   - All tool methods are static and receive IServiceProvider for dependency access
   - Tools handle validation, error handling, and result formatting

3. **Services Layer**
   - **SymbolSearchService**: Core symbol search and reference finding using Roslyn's SymbolFinder
   - **IncrementalAnalyzer**: Provides file-level caching for incremental analysis
   - **MultiLevelCacheManager**: Three-tier caching (L1: Memory, L2: Redis/optional, L3: File system)
   - **SecurityValidator**: Input validation and path sanitization
   - **DiagnosticLogger**: Operation timing and logging

4. **Models Layer** (Models/SearchModels.cs)
   - Data transfer objects for all tool results
   - Includes: SymbolSearchResult, ReferenceResult, SymbolInfo, DependencyAnalysis, ComplexityResult

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

**Security Model**
- SecurityValidator enforces:
  - Path traversal prevention (blocks ".." and "~")
  - Allowed file extensions (.sln, .csproj only)
  - Safe path regex validation (Windows paths)
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
- Returns top 20 results per category

**FindReferences**
- Uses Roslyn's SymbolFinder.FindReferencesAsync
- Distinguishes definitions from references by comparing source spans
- Provides 5-line context around each reference
- Deduplicates by DocumentPath:LineNumber

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

1. Add a static method to CodeNavigationTools.cs
2. Decorate with `[McpServerTool]` and `Description` attributes
3. Add `Description` attributes to all parameters
4. Include `IServiceProvider? serviceProvider = null` as the last parameter
5. Use SecurityValidator for path/input validation
6. Wrap in try-catch with appropriate error logging
7. Return formatted strings (markdown supported)

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

The server runs via `dotnet run --project /path/to/RoslynMcpServer` with environment variables:
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
