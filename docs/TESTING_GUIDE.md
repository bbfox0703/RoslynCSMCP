# Testing Guide

## Overview

This guide covers testing and validation for the RoslynCSMCP project, including installation verification, unit tests, and integration tests.

## Installation Validation Scripts

### Purpose

The installation test scripts (`test-installation.bat` / `test-installation.sh`) provide a quick way to verify that:
- ✅ .NET SDK is installed and meets version requirements
- ✅ All required project files exist
- ✅ NuGet packages can be restored
- ✅ The project builds successfully
- ✅ The server can start without errors
- ✅ Test project is present and builds

### When to Use

**Use these scripts when:**
- ✅ Setting up a new development environment
- ✅ Verifying the installation before running full setup
- ✅ Troubleshooting build or startup issues
- ✅ Running CI/CD validation
- ✅ Testing after major code changes

**Don't use these when:**
- ❌ You want to actually configure Claude Desktop (use `install/setup-claude-desktop.*` instead)
- ❌ You want to run unit tests (use `dotnet test` instead)

### Differences from Setup Scripts

| Feature | `test-installation.*` | `install/setup-claude-desktop.*` |
|---------|----------------------|----------------------------------|
| Checks .NET version | ✅ Yes | ✅ Yes |
| Verifies file structure | ✅ Yes | ❌ No |
| Builds project | ✅ Yes | ✅ Yes |
| Tests server startup | ✅ Yes | ✅ Yes |
| Configures Claude Desktop | ❌ No | ✅ Yes |
| Modifies system files | ❌ No | ✅ Yes |
| Use case | Verification only | Full installation |

## Running Installation Tests

### Windows

```cmd
# From project root
.\test-installation.bat

# Or using Command Prompt
cd D:\Github\RoslynCSMCP
test-installation.bat
```

### Linux / macOS

```bash
# From project root
./test-installation.sh

# Or explicitly with bash
bash test-installation.sh
```

### Expected Output

```
=== Roslyn MCP Server Installation Test ===

1. Checking .NET installation...
   ✓ .NET SDK found: 10.0.1

2. Checking project structure...
   ✓ RoslynMcpServer.csproj exists
   ✓ Program.cs exists
   ✓ Services/CodeAnalysisService.cs exists
   ✓ Services/SymbolSearchService.cs exists
   ✓ Services/IncrementalAnalyzer.cs exists
   ✓ Services/CacheManager.cs exists
   ✓ Services/SecurityValidator.cs exists
   ✓ Services/DiagnosticLogger.cs exists
   ✓ Tools/CodeNavigationTools.cs exists
   ✓ Models/SearchModels.cs exists

3. Restoring NuGet packages...
   ✓ NuGet packages restored successfully

4. Building project...
   ✓ Project built successfully

5. Testing basic server startup...
   ✓ Server started successfully

6. Checking test project...
   ✓ Test project found
   ✓ Test project built successfully

7. Checking MCP Inspector availability...
   ✓ npm/npx found - MCP Inspector can be used for testing

=== Installation Test Complete ===

✓ All tests passed! The Roslyn MCP Server is ready to use.
```

## MCP Connection Tests

### Purpose

The MCP connection test scripts verify that RoslynCSMCP is properly configured and being used by Claude:

- **`test-mcp-connection.ps1`** - Tests Claude Desktop integration
- **`test-mcp-cli.ps1`** - Tests Claude CLI (Claude Code) integration

### Differences Between Desktop and CLI Testing

| Feature | `test-mcp-connection.ps1` | `test-mcp-cli.ps1` |
|---------|--------------------------|-------------------|
| Target | Claude Desktop | Claude CLI (Claude Code) |
| Config Path (Windows) | `%APPDATA%\Claude\claude_desktop_config.json` | `%APPDATA%\Claude\claude_code_config.json` |
| Verifies config | ✅ Yes | ✅ Yes |
| Tests server startup | ✅ Yes | ✅ Yes |
| Checks logs | ✅ Yes | ✅ Yes |
| Provides test guidance | ✅ Yes | ✅ Yes |
| Real-time log monitoring | ❌ No | ✅ Yes |

### Testing Claude Desktop Integration

```powershell
# Run the Desktop connection test
.\test-mcp-connection.ps1
```

This script:
1. Verifies RoslynCSMCP is in Claude Desktop config
2. Tests that the server can start
3. Checks for recent log files
4. Provides example prompts to test with

**When to use:** After installing RoslynCSMCP, before using Claude Desktop with C# projects.

### Testing Claude CLI Integration

```powershell
# Run the CLI connection test
.\test-mcp-cli.ps1
```

This script:
1. Verifies Claude CLI is installed
2. Checks if RoslynCSMCP MCP server is configured (via `claude mcp list`)
3. Tests that the server can start
4. Checks for recent log files
5. Provides step-by-step testing instructions
6. Shows how to monitor logs in real-time

**When to use:** When using Claude CLI (command line) with C# projects.

**Adding RoslynCSMCP to Claude CLI:**

