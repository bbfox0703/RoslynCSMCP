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
| Navigation | 7 | ~1,225 |
| Quality | 8 | ~1,400 |
| Security | 3 | ~525 |
| Dependencies | 5 | ~875 |
| Refactoring | 5 | ~875 |
| Testing | 2 | ~350 |
| Metrics | 4 | ~700 |
| Advanced | 15 | ~2,625 |
| Interop | 3 | ~525 |

---

## Full Version Configuration

All 51 tools in a single server:

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

## Automated Setup & Uninstall

The `install/` scripts configure (or remove) the MCP servers for you, interactively or via flags.

| Target | Windows | Linux/macOS |
|--------|---------|-------------|
| Claude Desktop | `.\install\setup-claude-desktop.ps1` | `./install/setup-claude-desktop.sh` |
| Claude CLI | `.\install\setup-claude-cli.ps1` | `./install/setup-claude-cli.sh` |

Run without arguments for the interactive menu (pick a tool set, or `[R]` to remove all).

**Uninstall all Roslyn MCP servers:**

```powershell
# Windows
.\install\setup-claude-desktop.ps1 -RemoveAll
.\install\setup-claude-cli.ps1 -RemoveAll
```

```bash
# Linux/macOS
./install/setup-claude-desktop.sh --remove-all
./install/setup-claude-cli.sh --remove-all
```

Removal targets only the servers these scripts create (`roslyn-full` … `roslyn-interop`); any other `roslyn-*` entries you added manually are left untouched. The Desktop scripts edit `claude_desktop_config.json` directly (the Linux/macOS script needs `jq`); the CLI scripts call `claude mcp remove`. Restart Claude Desktop after removal.

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
