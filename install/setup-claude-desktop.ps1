# Roslyn MCP Server Setup Script
# This script helps set up the Roslyn MCP Server for Claude Desktop

param(
    [string]$ClaudeConfigPath = "",
    [switch]$SkipBuild,
    [switch]$Help
)

# Module definitions
$modules = @{
    "1" = @{ Name = "Full"; Path = "RoslynMcpServer"; Tools = 42; Tokens = 7350; Description = "All tools in one server" }
    "2" = @{ Name = "Navigation"; Path = "src/RoslynMcpServer.Navigation"; Tools = 6; Tokens = 1050; Description = "Symbol search, references, file outline" }
    "3" = @{ Name = "Quality"; Path = "src/RoslynMcpServer.Quality"; Tools = 6; Tokens = 1050; Description = "Code smells, complexity, naming" }
    "4" = @{ Name = "Security"; Path = "src/RoslynMcpServer.Security"; Tools = 3; Tokens = 525; Description = "Security issues, thread safety" }
    "5" = @{ Name = "Dependencies"; Path = "src/RoslynMcpServer.Dependencies"; Tools = 5; Tokens = 875; Description = "Dependency analysis, packages" }
    "6" = @{ Name = "Refactoring"; Path = "src/RoslynMcpServer.Refactoring"; Tools = 4; Tokens = 700; Description = "Rename, extract interface" }
    "7" = @{ Name = "Testing"; Path = "src/RoslynMcpServer.Testing"; Tools = 2; Tokens = 350; Description = "Test discovery, coverage" }
    "8" = @{ Name = "Metrics"; Path = "src/RoslynMcpServer.Metrics"; Tools = 3; Tokens = 525; Description = "Code metrics, statistics" }
    "9" = @{ Name = "Advanced"; Path = "src/RoslynMcpServer.Advanced"; Tools = 13; Tokens = 2275; Description = "Batch queries, call hierarchy" }
}

function Show-Menu {
    Write-Host
    Write-Host "=== Select Tool Set ===" -ForegroundColor Cyan
    Write-Host
    Write-Host "  [1] Full          (42 tools, ~7,350 tokens) - All tools in one server" -ForegroundColor White
    Write-Host
    Write-Host "  --- Modular Options (for token optimization) ---" -ForegroundColor Gray
    Write-Host "  [2] Navigation    ( 6 tools, ~1,050 tokens) - Symbol search, references, file outline" -ForegroundColor White
    Write-Host "  [3] Quality       ( 6 tools, ~1,050 tokens) - Code smells, complexity, naming" -ForegroundColor White
    Write-Host "  [4] Security      ( 3 tools,   ~525 tokens) - Security issues, thread safety" -ForegroundColor White
    Write-Host "  [5] Dependencies  ( 5 tools,   ~875 tokens) - Dependency analysis, packages" -ForegroundColor White
    Write-Host "  [6] Refactoring   ( 4 tools,   ~700 tokens) - Rename, extract interface" -ForegroundColor White
    Write-Host "  [7] Testing       ( 2 tools,   ~350 tokens) - Test discovery, coverage" -ForegroundColor White
    Write-Host "  [8] Metrics       ( 3 tools,   ~525 tokens) - Code metrics, statistics" -ForegroundColor White
    Write-Host "  [9] Advanced      (13 tools, ~2,275 tokens) - Batch queries, call hierarchy" -ForegroundColor White
    Write-Host
    Write-Host "  [A] All Modular   (Select multiple modules)" -ForegroundColor Yellow
    Write-Host "  [Q] Quit" -ForegroundColor Red
    Write-Host
}

