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
| Run all tests | `dotnet test RoslynCSMCP.sln` | Before commits, CI/CD |
| Test interactively | `npx @modelcontextprotocol/inspector ...` | Debugging MCP tools |
| Check coverage | `dotnet test --collect:...` | Periodic quality checks |

For questions or issues with testing, see the documentation or create an issue on GitHub.