If not configured, add it using the `claude mcp add` command:

```powershell
# Add with full project path
claude mcp add roslyn-mcp -e DOTNET_ENVIRONMENT=Development -- dotnet run --project "D:\Github\RoslynCSMCP\RoslynMcpServer"

# Or use relative path from the script directory
cd D:\Github\RoslynCSMCP
claude mcp add roslyn-mcp -e DOTNET_ENVIRONMENT=Development -- dotnet run --project ".\RoslynMcpServer"

# If already built (faster startup)
claude mcp add roslyn-mcp -e DOTNET_ENVIRONMENT=Development -- dotnet run --project ".\RoslynMcpServer" --no-build
```

**Managing the MCP server:**

```powershell
# List all configured MCP servers
claude mcp list

# Get details about RoslynCSMCP
claude mcp get roslyn-mcp

# Remove RoslynCSMCP
claude mcp remove roslyn-mcp
```

### How to Verify RoslynCSMCP is Being Used

After running either test script, follow these steps:

**Step 1: Clear old logs**
```powershell
Remove-Item "$env:TEMP\RoslynCSMCP\logs\*.log"
```

**Step 2: Use Claude with a C# solution**

Ask Claude to analyze your C# code with prompts like:
- "Search for classes named UserService"
- "Find all references to the GetUser method"
- "Analyze dependencies in this solution"
- "Find methods with high complexity"

**Step 3: Check if RoslynCSMCP was used**

Look for evidence in logs:
```powershell
# List recent log files
$logDir = "$env:TEMP\RoslynCSMCP\logs"
Get-ChildItem $logDir -Filter *.log | Sort-Object LastWriteTime

# Watch logs in real-time
$today = Get-Date -Format "yyyyMMdd"
Get-Content "$logDir\debug-$today.log" -Wait -Tail 50
```

**Signs RoslynCSMCP is being used:**
- ✅ New `debug-YYYYMMDD.log` files appear
- ✅ Log entries show "Executing tool: SearchSymbols", "FindReferences", etc.
- ✅ Roslyn workspace loading messages
- ✅ Symbol search results with relevance scores

**Signs built-in tools are being used instead:**
- ❌ No new RoslynCSMCP log files
- ❌ Claude uses generic tools like "Explore", "Task", "Grep"
- ❌ No Roslyn-specific messages

### Troubleshooting

**Issue: "RoslynCSMCP is NOT configured"**
```powershell
# Run the appropriate setup script
.\install\setup-claude-desktop.ps1   # For Desktop
# Or manually edit the config file shown in the error message
```

**Issue: "Server failed to start"**
```bash
# Check build status
cd RoslynMcpServer
dotnet build --verbosity detailed
```

**Issue: "No log files appear when using Claude"**

Possible causes:
1. Claude is using built-in tools instead of RoslynCSMCP
2. Not in a directory with .sln or .csproj files
3. Config not loaded (restart Claude Desktop/CLI)
4. Server failed to start (check error logs)

Try:
- Restart Claude Desktop/CLI completely
- Ensure you're in a C# solution directory
- Mention "using Roslyn analysis" explicitly in prompts
- Check server error logs: `Get-Content "$env:TEMP\RoslynCSMCP\logs\debug-$(Get-Date -Format yyyyMMdd).log"`

## Unit Testing

### Running Unit Tests

```bash
# Run all tests
dotnet test RoslynCSMCP.sln

# Run tests with detailed output
dotnet test RoslynCSMCP.sln --verbosity normal

# Run specific test class
dotnet test --filter "FullyQualifiedName~CacheManagerTests"

# Run tests in Release mode
dotnet test RoslynCSMCP.sln -c Release
```

### Test Coverage

```bash
# Generate code coverage report
dotnet test RoslynCSMCP.sln --collect:"XPlat Code Coverage"

# With ReportGenerator (install first: dotnet tool install -g dotnet-reportgenerator-globaltool)
dotnet test RoslynCSMCP.sln --collect:"XPlat Code Coverage"
reportgenerator -reports:"**/coverage.cobertura.xml" -targetdir:"coveragereport" -reporttypes:Html
```

### Current Test Status

See `docs/TEST_IMPLEMENTATION_SUMMARY.md` for:
- Current test pass rate (16/30 passing)
- Known issues and workarounds
- Test coverage by feature
- Recommendations for improvement

## Integration Testing with MCP Inspector

The MCP Inspector allows you to test the server interactively:

```bash
# Windows
npx @modelcontextprotocol/inspector dotnet run --project .\RoslynMcpServer

# Linux/macOS
npx @modelcontextprotocol/inspector dotnet run --project ./RoslynMcpServer
```

This opens a web interface where you can:
- Test MCP tool calls
- Inspect request/response messages
- Debug server behavior
- Validate JSON schemas

## CI/CD Integration

### GitHub Actions Example

