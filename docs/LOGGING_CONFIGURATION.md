# RoslynCSMCP Logging Configuration

**Date**: 2026-01-11
**Status**: ✅ Configured and Optimized

---

## 📋 Overview

RoslynCSMCP uses **Serilog** for structured logging with dual output: stderr (for MCP protocol) and file logging (for diagnostics).

### Logging Behavior by Environment

| Environment | Console Level | File Level | File Location | Retention |
|-------------|--------------|------------|---------------|-----------|
| **Production** | Information+ | Warning+ | `%TEMP%/RoslynCSMCP/logs/roslyn-mcp-YYYYMMDD.log` | 30 days |
| **Development** | Verbose+ | Verbose+ | `%TEMP%/RoslynCSMCP/logs/debug-YYYYMMDD.log` | 7 days |

**Note**: `%TEMP%` on Windows, `/tmp` on Linux/macOS

---

## 🔧 Configuration Files

### appsettings.json (Production)

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "Console",
        "Args": {
          "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
          "standardErrorFromLevel": "Verbose"
        }
      }
    ]
  }
}
```

**Production behavior**:
- Console (stderr): All logs Information and above
- File: All logs Warning and above
- Microsoft/System libraries: Only Warning and above
- Minimal overhead for production

### appsettings.Development.json (Development)

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Verbose",
      "Override": {
        "Microsoft": "Information",
        "System": "Information"
      }
    },
    "WriteTo": [
      {
        "Name": "Console",
        "Args": {
          "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
          "standardErrorFromLevel": "Verbose"
        }
      }
    ]
  }
}
```

**Development behavior**:
- Console (stderr): All logs Verbose and above
- File: All logs Verbose and above, **with structured properties**
- Microsoft/System libraries: Information and above
- Detailed logging for debugging

---

## 🔄 How Environment is Determined

The environment is set via the `DOTNET_ENVIRONMENT` environment variable:

### Claude Desktop (claude_desktop_config.json)

```json
{
  "mcpServers": {
    "roslyn-mcp": {
      "command": "dotnet",
      "args": ["run", "--project", "D:\\Github\\RoslynCSMCP\\RoslynMcpServer"],
      "env": {
        "DOTNET_ENVIRONMENT": "Production"
      }
    }
  }
}
```

### Claude CLI

```bash
# Production (default)
claude mcp add roslyn-mcp -- dotnet run --project D:\Github\RoslynCSMCP\RoslynMcpServer

# Development (verbose logging)
claude mcp add roslyn-mcp --env DOTNET_ENVIRONMENT=Development -- dotnet run --project D:\Github\RoslynCSMCP\RoslynMcpServer
```

---

## 📊 Log Output Details

### Console Output (stderr)

**All environments output to stderr** (required by MCP protocol):
- Timestamp (HH:mm:ss)
- Log Level (VRB/DBG/INF/WRN/ERR/FTL)
- Message
- Exception (if any)

**Example**:
```
[14:23:45 INF] Starting RoslynCSMCP Server...
[14:23:45 INF] MSBuild registered successfully
[14:23:46 INF] Roslyn MCP Server started successfully
[14:23:46 INF] Environment: Production
```

### File Output

#### Production (roslyn-mcp-YYYYMMDD.log)

**Format**:
```
{Timestamp} [{Level}] [{SourceContext}] {Message}
{Exception}
```

**Example**:
```
2026-01-11 14:23:45.123 +08:00 [WRN] [RoslynMcpServer.Services.CodeAnalysisService] Failed to load project: MyProject.csproj
2026-01-11 14:23:46.456 +08:00 [ERR] [RoslynMcpServer.Tools.CodeNavigationTools] Error finding references for symbol: UserService
System.ArgumentNullException: Value cannot be null. (Parameter 'symbol')
   at RoslynMcpServer.Services.SymbolSearchService.FindReferencesAsync(...)
```

#### Development (debug-YYYYMMDD.log)

**Format**:
```
{Timestamp} [{Level}] [{SourceContext}] {Message}
{Properties (JSON)}
{Exception}
```

**Example**:
```
2026-01-11 14:23:45.123 +08:00 [VRB] [RoslynMcpServer.Services.SymbolSearchService] Searching for symbol: UserService
{"SolutionPath":"D:\\Projects\\MyApp\\MyApp.sln","Pattern":"UserService","SearchDepth":5}

2026-01-11 14:23:45.456 +08:00 [DBG] [RoslynMcpServer.Services.IncrementalAnalyzer] Cache hit for solution: MyApp.sln
{"CacheKey":"solution:MyApp.sln","CacheLevel":"L1","HitRate":0.85}
```

**Benefits of Development format**:
- Structured properties in JSON for easy parsing
- Full context for debugging
- Operation IDs for tracing

---

## 🎯 Log Levels Explained

| Level | Production | Development | Use Case |
|-------|-----------|-------------|----------|
| **Verbose** | ❌ Not logged | ✅ File only | Trace-level debugging, very detailed |
| **Debug** | ❌ Not logged | ✅ File + Console | Development diagnostics |
| **Information** | ✅ Console only | ✅ File + Console | Normal operations, startup/shutdown |
| **Warning** | ✅ File + Console | ✅ File + Console | Recoverable issues, deprecations |
| **Error** | ✅ File + Console | ✅ File + Console | Errors that don't crash the app |
| **Fatal** | ✅ File + Console | ✅ File + Console | Unrecoverable errors, app termination |

