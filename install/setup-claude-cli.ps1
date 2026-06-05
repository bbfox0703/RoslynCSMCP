# RoslynCSMCP Setup Script for Claude CLI
# This script helps configure RoslynCSMCP for Claude Code CLI

param(
    [ValidateSet("user", "project", "local")]
    [string]$Scope = "user",
    [switch]$RemoveAll,
    [switch]$Help
)

# Module definitions
$modules = @{
    "1" = @{ Name = "Full"; Path = "RoslynMcpServer"; Tools = 51; Tokens = 8925; Description = "All tools in one server" }
    "2" = @{ Name = "Navigation"; Path = "src/RoslynMcpServer.Navigation"; Tools = 7; Tokens = 1225; Description = "Symbol search, references, file outline" }
    "3" = @{ Name = "Quality"; Path = "src/RoslynMcpServer.Quality"; Tools = 8; Tokens = 1400; Description = "Code smells, complexity, naming" }
    "4" = @{ Name = "Security"; Path = "src/RoslynMcpServer.Security"; Tools = 3; Tokens = 525; Description = "Security issues, thread safety" }
    "5" = @{ Name = "Dependencies"; Path = "src/RoslynMcpServer.Dependencies"; Tools = 5; Tokens = 875; Description = "Dependency analysis, packages" }
    "6" = @{ Name = "Refactoring"; Path = "src/RoslynMcpServer.Refactoring"; Tools = 5; Tokens = 875; Description = "Rename, extract interface" }
    "7" = @{ Name = "Testing"; Path = "src/RoslynMcpServer.Testing"; Tools = 2; Tokens = 350; Description = "Test discovery, coverage" }
    "8" = @{ Name = "Metrics"; Path = "src/RoslynMcpServer.Metrics"; Tools = 4; Tokens = 700; Description = "Code metrics, statistics" }
    "9" = @{ Name = "Advanced"; Path = "src/RoslynMcpServer.Advanced"; Tools = 15; Tokens = 2625; Description = "Batch queries, call hierarchy" }
    "10" = @{ Name = "Interop"; Path = "src/RoslynMcpServer.Interop"; Tools = 3; Tokens = 525; Description = "AOT/trimming, P/Invoke, unsafe code" }
}

function Show-Menu {
    Write-Host
    Write-Host "=== Select Tool Set ===" -ForegroundColor Cyan
    Write-Host
    Write-Host "  [1] Full          (51 tools, ~8,925 tokens) - All tools in one server" -ForegroundColor White
    Write-Host
    Write-Host "  --- Modular Options (for token optimization) ---" -ForegroundColor Gray
    Write-Host "  [2] Navigation    ( 7 tools, ~1,225 tokens) - Symbol search, references, file outline" -ForegroundColor White
    Write-Host "  [3] Quality       ( 8 tools, ~1,400 tokens) - Code smells, complexity, naming" -ForegroundColor White
    Write-Host "  [4] Security      ( 3 tools,   ~525 tokens) - Security issues, thread safety" -ForegroundColor White
    Write-Host "  [5] Dependencies  ( 5 tools,   ~875 tokens) - Dependency analysis, packages" -ForegroundColor White
    Write-Host "  [6] Refactoring   ( 5 tools,   ~875 tokens) - Rename, extract interface" -ForegroundColor White
    Write-Host "  [7] Testing       ( 2 tools,   ~350 tokens) - Test discovery, coverage" -ForegroundColor White
    Write-Host "  [8] Metrics       ( 4 tools,   ~700 tokens) - Code metrics, statistics" -ForegroundColor White
    Write-Host "  [9] Advanced      (15 tools, ~2,625 tokens) - Batch queries, call hierarchy" -ForegroundColor White
    Write-Host "  [10] Interop      ( 3 tools,   ~525 tokens) - AOT/trimming, P/Invoke, unsafe code" -ForegroundColor White
    Write-Host
    Write-Host "  [A] All Modular   (Select multiple modules)" -ForegroundColor Yellow
    Write-Host "  [R] Remove All    (Uninstall all Roslyn MCP servers)" -ForegroundColor Magenta
    Write-Host "  [Q] Quit" -ForegroundColor Red
    Write-Host
}

