# Test RoslynCSMCP with Claude CLI (Claude Code)
# This script verifies that RoslynCSMCP is configured for Claude CLI and provides testing guidance

Write-Host "=== Testing RoslynCSMCP with Claude CLI ===" -ForegroundColor Cyan
Write-Host

# 1. Check Claude CLI installation
Write-Host "1. Checking Claude CLI installation..." -ForegroundColor Yellow

$claudeVersion = & claude --version 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "   ✓ Claude CLI found: $claudeVersion" -ForegroundColor Green
} else {
    Write-Host "   ✗ Claude CLI not found" -ForegroundColor Red
    Write-Host "   Please install Claude Code first" -ForegroundColor Yellow
    exit 1
}

# 2. Check if RoslynCSMCP is configured
Write-Host
Write-Host "2. Checking RoslynCSMCP MCP configuration..." -ForegroundColor Yellow

$mcpList = & claude mcp list 2>&1 | Out-String
if ($mcpList -match "roslyn-mcp") {
    Write-Host "   ✓ RoslynCSMCP is configured in Claude CLI" -ForegroundColor Green
    Write-Host "   Details:" -ForegroundColor Gray
    & claude mcp get roslyn-mcp 2>&1 | ForEach-Object { Write-Host "     $_" -ForegroundColor Gray }
} else {
    Write-Host "   ✗ RoslynCSMCP is NOT configured in Claude CLI" -ForegroundColor Red
    Write-Host
    Write-Host "   To add RoslynCSMCP, run:" -ForegroundColor Yellow
    Write-Host "     claude mcp add roslyn-mcp -e DOTNET_ENVIRONMENT=Development -- dotnet run --project `"$PSScriptRoot\RoslynMcpServer`"" -ForegroundColor Gray
    Write-Host
    Write-Host "   Or if already built:" -ForegroundColor Yellow
    Write-Host "     claude mcp add roslyn-mcp -e DOTNET_ENVIRONMENT=Development -- dotnet run --project `"$PSScriptRoot\RoslynMcpServer`" --no-build" -ForegroundColor Gray
    exit 1
}

# 3. Test server can start
Write-Host
Write-Host "3. Testing server startup..." -ForegroundColor Yellow

$projectPath = Join-Path $PSScriptRoot "RoslynMcpServer"
$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = "dotnet"
$startInfo.Arguments = "run --project `"$projectPath`" --no-build"
$startInfo.UseShellExecute = $false
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.CreateNoWindow = $true
$startInfo.EnvironmentVariables["DOTNET_ENVIRONMENT"] = "Development"

$process = New-Object System.Diagnostics.Process
$process.StartInfo = $startInfo

try {
    $process.Start() | Out-Null
    Start-Sleep -Seconds 3

    if (!$process.HasExited) {
        Write-Host "   ✓ Server started successfully" -ForegroundColor Green
        $process.Kill()
        $process.WaitForExit()
    } else {
        Write-Host "   ✗ Server exited immediately" -ForegroundColor Red
        Write-Host "   Exit code: $($process.ExitCode)" -ForegroundColor Gray
        exit 1
    }
} catch {
    Write-Host "   ✗ Failed to start server: $_" -ForegroundColor Red
    exit 1
} finally {
    if (!$process.HasExited) {
        $process.Kill()
        $process.WaitForExit()
    }
    $process.Dispose()
}

# 4. Check log directory
Write-Host
Write-Host "4. Checking log directory..." -ForegroundColor Yellow

$logDir = Join-Path $env:TEMP "RoslynCSMCP\logs"
if (Test-Path $logDir) {
    Write-Host "   ✓ Log directory exists: $logDir" -ForegroundColor Green

    # Check for recent log files
    $recentLogs = Get-ChildItem $logDir -Filter "*.log" |
                  Where-Object { $_.LastWriteTime -gt (Get-Date).AddMinutes(-10) } |
                  Sort-Object LastWriteTime -Descending

    if ($recentLogs) {
        Write-Host "   ✓ Recent log files found (written in last 10 minutes):" -ForegroundColor Green
        $recentLogs | ForEach-Object {
            Write-Host "     - $($_.Name) ($(Get-Date $_.LastWriteTime -Format 'HH:mm:ss'))" -ForegroundColor Gray
        }
    } else {
        Write-Host "   ⚠ No recent log files (this is OK if server hasn't been used)" -ForegroundColor Yellow
    }
} else {
    Write-Host "   ⚠ Log directory doesn't exist yet (will be created on first use)" -ForegroundColor Yellow
}

# 4. Instructions for testing with Claude CLI
Write-Host
Write-Host "=== How to Test RoslynCSMCP with Claude CLI ===" -ForegroundColor Cyan
Write-Host

Write-Host "To verify RoslynCSMCP is being used by Claude CLI:" -ForegroundColor White
Write-Host

Write-Host "Step 1: Clear old logs" -ForegroundColor Yellow
Write-Host "  Remove-Item `"$logDir\*.log`"" -ForegroundColor Gray
Write-Host

