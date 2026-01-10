# MCP Tool Selection: How Claude Chooses RoslynCSMCP

**Date**: 2026-01-10
**Status**: Documentation

## Overview

This document explains how Claude Desktop and Claude CLI decide when to use RoslynCSMCP tools versus built-in tools, and how to avoid misuse with non-C# projects.

## How MCP Tool Selection Works

### 1. Tool Discovery Phase

When Claude starts:
1. **Loads all configured MCP servers** from configuration
   - Desktop: `%APPDATA%\Claude\claude_desktop_config.json`
   - CLI: Via `claude mcp list` (stored in user settings)
2. **Retrieves available tools** from each MCP server
3. **Reads tool descriptions and parameters** to understand capabilities

For RoslynCSMCP, Claude sees these tools:
```
✓ SearchSymbols - "Search for symbols in C# code using wildcard patterns"
✓ FindReferences - "Find all references to a specific symbol"
✓ GetSymbolInfo - "Get detailed information about a specific symbol"
✓ AnalyzeDependencies - "Analyze project dependencies"
✓ AnalyzeCodeComplexity - "Analyze code complexity"
... (15 total tools)
```

### 2. Context Analysis Phase

When you ask Claude a question, Claude analyzes:

**A. Working Directory Context**
```powershell
# Claude automatically scans the current directory
Get-ChildItem -File | Select-Object Extension

# Examples that suggest C# project:
✓ *.sln files (solution files)
✓ *.csproj files (C# project files)
✓ *.cs files (C# source files)
✓ packages.config or *.nuget files
✓ Directory structure like src/, tests/, etc.

# Examples that suggest NOT C# project:
✗ *.vcxproj (C++ project)
✗ *.lua files (Lua scripts)
✗ CMakeLists.txt (C++ build system)
✗ package.json without .cs files (JavaScript project)
```

**B. User Prompt Analysis**
```
Examples that trigger RoslynCSMCP:
✓ "Find all classes named UserService"  → C# class search
✓ "Show me references to GetUser method" → C# method references
✓ "Analyze dependencies in this solution" → .NET solution analysis
✓ "What's the complexity of this C# method?" → C# complexity analysis

Examples that DON'T trigger RoslynCSMCP:
✗ "Find all files with 'User' in the name" → Generic file search (uses Glob/Grep)
✗ "Show me the project structure" → Generic file listing
✗ "Search for TODO comments" → Text search (uses Grep)
```

**C. Tool Description Matching**

Claude matches user intent to tool capabilities:

| User Intent | Best Tool | Why |
|-------------|-----------|-----|
| "Find C# class UserService" | `SearchSymbols` | Tool description mentions "symbols in C# code" |
| "Find references to GetUser" | `FindReferences` | Tool description mentions "references to a symbol" |
| "Analyze this solution" | `AnalyzeDependencies` | Requires `.sln` file, C#-specific |
| "List files in directory" | Built-in `Glob` | Generic file operation, faster |
| "Search for text pattern" | Built-in `Grep` | Generic text search, doesn't need Roslyn |

### 3. Decision Making

Claude selects tools based on a decision tree:

```
User asks question
    │
    ├─ Is this a C# codebase? (*.sln, *.cs files present)
    │   ├─ YES → Continue to C# tool selection
    │   └─ NO → Use generic tools (Glob, Grep, Read)
    │
    ├─ Does the question need semantic analysis?
    │   ├─ YES (symbol search, references, complexity) → Use RoslynCSMCP
    │   └─ NO (file search, text grep) → Use built-in tools
    │
    └─ Are RoslynCSMCP tools available?
        ├─ YES → Use RoslynCSMCP
        └─ NO → Fallback to generic tools (with warning)
```

## Language-Specific Detection

### C# Projects (Use RoslynCSMCP)

