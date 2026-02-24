# Roslyn CSharp MCP Server

![.NET 10.0](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet) ![MCP 1.0.0-rc.1](https://img.shields.io/badge/MCP-1.0.0--rc.1-00A4EF) ![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)

A C# MCP (Model Context Protocol) server that integrates with Microsoft's Roslyn compiler platform to provide Vibe-coding tools like **Claude Desktop** and **Claude CLI** with powerful code analysis and navigation capabilities for C# codebases.

Notice: most codes after forked from original repository was completed by Claude code. 

## Features

- **Wildcard Symbol Search** - Find classes, methods, and properties using pattern matching (`*Service`, `Get*User`, etc.)
- **Reference Tracking** - Locate all usages of symbols across entire solutions
- **Symbol Information** - Get detailed information about types, methods, properties, and more
- **Dependency Analysis** - Analyze project dependencies and namespace usage patterns
- **Code Complexity Analysis** - Identify high-complexity methods using cyclomatic complexity metrics
- **Performance Optimized** - Multi-level caching (Memory, Redis, File system) and incremental analysis for large codebases
- **Security Hardened** - Input validation, path sanitization, and safe file operations
- **Modular Architecture** - Load only the tools you need to minimize token overhead (350-2,275 tokens per module vs 7,350 for full)
- ...and more!

## What is RoslynCSMCP?

RoslynCSMCP is a **specialized application** built on top of Microsoft's MCP SDK, not a replacement for it.

### Relationship with Microsoft MCP C# SDK

| Component | Role | What It Provides |
|-----------|------|------------------|
| **Microsoft MCP C# SDK** | 🔧 Infrastructure | MCP protocol implementation, transport layer, serialization |
| **RoslynCSMCP (This Project)** | Application | Ready-to-use C# code analysis tools powered by Roslyn |

**Analogy**:
- Microsoft MCP SDK = Kitchen equipment (stove, refrigerator, knives)
- RoslynCSMCP = A restaurant with complete menu (using that equipment to serve specific dishes)

### RoslynCSMCP major tools and benefits:

1. **Roslyn Integration**: Deep integration with Microsoft's Roslyn compiler platform for semantic code analysis
2. **Specialized Tools**: From symbol search to test discovery, compilation errors to class hierarchies
3. **Token Optimization**: Intelligent filtering and output formatting reduces token usage by 60-98%
4. **Production Ready**: Security validation, multi-level caching, incremental analysis
5. **Claude Native**: Designed specifically for Claude Desktop and Claude CLI workflows

**Bottom Line**: Microsoft provides the foundation (SDK), RoslynCSMCP provides the complete, ready-to-use C# code analysis solution.

---

## 🧩 Modular Architecture (NEW)

RoslynCSMCP now supports **modular MCP servers** for optimized token usage. Instead of loading all 42 tools (~7,350 tokens), you can load only the modules you need.

### Available Modules

| Module | Tools | Tokens | Description |
|--------|-------|--------|-------------|
| **Full** | 42 | ~7,350 | Complete toolset (backward compatible) |
| **Navigation** | 6 | ~1,050 | Symbol search, references, file outline |
| **Quality** | 6 | ~1,050 | Code smells, complexity, naming conventions |
| **Security** | 3 | ~525 | Security issues, thread safety, exception handling |
| **Dependencies** | 5 | ~875 | Dependency analysis, packages, DI container |
| **Refactoring** | 4 | ~700 | Rename, extract interface, change impact |
| **Testing** | 2 | ~350 | Test discovery, coverage analysis |
| **Metrics** | 3 | ~525 | Code metrics, file statistics, documentation |
| **Advanced** | 13 | ~2,275 | Batch queries, cross-solution, call hierarchy |

### Quick Start - Modular Configuration

**Claude Desktop** (`claude_desktop_config.json`):
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

**Claude CLI**:
```bash
# Add Navigation module
claude mcp add roslyn-nav --scope user -- dotnet run --project /path/to/RoslynCSMCP/src/RoslynMcpServer.Navigation

# Add Quality module
claude mcp add roslyn-quality --scope user -- dotnet run --project /path/to/RoslynCSMCP/src/RoslynMcpServer.Quality
```

### Module Selection Guide

| Use Case | Recommended Modules | Token Savings |
|----------|---------------------|---------------|
| Code navigation only | Navigation | 86% |
| Code review | Navigation + Quality | 71% |
| Security audit | Security | 93% |
| Full analysis | Full (or all modules) | 0% |

> 💡 **Tip**: Start with Navigation module. Add more modules as needed.

---

## Prerequisites

### Common Requirements
- **.NET 10.0 SDK or later** - [Download](https://dotnet.microsoft.com/download/dotnet/10.0)
- **Visual Studio 2026** or **VS Code** (recommended for development)
- **Git** (for cloning the repository)

### Platform-Specific Requirements

**For Claude Desktop:**
- Claude Desktop application installed
  - [Windows/macOS Download](https://claude.ai/download)

**For Claude CLI:**
- Claude CLI (Claude Code) installed
  - Native Install via: (for example) `curl -fsSL https://claude.ai/install.sh | bash` (macOS, Linux, WSL) or PS `irm https://claude.ai/install.ps1 | iex` (Windows).
    - Please refer to the [Claude CLI Installation Guide](https://code.claude.com/docs/en/overview) for detailed / updated instructions.
  - Verify: `claude --version` (should show version 2.1.2 or later)

## Installation

### 1. Clone the Repository

```bash
git clone https://github.com/bbfox0703/RoslynCSMCP.git
cd RoslynCSMCP
```

### 2. Build the Project

```bash
# Restore NuGet packages
dotnet restore

# Build the project
dotnet build -c Release
```

### 3. Verify Installation (Optional but Recommended)

```bash
# Windows
.\test-installation.bat

# Linux/macOS
./test-installation.sh
```

This script will:
- ✅ Check .NET version (must be 10.0+)
- ✅ Verify all required files exist
- ✅ Build the project
- ✅ Test server startup
- ✅ Check test project (if present)

For manual testing:
```bash
# Run the server (should show MCP protocol initialization)
cd RoslynMcpServer
dotnet run
```

Press `Ctrl+C` to stop the server after verifying it starts successfully.

---

## 🖥️ Claude Desktop Setup

<details>
<summary><b>Click to expand Claude Desktop setup instructions</b></summary>

### Automated Setup (Recommended)

#### Windows
```powershell
.\install\setup-claude-desktop.ps1
```

#### Linux/macOS
```bash
chmod +x install/setup-claude-desktop.sh
./install/setup-claude-desktop.sh
```

The automated scripts will:
- Check .NET installation
- Build the project
- Configure Claude Desktop automatically
- Test server startup

### Manual Setup

#### 1. Locate Claude Desktop Config File

- **Windows**: `%APPDATA%\Claude\claude_desktop_config.json`
- **macOS**: `~/Library/Application Support/Claude/claude_desktop_config.json`

#### 2. Edit Configuration

Add the following to your `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "roslyn-code-navigator": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "C:\\absolute\\path\\to\\RoslynCSMCP\\RoslynMcpServer"
      ],
      "env": {
        "DOTNET_ENVIRONMENT": "Production",
        "LOG_LEVEL": "Information"
      }
    }
  }
}
```

**Important**: Replace `C:\\absolute\\path\\to\\RoslynCSMCP\\RoslynMcpServer` with your actual absolute path.

- Windows example: `C:\\Users\\YourName\\Projects\\RoslynCSMCP\\RoslynMcpServer`
- macOS/Linux example: `/Users/YourName/Projects/RoslynCSMCP/RoslynMcpServer`

See [claude-desktop-config.example.json](install/claude-desktop-config.example.json) for a complete example.

#### 3. Restart Claude Desktop

Close and restart the Claude Desktop application to load the new configuration.

### Verification

Once restarted, you should see a small hammer icon (🔨) or MCP indicator in Claude Desktop. You can now use natural language to analyze C# code:

```
Search for all classes ending with 'Service' in C:\MyProject\MyProject.sln
Find all references to UserRepository in C:\MyProject\MyProject.sln
Analyze code complexity in C:\MyProject\MyProject.sln
```

</details>

---

## 💻 Claude CLI Setup

<details>
<summary><b>Click to expand Claude CLI setup instructions</b></summary>

### Prerequisites Check

Before configuring, ensure Claude CLI is installed:

```bash
# Check Claude CLI installation
claude --version

# Should output: claude version 2.1.2 (or later)
```

If not installed:
Please refer to the [Claude CLI Installation Guide](https://code.claude.com/docs/en/overview) for detail.

### Automated Setup (Recommended)

#### Windows
```powershell
.\install\setup-claude-cli.ps1
```

Options:
```powershell
# Interactive mode (choose scope: user/project/local)
.\install\setup-claude-cli.ps1

# User scope (available in all your projects)
.\install\setup-claude-cli.ps1 -Scope user

# Project scope (team-shared via .mcp.json)
.\install\setup-claude-cli.ps1 -Scope project

# Skip build step
.\install\setup-claude-cli.ps1 -SkipBuild
```

#### Linux/macOS
```bash
chmod +x install/setup-claude-cli.sh
./install/setup-claude-cli.sh
```

Options:
```bash
# Interactive mode
./install/setup-claude-cli.sh

# User scope
./install/setup-claude-cli.sh --scope user

# Project scope
./install/setup-claude-cli.sh --scope project

# Skip build
./install/setup-claude-cli.sh --skip-build
```

### Manual Setup

Choose the configuration scope that fits your needs:

| Scope | Storage | Use Case | Team Sharing |
|-------|---------|----------|--------------|
| **User** | `~/.config/claude/` | Personal use across all projects | ❌ No |
| **Project** | `.mcp.json` in repo | Team collaboration (committed to git) | ✅ Yes |
| **Local** | `.mcp.local.json` in repo | Project-specific, not shared (gitignored) | ❌ No |

#### User Scope (Personal Use)

Available in all your projects:

```bash
claude mcp add --transport stdio roslyn --scope user \
  --env DOTNET_ENVIRONMENT=Production \
  --env LOG_LEVEL=Information \
  -- dotnet run --project /absolute/path/to/RoslynCSMCP/RoslynMcpServer
```

#### Project Scope (Team Sharing)

Best for shared projects - configuration is committed to `.mcp.json`:

```bash
# From the RoslynCSMCP directory
claude mcp add --transport stdio roslyn --scope project \
  --env DOTNET_ENVIRONMENT=Production \
  --env LOG_LEVEL=Information \
  -- dotnet run --project ./RoslynMcpServer
```

This creates/updates `.mcp.json` which can be committed to version control.

#### Local Scope (Project-Specific, Private)

For project-specific configuration that won't be shared:

```bash
claude mcp add --transport stdio roslyn --scope local \
  --env DOTNET_ENVIRONMENT=Production \
  --env LOG_LEVEL=Information \
  -- dotnet run --project ./RoslynMcpServer
```

See [claude-cli-config.example.json](install/claude-cli-config.example.json) for a complete configuration example.

### Verification

```bash
# List all configured MCP servers
claude mcp list

# Check RoslynCSMCP configuration
claude mcp get roslyn

# Should show: roslyn (stdio) with command and environment details
```

### Using RoslynCSMCP in Claude CLI

Start Claude CLI and use natural language to analyze C# code:

```bash
claude

> Search for all classes implementing IRepository in MySolution.sln
> Find all references to UserService in MySolution.sln
> Analyze code complexity in src/Services/*.cs
> Show me the dependency graph for this solution
> Get information about the CalculateTotal method in MyProject.sln
```

📚 **For detailed Claude CLI integration guide**, see [CLAUDE_CLI_INTEGRATION.md](CLAUDE_CLI_INTEGRATION.md)

</details>

---

## Usage Examples

Once configured with either Claude Desktop or Claude CLI, you can use natural language queries:

### Search for Symbols
```
Search for all classes ending with 'Service' in C:\MyProject\MyProject.sln
Find all methods starting with 'Calculate' in /path/to/MySolution.sln
```

### Find References
```
Find all references to the UserRepository class in C:\MyProject\MyProject.sln
Where is the ProcessOrder method used in MySolution.sln?
```

### Get Symbol Information
```
Get information about the CalculateTotal method in C:\MyProject\MyProject.sln
Show me details about the UserService class
```

### Analyze Dependencies
```
Analyze dependencies for the solution at C:\MyProject\MyProject.sln
What are the namespace dependencies in MySolution.sln?
```

### Code Complexity Analysis
```
Find methods with complexity higher than 7 in C:\MyProject\MyProject.sln
Identify complex methods in src/Services/*.cs with threshold 10
```

## Available MCP Tools

The server exposes **42 MCP tools** for comprehensive C# code analysis, organized into 8 modules:

> 💡 **Modular Loading**: Each module can be loaded independently. See [Modular Architecture](#-modular-architecture-new) for configuration.

### Core Analysis Tools

1. **SearchSymbols** - Search for symbols using wildcard patterns (`*Service`, `Get*`, etc.)
   - Supports filtering by symbol kind (class, interface, method, property, field, event)
   - Returns top results ranked by relevance

2. **FindReferences** - Find all references to a specific symbol with configurable detail levels ⚡ **NEW**
   - **Summary mode** - Only file names and line numbers (95% token savings)
   - **Locations mode** - File names with code lines (80% token savings)
   - **Full mode** - Complete 5-line context around each reference
   - Distinguishes between definitions and usages

3. **GetSymbolInfo** - Get detailed information about a symbol
   - Returns type information, accessibility, location
   - Shows method signatures, property types, etc.

### Token Optimization Tools ⚡ **NEW**

4. **GetProjectStructure** - Get hierarchical overview of projects, namespaces, and types
   - Shows project organization at a glance (90% token savings)
   - Optional member signatures
   - Filter by namespace pattern
   - ~300 tokens vs ~3,000 tokens for multiple searches

5. **GetTypeSignature** - Get type signatures without implementation
   - Shows class/interface structure with all members (90% token savings)
   - Includes XML documentation comments
   - Option to include private members
   - ~200 tokens vs ~2,000 tokens for reading full file

### Advanced Analysis Tools

6. **AnalyzeDependencies** - Analyze project dependencies and namespace usage
   - Identifies inter-project dependencies
   - Shows most-used namespaces
   - Counts public vs internal symbols

7. **AnalyzeCodeComplexity** - Identify high-complexity methods
   - Calculates cyclomatic complexity
   - Configurable threshold (default: 5)
   - Helps identify refactoring candidates

### Advanced Analysis Tools

8. **GetCodeMetrics** - Comprehensive code statistics and quality metrics
   - Lines of code (total, code, comments, blank)
   - Type statistics (classes, interfaces, structs, enums)
   - Cyclomatic complexity analysis with hotspot identification
   - Project-by-project breakdown
   - Largest types and complexity hotspots
   - ~400 tokens for complete solution analysis

9. **GetDependencyGraph** - Project dependency visualization in multiple formats
   - Text format - Simple, readable dependency tree
   - DOT format - Graphviz visualization (for professional diagrams)
   - Mermaid format - Markdown diagrams (renders in GitHub/docs)
   - Optional package dependency inclusion
   - ~200-350 tokens depending on format

10. **GetCallHierarchy** - Method call chain analysis
    - Shows callers (who calls this method)
    - Shows callees (what this method calls)
    - Configurable direction (both, callers, callees)
    - Depth-limited traversal (default: 3 levels)
    - Call count tracking for frequently called methods
    - ~250-500 tokens depending on direction

11. **BatchQuery** - Execute multiple queries in a single request
    - Combine any MCP tools in one batch
    - Parallel or sequential execution
    - Graceful error handling (partial failures don't stop others)
    - Saves ~50-100 tokens per additional query vs separate requests
    - Reduces MCP protocol overhead

### Advanced Filtering

12. **FindReferencesFiltered** - Find references with intelligent filtering
    - **excludeTests**: Exclude test projects (Test, Tests, Testing, Spec) ⭐ Most useful
    - **crossProjectOnly**: Only cross-project references (API usage analysis)
    - **publicOnly**: Only public API references
    - **writesOnly**: Only write operations (assignments, increments)
    - **projectFilter**: Filter by project name pattern (supports wildcards)
    - Combines with detail levels (summary/locations/full)
    - **Token savings: 60-90%** by filtering out irrelevant references
    - Use case: "Find who uses DeleteUser in production code (exclude tests)" → 88% token savings

### Diagnostics & File Analysis

13. **GetCompilationErrors** - Get compilation errors and warnings without running full build
    - **severity**: Filter by Error, Warning, Info, or All
    - **projectFilter**: Filter by project name pattern (supports wildcards)
    - **errorCodes**: Filter by specific error codes (e.g., CS0103, CS0246)
    - Provides error location, message, and source code line
    - Groups by project and severity for easy navigation
    - **Token savings: 97.5%** vs reading files to find errors manually
    - Use case: "Get all CS0103 errors in MySolution.sln" → Quick missing variable detection

14. **GetFileOutline** - Get structural outline of C# files without implementation details
    - Shows file statistics (lines of code, comments, blanks)
    - Lists using statements and namespaces
    - Shows all types with inheritance and attributes
    - Lists members (constructors, fields, properties, methods, events)
    - Includes XML documentation comments
    - Option to include/exclude members and documentation
    - **Token savings: 95%** vs reading full file implementation
    - Use case: "Get outline of UserService.cs" → Understand file structure in 400 tokens instead of 8,000

### Navigation & Testing

15. **FindImplementations** - Find all implementations of interfaces or abstract classes
    - Discovers all concrete classes implementing an interface
    - Finds all classes inheriting from abstract base classes
    - Option to include abstract implementations
    - Shows inheritance hierarchy and implemented interfaces
    - Groups results by project
    - **Token savings: 70-80%** vs manual file searching
    - Use case: "Find implementations of IUserRepository" → Quickly discover all repository implementations

16. **FindTestsForType** - Find test classes and methods for a given type
    - Discovers test classes using naming conventions ({Type}Tests, Test{Type}, etc.)
    - Detects test frameworks (xUnit, NUnit, MSTest)
    - Lists all test methods with attributes
    - Supports partial name matching
    - Shows test display names and descriptions
    - Groups by test project and framework
    - **Token savings: 90%+** vs reading test files manually
    - Use case: "Find tests for UserService" → See complete test coverage with 28 tests in 800 tokens

### Hierarchy & Attribute Analysis

17. **GetClassHierarchy** - Get complete class hierarchy showing ancestors and descendants
    - Shows inheritance chain (base classes and interfaces)
    - Displays derived types (classes that inherit from or implement the type)
    - Configurable direction (ancestors/descendants/both)
    - Recursive traversal with max depth control
    - Visual tree structure with type information
    - **Token savings: 60-70%** vs reading multiple files to understand hierarchy
    - Use case: "Get hierarchy for UserService" → See complete inheritance tree with ancestors and descendants

18. **FindAttributeUsages** - Find all usages of specific attributes across the solution
    - Discovers all types/members decorated with an attribute
    - Supports all attribute targets (class, method, property, field, parameter, etc.)
    - Shows attribute arguments (positional and named)
    - Filter by target type for focused results
    - Groups by target type and project
    - **Token savings: 80-90%** vs searching and reading files manually
    - Use case: "Find all [Obsolete] attributes" → Identify all deprecated code quickly

### Code Quality & Detection

19. **FindUnusedCode** - Detect unused code in your solution
20. **FindDuplicateCode** - Find duplicate or similar code blocks
21. **AnalyzeDocumentationCoverage** - Check XML documentation coverage
22. **FindSecurityIssues** - Detect potential security vulnerabilities
23. **FindUnusedDependencies** - Find unused NuGet packages

### Package Analysis

24. **AnalyzePackages** - Analyze NuGet package usage and versions

### Testing & Quality

25. **GetTestCoverage** - Analyze test coverage metrics
26. **GetChangeImpact** - Analyze impact of code changes

### Code Maintenance

27. **FindTODOComments** - Find TODO, FIXME, HACK comments
28. **FindLargeFiles** - Find files exceeding size thresholds
29. **FindDeprecatedAPIs** - Find usage of deprecated APIs
30. **GetFileStatistics** - Get detailed statistics for files

### Batch Operations

31. **BatchQuery** - Execute multiple queries in a single request

### Performance, Security & Standards

32. **FindPerformanceIssues** - Detect C# performance anti-patterns
33. **AnalyzeNamingConventions** - Check naming convention compliance
34. **AnalyzeAPIChanges** - Track API changes between versions

📚 **[Complete Usage Examples](docs/EXAMPLES.md)** - Comprehensive guide with 100+ examples organized by feature category

## Development and Testing

### Using MCP Inspector

For development and testing without Claude Desktop/CLI:

```bash
# Install the MCP Inspector
npm install -g @modelcontextprotocol/inspector

# Run the inspector
npx @modelcontextprotocol/inspector dotnet run --project ./RoslynMcpServer
```

The inspector opens a web interface where you can:
- Test all MCP tools interactively
- View request/response JSON
- Debug server behavior

### Running Tests

```bash
# Run from the project root
dotnet test
```

### Building for Release

```bash
dotnet build -c Release
dotnet publish -c Release -o ./publish
```

## Architecture

The server features a **modular, layered architecture** that supports both full and selective tool loading:

```
RoslynCSMCP.sln
├── src/
│   ├── RoslynMcpServer.Core/           # Shared library (services, models)
│   ├── RoslynMcpServer.Navigation/     # Navigation MCP (6 tools)
│   ├── RoslynMcpServer.Quality/        # Quality MCP (6 tools)
│   ├── RoslynMcpServer.Security/       # Security MCP (3 tools)
│   ├── RoslynMcpServer.Dependencies/   # Dependencies MCP (5 tools)
│   ├── RoslynMcpServer.Refactoring/    # Refactoring MCP (4 tools)
│   ├── RoslynMcpServer.Testing/        # Testing MCP (2 tools)
│   ├── RoslynMcpServer.Metrics/        # Metrics MCP (3 tools)
│   └── RoslynMcpServer.Advanced/       # Advanced MCP (13 tools)
├── RoslynMcpServer/                    # Full version (42 tools)
└── RoslynMcpServer.Tests/              # Unit & integration tests
```

### Layer Structure

- **MCP Server Layer** (`Program.cs` in each module) - Handles MCP protocol communication via stdio transport
- **Tools Layer** (`Tools/*.cs`) - Exposes MCP tools with `[McpServerTool]` attributes
- **Core Library** (`RoslynMcpServer.Core/`):
  - **Services**: All analysis services (SymbolSearchService, CodeAnalysisService, etc.)
  - **Models**: DTOs for all tool results
  - **Configuration**: Tool profile configuration
  - **Utilities**: SecurityValidator, DiagnosticLogger, CacheManager
- **Roslyn Integration** - MSBuildWorkspace for loading .sln files and performing semantic analysis

### Key Features

**Performance Optimization:**
- Multi-level caching (L1: Memory 10min, L2: Redis 1hr, L3: File 7days)
- Incremental analysis with file timestamp tracking
- Parallel project processing
- Throttled concurrent operations (CPU count limit)

**Security:**
- Path traversal prevention
- Allowed file extensions (.sln, .csproj only)
- Input sanitization for search patterns
- Safe regex pattern compilation

**Roslyn Integration:**
- MSBuild workspace for solution loading
- Semantic model for symbol analysis
- SymbolFinder for reference tracking
- Compilation caching for performance

For detailed architecture documentation, see [CLAUDE.md](CLAUDE.md).

## Troubleshooting

### Server Not Appearing in Claude Desktop

1. Check the config file path is correct for your OS
2. Verify the project path is absolute, not relative
3. Ensure the JSON syntax is valid (no trailing commas)
4. Restart Claude Desktop completely (check system tray on Windows)
5. Check Claude Desktop logs:
   - Windows: `%APPDATA%\Claude\logs\`
   - macOS: `~/Library/Logs/Claude/`

### Server Not Appearing in Claude CLI

```bash
# List configured servers
claude mcp list

# Check specific server configuration
claude mcp get roslyn

# Test MCP connection
claude mcp test roslyn
```

### Build Errors

```bash
# Clean and rebuild
dotnet clean
dotnet restore
dotnet build -c Release
```

Ensure you have **.NET 10.0 SDK** installed:
```bash
dotnet --version
# Should show 10.0.x
```

### Server Fails to Start

1. **Check .NET installation**: `dotnet --version` should show 10.0.x
2. **Check MSBuild**: Ensure MSBuild 17.11+ is available
3. **Review logs**: Check stderr output for error messages
4. **Test manually**: Run `dotnet run --project ./RoslynMcpServer` and check for errors

### Permission Issues (Linux/macOS)

```bash
# Make scripts executable
chmod +x install/setup-claude-desktop.sh
chmod +x install/setup-claude-cli.sh
chmod +x RoslynMcpServer/test-installation.sh
```

### Debugging with File Logs 🔍

**Problem**: When running as an MCP server from Claude Desktop/CLI, debug output goes to stderr and may be hard to view.

**Solution**: Enable Development mode to write detailed logs to file.

#### Enable Debug Logging

**For Claude Desktop**, edit your config file:
```json
{
  "mcpServers": {
    "roslyn": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/RoslynCSMCP/RoslynMcpServer"],
      "env": {
        "DOTNET_ENVIRONMENT": "Development"
      }
    }
  }
}
```

**For Claude CLI**, add the environment variable:
```bash
claude mcp add --transport stdio roslyn --scope user \
  --env DOTNET_ENVIRONMENT=Development \
  -- dotnet run --project /path/to/RoslynCSMCP/RoslynMcpServer
```

#### Locate Log Files

After enabling Development mode, logs are written to:

- **Windows**: `%TEMP%\RoslynCSMCP\logs\debug-YYYYMMDD.log`
  ```powershell
  # View in PowerShell
  Get-Content "$env:TEMP\RoslynCSMCP\logs\debug-$(Get-Date -Format yyyyMMdd).log" -Wait -Tail 50
  ```

- **Linux/macOS**: `/tmp/RoslynCSMCP/logs/debug-YYYYMMDD.log`
  ```bash
  # View in terminal
  tail -f /tmp/RoslynCSMCP/logs/debug-$(date +%Y%m%d).log
  ```

#### Log Contents

Debug logs include:
- All MCP tool invocations with parameters
- Operation timing from DiagnosticLogger
- MSBuild workspace loading events
- Symbol search and analysis details
- Cache hits/misses
- Full error stack traces

#### Production Logs

In Production mode (default), only warnings and errors are logged to:
- **Windows**: `%TEMP%\RoslynCSMCP\logs\roslyn-mcp-YYYYMMDD.log`
- **Linux/macOS**: `/tmp/RoslynCSMCP/logs/roslyn-mcp-YYYYMMDD.log`
- Retained for 30 days (vs 7 days for debug logs)

## Documentation

| Document | Description |
|----------|-------------|
| **[CLAUDE.md](CLAUDE.md)** | Development guide and architecture for Claude Code |
| **[docs/FEATURES.md](docs/FEATURES.md)** | Complete API reference for all 42 tools |
| **[docs/EXAMPLES.md](docs/EXAMPLES.md)** | Comprehensive usage guide with 100+ examples |
| **[docs/TESTING.md](docs/TESTING.md)** | Testing guide (Desktop & CLI setup, MCP tests) |
| **[docs/AGENT_SKILLS.md](docs/AGENT_SKILLS.md)** | Agent skills guide for effective tool usage |

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- Fork from [RoslynMCP](https://github.com/carquiza/RoslynMCP), originally by [Chris Arquiza](https://github.com/carquiza). Here is for personal playground.
- Not all scripts (installation, test, PowerShell script) are tested!
- Built with [Roslyn](https://github.com/dotnet/roslyn) - The .NET Compiler Platform
- Uses [Model Context Protocol](https://modelcontextprotocol.io) for Claude integration
- Powered by [.NET 10.0](https://dotnet.microsoft.com/download/dotnet/10.0)
