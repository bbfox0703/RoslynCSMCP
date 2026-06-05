#!/bin/bash
# Roslyn MCP Server Setup Script for Claude Desktop
# This script helps set up the Roslyn MCP Server for Claude Desktop

set -e

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
GRAY='\033[0;90m'
WHITE='\033[1;37m'
NC='\033[0m' # No Color

# Default values
CLAUDE_CONFIG_PATH=""
SKIP_BUILD=false
REMOVE_ALL=false

# Module definitions (name|path|tools|tokens|description)
declare -A MODULES
MODULES["1"]="Full|RoslynMcpServer|51|8925|All tools in one server"
MODULES["2"]="Navigation|src/RoslynMcpServer.Navigation|7|1225|Symbol search, references, file outline"
MODULES["3"]="Quality|src/RoslynMcpServer.Quality|8|1400|Code smells, complexity, naming"
MODULES["4"]="Security|src/RoslynMcpServer.Security|3|525|Security issues, thread safety"
MODULES["5"]="Dependencies|src/RoslynMcpServer.Dependencies|5|875|Dependency analysis, packages"
MODULES["6"]="Refactoring|src/RoslynMcpServer.Refactoring|5|875|Rename, extract interface"
MODULES["7"]="Testing|src/RoslynMcpServer.Testing|2|350|Test discovery, coverage"
MODULES["8"]="Metrics|src/RoslynMcpServer.Metrics|4|700|Code metrics, statistics"
MODULES["9"]="Advanced|src/RoslynMcpServer.Advanced|15|2625|Batch queries, call hierarchy"
MODULES["10"]="Interop|src/RoslynMcpServer.Interop|3|525|AOT/trimming, P/Invoke, unsafe code"

get_module_field() {
    local key="$1"
    local field="$2"
    local data="${MODULES[$key]}"
    echo "$data" | cut -d'|' -f"$field"
}

show_menu() {
    echo
    echo -e "${CYAN}=== Select Tool Set ===${NC}"
    echo
    echo -e "${WHITE}  [1] Full          (51 tools, ~8,925 tokens) - All tools in one server${NC}"
    echo
    echo -e "${GRAY}  --- Modular Options (for token optimization) ---${NC}"
    echo -e "${WHITE}  [2] Navigation    ( 7 tools, ~1,225 tokens) - Symbol search, references, file outline${NC}"
    echo -e "${WHITE}  [3] Quality       ( 8 tools, ~1,400 tokens) - Code smells, complexity, naming${NC}"
    echo -e "${WHITE}  [4] Security      ( 3 tools,   ~525 tokens) - Security issues, thread safety${NC}"
    echo -e "${WHITE}  [5] Dependencies  ( 5 tools,   ~875 tokens) - Dependency analysis, packages${NC}"
    echo -e "${WHITE}  [6] Refactoring   ( 5 tools,   ~875 tokens) - Rename, extract interface${NC}"
    echo -e "${WHITE}  [7] Testing       ( 2 tools,   ~350 tokens) - Test discovery, coverage${NC}"
    echo -e "${WHITE}  [8] Metrics       ( 4 tools,   ~700 tokens) - Code metrics, statistics${NC}"
    echo -e "${WHITE}  [9] Advanced      (15 tools, ~2,625 tokens) - Batch queries, call hierarchy${NC}"
    echo -e "${WHITE}  [10] Interop      ( 3 tools,   ~525 tokens) - AOT/trimming, P/Invoke, unsafe code${NC}"
    echo
    echo -e "${YELLOW}  [A] All Modular   (Select multiple modules)${NC}"
    echo -e "${YELLOW}  [R] Remove All    (Uninstall all Roslyn MCP servers)${NC}"
    echo -e "${RED}  [Q] Quit${NC}"
    echo
}

remove_all_servers() {
    local config_path="$1"

    if [ -z "$config_path" ]; then
        if [[ "$OSTYPE" == "darwin"* ]]; then
            config_path="$HOME/Library/Application Support/Claude/claude_desktop_config.json"
        elif [[ "$OSTYPE" == "linux-gnu"* ]]; then
            config_path="$HOME/.config/Claude/claude_desktop_config.json"
        else
            echo -e "${RED}   Unsupported operating system${NC}"
            exit 1
        fi
    fi

    echo
    echo -e "${YELLOW}Removing all Roslyn MCP servers...${NC}"
    echo "   Config path: $config_path"

    if [ ! -f "$config_path" ]; then
        echo -e "${YELLOW}   Config file not found, nothing to remove.${NC}"
        return
    fi

    if ! command -v jq &> /dev/null; then
        echo -e "${RED}   jq is required to remove servers. Please install jq.${NC}"
        exit 1
    fi

    local removed=0
    for key in "${!MODULES[@]}"; do
        local name
        name=$(get_module_field "$key" 1)
        local server_name="roslyn-$(echo "$name" | tr '[:upper:]' '[:lower:]')"
        if [ "$(jq --arg n "$server_name" '(.mcpServers // {}) | has($n)' "$config_path")" = "true" ]; then
            jq --arg n "$server_name" 'del(.mcpServers[$n])' "$config_path" > "${config_path}.tmp" && mv "${config_path}.tmp" "$config_path"
            echo -e "${GREEN}   Removed $server_name${NC}"
            removed=$((removed + 1))
        fi
    done

    if [ "$removed" -eq 0 ]; then
        echo -e "${YELLOW}   No Roslyn MCP servers were configured.${NC}"
    else
        echo
        echo -e "${GREEN}Removed $removed server(s). Restart Claude Desktop to apply.${NC}"
    fi
}

