#!/bin/bash
# RoslynCSMCP Setup Script for Claude CLI
# This script helps configure RoslynCSMCP for Claude Code CLI

set -e

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
GRAY='\033[0;90m'
WHITE='\033[1;37m'
NC='\033[0m' # No Color

# Default scope
SCOPE="user"
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

remove_all_servers() {
    echo
    echo -e "${YELLOW}Removing all Roslyn MCP servers...${NC}"

    if ! command -v claude &> /dev/null; then
        echo -e "${RED}   Claude CLI not found.${NC}"
        exit 1
    fi

    local removed=0
    for key in "${!MODULES[@]}"; do
        local name
        name=$(get_module_field "$key" 1)
        local server_name="roslyn-$(echo "$name" | tr '[:upper:]' '[:lower:]')"
        if claude mcp remove "$server_name" > /dev/null 2>&1; then
            echo -e "${GREEN}   Removed $server_name${NC}"
            removed=$((removed + 1))
        fi
    done

    if [ "$removed" -eq 0 ]; then
        echo -e "${YELLOW}   No Roslyn MCP servers were configured.${NC}"
    else
        echo
        echo -e "${GREEN}Removed $removed server(s).${NC}"
    fi
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

show_config_example() {
    local root_path="$1"
    local scope="$2"
    shift 2
    local selected_modules=("$@")

    echo
    echo -e "${CYAN}=== Configuration Commands ===${NC}"
    echo
    echo -e "${YELLOW}To manually add these servers, run:${NC}"
    echo

    for mod in "${selected_modules[@]}"; do
        local name=$(get_module_field "$mod" 1)
        local path=$(get_module_field "$mod" 2)
        local server_name="roslyn-$(echo "$name" | tr '[:upper:]' '[:lower:]')"
        local project_path="$root_path/$path"

        local scope_arg=""
        if [ "$scope" != "local" ]; then
            scope_arg="--scope $scope "
        fi

        echo -e "${GREEN}claude mcp add --transport stdio $server_name $scope_arg\\${NC}"
        echo -e "${GREEN}    --env DOTNET_ENVIRONMENT=Production \\${NC}"
        echo -e "${GREEN}    -- dotnet run --project \"$project_path\"${NC}"
        echo
    done
}

show_mcp_json_example() {
    local root_path="$1"
    shift
    local selected_modules=("$@")

    echo
    echo -e "${CYAN}=== .mcp.json Example (Local Repo Scope) ===${NC}"
    echo
    echo -e "${YELLOW}Copy this to your project root as .mcp.json:${NC}"
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
        echo "      \"args\": [\"run\", \"--project\", \"$project_path\"],"
        echo '      "env": { "DOTNET_ENVIRONMENT": "Production" }'
        echo -n "    }"
    done

    echo
    echo '  }'
    echo -e "}${NC}"
    echo
}

generate_mcp_json() {
    local root_path="$1"
    local output_path="$2"
    shift 2
    local selected_modules=("$@")

    {
        echo '{'
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

            cat <<ENTRY
    "$server_name": {
      "command": "dotnet",
      "args": ["run", "--project", "$project_path"],
      "env": { "DOTNET_ENVIRONMENT": "Production" }
    }
ENTRY
        done

        echo
        echo '  }'
        echo '}'
    } > "$output_path"

    echo -e "${GREEN}   Generated: $output_path${NC}"
}

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --scope)
            SCOPE="$2"
            shift 2
            ;;
        --remove-all)
            REMOVE_ALL=true
            shift
            ;;
        --help|-h)
            cat <<EOF
RoslynCSMCP Setup Script for Claude CLI

Usage: ./setup-claude-cli.sh [--scope <user|project|local>] [--remove-all] [--help]

Scopes:
  user    - Available in all projects (recommended for personal use)
  project - Team-shared via .mcp.json (recommended for teams)
  local   - Only in current directory (for testing)

Options:
  --remove-all - Remove all configured Roslyn MCP servers and exit

Examples:
  ./setup-claude-cli.sh                    # Interactive mode, user scope (default)
  ./setup-claude-cli.sh --scope project    # Project scope
  ./setup-claude-cli.sh --scope local      # Local scope
  ./setup-claude-cli.sh --remove-all       # Uninstall all Roslyn servers
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

echo -e "${CYAN}=== RoslynCSMCP Setup for Claude CLI ===${NC}"
echo

# Handle removal (no build needed)
if [ "$REMOVE_ALL" = true ]; then
    remove_all_servers
    exit 0
fi

# Get script directory and project path
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_PATH="$(cd "$SCRIPT_DIR/.." && pwd)"

