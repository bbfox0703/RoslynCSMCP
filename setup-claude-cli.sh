#!/bin/bash
# RoslynMCP Setup Script for Claude CLI
# This script helps configure RoslynMCP for Claude Code CLI

set -e

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Default scope
SCOPE="user"

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --scope)
            SCOPE="$2"
            shift 2
            ;;
        --help|-h)
            cat <<EOF
RoslynMCP Setup Script for Claude CLI

Usage: ./setup-claude-cli.sh [--scope <user|project|local>] [--help]

Scopes:
  user    - Available in all projects (recommended for personal use)
  project - Team-shared via .mcp.json (recommended for teams)
  local   - Only in current directory (for testing)

Examples:
  ./setup-claude-cli.sh                    # User scope (default)
  ./setup-claude-cli.sh --scope project    # Project scope
  ./setup-claude-cli.sh --scope local      # Local scope
EOF
            exit 0
            ;;
        *)
            echo -e "${RED}Unknown option: $1${NC}"
            exit 1
            ;;
    esac
done

# Validate scope
if [[ ! "$SCOPE" =~ ^(user|project|local)$ ]]; then
    echo -e "${RED}Invalid scope: $SCOPE${NC}"
    echo "Valid scopes: user, project, local"
    exit 1
fi

echo -e "${CYAN}=== RoslynMCP Setup for Claude CLI ===${NC}"
echo

# Get current directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_PATH="$SCRIPT_DIR/RoslynMcpServer"

echo -e "${GREEN}Project location: $PROJECT_PATH${NC}"
echo -e "${GREEN}Configuration scope: $SCOPE${NC}"
echo

# Check if Claude CLI is installed
echo -e "${YELLOW}1. Checking Claude CLI installation...${NC}"
if ! command -v claude &> /dev/null; then
    echo -e "${RED}   ✗ Claude CLI not found.${NC}"
    echo -e "${YELLOW}   Please install Claude CLI first:${NC}"
    echo -e "${YELLOW}   https://claude.ai/download${NC}"
    exit 1
fi
echo -e "${GREEN}   ✓ Claude CLI found${NC}"

# Check .NET installation
echo
echo -e "${YELLOW}2. Checking .NET installation...${NC}"
if ! command -v dotnet &> /dev/null; then
    echo -e "${RED}   ✗ .NET SDK not found.${NC}"
    echo -e "${YELLOW}   Please install .NET 10.0 SDK or later.${NC}"
    exit 1
fi
DOTNET_VERSION=$(dotnet --version)
echo -e "${GREEN}   ✓ .NET SDK found: $DOTNET_VERSION${NC}"

# Build project
echo
echo -e "${YELLOW}3. Building RoslynMCP...${NC}"
cd "$PROJECT_PATH"
if ! dotnet build -c Release > /dev/null 2>&1; then
    echo -e "${RED}   ✗ Build failed${NC}"
    exit 1
fi
echo -e "${GREEN}   ✓ Build successful${NC}"
cd "$SCRIPT_DIR"

# Configure MCP server
echo
echo -e "${YELLOW}4. Configuring MCP server...${NC}"

# Prepare scope argument
SCOPE_ARG=""
if [ "$SCOPE" != "local" ]; then
    SCOPE_ARG="--scope $SCOPE"
fi

# Add MCP server
if ! claude mcp add --transport stdio roslyn $SCOPE_ARG \
    --env DOTNET_ENVIRONMENT=Production \
    --env LOG_LEVEL=Information \
    -- dotnet run --project "$PROJECT_PATH"; then
    echo -e "${RED}   ✗ Failed to configure MCP server${NC}"
    exit 1
fi
echo -e "${GREEN}   ✓ MCP server configured successfully${NC}"

# Verify configuration
echo
echo -e "${YELLOW}5. Verifying configuration...${NC}"
if claude mcp get roslyn > /dev/null 2>&1; then
    echo -e "${GREEN}   ✓ Configuration verified${NC}"
else
    echo -e "${YELLOW}   ⚠ Configuration may not be active yet${NC}"
fi

# Summary
echo
echo -e "${CYAN}=== Setup Complete ===${NC}"
echo
echo -e "${GREEN}✓ RoslynMCP is configured for Claude CLI!${NC}"
echo
echo -e "${YELLOW}Scope: $SCOPE${NC}"

case $SCOPE in
    user)
        echo -e "${YELLOW}Configuration is available in all your projects${NC}"
        ;;
    project)
        echo -e "${YELLOW}Configuration is shared via .mcp.json (commit to Git)${NC}"
        ;;
    local)
        echo -e "${YELLOW}Configuration is only available in current directory${NC}"
        ;;
esac

echo
echo -e "${YELLOW}Next steps:${NC}"
echo "1. Run 'claude' to start Claude CLI"
echo "2. Use RoslynMCP tools to analyze C# code:"
echo "   > Search for all classes in MySolution.sln"
echo "   > Find references to UserService in MySolution.sln"
echo "   > Analyze dependencies for MySolution.sln"
echo
echo -e "${YELLOW}Verify configuration:${NC}"
echo "  claude mcp list       # List all MCP servers"
echo "  claude mcp get roslyn # Show RoslynMCP details"
echo
