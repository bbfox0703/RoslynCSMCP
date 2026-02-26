# CLAUDE.md

This file provides guidance to Claude Code when working with this repository.

> For detailed documentation, see the [`docs/` directory](docs/).

---

## Project Overview

RoslynCSMCP is a C# MCP (Model Context Protocol) server integrating Microsoft's Roslyn compiler platform with Claude Desktop. It provides tools for C# code analysis: symbol search, reference tracking, dependency analysis, and complexity analysis.

---

## Build Commands

```bash
dotnet restore            # Restore NuGet packages
dotnet build              # Build (debug)
dotnet build -c Release   # Build (release)
dotnet run                # Run the server
dotnet run --no-build     # Run without rebuild
dotnet test               # Run unit tests

# Test with MCP Inspector
npx @modelcontextprotocol/inspector dotnet run --project ./RoslynMcpServer
```

**Setup scripts**:
- Windows: `.\install\setup-claude-desktop.ps1`
- Linux/macOS: `./install/setup-claude-desktop.sh`

---

## Critical: MSBuild Registration

`MSBuildLocator.RegisterDefaults()` **must** be called before any Roslyn workspace operation. This is already handled in `Program.cs:12-33` — do not move or remove it.

---

## Project Structure

```
RoslynCSMCP.sln
├── src/
│   ├── RoslynMcpServer.Core/           # Shared library (services, models, auth)
│   ├── RoslynMcpServer.Navigation/     # Navigation MCP   (6 tools,  ~1,050 tokens)
│   ├── RoslynMcpServer.Quality/        # Quality MCP      (8 tools,  ~1,400 tokens)
│   ├── RoslynMcpServer.Security/       # Security MCP     (3 tools,    ~525 tokens)
│   ├── RoslynMcpServer.Dependencies/   # Dependencies MCP (5 tools,    ~875 tokens)
│   ├── RoslynMcpServer.Refactoring/    # Refactoring MCP  (5 tools,    ~875 tokens)
│   ├── RoslynMcpServer.Testing/        # Testing MCP      (2 tools,    ~350 tokens)
│   ├── RoslynMcpServer.Metrics/        # Metrics MCP      (4 tools,    ~700 tokens)
│   ├── RoslynMcpServer.Advanced/       # Advanced MCP    (15 tools,  ~2,625 tokens)
│   └── RoslynMcpServer.Interop/        # Interop MCP      (3 tools,    ~525 tokens)
├── RoslynMcpServer/                    # Full version     (51 tools,  ~8,925 tokens)
└── RoslynMcpServer.Tests/              # Unit & integration tests
```

---

## Documentation

| File | Description |
|------|-------------|
| [docs/architecture.md](docs/architecture.md) | Layer design, key patterns, service registration |
| [docs/development-guide.md](docs/development-guide.md) | Adding tools, logging, Roslyn tips, performance |
| [docs/configuration.md](docs/configuration.md) | Claude Desktop config, env vars, debug logging |
| [docs/mcp-protocol.md](docs/mcp-protocol.md) | Error codes, pagination, cancellation, OAuth |
| [docs/tool-reference.md](docs/tool-reference.md) | All tools with parameters and descriptions |
| [docs/usage-examples.md](docs/usage-examples.md) | Tool usage examples and common workflows |
| [docs/testing-guide.md](docs/testing-guide.md) | Testing and validation guide |
| [docs/agent-skills.md](docs/agent-skills.md) | Agent skill definitions (`/roslyn-*` commands) |
| [docs/skills-setup.md](docs/skills-setup.md) | How to install Claude Code skills |
