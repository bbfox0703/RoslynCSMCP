# Configuration

## Claude Desktop Config Location

| Platform | Path |
|----------|------|
| Windows | `%APPDATA%\Claude\claude_desktop_config.json` |
| macOS | `~/Library/Application Support/Claude/claude_desktop_config.json` |

---

## Modular Configuration (Recommended)

Load only the modules you need to minimize token usage:

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

| Module | Tools | ~Tokens |
|--------|-------|---------|
| Navigation | 6 | ~1,050 |
| Quality | 6 | ~1,050 |
| Security | 3 | ~525 |
| Dependencies | 5 | ~875 |
| Refactoring | 4 | ~700 |
| Testing | 2 | ~350 |
| Metrics | 3 | ~525 |
| Advanced | 13 | ~2,275 |

---

## Full Version Configuration

All 42 tools in a single server:

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

---

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `DOTNET_ENVIRONMENT` | `Production` | Set to `Development` for verbose debug logs |
| `HEARTBEAT_INTERVAL_MINUTES` | `20` | Heartbeat interval (5–60 minutes) |
| `LOG_LEVEL` | — | Deprecated; use Serilog config instead |

---

## Debug Logging

### Enable Development Mode

```json
{
  "mcpServers": {
    "roslyn": {
      "command": "dotnet",
      "args": ["run", "--project", "D:/RoslynCSMCP/RoslynMcpServer"],
      "env": {
        "DOTNET_ENVIRONMENT": "Development"
      }
    }
  }
}
```

### Log File Locations

| Platform | Development | Production |
|----------|-------------|------------|
| Windows | `%TEMP%\RoslynCSMCP\logs\debug-YYYYMMDD.log` | `%TEMP%\RoslynCSMCP\logs\roslyn-mcp-YYYYMMDD.log` |
| Linux/macOS | `/tmp/RoslynCSMCP/logs/debug-YYYYMMDD.log` | `/tmp/RoslynCSMCP/logs/roslyn-mcp-YYYYMMDD.log` |

Startup logs: `startup-YYYYMMDD.log` (captures early initialization before main log is ready).

### What's in the Logs

- All tool invocations with parameters
- Operation timing (from `DiagnosticLogger`)
- MSBuild workspace loading events
- Symbol search and analysis details
- Cache hits/misses
- Errors with full stack traces

### View Logs in Real-time

```powershell
# Windows PowerShell
Get-Content "$env:TEMP\RoslynCSMCP\logs\debug-$(Get-Date -Format yyyyMMdd).log" -Wait -Tail 50
```
```bash
# Linux/macOS
tail -f /tmp/RoslynCSMCP/logs/debug-$(date +%Y%m%d).log
```

When the server starts in Development mode, it prints the exact tail command to use.