```yaml
name: Test

on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3

      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '10.0.x'

      - name: Run Installation Test
        run: ./test-installation.sh

      - name: Run Unit Tests
        run: dotnet test RoslynCSMCP.sln --verbosity normal

      - name: Generate Coverage Report
        run: |
          dotnet test RoslynCSMCP.sln --collect:"XPlat Code Coverage"
          # Upload coverage to codecov or similar
```

### GitLab CI Example

```yaml
test:
  image: mcr.microsoft.com/dotnet/sdk:10.0
  script:
    - chmod +x test-installation.sh
    - ./test-installation.sh
    - dotnet test RoslynCSMCP.sln --verbosity normal
```

## Troubleshooting Test Failures

### Installation Test Failures

**Issue: ".NET SDK not found"**
```bash
# Install .NET 10.0 SDK
# Windows: https://dotnet.microsoft.com/download/dotnet/10.0
# Linux: https://learn.microsoft.com/en-us/dotnet/core/install/linux
```

**Issue: "Some required files are missing"**
```bash
# Check if you're in the correct directory
cd D:\Github\RoslynCSMCP

# Ensure all files are checked out
git status
git pull
```

**Issue: "Build failed"**
```bash
# Run build with detailed output
cd RoslynMcpServer
dotnet build --verbosity detailed
```

**Issue: "Server failed to start"**
```bash
# Check for port conflicts or MSBuild registration issues
cd RoslynMcpServer
dotnet run --verbosity detailed
```

### Unit Test Failures

**Issue: "Extension method mocking errors"**
- Known issue with Moq and extension methods
- See `docs/TEST_IMPLEMENTATION_SUMMARY.md` for workarounds

**Issue: "File not found in tests"**
- Tests expect specific file structure
- Verify all test helper classes exist
- Check that TestSolutionBuilder can create in-memory solutions

## Test Organization

```
RoslynMcpServer.Tests/
├── Unit/
│   ├── Services/
│   │   ├── CacheManagerTests.cs       # Multi-level cache tests
│   │   ├── IncrementalAnalyzerTests.cs # Complexity analysis tests
│   │   ├── CodeAnalysisServiceTests.cs # Dependency analysis tests
│   │   └── SymbolSearchServiceTests.cs # Symbol search tests
│   └── Tools/
│       └── [Future: MCP tool tests]
├── Integration/
│   └── [Future: End-to-end tests]
├── Performance/
│   └── [Future: Performance benchmarks]
└── Helpers/
    ├── MockServiceProvider.cs         # Mock creation helpers
    └── TestSolutionBuilder.cs         # Test solution builders
```

## Best Practices

### When Writing Tests

1. **Use Helpers**
   ```csharp
   // Use TestSolutionBuilder for creating test solutions
   var solution = new TestSolutionBuilder()
       .AddProject("TestProject")
       .AddDocument("TestProject", "Test.cs", code)
       .Solution;
   ```

2. **Mock Dependencies**
   ```csharp
   // Use MockServiceProvider for creating mocks
   var mockLogger = MockServiceProvider.CreateMockLogger<MyService>();
   var memoryCache = MockServiceProvider.CreateMemoryCache();
   ```

3. **Follow AAA Pattern**
   ```csharp
   // Arrange - Set up test data
   var input = "test";

   // Act - Execute the code under test
   var result = await service.ProcessAsync(input);

   // Assert - Verify the results
   result.Should().Be("expected");
   ```

4. **Use Descriptive Names**
   ```csharp
   // Good: Clear what is being tested and expected outcome
   [Fact]
   public async Task Circuit_ThreeFailures_OpensCircuit()

   // Bad: Unclear test purpose
   [Fact]
   public async Task Test1()
   ```

### When Running Tests

1. **Run tests before committing**
   ```bash
   dotnet test RoslynCSMCP.sln
   ```

2. **Check coverage periodically**
   ```bash
   dotnet test --collect:"XPlat Code Coverage"
   ```

3. **Fix failing tests immediately**
   - Don't let technical debt accumulate
   - Update tests when changing code

4. **Use filters for focused testing**
   ```bash
   # Test only cache-related tests
   dotnet test --filter "FullyQualifiedName~Cache"
   ```

## Related Documentation

- `TEST_IMPLEMENTATION_SUMMARY.md` - Current test status and known issues
- `TEST_CASES_EVALUATION.md` - Comprehensive test case catalog
- `SOLUTION_STRUCTURE.md` - Solution file organization
- `README.md` - General project setup

## Summary

| Task | Command | When to Use |
|------|---------|-------------|
| Verify installation | `./test-installation.*` | Before setup, troubleshooting |
| Test Desktop MCP | `./test-mcp-connection.ps1` | Verify Claude Desktop integration |
| Test CLI MCP | `./test-mcp-cli.ps1` | Verify Claude CLI integration |
| Run all tests | `dotnet test RoslynCSMCP.sln` | Before commits, CI/CD |
| Test interactively | `npx @modelcontextprotocol/inspector ...` | Debugging MCP tools |
| Check coverage | `dotnet test --collect:...` | Periodic quality checks |

For questions or issues with testing, see the documentation or create an issue on GitHub.
