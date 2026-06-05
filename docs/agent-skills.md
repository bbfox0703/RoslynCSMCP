Here is the translated Markdown file in full English:

# RoslynCSMCP Agent Skills

Each skill corresponds to an MCP module; simply load the corresponding module to use it.

---

## Module: Navigation (7 tools)

### /roslyn-explore

Explore C# codebase structure.
**Required Module**: `RoslynMcpServer.Navigation`
**Usage**: `/roslyn-explore <solution-path>`
**Steps**:

1. Call `GetProjectStructure` to see all projects and namespaces
2. Call `SearchSymbols` with pattern `*Service` to find service classes
3. Call `SearchSymbols` with pattern `I*` and `symbolTypes: "interface"` to find interfaces
4. Present summary with project organization and key types

---

### /roslyn-navigate

Find symbols and their references.
**Required Module**: `RoslynMcpServer.Navigation`
**Usage**: `/roslyn-navigate <symbol-name> <solution-path>`
**Steps**:

1. Call `SearchSymbols` to locate the symbol
2. Call `GetSymbolInfo` with `detailLevel: "basic"` for details
3. Call `FindReferences` with `detailLevel: "summary"` for usage count
4. Call `FindImplementations` if it's an interface
5. Present location, usage count, and implementations

---

### /roslyn-outline

View file structure outline.
**Required Module**: `RoslynMcpServer.Navigation`
**Usage**: `/roslyn-outline <file-path>`
**Steps**:

1. Call `GetFileOutline` with `mode: "normal"`
2. Present file structure with classes, methods, and properties

---

## Module: Quality (8 tools)

### /roslyn-quality

Code quality analysis.
**Required Module**: `RoslynMcpServer.Quality`
**Usage**: `/roslyn-quality <solution-path>`
**Steps**:

1. Call `AnalyzeCodeComplexity` with `threshold: 10`
2. Call `FindCodeSmells` to detect anti-patterns
3. Call `FindUnusedCode` with `format: "summary"`
4. Call `AnalyzeNamingConventions` with `scope: "public"`
5. Call `FindDuplicateCode` to detect copy-paste
6. Call `FindMagicNumbers` to find hardcoded values
7. Present quality report with recommendations

---

## Module: Security (3 tools)

### /roslyn-security

Security audit.
**Required Module**: `RoslynMcpServer.Security`
**Usage**: `/roslyn-security <solution-path>`
**Steps**:

1. Call `FindSecurityIssues` with `severity: "High"`
2. Call `FindThreadSafetyIssues` to detect concurrency problems
3. Call `AnalyzeExceptionHandling` to review error handling
4. Present security report with vulnerabilities and remediation

---

## Module: Dependencies (5 tools)

### /roslyn-dependencies

Dependency and architectural analysis.
**Required Module**: `RoslynMcpServer.Dependencies`
**Usage**: `/roslyn-dependencies <solution-path>`
**Steps**:

1. Call `AnalyzeDependencies` to check circular references
2. Call `GetDependencyGraph` with `format: "mermaid"`
3. Call `FindUnusedDependencies` to find removable packages
4. Call `AnalyzePackages` to check versions
5. Call `AnalyzeDIContainer` to review DI setup
6. Present dependency diagram and recommendations

---

## Module: Refactoring (5 tools)

### /roslyn-refactor

Refactoring impact assessment.
**Required Module**: `RoslynMcpServer.Refactoring`
**Usage**: `/roslyn-refactor <symbol-name> <solution-path>`
**Steps**:

1. Call `GetChangeImpact` to assess risk
2. Call `RenameSymbolSafely` with `previewOnly: true` to preview
3. Call `AnalyzeLayerViolations` to check architecture
4. Call `ExtractInterface` with `previewOnly: true` if applicable
5. Present impact report with risk assessment

---

## Module: Testing (2 tools)

### /roslyn-testing

Test coverage analysis.
**Required Module**: `RoslynMcpServer.Testing`
**Usage**: `/roslyn-testing <type-name> <solution-path>`
**Steps**:

1. Call `FindTestsForType` to find related tests
2. Call `GetTestCoverage` to analyze coverage
3. Present test inventory and coverage gaps

---

## Module: Metrics (4 tools)

### /roslyn-metrics

Code metrics report.
**Required Module**: `RoslynMcpServer.Metrics`
**Usage**: `/roslyn-metrics <solution-path>`
**Steps**:

1. Call `GetCodeMetrics` for overall statistics
2. Call `GetFileStatistics` for file-level details
3. Call `AnalyzeDocumentationCoverage` for doc coverage
4. Present metrics with LOC, complexity, and documentation %

---

## Module: Advanced (15 tools)

### /roslyn-deep-analysis

Deep symbol analysis (including filtering and cross-solution).
**Required Module**: `RoslynMcpServer.Advanced`
**Usage**: `/roslyn-deep-analysis <symbol-name> <solution-path>`
**Steps**:

1. Call `FindReferencesFiltered` with `excludeTests: true`
2. Call `GetCallHierarchy` with `direction: "both"`
3. Call `GetClassHierarchy` if it's a class
4. Call `GetTypeSignature` for API surface
5. Present comprehensive analysis

---

### /roslyn-batch

Batch comprehensive analysis.
**Required Module**: `RoslynMcpServer.Advanced`
**Usage**: `/roslyn-batch <solution-path>`
**Steps**:

