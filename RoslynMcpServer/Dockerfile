# =============================================================================
# RoslynCSMCP Docker Configuration
# =============================================================================
# This Dockerfile builds a containerized version of the RoslynCSMCP server
# for use with both Claude Desktop and Claude CLI.
#
# Build Arguments:
#   ENVIRONMENT: Production (default) or Development
#
# Usage:
#   1. Claude Desktop: Use docker-compose.claude-desktop.yml
#   2. Claude CLI: Use docker-compose.claude-cli.yml
#
# See DOCKER_EVALUATION.md for detailed deployment guidance.
# =============================================================================

# Build argument for environment (Production or Development)
ARG ENVIRONMENT=Production

# =============================================================================
# Base Stage - Runtime Environment
# =============================================================================
FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine AS base
WORKDIR /app

# Install MSBuild dependencies required by Roslyn
# These are necessary for MSBuildLocator.RegisterDefaults() in Program.cs
RUN apk add --no-cache \
    icu-libs \
    libgcc \
    libgssapi-krb5 \
    libintl \
    libssl3 \
    libstdc++ \
    zlib

# Create directories for volume mounts
RUN mkdir -p /workspace /logs

# =============================================================================
# Build Stage - Compile the Application
# =============================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG ENVIRONMENT
WORKDIR /src

# Copy project file and restore dependencies
# This layer is cached if dependencies don't change
COPY ["RoslynMcpServer.csproj", "."]
RUN dotnet restore "RoslynMcpServer.csproj"

# Copy source code and build
COPY . .
RUN dotnet build "RoslynMcpServer.csproj" \
    -c Release \
    -o /app/build

# =============================================================================
# Publish Stage - Create Deployment Package
# =============================================================================
FROM build AS publish
ARG ENVIRONMENT
RUN dotnet publish "RoslynMcpServer.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    --self-contained false

# =============================================================================
# Final Stage - Production Image
# =============================================================================
FROM base AS final
ARG ENVIRONMENT

WORKDIR /app

# Copy published application
COPY --from=publish /app/publish .

# Set environment variables
# These can be overridden in docker-compose.yml or docker run
ENV DOTNET_ENVIRONMENT=${ENVIRONMENT} \
    LOG_LEVEL=Information \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

# Production vs Development Configuration
# ========================================
#
# PRODUCTION (default):
#   - DOTNET_ENVIRONMENT=Production
#   - LOG_LEVEL=Information
#   - Logs go to /logs directory (mount as volume)
#   - No debug symbols included
#
# DEVELOPMENT:
#   - DOTNET_ENVIRONMENT=Development
#   - LOG_LEVEL=Debug
#   - More verbose logging
#   - Include debug output
#
# To switch to Development mode:
#   1. Build: docker build --build-arg ENVIRONMENT=Development -t roslynmcp:dev .
#   2. Or set in docker-compose.yml: environment: DOTNET_ENVIRONMENT=Development

# Volume mount points
# ===================
# /workspace - Mount your solution directory here (read-only recommended)
#              Example: /path/to/your/solution:/workspace:ro
#
# /logs - Mount for persistent log files (read-write)
#         Example: /path/to/logs:/logs:rw

# Port exposure (not typically needed for MCP stdio transport)
# If you need HTTP endpoints in the future, expose them here

# MCP Server Entry Point
# ======================
# The server communicates via stdin/stdout (stdio transport)
# Claude Desktop/CLI will connect to these streams
ENTRYPOINT ["dotnet", "RoslynMcpServer.dll"]

# Labels for container metadata
LABEL maintainer="RoslynCSMCP"
LABEL description="Roslyn-based MCP server for C# code analysis"
LABEL version="1.0"
LABEL environment="${ENVIRONMENT}"