function Show-ConfigExample {
    param(
        [array]$SelectedModules,
        [string]$RootPath
    )

    Write-Host
    Write-Host "=== Configuration Example ===" -ForegroundColor Cyan
    Write-Host
    Write-Host "Add to your claude_desktop_config.json:" -ForegroundColor Yellow
    Write-Host

    $configJson = @{
        mcpServers = @{}
    }

    foreach ($mod in $SelectedModules) {
        $module = $modules[$mod]
        $serverName = "roslyn-$($module.Name.ToLower())"
        $projectPath = Join-Path $RootPath $module.Path

        $configJson.mcpServers[$serverName] = @{
            command = "dotnet"
            args = @("run", "--project", $projectPath.Replace('\', '/'))
            env = @{
                DOTNET_ENVIRONMENT = "Production"
            }
        }
    }

    $jsonOutput = $configJson | ConvertTo-Json -Depth 10
    Write-Host $jsonOutput -ForegroundColor Green
    Write-Host
}

if ($Help) {
    Write-Host @"
Roslyn MCP Server Setup Script

Usage: .\setup-claude-desktop.ps1 [options]

Options:
  -ClaudeConfigPath <path>   Specify custom path to Claude Desktop config file
  -SkipBuild                 Skip building the project
  -Help                      Show this help message

Examples:
  .\setup-claude-desktop.ps1                                    # Interactive mode
  .\setup-claude-desktop.ps1 -SkipBuild                        # Skip building step
  .\setup-claude-desktop.ps1 -ClaudeConfigPath "C:\custom\config.json"  # Custom config path
"@
    exit 0
}

Write-Host "=== Roslyn MCP Server Setup ===" -ForegroundColor Cyan
Write-Host

# Get script directory and project path
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootPath = Split-Path -Parent $scriptDir

Write-Host "Project location: $rootPath" -ForegroundColor Green
Write-Host

# Check .NET installation
Write-Host "1. Checking .NET installation..." -ForegroundColor Yellow
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

# Build project (unless skipped)
if (-not $SkipBuild) {
    Write-Host
    Write-Host "2. Building selected modules..." -ForegroundColor Yellow

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
}

# Determine Claude Desktop config path
Write-Host
Write-Host "3. Configuring Claude Desktop..." -ForegroundColor Yellow

if ($ClaudeConfigPath -eq "") {
    $ClaudeConfigPath = "$env:APPDATA\Claude\claude_desktop_config.json"
}

Write-Host "   Config path: $ClaudeConfigPath"

# Create config directory if it doesn't exist
$configDir = Split-Path $ClaudeConfigPath -Parent
if (-not (Test-Path $configDir)) {
    Write-Host "   Creating config directory..."
    New-Item -ItemType Directory -Path $configDir -Force | Out-Null
}

# Read existing config if it exists
$existingConfig = @{ mcpServers = @{} }
if (Test-Path $ClaudeConfigPath) {
    try {
        $existingConfig = Get-Content $ClaudeConfigPath | ConvertFrom-Json -AsHashtable
        if (-not $existingConfig.ContainsKey('mcpServers')) {
            $existingConfig['mcpServers'] = @{}
        }
        Write-Host "   Found existing configuration"
    } catch {
        Write-Host "   Could not parse existing config, creating new one" -ForegroundColor Yellow
        $existingConfig = @{ mcpServers = @{} }
    }
}

# Add selected modules to config
foreach ($mod in $selectedModules) {
    $module = $modules[$mod]
    $serverName = "roslyn-$($module.Name.ToLower())"
    $projectPath = Join-Path $rootPath $module.Path

    $existingConfig.mcpServers[$serverName] = @{
        command = "dotnet"
        args = @("run", "--project", $projectPath)
        env = @{
            DOTNET_ENVIRONMENT = "Production"
        }
    }
}

# Write configuration
try {
    $existingConfig | ConvertTo-Json -Depth 10 | Set-Content $ClaudeConfigPath -Encoding UTF8
    Write-Host "   Configuration written successfully" -ForegroundColor Green
} catch {
    Write-Host "   Failed to write configuration: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Test server startup
Write-Host
Write-Host "4. Testing server startup..." -ForegroundColor Yellow

$firstModule = $modules[$selectedModules[0]]
$testProjectPath = Join-Path $rootPath $firstModule.Path

$job = Start-Job -ScriptBlock {
    param($path)
    Set-Location $path
    dotnet run --no-build
} -ArgumentList $testProjectPath

Start-Sleep -Seconds 3

if ($job.State -eq "Running") {
    Write-Host "   Server started successfully" -ForegroundColor Green
    Stop-Job $job
    Remove-Job $job
} else {
    Write-Host "   Server failed to start" -ForegroundColor Red
    Receive-Job $job
    Remove-Job $job
    exit 1
}

# Show config example
Show-ConfigExample -SelectedModules $selectedModules -RootPath $rootPath

# Summary
Write-Host "=== Setup Complete ===" -ForegroundColor Cyan
Write-Host
Write-Host "Roslyn MCP Server is configured and ready!" -ForegroundColor Green
Write-Host
Write-Host "Configured servers:" -ForegroundColor Yellow
foreach ($mod in $selectedModules) {
    $module = $modules[$mod]
    Write-Host "   - roslyn-$($module.Name.ToLower())" -ForegroundColor White
}
Write-Host
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Restart Claude Desktop application"
Write-Host "2. Look for the configured tools in Claude"
Write-Host "3. Test with a C# solution file"
Write-Host
Write-Host "Example usage in Claude:" -ForegroundColor Cyan
Write-Host "  'Search for all classes ending with Service in C:\MyProject\MyProject.sln'"
Write-Host "  'Find all references to UserRepository in C:\MyProject\MyProject.sln'"
Write-Host
Write-Host "For testing without Claude Desktop:" -ForegroundColor Yellow
Write-Host "  npx @modelcontextprotocol/inspector dotnet run --project `"$testProjectPath`""