1. Call `BatchQuery` with:
```json
[
  {"tool": "GetCompilationErrors", "parameters": {"solutionPath": "<path>"}},
  {"tool": "FindTODOComments", "parameters": {"solutionPath": "<path>"}},
  {"tool": "FindLargeFiles", "parameters": {"solutionPath": "<path>"}},
  {"tool": "FindDeprecatedAPIs", "parameters": {"solutionPath": "<path>"}},
  {"tool": "FindPerformanceIssues", "parameters": {"solutionPath": "<path>"}}
]

```


2. Present consolidated report

---

### /roslyn-api-diff

API change analysis.
**Required Module**: `RoslynMcpServer.Advanced`
**Usage**: `/roslyn-api-diff <old-solution> <new-solution>`
**Steps**:

1. Call `AnalyzeAPIChanges` with both solution paths
2. Present breaking changes and version recommendation

---

## Module: Interop (3 tools)

### /roslyn-interop

Native interop and AOT/trimming readiness review.
**Required Module**: `RoslynMcpServer.Interop`
**Usage**: `/roslyn-interop <solution-path>`
**Steps**:

1. Call `AnalyzeAotCompatibility` to detect AOT/trimming incompatibilities (reflection, XAML, `.csproj` settings)
2. Call `AnalyzePInvoke` to audit P/Invoke patterns and `[DllImport]` → `[LibraryImport]` migration opportunities
3. Call `AnalyzeUnsafeCode` to review pointer usage, `fixed` blocks, and `stackalloc` patterns
4. Present interop report with migration and safety recommendations

---

## Full Version Only

### /roslyn-full-audit

Full code audit (Requires Full version or all modules).
**Required**: `RoslynMcpServer` (Full) or all 9 modules
**Usage**: `/roslyn-full-audit <solution-path>`
**Steps**:

1. Call `GetProjectStructure` (Navigation)
2. Call `GetDependencyGraph` (Dependencies)
3. Call `GetCodeMetrics` (Metrics)
4. Call `FindCodeSmells` (Quality)
5. Call `FindSecurityIssues` (Security)
6. Call `GetTestCoverage` (Testing)
7. Present comprehensive audit report

---

## Module and Skill Reference Table

| Module | Skills | Tools |
| --- | --- | --- |
| Navigation | `/roslyn-explore`, `/roslyn-navigate`, `/roslyn-outline` | 7 |
| Quality | `/roslyn-quality` | 8 |
| Security | `/roslyn-security` | 3 |
| Dependencies | `/roslyn-dependencies` | 5 |
| Refactoring | `/roslyn-refactor` | 5 |
| Testing | `/roslyn-testing` | 2 |
| Metrics | `/roslyn-metrics` | 4 |
| Advanced | `/roslyn-deep-analysis`, `/roslyn-batch`, `/roslyn-api-diff` | 15 |
| Interop | `/roslyn-interop` | 3 |
| **Full** | `/roslyn-full-audit` + all above | 51 |

---

## Installation

### Option 1: Add to CLAUDE.md

Copy the required skill definitions to the project's `CLAUDE.md`.

### Option 2: Create skill files

```
.claude/
└── skills/
    ├── roslyn-explore.md      # Navigation
    ├── roslyn-navigate.md     # Navigation
    ├── roslyn-quality.md      # Quality
    ├── roslyn-security.md     # Security
    ├── roslyn-dependencies.md # Dependencies
    ├── roslyn-refactor.md     # Refactoring
    ├── roslyn-testing.md      # Testing
    ├── roslyn-metrics.md      # Metrics
    ├── roslyn-batch.md        # Advanced
    ├── roslyn-interop.md      # Interop
    └── roslyn-full-audit.md   # Full only

```

### Select Corresponding Modules

Install only the MCP modules corresponding to the skills:

```json
{
  "mcpServers": {
    "roslyn-nav": {
      "command": "dotnet",
      "args": ["run", "--project", "src/RoslynMcpServer.Navigation"]
    }
  }
}

```

With this configuration, only `/roslyn-explore`, `/roslyn-navigate`, and `/roslyn-outline` will be available.

## Module and Skill Mapping

| Loaded Module | Available Skills |
| --- | --- |
| **Navigation** | `/roslyn-explore`, `/roslyn-navigate`, `/roslyn-outline` |
| **Quality** | `/roslyn-quality` |
| **Security** | `/roslyn-security` |
| **Dependencies** | `/roslyn-dependencies` |
| **Refactoring** | `/roslyn-refactor` |
| **Testing** | `/roslyn-testing` |
| **Metrics** | `/roslyn-metrics` |
| **Advanced** | `/roslyn-deep-analysis`, `/roslyn-batch`, `/roslyn-api-diff` |
| **Interop** | `/roslyn-interop` |
| **Full** | All of the above + `/roslyn-full-audit` |

---

## Usage Guide

For example, if you only load the **Navigation** module:

```json
{
  "mcpServers": {
    "roslyn-nav": {
      "command": "dotnet",
      "args": ["run", "--project", "src/RoslynMcpServer.Navigation"]
    }
  }
}

```

In this scenario, you only need to copy the 3 skill definitions corresponding to **Navigation** into your `CLAUDE.md`. Other skills will not cause errors because the system won't attempt to call tools that aren't defined.