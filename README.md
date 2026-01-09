# Roslyn MCP Server

![.NET 10.0](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet) ![MCP 0.5.0](https://img.shields.io/badge/MCP-0.5.0--preview.1-00A4EF) ![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)

A C# MCP (Model Context Protocol) server that integrates with Microsoft's Roslyn compiler platform to provide **Claude Desktop** and **Claude CLI** with powerful code analysis and navigation capabilities for C# codebases.

## Features

- **Wildcard Symbol Search** - Find classes, methods, and properties using pattern matching (`*Service`, `Get*User`, etc.)
- **Reference Tracking** - Locate all usages of symbols across entire solutions
- **Symbol Information** - Get detailed information about types, methods, properties, and more
- **Dependency Analysis** - Analyze project dependencies and namespace usage patterns
- **Code Complexity Analysis** - Identify high-complexity methods using cyclomatic complexity metrics
- **Performance Optimized** - Multi-level caching (Memory, Redis, File system) and incremental analysis for large codebases
- **Security Hardened** - Input validation, path sanitization, and safe file operations

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
  - Install via: `npm install -g @anthropics/claude-cli`
  - Verify: `claude --version` (should show version 0.2.0 or later)
- Node.js 18+ (required for Claude CLI)

## Installation

### 1. Clone the Repository

```bash
git clone https://github.com/bbfox0703/RoslynMCP.git
cd RoslynMCP
```

### 2. Build the Project

```bash
# Restore NuGet packages
dotnet restore

# Build the project
dotnet build -c Release
```

### 3. Test the Server (Optional)

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
        "C:\\absolute\\path\\to\\RoslynMCP\\RoslynMcpServer"
      ],
      "env": {
        "DOTNET_ENVIRONMENT": "Production",
        "LOG_LEVEL": "Information"
      }
    }
  }
}
```

**Important**: Replace `C:\\absolute\\path\\to\\RoslynMCP\\RoslynMcpServer` with your actual absolute path.

- Windows example: `C:\\Users\\YourName\\Projects\\RoslynMCP\\RoslynMcpServer`
- macOS/Linux example: `/Users/YourName/Projects/RoslynMCP/RoslynMcpServer`

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

# Should output: claude version 0.2.0 (or later)
```

If not installed:
```bash
npm install -g @anthropics/claude-cli
```

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
  -- dotnet run --project /absolute/path/to/RoslynMCP/RoslynMcpServer
```

#### Project Scope (Team Sharing)

Best for shared projects - configuration is committed to `.mcp.json`:

```bash
# From the RoslynMCP directory
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

# Check RoslynMCP configuration
claude mcp get roslyn

# Should show: roslyn (stdio) with command and environment details
```

### Using RoslynMCP in Claude CLI

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

The server exposes 8 MCP tools:

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

📚 **[Phase 1 Usage Examples](docs/PHASE1_USAGE_EXAMPLES.md)** - Detailed examples and token savings guide

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

The server features a modular, layered architecture:

- **MCP Server Layer** (`Program.cs`) - Handles MCP protocol communication via stdio transport
- **Tools Layer** (`Tools/CodeNavigationTools.cs`) - Exposes 5 MCP tools with `[McpServerTool]` attributes
- **Services Layer**:
  - `SymbolSearchService` - Core symbol search and reference finding using Roslyn
  - `CodeAnalysisService` - Solution loading and dependency analysis
  - `IncrementalAnalyzer` - File-level caching for performance
  - `MultiLevelCacheManager` - 3-tier caching (Memory → Redis → File system)
  - `SecurityValidator` - Input validation and path sanitization
- **Models Layer** (`Models/SearchModels.cs`) - DTOs for all tool results
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

## Project Structure

```
RoslynMCP/
├── RoslynMcpServer/              # Main MCP server project
│   ├── Program.cs                # MCP server entry point
│   ├── Tools/
│   │   └── CodeNavigationTools.cs # 5 MCP tool implementations
│   ├── Services/
│   │   ├── SymbolSearchService.cs
│   │   ├── CodeAnalysisService.cs
│   │   ├── IncrementalAnalyzer.cs
│   │   ├── MultiLevelCacheManager.cs
│   │   └── SecurityValidator.cs
│   └── Models/
│       └── SearchModels.cs       # DTOs for tool results
├── install/                      # Installation and setup scripts
│   ├── setup-claude-desktop.ps1  # Windows automated setup for Claude Desktop
│   ├── setup-claude-desktop.sh   # Linux/macOS setup for Claude Desktop
│   ├── setup-claude-cli.ps1      # Windows automated setup for Claude CLI
│   ├── setup-claude-cli.sh       # Linux/macOS setup for Claude CLI
│   ├── claude-desktop-config.example.json # Example Claude Desktop config
│   └── claude-cli-config.example.json     # Example Claude CLI config
├── docs/                         # documents
│   ├── CLAUDE_CLI_INTEGRATION.md     # Detailed Claude CLI integration guide
│   └── UPGRADE_COMPLETE.md           # .NET 10 upgrade documentation
├── .mcp.json                     # Project-scope MCP config (Claude CLI)
├── CLAUDE.md                     # Architecture documentation for Claude Code
└── README.md                     # This file
```

## Documentation

- **[CLAUDE.md](CLAUDE.md)** - Development guide and architecture documentation for Claude Code
- **[CLAUDE_CLI_INTEGRATION.md](docs/CLAUDE_CLI_INTEGRATION.md)** - Comprehensive Claude CLI integration guide
- **[UPGRADE_COMPLETE.md](docs/UPGRADE_COMPLETE.md)** - .NET 10 upgrade report and details
- **[TOKEN_OPTIMIZATION_PLAN.md](docs/TOKEN_OPTIMIZATION_PLAN.md)** - Token optimization features evaluation and implementation plan
- **[PHASE1_USAGE_EXAMPLES.md](docs/PHASE1_USAGE_EXAMPLES.md)** - ⚡ Phase 1 token optimization features usage guide with examples

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Author

**Christopher Arquiza**

## Acknowledgments

- Fork from [RoslynMCP](https://github.com/carquiza/RoslynMCP), originally by [Chris Arquiza](https://github.com/carquiza). For personal playground.
- Built with [Roslyn](https://github.com/dotnet/roslyn) - The .NET Compiler Platform
- Uses [Model Context Protocol](https://modelcontextprotocol.io) for Claude integration
- Powered by [.NET 10.0](https://dotnet.microsoft.com/download/dotnet/10.0)
