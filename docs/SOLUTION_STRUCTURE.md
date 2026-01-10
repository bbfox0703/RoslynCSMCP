# Solution Structure

## Overview

The RoslynCSMCP project uses a unified solution file that contains both the main server project and the test project. This follows .NET best practices and improves the development experience.

## Solution Files

### Primary Solution (Recommended)
**File**: `RoslynCSMCP.sln` (root directory)

This is the main solution file that includes both projects:
- `RoslynMcpServer` - The main MCP server
- `RoslynMcpServer.Tests` - Unit and integration tests

**Benefits**:
- ✅ Open both projects simultaneously in your IDE
- ✅ Build and test with a single command
- ✅ Better IntelliSense and code navigation
- ✅ Standard .NET project structure

### Individual Solutions (Legacy)
For backwards compatibility, the individual solution files are retained:
- `RoslynMcpServer/RoslynMcpServer.sln`
- `RoslynMcpServer.Tests/RoslynMcpServer.Tests.sln`

These can be used if you only need to work on one project, but the unified solution is recommended.

## Building the Project

### Using the Unified Solution (Recommended)
```bash
# Build everything
dotnet build RoslynCSMCP.sln

# Run tests
dotnet test RoslynCSMCP.sln

# Build in Release mode
dotnet build RoslynCSMCP.sln -c Release
```

### Using Individual Projects
```bash
# Build only the server
dotnet build RoslynMcpServer/RoslynMcpServer.csproj

# Build only the tests
dotnet build RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj
```

## Impact on Existing Workflows

### ✅ No Impact on Installation Scripts

The setup scripts (`install/setup-claude-desktop.ps1`, etc.) are **NOT affected** by this change because they use project files directly, not solution files:

```powershell
# Line 62: Uses dotnet build without .sln
dotnet build -c Release

# Line 93: Runs using project path
args = @("run", "--project", $projectPath)
```

### ✅ No Impact on Docker

The Dockerfile is **NOT affected** because it references the `.csproj` file directly:

```dockerfile
# Line 49-50: Uses .csproj, not .sln
COPY ["RoslynMcpServer.csproj", "."]
RUN dotnet restore "RoslynMcpServer.csproj"

# Line 54-56: Builds using .csproj
RUN dotnet build "RoslynMcpServer.csproj" -c Release -o /app/build
```

### ✅ No Impact on Runtime

The server runs using `dotnet run --project` which works with project files, not solution files.

## IDE Support

### Visual Studio
1. Open `RoslynCSMCP.sln`
2. Both projects appear in Solution Explorer
3. Right-click on test project and select "Run Tests"

### Visual Studio Code
1. Open the root folder
2. The C# extension will detect the solution
3. Use "Run Test" CodeLens or the Test Explorer

### JetBrains Rider
1. Open `RoslynCSMCP.sln`
2. Both projects load automatically
3. Use built-in test runner

## CI/CD Integration

For GitHub Actions or other CI/CD systems:

```yaml
# Build everything
- name: Build
  run: dotnet build RoslynCSMCP.sln -c Release

# Run all tests
- name: Test
  run: dotnet test RoslynCSMCP.sln --no-build --verbosity normal

# Run tests with coverage
- name: Test with Coverage
  run: dotnet test RoslynCSMCP.sln --collect:"XPlat Code Coverage"
```

## Project Structure

```
RoslynCSMCP/
├── RoslynCSMCP.sln                 # ← Unified solution (RECOMMENDED)
│
├── RoslynMcpServer/
│   ├── RoslynMcpServer.csproj      # Main server project
│   ├── RoslynMcpServer.sln         # Legacy individual solution
│   ├── Program.cs
│   ├── Services/
│   ├── Models/
│   └── Tools/
│
├── RoslynMcpServer.Tests/
│   ├── RoslynMcpServer.Tests.csproj  # Test project
│   ├── RoslynMcpServer.Tests.sln     # Legacy individual solution
│   ├── Unit/
│   ├── Integration/
│   └── Helpers/
│
├── install/
│   ├── setup-claude-desktop.ps1    # Not affected by .sln changes
│   └── setup-claude-cli.ps1
│
└── docs/
    └── SOLUTION_STRUCTURE.md       # This file
```

## Migration Guide

If you were using the individual solution files:

### Before (Old Way)
```bash
# Had to build separately
cd RoslynMcpServer
dotnet build RoslynMcpServer.sln

cd ../RoslynMcpServer.Tests
dotnet build RoslynMcpServer.Tests.sln
```

### After (New Way)
```bash
# Build everything at once
dotnet build RoslynCSMCP.sln

# Or run tests directly
dotnet test RoslynCSMCP.sln
```

## Recommendations

1. **For Development**: Use `RoslynCSMCP.sln`
   - Open this in your IDE
   - Best developer experience
   - Can navigate between main code and tests easily

2. **For Quick Server-Only Work**: Use project file directly
   ```bash
   dotnet run --project RoslynMcpServer
   ```

3. **For CI/CD**: Use `RoslynCSMCP.sln`
   - Single build command
   - Ensures tests build against latest main code
   - Consistent with most .NET projects

## Troubleshooting

### Issue: "Project not found"
**Solution**: Make sure you're in the root directory (`RoslynCSMCP/`) when running commands with the unified solution.

### Issue: "Cannot find solution file"
**Solution**: Use the correct path:
```bash
# From root directory
dotnet build RoslynCSMCP.sln

# From subdirectory
dotnet build ../RoslynCSMCP.sln
```

### Issue: "Tests don't run"
**Solution**: Rebuild the solution first:
```bash
dotnet build RoslynCSMCP.sln
dotnet test RoslynCSMCP.sln --no-build
```

## Verification

To verify the unified solution works correctly:

```bash
# 1. Clean everything
dotnet clean RoslynCSMCP.sln

# 2. Restore packages
dotnet restore RoslynCSMCP.sln

# 3. Build
dotnet build RoslynCSMCP.sln

# 4. Run tests
dotnet test RoslynCSMCP.sln

# Expected output:
# ✓ Building 2 projects
# ✓ RoslynMcpServer builds successfully
# ✓ RoslynMcpServer.Tests builds successfully
# ✓ Tests execute (16+ passing)
```

## Summary

✅ **Created**: `RoslynCSMCP.sln` in root directory
✅ **Includes**: Both RoslynMcpServer and RoslynMcpServer.Tests projects
✅ **Compatible**: All existing scripts, Docker, and workflows
✅ **Tested**: Builds successfully with 0 warnings, 0 errors
✅ **Recommended**: Use this for all development work

The unified solution improves productivity while maintaining full backward compatibility with all existing tools and workflows.
