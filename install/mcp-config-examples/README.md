# MCP Configuration Examples

Copy the appropriate `.mcp.json` file to your project root for local repo-scope configuration.

## Usage

1. Choose a configuration file based on your needs
2. Copy it to your project root as `.mcp.json`
3. Update the path to match your RoslynCSMCP installation location

```bash
# Example: Copy navigation config
cp mcp.json.navigation /path/to/your/project/.mcp.json
```

## Available Configurations

| File | Modules | Tools | Tokens | Use Case |
|------|---------|-------|--------|----------|
| `mcp.json.full` | Full | 42 | ~7,350 | All features |
| `mcp.json.navigation` | Navigation | 6 | ~1,050 | Code exploration |
| `mcp.json.quality` | Quality | 6 | ~1,050 | Code review |
| `mcp.json.standard` | Nav + Quality + Security | 15 | ~2,625 | Daily development |

## Path Configuration

Replace `${workspaceFolder}/../RoslynCSMCP` with:

**Absolute Path (Recommended)**:
```json
"args": ["run", "--project", "D:/Tools/RoslynCSMCP/src/RoslynMcpServer.Navigation"]
```

**Relative Path**:
```json
"args": ["run", "--project", "../RoslynCSMCP/src/RoslynMcpServer.Navigation"]
```

## Module Reference

| Module | Path | Tools | Available Skills |
|--------|------|-------|------------------|
| Full | `RoslynMcpServer` | 42 | All skills |
| Navigation | `src/RoslynMcpServer.Navigation` | 6 | `/roslyn-explore`, `/roslyn-navigate`, `/roslyn-outline` |
| Quality | `src/RoslynMcpServer.Quality` | 6 | `/roslyn-quality` |
| Security | `src/RoslynMcpServer.Security` | 3 | `/roslyn-security` |
| Dependencies | `src/RoslynMcpServer.Dependencies` | 5 | `/roslyn-dependencies` |
| Refactoring | `src/RoslynMcpServer.Refactoring` | 4 | `/roslyn-refactor` |
| Testing | `src/RoslynMcpServer.Testing` | 2 | `/roslyn-testing` |
| Metrics | `src/RoslynMcpServer.Metrics` | 3 | `/roslyn-metrics` |
| Advanced | `src/RoslynMcpServer.Advanced` | 13 | `/roslyn-deep-analysis`, `/roslyn-batch`, `/roslyn-api-diff` |

## Custom Configuration

Create your own `.mcp.json` by combining modules:

```json
{
  "mcpServers": {
    "roslyn-navigation": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/RoslynCSMCP/src/RoslynMcpServer.Navigation"],
      "env": { "DOTNET_ENVIRONMENT": "Production" }
    },
    "roslyn-dependencies": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/RoslynCSMCP/src/RoslynMcpServer.Dependencies"],
      "env": { "DOTNET_ENVIRONMENT": "Production" }
    }
  }
}
```
