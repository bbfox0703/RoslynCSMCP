#!/bin/bash

# Test script for Roslyn MCP Server
# This script validates the installation and basic functionality

set -e  # Exit on error

echo "=== Roslyn MCP Server Installation Test ==="
echo

# Get script directory
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR/RoslynMcpServer"

# Check .NET installation
echo "1. Checking .NET installation..."
if command -v dotnet &> /dev/null; then
    dotnet_version=$(dotnet --version)
    echo "   ✓ .NET SDK found: $dotnet_version"

    # Check if version is 10.0 or later
    major_version=$(echo $dotnet_version | cut -d'.' -f1)
    if [ "$major_version" -lt 10 ]; then
        echo "   ⚠ .NET 10.0 or later is required, found $dotnet_version"
        echo "   Please upgrade to .NET 10.0 SDK"
        exit 1
    fi
else
    echo "   ✗ .NET SDK not found. Please install .NET 10.0 SDK or later."
    exit 1
fi

# Check project files
echo
echo "2. Checking project structure..."
required_files=(
    "RoslynMcpServer.csproj"
    "Program.cs"
    "Services/CodeAnalysisService.cs"
    "Services/SymbolSearchService.cs"
    "Services/IncrementalAnalyzer.cs"
    "Services/CacheManager.cs"
    "Services/SecurityValidator.cs"
    "Services/DiagnosticLogger.cs"
    "Tools/CodeNavigationTools.cs"
    "Models/SearchModels.cs"
)

all_exists=true
for file in "${required_files[@]}"; do
    if [ -f "$file" ]; then
        echo "   ✓ $file exists"
    else
        echo "   ✗ $file missing"
        all_exists=false
    fi
done

if [ "$all_exists" = false ]; then
    echo "   ✗ Some required files are missing"
    exit 1
fi

# Restore packages
echo
echo "3. Restoring NuGet packages..."
if dotnet restore > /dev/null 2>&1; then
    echo "   ✓ NuGet packages restored successfully"
else
    echo "   ✗ Failed to restore NuGet packages"
    exit 1
fi

# Build project
echo
echo "4. Building project..."
if dotnet build -c Release > /dev/null 2>&1; then
    echo "   ✓ Project built successfully"
else
    echo "   ✗ Build failed"
    echo "   Run 'dotnet build' in RoslynMcpServer directory to see detailed error messages"
    exit 1
fi

# Test basic functionality
echo
echo "5. Testing basic server startup..."
timeout 10 dotnet run --no-build > /dev/null 2>&1 &
SERVER_PID=$!
sleep 3

if kill -0 $SERVER_PID 2>/dev/null; then
    echo "   ✓ Server started successfully"
    kill $SERVER_PID 2>/dev/null || true
    wait $SERVER_PID 2>/dev/null || true
else
    echo "   ✗ Server failed to start"
    exit 1
fi

# Check test project
echo
echo "6. Checking test project..."
cd "$SCRIPT_DIR/RoslynMcpServer.Tests"
if [ -f "RoslynMcpServer.Tests.csproj" ]; then
    echo "   ✓ Test project found"
    if dotnet build -c Release > /dev/null 2>&1; then
        echo "   ✓ Test project built successfully"
    else
        echo "   ⚠ Test project build failed (non-critical)"
    fi
else
    echo "   ⚠ Test project not found (optional)"
fi

# Check MCP Inspector availability
cd "$SCRIPT_DIR/RoslynMcpServer"
echo
echo "7. Checking MCP Inspector availability..."
if command -v npx &> /dev/null; then
    echo "   ✓ npm/npx found - MCP Inspector can be used for testing"
    echo "   Run: npx @modelcontextprotocol/inspector dotnet run --project $SCRIPT_DIR/RoslynMcpServer"
else
    echo "   ⚠ npm/npx not found - MCP Inspector testing not available"
    echo "   Install Node.js to use MCP Inspector for testing"
fi

echo
echo "=== Installation Test Complete ==="
echo
echo "✓ All tests passed! The Roslyn MCP Server is ready to use."
echo
echo "Next steps:"
echo "1. Run setup script: ./install/setup-claude-desktop.sh"
echo "2. Or manually add the server configuration to Claude Desktop"
echo "3. Restart Claude Desktop"
echo "4. Test with a C# solution file"
echo
echo "Configuration path:"
echo "  Windows: %APPDATA%\\Claude\\claude_desktop_config.json"
echo "  macOS: ~/Library/Application Support/Claude/claude_desktop_config.json"
echo "  Linux: ~/.config/Claude/claude_desktop_config.json"
echo
echo "For detailed setup instructions, see README.md"