show_config_example() {
    local root_path="$1"
    shift
    local selected_modules=("$@")

    echo
    echo -e "${CYAN}=== Configuration Example ===${NC}"
    echo
    echo -e "${YELLOW}Add to your claude_desktop_config.json:${NC}"
    echo

    echo -e "${GREEN}{"
    echo '  "mcpServers": {'

    local first=true
    for mod in "${selected_modules[@]}"; do
        local name=$(get_module_field "$mod" 1)
        local path=$(get_module_field "$mod" 2)
        local server_name="roslyn-$(echo "$name" | tr '[:upper:]' '[:lower:]')"
        local project_path="$root_path/$path"

        if [ "$first" = false ]; then
            echo ","
        fi
        first=false

        echo "    \"$server_name\": {"
        echo '      "command": "dotnet",'
        echo "      \"args\": [\"run\", \"--project\", \"$project_path\", \"-c\", \"Release\"],"
        echo '      "env": {'
        echo '        "DOTNET_ENVIRONMENT": "Production"'
        echo '      }'
        echo -n "    }"
    done

    echo
    echo '  }'
    echo -e "}${NC}"
    echo
}

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
        --remove-all)
            REMOVE_ALL=true
            shift
            ;;
        --help|-h)
            cat <<EOF
Roslyn MCP Server Setup Script for Claude Desktop

Usage: ./setup-claude-desktop.sh [options]

Options:
  --config-path <path>  Specify custom path to Claude Desktop config file
  --skip-build          Skip building the project
  --remove-all          Remove all configured Roslyn MCP servers and exit
  --help, -h            Show this help message

Examples:
  ./setup-claude-desktop.sh                                    # Interactive mode
  ./setup-claude-desktop.sh --skip-build                       # Skip building step
  ./setup-claude-desktop.sh --remove-all                       # Uninstall all Roslyn servers
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

# Get script directory and project path
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_PATH="$(cd "$SCRIPT_DIR/.." && pwd)"

echo -e "${GREEN}Project location: $ROOT_PATH${NC}"
echo

# Handle removal (no build / .NET needed)
if [ "$REMOVE_ALL" = true ]; then
    remove_all_servers "$CLAUDE_CONFIG_PATH"
    exit 0
fi

# Check .NET installation
echo -e "${YELLOW}1. Checking .NET installation...${NC}"
if ! command -v dotnet &> /dev/null; then
    echo -e "${RED}   .NET SDK not found. Please install .NET 10.0 SDK or later.${NC}"
    exit 1
fi
DOTNET_VERSION=$(dotnet --version)
echo -e "${GREEN}   .NET SDK found: $DOTNET_VERSION${NC}"

# Show menu and get selection
SELECTED_MODULES=()