Write-Host "Step 2: Run Claude CLI in your C# solution directory" -ForegroundColor Yellow
Write-Host "  cd <your-csharp-solution-directory>" -ForegroundColor Gray
Write-Host "  claude" -ForegroundColor Gray
Write-Host

Write-Host "Step 3: Ask Claude to analyze your C# code" -ForegroundColor Yellow
Write-Host "  Example prompts:" -ForegroundColor Gray
Write-Host "    • 'Search for classes named UserService'" -ForegroundColor Gray
Write-Host "    • 'Find all references to the GetUser method'" -ForegroundColor Gray
Write-Host "    • 'Analyze dependencies in this solution'" -ForegroundColor Gray
Write-Host "    • 'Find methods with high complexity'" -ForegroundColor Gray
Write-Host

Write-Host "Step 4: Check if RoslynCSMCP was used" -ForegroundColor Yellow
Write-Host "  A) Check for new log files:" -ForegroundColor Gray
Write-Host "     Get-ChildItem `"$logDir`" -Filter *.log | Sort-Object LastWriteTime" -ForegroundColor Gray
Write-Host
Write-Host "  B) Look for these indicators in logs:" -ForegroundColor Gray
Write-Host "     • 'Executing tool: SearchSymbols'" -ForegroundColor Gray
Write-Host "     • 'Executing tool: FindReferences'" -ForegroundColor Gray
Write-Host "     • 'Executing tool: AnalyzeDependencies'" -ForegroundColor Gray
Write-Host "     • 'Executing tool: AnalyzeCodeComplexity'" -ForegroundColor Gray
Write-Host

Write-Host "If RoslynCSMCP is being used, you will see:" -ForegroundColor White
Write-Host "  ✓ New debug-YYYYMMDD.log files in $logDir" -ForegroundColor Green
Write-Host "  ✓ Tool execution logs with parameters and results" -ForegroundColor Green
Write-Host "  ✓ Roslyn workspace loading messages" -ForegroundColor Green
Write-Host

Write-Host "If Claude uses built-in tools instead, you will see:" -ForegroundColor White
Write-Host "  • No new RoslynCSMCP log files" -ForegroundColor Gray
Write-Host "  • Claude may use 'Explore', 'Task', or 'Grep' tools instead" -ForegroundColor Gray
Write-Host

Write-Host "=== Monitoring Logs in Real-Time ===" -ForegroundColor Cyan
Write-Host

Write-Host "To watch logs as they're written:" -ForegroundColor Yellow
$today = Get-Date -Format "yyyyMMdd"
$debugLogPath = Join-Path $logDir "debug-$today.log"
Write-Host "  Get-Content `"$debugLogPath`" -Wait -Tail 50" -ForegroundColor Gray
Write-Host

Write-Host "=== Troubleshooting ===" -ForegroundColor Cyan
Write-Host

Write-Host "If RoslynCSMCP is not being used:" -ForegroundColor Yellow
Write-Host "  1. Verify MCP server is configured: claude mcp list" -ForegroundColor Gray
Write-Host "  2. Check MCP server health: claude mcp get roslyn-mcp" -ForegroundColor Gray
Write-Host "  3. Restart Claude CLI completely (exit and start new session)" -ForegroundColor Gray
Write-Host "  4. Ensure you're in a directory with a .sln or .csproj file" -ForegroundColor Gray
Write-Host "  5. Try explicitly mentioning 'using Roslyn analysis' in your prompt" -ForegroundColor Gray
Write-Host "  6. Check server logs for errors: Get-Content `"$debugLogPath`"" -ForegroundColor Gray
Write-Host

Write-Host "=== Test Complete ===" -ForegroundColor Cyan
Write-Host
Write-Host "RoslynCSMCP is configured. Follow the steps above to test it with Claude CLI." -ForegroundColor Green
Write-Host
Write-Host "to remove:" -ForegroundColor Green
Write-Host "  claude mcp remove roslyn-mcp" -ForegroundColor Gray
Write-Host