**Indicators:**
- `.sln` files (Visual Studio Solution)
- `.csproj` files (C# Project files)
- `.cs` files (C# source code)
- NuGet package files (`packages.config`, `*.nupkg`)
- Common C# directories: `bin/`, `obj/`, `Properties/`

**RoslynCSMCP behavior:**
```csharp
// SecurityValidator checks file extensions
public bool ValidateSolutionPath(string path)
{
    var extension = Path.GetExtension(path).ToLowerInvariant();
    if (extension != ".sln" && extension != ".csproj")
    {
        return false; // ❌ Not a C# solution/project
    }
    // ✅ Valid C# solution/project
    return true;
}
```

### C++ Projects (Don't use RoslynCSMCP)

**Indicators:**
- `.sln` files (Visual Studio can use .sln for C++)
- `.vcxproj` files (C++ project files)
- `.cpp`, `.h`, `.hpp` files (C++ source)
- `CMakeLists.txt` (CMake build system)

**What happens if misused:**
```
User: "Analyze this C++ solution"
Claude: *Sees .sln file*
Claude: *Tries RoslynCSMCP tools*
RoslynCSMCP: ❌ Error: Failed to load workspace
               (Roslyn can't parse C++ projects in .sln)
Claude: *Receives error*
Claude: *Fallback to generic tools*
Result: No crash, graceful degradation
```

### Lua Projects (Don't use RoslynCSMCP)

**Indicators:**
- `.lua` files (Lua scripts)
- No `.sln`, `.csproj`, or `.cs` files
- Common Lua directories: `scripts/`, `mods/`

**What happens:**
```
User: "Find function definitions in Lua files"
Claude: *No .sln or .cs files detected*
Claude: *RoslynCSMCP tools not relevant*
Claude: *Uses Grep to search .lua files*
Result: Built-in tools used, RoslynCSMCP never invoked
```

### Mixed Projects (Selective Use)

**Example: Unity Game Project**
```
GameProject/
├── GameProject.sln          ← C# solution (Unity uses C#)
├── Assets/
│   ├── Scripts/
│   │   ├── Player.cs        ← C# scripts
│   │   └── Enemy.cs
│   └── Lua/
│       └── config.lua       ← Lua configuration
└── Plugins/
    └── native.dll
```

**Expected behavior:**
```
User: "Find all Player classes"
→ Claude uses SearchSymbols (C# project present)
→ Finds Player.cs via Roslyn semantic search

User: "Find 'config' in Lua files"
→ Claude uses Grep (text search)
→ Finds config.lua via pattern matching
```

## Preventing Misuse

### 1. Clear Tool Descriptions

All RoslynCSMCP tools explicitly mention **"C# code"** or **".sln file"**:

```csharp
[McpServerTool, Description("Search for symbols in C# code using wildcard patterns")]
public static async Task<string> SearchSymbols(...)

[McpServerTool, Description("Find all references to a specific symbol")]
public static async Task<string> FindReferences(
    [Description("Path to solution file (.sln)")] string solutionPath, ...)
```

**Impact**: Claude's LLM understands these are C#-specific tools.

### 2. Path Validation

`SecurityValidator` enforces file extension restrictions:

```csharp
public bool ValidateSolutionPath(string path)
{
    // Only allow .sln and .csproj files
    var extension = Path.GetExtension(path).ToLowerInvariant();
    return extension == ".sln" || extension == ".csproj";
}
```

**Impact**:
- ✅ `MyProject.sln` → Allowed
- ✅ `MyProject.csproj` → Allowed
- ❌ `MyProject.vcxproj` → Rejected (C++ project)
- ❌ `build.gradle` → Rejected (Java project)

### 3. Roslyn Workspace Validation

When loading a solution, Roslyn automatically validates:

```csharp
// In CodeAnalysisService
var workspace = MSBuildWorkspace.Create();
var solution = await workspace.OpenSolutionAsync(solutionPath);

// If solution contains non-C# projects:
foreach (var project in solution.Projects)
{
    if (!project.SupportsCompilation) // ← Non-C# project
    {
        // Skip this project
        continue;
    }

    var compilation = await project.GetCompilationAsync();
    if (compilation == null) // ← Failed to compile
    {
        // Error: Not a valid C# project
    }
}
```

**Impact**: Even if a .sln contains C++ projects, Roslyn skips them.

### 4. Graceful Error Handling

All tools have comprehensive error handling:

```csharp
catch (FileNotFoundException)
{
    return "Error: Solution file not found.";
}
catch (InvalidProjectFileException)
{
    return "Error: Invalid or corrupted project file.";
}
catch (Exception ex)
{
    logger.LogError(ex, "Unexpected error");
    return "Error: An unexpected error occurred.";
}
```

**Impact**: Errors are reported to Claude, which then tries alternative approaches.

## User Control Mechanisms

### 1. Explicit Tool Specification (Claude Desktop)

Users can't directly specify tools in Claude Desktop, but can be explicit in prompts:

```
❌ Vague: "Find UserService"
   → Claude might use generic Grep

✅ Explicit: "Use Roslyn to find C# class UserService in this solution"
   → Claude understands to use SearchSymbols

✅ Very specific: "Find all references to GetUser method using semantic analysis"
   → Claude uses FindReferences (semantic = Roslyn)
```

### 2. Claude CLI Tool Filtering

Claude CLI allows restricting available tools:

```bash
# Only allow specific tools
claude --allowed-tools "Bash,Edit,Read" --strict-mcp-config

# Disable all MCP tools, use only built-in
claude --tools "Bash,Edit,Read,Write,Glob,Grep"

# Disable specific MCP servers
claude --strict-mcp-config --mcp-config ""
```

### 3. MCP Server Management

Users can disable RoslynCSMCP when not needed:

```powershell
# Temporarily disable
claude mcp remove roslyn-mcp

# Re-enable later
claude mcp add roslyn-mcp -e DOTNET_ENVIRONMENT=Development -- dotnet run --project ".\RoslynMcpServer"
```

## Verification: Is RoslynCSMCP Being Used?

### Check Logs

**Location**: `%TEMP%\RoslynCSMCP\logs\debug-YYYYMMDD.log`

**If RoslynCSMCP is used:**
```
[INF] Executing tool: SearchSymbols
[INF] Parameters: { pattern: "UserService", solutionPath: "D:\MyProject\MyProject.sln" }
[INF] MSBuildWorkspace opened successfully
[INF] Loading solution from D:\MyProject\MyProject.sln
[INF] Found 15 matching symbols
```

**If built-in tools are used:**
```
(No RoslynCSMCP logs)
```

**In Claude's response:**

```
✓ Using RoslynCSMCP:
   "I found 3 classes named UserService using Roslyn semantic search:
    1. MyProject.Services.UserService (UserService.cs:15)
    2. MyProject.Core.UserService (CoreUserService.cs:42)
    ..."

✗ Using built-in tools:
   "I found files containing 'UserService':
    - UserService.cs (grep match)
    - IUserService.cs (grep match)
    ..."
```

### Real-time Monitoring

```powershell
# Monitor logs during Claude usage
$today = Get-Date -Format "yyyyMMdd"
Get-Content "$env:TEMP\RoslynCSMCP\logs\debug-$today.log" -Wait -Tail 50
```

### Test Script

Use the provided test script:
```powershell
.\test-mcp-cli.ps1  # For Claude CLI
.\test-mcp-connection.ps1  # For Claude Desktop
```

## Summary

| Factor | How It Affects Tool Selection |
|--------|------------------------------|
| **File Extensions** | `.sln`, `.csproj`, `.cs` → RoslynCSMCP likely |
| **User Prompt** | Mentions "C# class", "references", "semantic" → RoslynCSMCP |
| **Tool Descriptions** | All RoslynCSMCP tools say "C# code" or ".sln file" |
| **Path Validation** | SecurityValidator blocks non-.sln/.csproj paths |
| **Roslyn Validation** | MSBuildWorkspace fails on non-C# projects |
| **Error Handling** | Graceful fallback to generic tools on failure |
| **Logs** | Check `debug-YYYYMMDD.log` to verify usage |

## Recommendations

### For C# Projects

✅ **Do:**
- Keep RoslynCSMCP configured
- Use specific prompts: "Find C# class UserService"
- Mention "using Roslyn" or "semantic search" for clarity
- Ensure .sln file is in working directory

❌ **Don't:**
- Use vague prompts like "find files"
- Expect RoslynCSMCP for generic text searches

### For Non-C# Projects

✅ **Do:**
- Remove RoslynCSMCP if not needed: `claude mcp remove roslyn-mcp`
- Use generic prompts that trigger built-in tools

❌ **Don't:**
- Keep RoslynCSMCP configured when only working with C++/Lua
- Worry about accidental usage (validation prevents it)

### For Mixed Projects

✅ **Do:**
- Keep RoslynCSMCP configured
- Use specific prompts based on file type:
  - "Find C# class X" → RoslynCSMCP
  - "Search for pattern Y in Lua files" → Built-in Grep

## Related Documentation

- `TESTING_GUIDE.md` - Testing RoslynCSMCP integration
- `CLAUDE.md` - Project overview and architecture
- `README.md` - Installation and setup
