# Docker Deployment Guide for RoslynCSMCP

This guide provides comprehensive instructions for deploying RoslynCSMCP using Docker with both Claude Desktop and Claude CLI.

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Quick Start](#quick-start)
3. [Configuration Options](#configuration-options)
4. [Claude Desktop Setup](#claude-desktop-setup)
5. [Claude CLI Setup](#claude-cli-setup)
6. [Production vs Development](#production-vs-development)
7. [Troubleshooting](#troubleshooting)
8. [Advanced Configuration](#advanced-configuration)

---

## Prerequisites

### Required Software

1. **Docker Desktop** (Windows/macOS) or **Docker Engine** (Linux)
   - Windows: [Download Docker Desktop](https://www.docker.com/products/docker-desktop)
   - macOS: [Download Docker Desktop](https://www.docker.com/products/docker-desktop)
   - Linux: [Install Docker Engine](https://docs.docker.com/engine/install/)

2. **Docker Compose** (usually included with Docker Desktop)
   - Verify: `docker-compose --version`

3. **Claude Desktop** or **Claude CLI**
   - Desktop: [Download from Anthropic](https://www.anthropic.com/claude)
   - CLI: Install via your package manager

### System Requirements

- **CPU**: 2+ cores recommended (4+ for large codebases)
- **RAM**: 2GB minimum, 4GB+ recommended
- **Disk**: 500MB for Docker image + space for logs

---

## Quick Start

### Option 1: Claude Desktop

```bash
# 1. Clone or navigate to RoslynCSMCP directory
cd /path/to/RoslynCSMCP

# 2. Create .env file with your paths
cat > .env << EOF
WORKSPACE_PATH=/path/to/your/solution
LOGS_PATH=/path/to/logs
EOF

# 3. Build and start the container
docker-compose -f docker-compose.claude-desktop.yml up -d

# 4. Configure Claude Desktop (see Claude Desktop Setup section below)

# 5. Verify it's running
docker ps | grep roslynmcp-desktop
```

### Option 2: Claude CLI

```bash
# 1. Clone or navigate to RoslynCSMCP directory
cd /path/to/RoslynCSMCP

# 2. Create .env file with your paths
cat > .env << EOF
WORKSPACE_PATH=/path/to/your/solution
LOGS_PATH=/path/to/logs
EOF

# 3. Build and start the container
docker-compose -f docker-compose.claude-cli.yml up -d

# 4. Configure Claude CLI (see Claude CLI Setup section below)

# 5. Test the connection
claude "Search for classes in the solution"
```

---

## Configuration Options

### Environment Variables

Create a `.env` file in the same directory as the docker-compose file:

```bash
# Windows Example
WORKSPACE_PATH=C:/Projects/MySolution
LOGS_PATH=C:/Logs/RoslynMCP

# macOS Example
WORKSPACE_PATH=/Users/username/Projects/MySolution
LOGS_PATH=/Users/username/Logs/RoslynMCP

# Linux Example
WORKSPACE_PATH=/home/username/projects/MySolution
LOGS_PATH=/var/log/roslynmcp
```

### Volume Mounts

The server requires two volume mounts:

1. **Workspace Volume** (`/workspace`): Mount your solution directory
   - **Recommended**: Read-only (`:ro`) for safety
   - Example: `C:/Projects/MySolution:/workspace:ro`

2. **Logs Volume** (`/logs`): Mount directory for log files
   - **Required**: Read-write (`:rw`)
   - Example: `C:/Logs/RoslynMCP:/logs:rw`

### Build Arguments

Control the environment at build time:

```yaml
build:
  args:
    ENVIRONMENT: Production  # or Development
```

- **Production**: Minimal logging, optimized for performance
- **Development**: Verbose logging, debug output

---

## Claude Desktop Setup

### Step 1: Start the Container

```bash
docker-compose -f docker-compose.claude-desktop.yml up -d
```

### Step 2: Configure Claude Desktop

1. Locate your Claude Desktop configuration file:
   - **Windows**: `%APPDATA%\Claude\claude_desktop_config.json`
   - **macOS**: `~/Library/Application Support/Claude/claude_desktop_config.json`
   - **Linux**: `~/.config/Claude/claude_desktop_config.json`

2. Open the file in a text editor

3. Add the RoslynMCP server configuration to the `mcpServers` section:

```json
{
  "mcpServers": {
    "roslyn": {
      "command": "docker",
      "args": [
        "exec",
        "-i",
        "roslynmcp-desktop",
        "dotnet",
        "RoslynMcpServer.dll"
      ],
      "env": {
        "DOTNET_ENVIRONMENT": "Production"
      }
    }
  }
}
```

### Step 3: Restart Claude Desktop

Close and reopen Claude Desktop for the changes to take effect.

### Step 4: Verify

Ask Claude to use the tools:
```
"Search for classes named 'Program' in the solution"
```

You should see RoslynMCP tools being used.

---

## Claude CLI Setup

### Step 1: Start the Container

```bash
docker-compose -f docker-compose.claude-cli.yml up -d
```

### Step 2: Configure Claude CLI

Choose one of three configuration methods:

#### Method 1: Global Configuration (Recommended)

Edit `~/.config/claude/settings.toml`:

```toml
[mcp]
servers = [
  {
    name = "roslyn",
    command = "docker",
    args = ["exec", "-i", "roslynmcp-cli", "dotnet", "RoslynMcpServer.dll"],
    env = { DOTNET_ENVIRONMENT = "Production" }
  }
]
```

#### Method 2: Per-Project Configuration

Create `.claude.toml` in your project root:

```toml
[mcp]
servers = [
  {
    name = "roslyn",
    command = "docker",
    args = ["exec", "-i", "roslynmcp-cli", "dotnet", "RoslynMcpServer.dll"],
    env = { DOTNET_ENVIRONMENT = "Production" }
  }
]
```

#### Method 3: Command-line Flag (One-off)

```bash
claude --mcp-server docker exec -i roslynmcp-cli dotnet RoslynMcpServer.dll
```

### Step 3: Test the Connection

```bash
claude "Search for classes in the solution"
```

---

## Production vs Development

### Production Mode (Default)

**Characteristics:**
- Minimal logging (Information level)
- Optimized performance
- Suitable for regular use

**Configuration:**
```yaml
build:
  args:
    ENVIRONMENT: Production
environment:
  - DOTNET_ENVIRONMENT=Production
  - LOG_LEVEL=Information
```

### Development Mode

**Characteristics:**
- Verbose logging (Debug level)
- Detailed diagnostics
- Useful for troubleshooting

**Configuration:**
```yaml
build:
  args:
    ENVIRONMENT: Development
environment:
  - DOTNET_ENVIRONMENT=Development
  - LOG_LEVEL=Debug
```

### Switching Modes

#### Build-time Switch (Recommended)

1. Edit the docker-compose file
2. Change `ENVIRONMENT: Development`
3. Rebuild: `docker-compose -f docker-compose.claude-[desktop|cli].yml build --no-cache`
4. Restart: `docker-compose -f docker-compose.claude-[desktop|cli].yml up -d`

#### Runtime Switch (Quick)

1. Edit the docker-compose file
2. Change environment variables only
3. Restart: `docker-compose -f docker-compose.claude-[desktop|cli].yml up -d`

---

## Troubleshooting

### Container Won't Start

**Check logs:**
```bash
docker-compose -f docker-compose.claude-[desktop|cli].yml logs
```

**Common causes:**
- Docker not running: `docker info`
- Volume paths don't exist
- Port conflicts (if exposing ports)
- Insufficient resources

### Claude Can't Connect

**Verify container is running:**
```bash
docker ps | grep roslynmcp
```

**Test the server directly:**
```bash
docker exec -i roslynmcp-[desktop|cli] dotnet --info
```

**Check configuration:**
- Verify container name in Claude config
- Ensure `stdin_open: true` in docker-compose
- Confirm `tty: false` (not true)

### Performance Issues

**Increase resource limits:**

Edit docker-compose file:
```yaml
deploy:
  resources:
    limits:
      cpus: '4.0'
      memory: 8G
```

**Check disk I/O:**
- On Windows, bind mounts may be slower than volumes
- Consider using volumes instead of bind mounts

**Monitor resource usage:**
```bash
docker stats roslynmcp-[desktop|cli]
```

### Log File Issues

**Verify log directory:**
```bash
# Check permissions
ls -la /path/to/logs

# View log files
tail -f /path/to/logs/roslyn-mcp-*.log
```

**Common issues:**
- Log directory doesn't exist (create it first)
- No write permissions
- Volume mount is read-only (should be `:rw`)

### Connection Drops

**Container stops unexpectedly:**
```bash
# Check restart policy
docker inspect roslynmcp-[desktop|cli] | grep RestartPolicy

# View exit code
docker ps -a | grep roslynmcp
```

**Enable automatic restart:**
```yaml
restart: unless-stopped
```

---

## Advanced Configuration

### Resource Limits

For large codebases, tune resource allocation:

```yaml
deploy:
  resources:
    limits:
      cpus: '8.0'        # Increase for parallel analysis
      memory: 8G         # Increase for large solutions
    reservations:
      cpus: '2.0'        # Minimum guaranteed cores
      memory: 2G         # Minimum guaranteed memory
```

### Health Checks

Monitor container health:

```yaml
healthcheck:
  test: ["CMD", "dotnet", "--info"]
  interval: 30s
  timeout: 10s
  retries: 3
  start_period: 40s
```

### Multiple Solutions

To analyze multiple solutions, create separate containers:

```yaml
services:
  roslynmcp-project1:
    container_name: roslynmcp-project1
    volumes:
      - /path/to/project1:/workspace:ro
      - /path/to/logs/project1:/logs:rw

  roslynmcp-project2:
    container_name: roslynmcp-project2
    volumes:
      - /path/to/project2:/workspace:ro
      - /path/to/logs/project2:/logs:rw
```

### CI/CD Integration

Example GitHub Actions workflow:

```yaml
name: Code Analysis

on: [push, pull_request]

jobs:
  analyze:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3

      - name: Start RoslynMCP
        run: |
          docker-compose -f docker-compose.claude-cli.yml up -d

      - name: Run Analysis
        run: |
          claude "Analyze code complexity" > complexity-report.txt

      - name: Upload Results
        uses: actions/upload-artifact@v3
        with:
          name: analysis-results
          path: complexity-report.txt

      - name: Cleanup
        run: docker-compose -f docker-compose.claude-cli.yml down
```

### Network Configuration

If you need to access external services:

```yaml
networks:
  roslyn-network:
    driver: bridge

services:
  roslynmcp:
    networks:
      - roslyn-network
```

### Security Hardening

Run with minimal permissions:

```yaml
security_opt:
  - no-new-privileges:true
user: "1000:1000"  # Non-root user
read_only: true    # Read-only root filesystem
tmpfs:
  - /tmp           # Writable temp directory
```

---

## Useful Commands Reference

```bash
# Build
docker-compose -f docker-compose.claude-[desktop|cli].yml build
docker-compose -f docker-compose.claude-[desktop|cli].yml build --no-cache

# Start/Stop
docker-compose -f docker-compose.claude-[desktop|cli].yml up -d
docker-compose -f docker-compose.claude-[desktop|cli].yml down
docker-compose -f docker-compose.claude-[desktop|cli].yml restart

# Logs
docker-compose -f docker-compose.claude-[desktop|cli].yml logs -f
docker-compose -f docker-compose.claude-[desktop|cli].yml logs --tail=100

# Shell Access
docker-compose -f docker-compose.claude-[desktop|cli].yml exec roslynmcp sh

# Cleanup
docker-compose -f docker-compose.claude-[desktop|cli].yml down -v
docker system prune -a

# Inspect
docker inspect roslynmcp-[desktop|cli]
docker stats roslynmcp-[desktop|cli]
```

---

## Getting Help

- **Issues**: [GitHub Issues](https://github.com/your-repo/RoslynCSMCP/issues)
- **Documentation**: See `README.md` and `CLAUDE.md`
- **Docker Evaluation**: See `DOCKER_EVALUATION.md`
- **Docker Logs**: Check `/logs` directory on host

---

## Related Documentation

- [README.md](../README.md) - Project overview and features
- [CLAUDE.md](../CLAUDE.md) - Development guidelines
- [DOCKER_EVALUATION.md](DOCKER_EVALUATION.md) - Docker practicality analysis
- [Dockerfile](Dockerfile) - Docker build configuration
