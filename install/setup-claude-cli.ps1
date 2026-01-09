# RoslynMCP Setup Script for Claude CLI
# This script helps configure RoslynMCP for Claude Code CLI

param(
    [ValidateSet("user", "project", "local")]
    [string]$Scope = "user",
    [switch]$Help
)

if ($Help) {
    Write-Host @"
RoslynMCP Setup Script for Claude CLI

Usage: .\setup-claude-cli.ps1 [-Scope <user|project|local>] [-Help]

Scopes:
  user    - Available in all projects (recommended for personal use)
  project - Team-shared via .mcp.json (recommended for teams)
  local   - Only in current directory (for testing)

Examples:
  .\setup-claude-cli.ps1                    # User scope (default)
  .\setup-claude-cli.ps1 -Scope project     # Project scope
  .\setup-claude-cli.ps1 -Scope local       # Local scope
"@
    exit 0
}

Write-Host "=== RoslynMCP Setup for Claude CLI ===" -ForegroundColor Cyan
Write-Host

# Get script directory and project path
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path (Split-Path -Parent $scriptDir) "RoslynMcpServer"

Write-Host "Project location: $projectPath" -ForegroundColor Green
Write-Host "Configuration scope: $Scope" -ForegroundColor Green
Write-Host

# Check if Claude CLI is installed
Write-Host "1. Checking Claude CLI installation..." -ForegroundColor Yellow
try {
    $claudeVersion = claude --version 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Claude CLI not responding"
    }
    Write-Host "   ✓ Claude CLI found" -ForegroundColor Green
} catch {
    Write-Host "   ✗ Claude CLI not found." -ForegroundColor Red
    Write-Host "   Please install Claude CLI first:" -ForegroundColor Yellow
    Write-Host "   https://claude.ai/download" -ForegroundColor Yellow
    exit 1
}

# Check .NET installation
Write-Host
Write-Host "2. Checking .NET installation..." -ForegroundColor Yellow
try {
    $dotnetVersion = dotnet --version
    Write-Host "   ✓ .NET SDK found: $dotnetVersion" -ForegroundColor Green
} catch {
    Write-Host "   ✗ .NET SDK not found. Please install .NET 10.0 SDK or later." -ForegroundColor Red
    exit 1
}

# Build project
Write-Host
Write-Host "3. Building RoslynMCP..." -ForegroundColor Yellow
Push-Location $projectPath
try {
    dotnet build -c Release | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed"
    }
    Write-Host "   ✓ Build successful" -ForegroundColor Green
} catch {
    Write-Host "   ✗ Build failed" -ForegroundColor Red
    Pop-Location
    exit 1
} finally {
    Pop-Location
}

# Configure MCP server
Write-Host
Write-Host "4. Configuring MCP server..." -ForegroundColor Yellow

# Prepare command based on scope
$scopeArg = if ($Scope -ne "local") { "--scope", $Scope } else { @() }

# Add MCP server
$addArgs = @(
    "mcp", "add",
    "--transport", "stdio",
    "roslyn"
) + $scopeArg + @(
    "--env", "DOTNET_ENVIRONMENT=Production",
    "--env", "LOG_LEVEL=Information",
    "--",
    "dotnet", "run", "--project", $projectPath
)

try {
    & claude $addArgs
    if ($LASTEXITCODE -ne 0) {
        throw "MCP server configuration failed"
    }
    Write-Host "   ✓ MCP server configured successfully" -ForegroundColor Green
} catch {
    Write-Host "   ✗ Failed to configure MCP server" -ForegroundColor Red
    Write-Host "   Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Verify configuration
Write-Host
Write-Host "5. Verifying configuration..." -ForegroundColor Yellow
try {
    $serverInfo = claude mcp get roslyn 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "   ✓ Configuration verified" -ForegroundColor Green
    } else {
        Write-Host "   ⚠ Configuration may not be active yet" -ForegroundColor Yellow
    }
} catch {
    Write-Host "   ⚠ Could not verify configuration" -ForegroundColor Yellow
}

# Summary
Write-Host
Write-Host "=== Setup Complete ===" -ForegroundColor Cyan
Write-Host
Write-Host "✓ RoslynMCP is configured for Claude CLI!" -ForegroundColor Green
Write-Host
Write-Host "Scope: $Scope" -ForegroundColor Yellow

switch ($Scope) {
    "user" {
        Write-Host "Configuration is available in all your projects" -ForegroundColor Yellow
    }
    "project" {
        Write-Host "Configuration is shared via .mcp.json (commit to Git)" -ForegroundColor Yellow
    }
    "local" {
        Write-Host "Configuration is only available in current directory" -ForegroundColor Yellow
    }
}

Write-Host
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Run 'claude' to start Claude CLI"
Write-Host "2. Use RoslynCSMCP tools to analyze C# code:"
Write-Host "   > Search for all classes in MySolution.sln"
Write-Host "   > Find references to UserService in MySolution.sln"
Write-Host "   > Analyze dependencies for MySolution.sln"
Write-Host
Write-Host "Verify configuration:" -ForegroundColor Yellow
Write-Host "  claude mcp list      # List all MCP servers"
Write-Host "  claude mcp get roslyn # Show RoslynCSMCP details"
Write-Host
