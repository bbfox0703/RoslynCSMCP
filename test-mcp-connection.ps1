# Test MCP Server Connection
# This script verifies that RoslynCSMCP is properly configured and responding

Write-Host "=== Testing RoslynCSMCP MCP Server ===" -ForegroundColor Cyan
Write-Host

# 1. Check if server is configured in Claude Code
Write-Host "1. Checking Claude Code configuration..." -ForegroundColor Yellow

$claudeConfigPath = "$env:APPDATA\Claude\claude_desktop_config.json"
if (Test-Path $claudeConfigPath) {
    $config = Get-Content $claudeConfigPath | ConvertFrom-Json

    if ($config.mcpServers.PSObject.Properties.Name -contains "roslyn-mcp") {
        Write-Host "   ✓ RoslynCSMCP is configured in Claude Code" -ForegroundColor Green
        Write-Host "   Config: $($config.mcpServers.'roslyn-mcp'.command) $($config.mcpServers.'roslyn-mcp'.args -join ' ')" -ForegroundColor Gray
    } else {
        Write-Host "   ✗ RoslynCSMCP is NOT configured" -ForegroundColor Red
        Write-Host "   Run: .\install\setup-claude-desktop.ps1" -ForegroundColor Yellow
        exit 1
    }
} else {
    Write-Host "   ✗ Claude Code config file not found" -ForegroundColor Red
    Write-Host "   Expected: $claudeConfigPath" -ForegroundColor Gray
    exit 1
}

# 2. Check if server can start
Write-Host
Write-Host "2. Testing server startup..." -ForegroundColor Yellow

$projectPath = Join-Path $PSScriptRoot "RoslynMcpServer"
$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = "dotnet"
$startInfo.Arguments = "run --project `"$projectPath`" --no-build"
$startInfo.UseShellExecute = $false
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.CreateNoWindow = $true

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

# 3. Check log directory
Write-Host
Write-Host "3. Checking log directory..." -ForegroundColor Yellow

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

# 4. Instructions for testing
Write-Host
Write-Host "=== How to Test RoslynCSMCP Tools ===" -ForegroundColor Cyan
Write-Host

Write-Host "In Claude Code, try these commands:" -ForegroundColor White
Write-Host
Write-Host "1. Search for symbols:" -ForegroundColor Yellow
Write-Host "   'Search for classes named UserService in this solution'" -ForegroundColor Gray
Write-Host
Write-Host "2. Find references:" -ForegroundColor Yellow
Write-Host "   'Find all references to the GetUser method'" -ForegroundColor Gray
Write-Host
Write-Host "3. Analyze dependencies:" -ForegroundColor Yellow
Write-Host "   'Show the dependency graph for this solution'" -ForegroundColor Gray
Write-Host
Write-Host "4. Check complexity:" -ForegroundColor Yellow
Write-Host "   'Find methods with high cyclomatic complexity'" -ForegroundColor Gray
Write-Host

Write-Host "If RoslynCSMCP is being used, you will see:" -ForegroundColor White
Write-Host "  ✓ New log files appear in: $logDir" -ForegroundColor Green
Write-Host "  ✓ Tool names like 'SearchSymbols', 'FindReferences' in Claude's output" -ForegroundColor Green
Write-Host "  ✓ Detailed C# code analysis results" -ForegroundColor Green
Write-Host

Write-Host "If Claude uses built-in tools instead, you will see:" -ForegroundColor White
Write-Host "  • Task names like 'Explore', 'Plan', 'general-purpose'" -ForegroundColor Gray
Write-Host "  • Generic file operations (Glob, Grep, Read)" -ForegroundColor Gray
Write-Host "  • No RoslynCSMCP logs" -ForegroundColor Gray
Write-Host

Write-Host "=== Test Complete ===" -ForegroundColor Cyan
