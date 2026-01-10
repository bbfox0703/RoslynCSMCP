@echo off
setlocal enabledelayedexpansion

echo === Roslyn MCP Server Installation Test ===
echo.

REM Change to RoslynMcpServer directory
cd /d "%~dp0RoslynMcpServer"

REM Check .NET installation
echo 1. Checking .NET installation...
dotnet --version >nul 2>&1
if %errorlevel% equ 0 (
    for /f %%i in ('dotnet --version') do set dotnet_version=%%i
    echo    ✓ .NET SDK found: !dotnet_version!

    REM Check if version is 10.0 or later
    for /f "tokens=1 delims=." %%v in ("!dotnet_version!") do set major_version=%%v
    if !major_version! LSS 10 (
        echo    ⚠ .NET 10.0 or later is required, found !dotnet_version!
        echo    Please upgrade to .NET 10.0 SDK
        exit /b 1
    )
) else (
    echo    ✗ .NET SDK not found. Please install .NET 10.0 SDK or later.
    exit /b 1
)

REM Check project files
echo.
echo 2. Checking project structure...
set "required_files=RoslynMcpServer.csproj Program.cs Services\CodeAnalysisService.cs Services\SymbolSearchService.cs Services\IncrementalAnalyzer.cs Services\CacheManager.cs Services\SecurityValidator.cs Services\DiagnosticLogger.cs Tools\CodeNavigationTools.cs Models\SearchModels.cs"

set all_exists=1
for %%f in (%required_files%) do (
    if exist "%%f" (
        echo    ✓ %%f exists
    ) else (
        echo    ✗ %%f missing
        set all_exists=0
    )
)

if !all_exists! equ 0 (
    echo    ✗ Some required files are missing
    exit /b 1
)

REM Restore packages
echo.
echo 3. Restoring NuGet packages...
dotnet restore >nul 2>&1
if %errorlevel% equ 0 (
    echo    ✓ NuGet packages restored successfully
) else (
    echo    ✗ Failed to restore NuGet packages
    exit /b 1
)

REM Build project
echo.
echo 4. Building project...
dotnet build -c Release >nul 2>&1
if %errorlevel% equ 0 (
    echo    ✓ Project built successfully
) else (
    echo    ✗ Build failed
    echo    Run 'dotnet build' in RoslynMcpServer directory to see detailed error messages
    exit /b 1
)

REM Test basic functionality
echo.
echo 5. Testing basic server startup...
start /b dotnet run --no-build >nul 2>&1
timeout /t 3 >nul
tasklist /fi "imagename eq dotnet.exe" | find "dotnet.exe" >nul
if %errorlevel% equ 0 (
    echo    ✓ Server started successfully
    taskkill /f /im dotnet.exe >nul 2>&1
) else (
    echo    ✗ Server failed to start
    exit /b 1
)

REM Check test project
echo.
echo 6. Checking test project...
cd /d "%~dp0RoslynMcpServer.Tests"
if exist "RoslynMcpServer.Tests.csproj" (
    echo    ✓ Test project found
    dotnet build -c Release >nul 2>&1
    if %errorlevel% equ 0 (
        echo    ✓ Test project built successfully
    ) else (
        echo    ⚠ Test project build failed (non-critical)
    )
) else (
    echo    ⚠ Test project not found (optional)
)

REM Check MCP Inspector availability
cd /d "%~dp0RoslynMcpServer"
echo.
echo 7. Checking MCP Inspector availability...
where npx >nul 2>&1
if %errorlevel% equ 0 (
    echo    ✓ npm/npx found - MCP Inspector can be used for testing
    echo    Run: npx @modelcontextprotocol/inspector dotnet run --project "%~dp0RoslynMcpServer"
) else (
    echo    ⚠ npm/npx not found - MCP Inspector testing not available
    echo    Install Node.js to use MCP Inspector for testing
)

echo.
echo === Installation Test Complete ===
echo.
echo ✓ All tests passed! The Roslyn MCP Server is ready to use.
echo.
echo Next steps:
echo 1. Run setup script: .\install\setup-claude-desktop.ps1
echo 2. Or manually add the server configuration to Claude Desktop
echo 3. Restart Claude Desktop
echo 4. Test with a C# solution file
echo.
echo Configuration path:
echo   Windows: %%APPDATA%%\Claude\claude_desktop_config.json
echo   macOS: ~/Library/Application Support/Claude/claude_desktop_config.json
echo.
echo For detailed setup instructions, see README.md
echo.
pause