while true; do
    show_menu
    read -p "Enter your choice: " selection

    case "${selection^^}" in
        Q)
            echo -e "${YELLOW}Setup cancelled.${NC}"
            exit 0
            ;;
        R)
            remove_all_servers "$CLAUDE_CONFIG_PATH"
            exit 0
            ;;
        A)
            echo
            echo -e "${YELLOW}Select modules to install (comma-separated, e.g., 2,3,4):${NC}"
            read -p "Modules: " multi_select
            IFS=',' read -ra selections <<< "$multi_select"
            for sel in "${selections[@]}"; do
                sel=$(echo "$sel" | tr -d ' ')
                if [[ -n "${MODULES[$sel]}" ]]; then
                    SELECTED_MODULES+=("$sel")
                fi
            done
            if [ ${#SELECTED_MODULES[@]} -eq 0 ]; then
                echo -e "${RED}No valid modules selected.${NC}"
                continue
            fi
            break
            ;;
        *)
            if [[ -n "${MODULES[$selection]}" ]]; then
                SELECTED_MODULES+=("$selection")
                break
            else
                echo -e "${RED}Invalid selection. Please try again.${NC}"
                continue
            fi
            ;;
    esac
done

echo
echo -e "${GREEN}Selected modules:${NC}"
for mod in "${SELECTED_MODULES[@]}"; do
    name=$(get_module_field "$mod" 1)
    tools=$(get_module_field "$mod" 3)
    tokens=$(get_module_field "$mod" 4)
    echo -e "${WHITE}   - $name ($tools tools, ~$tokens tokens)${NC}"
done

# Build project (unless skipped)
if [ "$SKIP_BUILD" = false ]; then
    echo
    echo -e "${YELLOW}2. Building selected modules...${NC}"

    for mod in "${SELECTED_MODULES[@]}"; do
        name=$(get_module_field "$mod" 1)
        path=$(get_module_field "$mod" 2)
        project_path="$ROOT_PATH/$path"

        echo "   Building $name..."
        if ! dotnet build "$project_path" -c Release --verbosity quiet > /dev/null 2>&1; then
            echo -e "${RED}   Build failed for $name${NC}"
            exit 1
        fi
    done

    echo -e "${GREEN}   All modules built successfully${NC}"
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
        echo -e "${RED}   Unsupported operating system${NC}"
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

# Build JSON configuration
build_config_json() {
    local first=true
    echo '{'
    echo '  "mcpServers": {'

    for mod in "${SELECTED_MODULES[@]}"; do
        local name=$(get_module_field "$mod" 1)
        local path=$(get_module_field "$mod" 2)
        local server_name="roslyn-$(echo "$name" | tr '[:upper:]' '[:lower:]')"
        local project_path="$ROOT_PATH/$path"

        if [ "$first" = false ]; then
            echo ","
        fi
        first=false

        cat <<ENTRY
    "$server_name": {
      "command": "dotnet",
      "args": ["run", "--project", "$project_path", "-c", "Release"],
      "env": {
        "DOTNET_ENVIRONMENT": "Production"
      }
    }
ENTRY
    done

    echo
    echo '  }'
    echo '}'
}

# If config file exists, merge configurations using jq if available
if [ -f "$CLAUDE_CONFIG_PATH" ]; then
    echo "   Found existing configuration"
    if command -v jq &> /dev/null; then
        EXISTING_CONFIG=$(cat "$CLAUDE_CONFIG_PATH")
        NEW_SERVERS=$(build_config_json | jq '.mcpServers')
        echo "$EXISTING_CONFIG" | jq ".mcpServers += $NEW_SERVERS" > "$CLAUDE_CONFIG_PATH"
    else
        echo -e "${YELLOW}   jq not found, overwriting config file${NC}"
        build_config_json > "$CLAUDE_CONFIG_PATH"
    fi
else
    build_config_json > "$CLAUDE_CONFIG_PATH"
fi

echo -e "${GREEN}   Configuration written successfully${NC}"

# Test server startup
echo
echo -e "${YELLOW}4. Testing server startup...${NC}"

FIRST_MOD="${SELECTED_MODULES[0]}"
FIRST_PATH=$(get_module_field "$FIRST_MOD" 2)
TEST_PROJECT_PATH="$ROOT_PATH/$FIRST_PATH"

# MCP stdio servers read from stdin and shut down cleanly on EOF.
# Feed empty stdin so the server initializes then exits; exit code 0 = healthy.
if echo '' | timeout 60s dotnet run --no-build -c Release --project "$TEST_PROJECT_PATH" > /dev/null 2>&1; then
    echo -e "${GREEN}   Server started successfully${NC}"
else
    echo -e "${RED}   Server failed to start${NC}"
    exit 1
fi

# Show config example
show_config_example "$ROOT_PATH" "${SELECTED_MODULES[@]}"

# Summary
echo -e "${CYAN}=== Setup Complete ===${NC}"
echo
echo -e "${GREEN}Roslyn MCP Server is configured and ready!${NC}"
echo
echo -e "${YELLOW}Configured servers:${NC}"
for mod in "${SELECTED_MODULES[@]}"; do
    name=$(get_module_field "$mod" 1)
    echo -e "${WHITE}   - roslyn-$(echo "$name" | tr '[:upper:]' '[:lower:]')${NC}"
done
echo
echo -e "${YELLOW}Next steps:${NC}"
echo "1. Restart Claude Desktop application"
echo "2. Look for the configured tools in Claude"
echo "3. Test with a C# solution file"
echo
echo -e "${CYAN}Example usage in Claude:${NC}"
echo "  'Search for all classes ending with Service in /path/to/MyProject.sln'"
echo "  'Find all references to UserRepository in /path/to/MyProject.sln'"
echo
echo -e "${YELLOW}For testing without Claude Desktop:${NC}"
echo "  npx @modelcontextprotocol/inspector dotnet run --project \"$TEST_PROJECT_PATH\""
echo