echo -e "${GREEN}Project location: $ROOT_PATH${NC}"
echo -e "${GREEN}Configuration scope: $SCOPE${NC}"
echo

# Check if Claude CLI is installed
echo -e "${YELLOW}1. Checking Claude CLI installation...${NC}"
if ! command -v claude &> /dev/null; then
    echo -e "${RED}   Claude CLI not found.${NC}"
    echo -e "${YELLOW}   Please install Claude CLI first:${NC}"
    echo -e "${YELLOW}   https://claude.ai/download${NC}"
    exit 1
fi
echo -e "${GREEN}   Claude CLI found${NC}"

# Check .NET installation
echo
echo -e "${YELLOW}2. Checking .NET installation...${NC}"
if ! command -v dotnet &> /dev/null; then
    echo -e "${RED}   .NET SDK not found.${NC}"
    echo -e "${YELLOW}   Please install .NET 10.0 SDK or later.${NC}"
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
            remove_all_servers
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

# Build project
echo
echo -e "${YELLOW}3. Building selected modules...${NC}"

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

# Configure MCP servers
echo
echo -e "${YELLOW}4. Configuring MCP servers...${NC}"

CONFIGURED_SERVERS=()

for mod in "${SELECTED_MODULES[@]}"; do
    name=$(get_module_field "$mod" 1)
    path=$(get_module_field "$mod" 2)
    server_name="roslyn-$(echo "$name" | tr '[:upper:]' '[:lower:]')"
    project_path="$ROOT_PATH/$path"

    echo "   Adding $server_name..."

    # Prepare scope argument
    scope_arg=""
    if [ "$SCOPE" != "local" ]; then
        scope_arg="--scope $SCOPE"
    fi

    # Add MCP server
    if claude mcp add --transport stdio "$server_name" $scope_arg \
        --env DOTNET_ENVIRONMENT=Production \
        -- dotnet run --project "$project_path" > /dev/null 2>&1; then
        CONFIGURED_SERVERS+=("$server_name")
    else
        echo -e "${RED}   Failed to configure $server_name${NC}"
    fi
done

if [ ${#CONFIGURED_SERVERS[@]} -eq 0 ]; then
    echo -e "${RED}   No servers were configured successfully${NC}"
    exit 1
fi

echo -e "${GREEN}   MCP servers configured successfully${NC}"

# Verify configuration
echo
echo -e "${YELLOW}5. Verifying configuration...${NC}"
for server_name in "${CONFIGURED_SERVERS[@]}"; do
    if claude mcp get "$server_name" > /dev/null 2>&1; then
        echo -e "${GREEN}   $server_name - OK${NC}"
    else
        echo -e "${YELLOW}   $server_name - may not be active yet${NC}"
    fi
done

# Show config example
show_config_example "$ROOT_PATH" "$SCOPE" "${SELECTED_MODULES[@]}"

# Show .mcp.json example for local repo scope
show_mcp_json_example "$ROOT_PATH" "${SELECTED_MODULES[@]}"

# Ask if user wants to generate .mcp.json
echo -e "${YELLOW}Generate .mcp.json file? (for local repo scope) [Y/N]${NC}"
read -p "" generate_mcp_json
if [[ "$generate_mcp_json" =~ ^[Yy]$ ]]; then
    echo -e "${YELLOW}Output path (default: ./.mcp.json):${NC}"
    read -p "" mcp_json_path
    if [ -z "$mcp_json_path" ]; then
        mcp_json_path="./.mcp.json"
    fi
    generate_mcp_json "$ROOT_PATH" "$mcp_json_path" "${SELECTED_MODULES[@]}"
fi

# Summary
echo
echo -e "${CYAN}=== Setup Complete ===${NC}"
echo
echo -e "${GREEN}RoslynCSMCP is configured for Claude CLI!${NC}"
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
echo -e "${YELLOW}Configured servers:${NC}"
for server_name in "${CONFIGURED_SERVERS[@]}"; do
    echo -e "${WHITE}   - $server_name${NC}"
done

echo
echo -e "${YELLOW}Next steps:${NC}"
echo "1. Run 'claude' to start Claude CLI"
echo "2. Use RoslynCSMCP tools to analyze C# code:"
echo "   > Search for all classes in MySolution.sln"
echo "   > Find references to UserService in MySolution.sln"
echo "   > Analyze dependencies for MySolution.sln"
echo
echo -e "${YELLOW}Verify configuration:${NC}"
echo "  claude mcp list              # List all MCP servers"
for server_name in "${CONFIGURED_SERVERS[@]}"; do
    echo "  claude mcp get $server_name  # Show server details"
done
echo