---

## 📂 Log File Locations

### Windows
- **Production**: `C:\Users\<User>\AppData\Local\Temp\RoslynCSMCP\logs\roslyn-mcp-YYYYMMDD.log`
- **Development**: `C:\Users\<User>\AppData\Local\Temp\RoslynCSMCP\logs\debug-YYYYMMDD.log`
- **Startup**: `C:\Users\<User>\AppData\Local\Temp\RoslynCSMCP\logs\startup-YYYYMMDD.log`

### Linux/macOS
- **Production**: `/tmp/RoslynCSMCP/logs/roslyn-mcp-YYYYMMDD.log`
- **Development**: `/tmp/RoslynCSMCP/logs/debug-YYYYMMDD.log`
- **Startup**: `/tmp/RoslynCSMCP/logs/startup-YYYYMMDD.log`

---

## 🔍 Viewing Logs in Real-time

### Windows (PowerShell)

```powershell
# Production logs
Get-Content "$env:TEMP\RoslynCSMCP\logs\roslyn-mcp-$(Get-Date -Format yyyyMMdd).log" -Wait -Tail 50

# Development logs
Get-Content "$env:TEMP\RoslynCSMCP\logs\debug-$(Get-Date -Format yyyyMMdd).log" -Wait -Tail 50

# Startup logs
Get-Content "$env:TEMP\RoslynCSMCP\logs\startup-$(Get-Date -Format yyyyMMdd).log" -Wait -Tail 50
```

### Linux/macOS

```bash
# Production logs
tail -f /tmp/RoslynCSMCP/logs/roslyn-mcp-$(date +%Y%m%d).log

# Development logs
tail -f /tmp/RoslynCSMCP/logs/debug-$(date +%Y%m%d).log

# Startup logs
tail -f /tmp/RoslynCSMCP/logs/startup-$(date +%Y%m%d).log
```

---

## 🚨 Troubleshooting

### No logs appearing in file

1. **Check log directory exists**:
   ```powershell
   # Windows
   Test-Path "$env:TEMP\RoslynCSMCP\logs"

   # Linux/macOS
   ls /tmp/RoslynCSMCP/logs
   ```

2. **Check environment variable**:
   ```bash
   echo $DOTNET_ENVIRONMENT
   ```

3. **Check file permissions** (Linux/macOS):
   ```bash
   ls -la /tmp/RoslynCSMCP/logs
   ```

### Logs too verbose

**Production**: Logs should only show Warning and above in files
- If seeing too many logs, ensure `DOTNET_ENVIRONMENT` is not set to "Development"

**Development**: Logs show everything (Verbose+)
- This is expected behavior for debugging
- Use `grep` or PowerShell filtering to find specific entries

### MCP protocol issues

**Important**: stderr output is required for MCP protocol
- Do not redirect stderr
- `standardErrorFromLevel: Verbose` ensures all logs go to stderr
- Claude Desktop/CLI reads logs from stderr

---

## ✅ Configuration Validation

### Test Production Logging

```powershell
# Set Production environment
$env:DOTNET_ENVIRONMENT="Production"

# Run server
dotnet run --project RoslynMcpServer

# Check log file (should only have Warning+)
Get-Content "$env:TEMP\RoslynCSMCP\logs\roslyn-mcp-$(Get-Date -Format yyyyMMdd).log" | Select-String "WRN|ERR|FTL"
```

### Test Development Logging

```powershell
# Set Development environment
$env:DOTNET_ENVIRONMENT="Development"

# Run server
dotnet run --project RoslynMcpServer

# Check log file (should have all levels including VRB/DBG)
Get-Content "$env:TEMP\RoslynCSMCP\logs\debug-$(Get-Date -Format yyyyMMdd).log" | Select-String "VRB|DBG|INF|WRN|ERR"
```

---

## 📝 Best Practices

### For Development

1. ✅ Use `DOTNET_ENVIRONMENT=Development`
2. ✅ Check `debug-YYYYMMDD.log` for detailed diagnostics
3. ✅ Use structured properties for context
4. ✅ Clean up old log files regularly (7-day retention)

### For Production

1. ✅ Use `DOTNET_ENVIRONMENT=Production` (or don't set it)
2. ✅ Monitor `roslyn-mcp-YYYYMMDD.log` for warnings/errors
3. ✅ Set up log rotation if disk space is limited
4. ✅ Review logs periodically (30-day retention)

### For Logging in Code

```csharp
// Use ILogger from DI
private readonly ILogger<MyService> _logger;

public MyService(ILogger<MyService> logger)
{
    _logger = logger;
}

// Log with structured data
_logger.LogInformation("Processing symbol: {SymbolName} in {FilePath}",
    symbolName, filePath);

// Log errors with exception
_logger.LogError(ex, "Failed to analyze solution: {SolutionPath}", solutionPath);

// Verbose logging for trace-level details
_logger.LogDebug("Cache hit: {CacheKey}, Level: {CacheLevel}", cacheKey, level);
```

---

## 🎉 Summary

✅ **Dual Logging**: Console (stderr) + File
✅ **Environment-Aware**: Production vs Development modes
✅ **Structured Logging**: JSON properties in Development
✅ **Automatic Rotation**: Daily log files with retention
✅ **Platform-Agnostic**: Works on Windows, Linux, macOS
✅ **MCP Compatible**: All logs to stderr for Claude integration

All logging is properly configured and tested!