function Show-ConfigExample {
    param(
        [array]$SelectedModules,
        [string]$RootPath,
        [string]$Scope
    )

    Write-Host
    Write-Host "=== Configuration Commands ===" -ForegroundColor Cyan
    Write-Host
    Write-Host "To manually add these servers, run:" -ForegroundColor Yellow
    Write-Host

    foreach ($mod in $SelectedModules) {
        $module = $modules[$mod]
        $serverName = "roslyn-$($module.Name.ToLower())"
        $projectPath = Join-Path $RootPath $module.Path

        $scopeArg = if ($Scope -ne "local") { "--scope $Scope " } else { "" }

        Write-Host "claude mcp add --transport stdio $serverName $scopeArg\" -ForegroundColor Green
        Write-Host "    --env DOTNET_ENVIRONMENT=Production \" -ForegroundColor Green
        Write-Host "    -- dotnet run --project `"$projectPath`" -c Release" -ForegroundColor Green
        Write-Host
    }
}

function Generate-McpJson {
    param(
        [array]$SelectedModules,
        [string]$RootPath,
        [string]$OutputPath
    )

    $config = @{ mcpServers = @{} }

    foreach ($mod in $SelectedModules) {
        $module = $modules[$mod]
        $serverName = "roslyn-$($module.Name.ToLower())"
        $projectPath = Join-Path $RootPath $module.Path

        $config.mcpServers[$serverName] = @{
            command = "dotnet"
            args = @("run", "--project", $projectPath.Replace('\', '/'), "-c", "Release")
            env = @{
                DOTNET_ENVIRONMENT = "Production"
            }
        }
    }

    $config | ConvertTo-Json -Depth 10 | Set-Content $OutputPath -Encoding UTF8
    Write-Host "   Generated: $OutputPath" -ForegroundColor Green
}

function Show-McpJsonExample {
    param(
        [array]$SelectedModules,
        [string]$RootPath
    )

    Write-Host
    Write-Host "=== .mcp.json Example (Local Repo Scope) ===" -ForegroundColor Cyan
    Write-Host
    Write-Host "Copy this to your project root as .mcp.json:" -ForegroundColor Yellow
    Write-Host

    $config = @{ mcpServers = @{} }

    foreach ($mod in $SelectedModules) {
        $module = $modules[$mod]
        $serverName = "roslyn-$($module.Name.ToLower())"
        $projectPath = Join-Path $RootPath $module.Path

        $config.mcpServers[$serverName] = @{
            command = "dotnet"
            args = @("run", "--project", $projectPath.Replace('\', '/'), "-c", "Release")
            env = @{
                DOTNET_ENVIRONMENT = "Production"
            }
        }
    }

    $jsonOutput = $config | ConvertTo-Json -Depth 10
    Write-Host $jsonOutput -ForegroundColor Green
    Write-Host
}

function Remove-AllRoslynServers {
    Write-Host
    Write-Host "Removing all Roslyn MCP servers..." -ForegroundColor Yellow

    try {
        claude --version 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Claude CLI not responding" }
    } catch {
        Write-Host "   Claude CLI not found." -ForegroundColor Red
        exit 1
    }

    $removed = @()
    foreach ($mod in $modules.Values) {
        $serverName = "roslyn-$($mod.Name.ToLower())"
        claude mcp remove $serverName 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0) {
            $removed += $serverName
            Write-Host "   Removed $serverName" -ForegroundColor Green
        }
    }

    if ($removed.Count -eq 0) {
        Write-Host "   No Roslyn MCP servers were configured." -ForegroundColor Yellow
    } else {
        Write-Host
        Write-Host "Removed $($removed.Count) server(s)." -ForegroundColor Green
    }
}

if ($Help) {
    Write-Host @"
RoslynCSMCP Setup Script for Claude CLI

Usage: .\setup-claude-cli.ps1 [-Scope <user|project|local>] [-RemoveAll] [-Help]

Scopes:
  user    - Available in all projects (recommended for personal use)
  project - Team-shared via .mcp.json (recommended for teams)
  local   - Only in current directory (for testing)

Options:
  -RemoveAll - Remove all configured Roslyn MCP servers and exit

Examples:
  .\setup-claude-cli.ps1                    # Interactive mode, user scope (default)
  .\setup-claude-cli.ps1 -Scope project     # Project scope
  .\setup-claude-cli.ps1 -Scope local       # Local scope
  .\setup-claude-cli.ps1 -RemoveAll         # Uninstall all Roslyn servers
"@
    exit 0
}

Write-Host "=== RoslynCSMCP Setup for Claude CLI ===" -ForegroundColor Cyan
Write-Host

# Get script directory and project path
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootPath = Split-Path -Parent $scriptDir

Write-Host "Project location: $rootPath" -ForegroundColor Green
Write-Host "Configuration scope: $Scope" -ForegroundColor Green
Write-Host

# Handle removal (no build needed)
if ($RemoveAll) {
    Remove-AllRoslynServers
    exit 0
}

# Check if Claude CLI is installed
Write-Host "1. Checking Claude CLI installation..." -ForegroundColor Yellow
try {
    $claudeVersion = claude --version 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Claude CLI not responding"
    }
    Write-Host "   Claude CLI found" -ForegroundColor Green
} catch {
    Write-Host "   Claude CLI not found." -ForegroundColor Red
    Write-Host "   Please install Claude CLI first:" -ForegroundColor Yellow
    Write-Host "   https://claude.ai/download" -ForegroundColor Yellow
    exit 1
}

# Check .NET installation
Write-Host
Write-Host "2. Checking .NET installation..." -ForegroundColor Yellow
try {
    $dotnetVersion = dotnet --version
    Write-Host "   .NET SDK found: $dotnetVersion" -ForegroundColor Green
} catch {
    Write-Host "   .NET SDK not found. Please install .NET 10.0 SDK or later." -ForegroundColor Red
    exit 1
}

# Show menu and get selection
$selectedModules = @()

while ($true) {
    Show-Menu
    $selection = Read-Host "Enter your choice"

    switch ($selection.ToUpper()) {
        "Q" {
            Write-Host "Setup cancelled." -ForegroundColor Yellow
            exit 0
        }
        "R" {
            Remove-AllRoslynServers
            exit 0
        }
        "A" {
            Write-Host
            Write-Host "Select modules to install (comma-separated, e.g., 2,3,4):" -ForegroundColor Yellow
            $multiSelect = Read-Host "Modules"
            $selectedModules = $multiSelect -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $modules.ContainsKey($_) }
            if ($selectedModules.Count -eq 0) {
                Write-Host "No valid modules selected." -ForegroundColor Red
                continue
            }
            break
        }
        default {
            if ($modules.ContainsKey($selection)) {
                $selectedModules = @($selection)
                break
            } else {
                Write-Host "Invalid selection. Please try again." -ForegroundColor Red
                continue
            }
        }
    }
    break
}

Write-Host
Write-Host "Selected modules:" -ForegroundColor Green
foreach ($mod in $selectedModules) {
    $module = $modules[$mod]
    Write-Host "   - $($module.Name) ($($module.Tools) tools, ~$($module.Tokens) tokens)" -ForegroundColor White
}

# Build project
Write-Host
Write-Host "3. Building selected modules..." -ForegroundColor Yellow

foreach ($mod in $selectedModules) {
    $module = $modules[$mod]
    $projectPath = Join-Path $rootPath $module.Path

    Write-Host "   Building $($module.Name)..."
    Push-Location $rootPath
    try {
        dotnet build $projectPath -c Release --verbosity quiet 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Host "   Build failed for $($module.Name)" -ForegroundColor Red
            Pop-Location
            exit 1
        }
    } finally {
        Pop-Location
    }
}

Write-Host "   All modules built successfully" -ForegroundColor Green

# Configure MCP servers
Write-Host
Write-Host "4. Configuring MCP servers..." -ForegroundColor Yellow

$configuredServers = @()

foreach ($mod in $selectedModules) {
    $module = $modules[$mod]
    $serverName = "roslyn-$($module.Name.ToLower())"
    $projectPath = Join-Path $rootPath $module.Path

    Write-Host "   Adding $serverName..."

    # Prepare command based on scope
    $scopeArg = if ($Scope -ne "local") { @("--scope", $Scope) } else { @() }

    # Add MCP server
    $addArgs = @(
        "mcp", "add",
        "--transport", "stdio",
        $serverName
    ) + $scopeArg + @(
        "--env", "DOTNET_ENVIRONMENT=Production",
        "--",
        "dotnet", "run", "--project", $projectPath, "-c", "Release"
    )

    try {
        & claude $addArgs 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Host "   Failed to configure $serverName" -ForegroundColor Red
        } else {
            $configuredServers += $serverName
        }
    } catch {
        Write-Host "   Failed to configure $serverName : $($_.Exception.Message)" -ForegroundColor Red
    }
}

if ($configuredServers.Count -eq 0) {
    Write-Host "   No servers were configured successfully" -ForegroundColor Red
    exit 1
}

Write-Host "   MCP servers configured successfully" -ForegroundColor Green

# Verify configuration
Write-Host
Write-Host "5. Verifying configuration..." -ForegroundColor Yellow
foreach ($serverName in $configuredServers) {
    try {
        $serverInfo = claude mcp get $serverName 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Host "   $serverName - OK" -ForegroundColor Green
        } else {
            Write-Host "   $serverName - may not be active yet" -ForegroundColor Yellow
        }
    } catch {
        Write-Host "   $serverName - could not verify" -ForegroundColor Yellow
    }
}

# Show config example
Show-ConfigExample -SelectedModules $selectedModules -RootPath $rootPath -Scope $Scope

# Show .mcp.json example for local repo scope
Show-McpJsonExample -SelectedModules $selectedModules -RootPath $rootPath

# Ask if user wants to generate .mcp.json
Write-Host "Generate .mcp.json file? (for local repo scope)" -ForegroundColor Yellow
$generateMcpJson = Read-Host "[Y/N]"
if ($generateMcpJson -eq "Y" -or $generateMcpJson -eq "y") {
    $mcpJsonPath = Read-Host "Output path (default: ./.mcp.json)"
    if ([string]::IsNullOrWhiteSpace($mcpJsonPath)) {
        $mcpJsonPath = "./.mcp.json"
    }
    Generate-McpJson -SelectedModules $selectedModules -RootPath $rootPath -OutputPath $mcpJsonPath
}

# Summary
Write-Host
Write-Host "=== Setup Complete ===" -ForegroundColor Cyan
Write-Host
Write-Host "RoslynCSMCP is configured for Claude CLI!" -ForegroundColor Green
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
Write-Host "Configured servers:" -ForegroundColor Yellow
foreach ($serverName in $configuredServers) {
    Write-Host "   - $serverName" -ForegroundColor White
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
Write-Host "  claude mcp list              # List all MCP servers"
foreach ($serverName in $configuredServers) {
    Write-Host "  claude mcp get $serverName  # Show server details"
}
Write-Host
