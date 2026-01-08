#!/bin/bash
# Roslyn MCP Server Setup Script for Claude Desktop
# This script helps set up the Roslyn MCP Server for Claude Desktop

set -e

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Default values
CLAUDE_CONFIG_PATH=""
SKIP_BUILD=false

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --config-path)
            CLAUDE_CONFIG_PATH="$2"
            shift 2
            ;;
        --skip-build)
            SKIP_BUILD=true
            shift
            ;;
        --help|-h)
            cat <<EOF
Roslyn MCP Server Setup Script for Claude Desktop

Usage: ./setup-claude-desktop.sh [options]

Options:
  --config-path <path>  Specify custom path to Claude Desktop config file
  --skip-build          Skip building the project
  --help, -h            Show this help message

Examples:
  ./setup-claude-desktop.sh                                    # Auto-detect config path and build
  ./setup-claude-desktop.sh --skip-build                       # Skip building step
  ./setup-claude-desktop.sh --config-path ~/custom/config.json # Custom config path
EOF
            exit 0
            ;;
        *)
            echo -e "${RED}Unknown option: $1${NC}"
            exit 1
            ;;
    esac
done

echo -e "${CYAN}=== Roslyn MCP Server Setup for Claude Desktop ===${NC}"
echo

# Get current directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_PATH="$SCRIPT_DIR/RoslynMcpServer"

echo -e "${GREEN}Project location: $PROJECT_PATH${NC}"
echo

# Check .NET installation
echo -e "${YELLOW}1. Checking .NET installation...${NC}"
if ! command -v dotnet &> /dev/null; then
    echo -e "${RED}   ✗ .NET SDK not found. Please install .NET 10.0 SDK or later.${NC}"
    exit 1
fi
DOTNET_VERSION=$(dotnet --version)
echo -e "${GREEN}   ✓ .NET SDK found: $DOTNET_VERSION${NC}"

# Build project (unless skipped)
if [ "$SKIP_BUILD" = false ]; then
    echo
    echo -e "${YELLOW}2. Building project...${NC}"

    echo "   Restoring packages..."
    cd "$PROJECT_PATH"
    if ! dotnet restore > /dev/null 2>&1; then
        echo -e "${RED}   ✗ Failed to restore packages${NC}"
        exit 1
    fi

    echo "   Building..."
    if ! dotnet build -c Release > /dev/null 2>&1; then
        echo -e "${RED}   ✗ Build failed${NC}"
        exit 1
    fi

    echo -e "${GREEN}   ✓ Project built successfully${NC}"
    cd "$SCRIPT_DIR"
fi

# Determine Claude Desktop config path
echo
echo -e "${YELLOW}3. Configuring Claude Desktop...${NC}"

if [ -z "$CLAUDE_CONFIG_PATH" ]; then
    # Detect OS and set default config path
    if [[ "$OSTYPE" == "darwin"* ]]; then
        # macOS
        CLAUDE_CONFIG_PATH="$HOME/Library/Application Support/Claude/claude_desktop_config.json"
    elif [[ "$OSTYPE" == "linux-gnu"* ]]; then
        # Linux
        CLAUDE_CONFIG_PATH="$HOME/.config/Claude/claude_desktop_config.json"
    else
        echo -e "${RED}   ✗ Unsupported operating system${NC}"
        exit 1
    fi
fi

echo "   Config path: $CLAUDE_CONFIG_PATH"

# Create config directory if it doesn't exist
CONFIG_DIR="$(dirname "$CLAUDE_CONFIG_PATH")"
if [ ! -d "$CONFIG_DIR" ]; then
    echo "   Creating config directory..."
    mkdir -p "$CONFIG_DIR"
fi

# Prepare configuration
read -r -d '' CONFIG_JSON <<EOF || true
{
  "mcpServers": {
    "roslyn-code-navigator": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "$PROJECT_PATH"
      ],
      "env": {
        "DOTNET_ENVIRONMENT": "Production",
        "LOG_LEVEL": "Information"
      }
    }
  }
}
EOF

# If config file exists, merge configurations
if [ -f "$CLAUDE_CONFIG_PATH" ]; then
    echo "   Found existing configuration"
    # Use jq if available for proper JSON merging
    if command -v jq &> /dev/null; then
        EXISTING_CONFIG=$(cat "$CLAUDE_CONFIG_PATH")
        echo "$EXISTING_CONFIG" | jq ".mcpServers[\"roslyn-code-navigator\"] = $(echo "$CONFIG_JSON" | jq '.mcpServers["roslyn-code-navigator"]')" > "$CLAUDE_CONFIG_PATH"
    else
        # If jq is not available, just overwrite (less safe)
        echo "   ⚠ jq not found, overwriting config file"
        echo "$CONFIG_JSON" > "$CLAUDE_CONFIG_PATH"
    fi
else
    echo "$CONFIG_JSON" > "$CLAUDE_CONFIG_PATH"
fi

echo -e "${GREEN}   ✓ Configuration written successfully${NC}"

# Test server startup
echo
echo -e "${YELLOW}4. Testing server startup...${NC}"

cd "$PROJECT_PATH"
timeout 3s dotnet run --no-build > /dev/null 2>&1 &
SERVER_PID=$!
sleep 2

if ps -p $SERVER_PID > /dev/null 2>&1; then
    echo -e "${GREEN}   ✓ Server started successfully${NC}"
    kill $SERVER_PID 2>/dev/null || true
else
    echo -e "${RED}   ✗ Server failed to start${NC}"
    exit 1
fi

cd "$SCRIPT_DIR"

# Summary
echo
echo -e "${CYAN}=== Setup Complete ===${NC}"
echo
echo -e "${GREEN}✓ Roslyn MCP Server is configured and ready!${NC}"
echo
echo -e "${YELLOW}Next steps:${NC}"
echo "1. Restart Claude Desktop application"
echo "2. Look for the 'roslyn-code-navigator' tool in Claude"
echo "3. Test with a C# solution file"
echo
echo -e "${CYAN}Example usage in Claude:${NC}"
echo "  'Search for all classes ending with Service in /path/to/MyProject.sln'"
echo "  'Find all references to UserRepository in /path/to/MyProject.sln'"
echo
echo -e "${YELLOW}For testing without Claude Desktop:${NC}"
echo "  npx @modelcontextprotocol/inspector dotnet run --project \"$PROJECT_PATH\""
echo
