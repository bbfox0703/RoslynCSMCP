# RoslynCSMCP Tools Reference

This document details the core MCP tools provided by RoslynCSMCP server for C# code analysis.

> **Last Updated**: 2026-01-12
> **Total Tools**: 51 (34 detailed below; see the [README](../README.md) for the full tool list including the Interop, performance, and modernization tools)
>
> **Recent Additions**: FindTODOComments, FindLargeFiles, FindDeprecatedAPIs, GetFileStatistics, AnalyzePackages, GetTestCoverage, GetChangeImpact, FindPerformanceIssues, AnalyzeNamingConventions, AnalyzeAPIChanges

---

## 🔍 Symbol Search & Navigation

### 1. SearchSymbols
**Description**: Search for symbols in C# code using wildcard patterns (* and ?)

**Parameters**:
- `solutionPath` (string): Path to solution file (.sln)
- `pattern` (string): Wildcard pattern to search for (e.g., 'User*', '*Service', 'Get*User')
- `symbolTypes` (string, optional): Symbol types to include: class,interface,method,property,field (comma-separated, default: "class,interface,method,property")
- `ignoreCase` (bool, optional): Whether to ignore case in search (default: true)

**Example Usage**:
```
Use SearchSymbols tool with:
- solutionPath: "D:\MyProject\MyProject.sln"
- pattern: "User*"
- symbolTypes: "class,interface"
```

---

### 2. FindReferences
**Description**: Find all references to a specific symbol with configurable detail level

**Parameters**:
- `symbolName` (string): Exact symbol name to find references for
- `solutionPath` (string): Path to solution file (.sln)
- `detailLevel` (string, optional): Detail level: summary (file stats only), locations (with code lines), full (with 5-line context). Default: locations
- `includeDefinition` (bool, optional): Include symbol definition in results (default: true)

**Example Usage**:
```
Use FindReferences tool with:
- symbolName: "UserService"
- solutionPath: "D:\MyProject\MyProject.sln"
- detailLevel: "full"
```

---

### 3. FindReferencesAcrossSolutions
**Description**: Find all references to a symbol across multiple solutions

**Parameters**:
- `symbolName` (string): Exact symbol name to find references for
- `solutionPaths` (string): Comma-separated list of solution file paths (.sln)
- `detailLevel` (string, optional): Detail level: summary, locations, full (default: locations)
- `includeDefinition` (bool, optional): Include symbol definition (default: true)

---

### 4. FindReferencesFiltered
**Description**: Find references with advanced filtering options to reduce noise and focus on specific usage patterns

**Parameters**:
- `symbolName` (string): Exact symbol name to find references for
- `solutionPath` (string): Path to solution file (.sln)
- `detailLevel` (string, optional): Detail level: summary, locations, full (default: locations)
- `includeDefinition` (bool, optional): Include symbol definition (default: true)
- `projectFilter` (string, optional): Filter by project name pattern (supports wildcards: * and ?)
- `excludeTests` (bool, optional): Exclude test projects (default: false)
- `writesOnly` (bool, optional): Only show write operations (assignments, increments, etc.) (default: false)
- `publicOnly` (bool, optional): Only show references in public API contexts (default: false)
- `crossProjectOnly` (bool, optional): Only show cross-project references (default: false)

---

### 5. GetSymbolInfo
**Description**: Get detailed information about a specific symbol

**Parameters**:
- `symbolName` (string): Exact symbol name or fully qualified name
- `solutionPath` (string): Path to solution file (.sln)
- `detailLevel` (string, optional): Detail level: summary, basic, full (default: basic)

---

## 📊 Code Structure & Analysis

### 6. GetProjectStructure
**Description**: Get hierarchical structure of projects, namespaces, and types

**Parameters**:
- `solutionPath` (string): Path to solution file (.sln)
- `publicOnly` (bool, optional): Include only public types (default: true)
- `includeMembers` (bool, optional): Include member signatures (default: false)
- `namespaceFilter` (string, optional): Filter by namespace pattern (e.g., 'MyProject.Services')

---

### 7. GetTypeSignature
**Description**: Get type signature with members but without implementation

**Parameters**:
- `typeName` (string): Fully qualified or simple type name
- `solutionPath` (string): Path to solution file (.sln)
- `includePrivate` (bool, optional): Include private members (default: false)
- `includeDocumentation` (bool, optional): Include XML documentation comments (default: true)

---

### 8. GetFileOutline
**Description**: Get structural outline of a C# file showing types and members without full implementation details

**Parameters**:
- `filePath` (string): Path to C# source file (.cs)
- `mode` (string, optional): Output mode: compact, normal, detailed (default: normal)
- `includeMembers` (bool, optional): Include member details (default: true)
- `includeDocumentation` (bool, optional): Include documentation comments (default: true)
- `maxMembers` (int, optional): Maximum members to show per type (default: 10, 0=show all)

---

### 9. GetClassHierarchy
**Description**: Get complete class hierarchy showing ancestors (base classes/interfaces) and descendants (derived classes)

**Parameters**:
- `typeName` (string): Type name to analyze hierarchy for
- `solutionPath` (string): Path to solution file (.sln)
- `direction` (string, optional): Direction: ancestors, descendants, or both (default: both)
- `format` (string, optional): Output format: compact, normal, detailed (default: normal)
- `maxDepth` (int, optional): Maximum depth to traverse (default: 10)

---

### 10. FindImplementations
**Description**: Find all implementations of an interface or abstract class

**Parameters**:
- `typeName` (string): Interface or abstract class name to find implementations for
- `solutionPath` (string): Path to solution file (.sln)
- `format` (string, optional): Output format: summary, normal, detailed (default: normal)
- `includeAbstractImplementations` (bool, optional): Include abstract implementations (default: false)

---

### 11. FindAttributeUsages
**Description**: Find all usages of a specific attribute across the solution

**Parameters**:
- `attributeName` (string): Attribute name to search for (with or without 'Attribute' suffix)
- `solutionPath` (string): Path to solution file (.sln)
- `targetType` (string, optional): Target type filter: class, interface, method, property, field, parameter, or all (default: all)
- `format` (string, optional): Output format: inline, normal, detailed (default: normal)

---

## 📈 Code Metrics & Quality

### 12. GetCodeMetrics
**Description**: Get code metrics and statistics for entire solution

**Parameters**:
- `solutionPath` (string): Path to solution file (.sln)
- `groupBy` (string, optional): Group by: project, namespace, type (default: project)

---

### 13. AnalyzeCodeComplexity
**Description**: Analyze code complexity and identify high-complexity methods

**Parameters**:
- `solutionPath` (string): Path to solution file (.sln)
- `threshold` (int, optional): Complexity threshold (1-10) (default: 5)

---

### 14. FindUnusedCode
**Description**: Find unused code (dead code) in the solution - types, methods, properties, and fields with no references

**Parameters**:
- `solutionPath` (string): Path to solution file (.sln)
- `format` (string, optional): Output format: summary, normal, detailed (default: normal)
- `scope` (string, optional): Scope: private, internal, public, all (default: all)
- `includeTests` (bool, optional): Include test projects in analysis (default: false)

---

### 15. FindDuplicateCode
**Description**: Find duplicate code blocks across the solution

**Parameters**:
- `solutionPath` (string): Path to solution file (.sln)
- `format` (string, optional): Output format: summary, normal, detailed (default: normal)
- `minLines` (int, optional): Minimum lines to consider duplicate (default: 5)
- `similarity` (int, optional): Similarity threshold percentage 70-100 (default: 90)

---

### 16. AnalyzeDocumentationCoverage
**Description**: Analyze XML documentation coverage for types and members

**Parameters**:
- `solutionPath` (string): Path to solution file (.sln)
- `format` (string, optional): Output format: summary, normal, detailed (default: normal)
- `scope` (string, optional): Scope filter: public, all (default: public)

---

## 🔒 Security & Dependencies

### 17. FindSecurityIssues
**Description**: Find security issues and anti-patterns in the solution (SQL injection, hardcoded secrets, weak crypto, etc.)

**Parameters**:
- `solutionPath` (string): Path to solution file (.sln)
- `format` (string, optional): Output format: summary, normal, detailed (default: normal)
- `categories` (string, optional): Categories to check (comma-separated): sql-injection, secrets, crypto, path-traversal, deserialization, all (default: all)
- `severity` (string, optional): Severity filter: critical, high, medium, low, all (default: all)

**Detection notes**: Analysis is semantic rather than keyword-based, to keep false positives low:
- **SQL injection** is reported only when a string's static shape looks like SQL *and* a non-constant (runtime) value is spliced in — constant-only queries and prose such as `"... was updated"` are not flagged.
- **Weak crypto / insecure deserialization** are matched by fully-qualified type name (including base types, so derived providers are caught) and de-duplicated per line — a substring like `DES` will not match an unrelated type such as `ResultDescriptor`.

---

### 18. FindUnusedDependencies
**Description**: Find unused dependencies (NuGet packages and project references) in the solution

**Parameters**:
- `solutionPath` (string): Path to solution file (.sln)
- `format` (string, optional): Output format: summary, normal, detailed (default: normal)
- `includeNuGetPackages` (bool, optional): Include NuGet package analysis (default: true)
- `includeProjectReferences` (bool, optional): Include project reference analysis (default: true)

---

### 19. AnalyzePackages
**Description**: Comprehensive NuGet package analysis including version management, update detection, security audits, and usage tracking

**Parameters**:
- `solutionPath` (string): Path to solution file (.sln)
- `format` (string, optional): Output format: summary (key metrics), normal (balanced), detailed (comprehensive). Default: normal
- `checkUpdates` (bool, optional): Check for available package updates (default: true)
- `checkVulnerabilities` (bool, optional): Check for security vulnerabilities (default: true)
- `analyzeUsage` (bool, optional): Analyze package usage to detect unused packages (default: true)

**Example Usage**:
```
Use AnalyzePackages tool with:
- solutionPath: "D:\MyProject\MyProject.sln"
- format: "normal"
- checkUpdates: true
```

**Features**:
- Lists all NuGet packages across all projects
- Detects outdated packages and available updates
- Identifies version conflicts between projects
- Finds unused packages (packages with no namespace usage)
- Checks for security vulnerabilities (placeholder - requires external API)
- Recommends version standardization

---

### 20. AnalyzeDependencies
**Description**: Analyze project dependencies and symbol usage patterns

**Parameters**:
- `solutionPath` (string): Path to solution file (.sln)
- `maxDepth` (int, optional): Maximum depth for dependency analysis (default: 3)

---

### 21. GetDependencyGraph
**Description**: Get project dependency graph in various formats

**Parameters**:
- `solutionPath` (string): Path to solution file (.sln)
- `format` (string, optional): Output format: text, dot, mermaid (default: text)
- `includePackages` (bool, optional): Include package dependencies (default: false)

---

## 🔧 Development Tools

### 22. GetCallHierarchy
**Description**: Get call hierarchy showing callers and callees for a method

**Parameters**:
- `solutionPath` (string): Path to solution file (.sln)
- `methodName` (string): Method name to analyze
- `direction` (string, optional): Direction: both, callers, callees (default: both)
- `maxDepth` (int, optional): Maximum depth for hierarchy traversal (default: 3)

---

### 23. GetCompilationErrors
**Description**: Get compilation errors and warnings from solution to quickly identify build issues without running full build

**Parameters**:
- `solutionPath` (string): Path to solution file (.sln)
- `mode` (string, optional): Output mode: compact, normal, detailed (default: normal)
- `severity` (string, optional): Severity filter: Error, Warning, Info, or All (default: All)
- `projectFilter` (string, optional): Filter by project name pattern (supports wildcards)
- `errorCodes` (string[], optional): Filter by specific error codes (e.g., CS0103, CS0246)

---

### 24. FindTestsForType
**Description**: Find test classes and methods for a given type

**Parameters**:
- `typeName` (string): Type name to find tests for
- `solutionPath` (string): Path to solution file (.sln)
- `includePartialMatches` (bool, optional): Include partial name matches (default: true)

---

### 25. GetTestCoverage
**Description**: Comprehensive test coverage analysis - identify untested code, calculate coverage percentages, and assess high-risk areas

**Parameters**:
- `solutionPath` (string): Path to solution file (.sln)
- `format` (string, optional): Output format: summary (key metrics), normal (balanced), detailed (comprehensive). Default: normal
- `scope` (string, optional): Scope: public (only public types), all (all types). Default: public
- `groupBy` (string, optional): Group by: project, namespace. Default: project

**Example Usage**:
```
Use GetTestCoverage tool with:
- solutionPath: "D:\MyProject\MyProject.sln"
- format: "normal"
- groupBy: "project"
```

**Features**:
- Analyzes test coverage for all types in non-test projects
- Calculates type-level and member-level coverage percentages
- Identifies uncovered types and methods
- Assesses risk levels based on complexity and test coverage:
  - **Critical Risk**: High complexity, no tests
  - **High Risk**: Medium complexity, no tests
  - **Medium Risk**: Low complexity without tests or high complexity with partial tests
  - **Low Risk**: Well-tested code
- Groups coverage statistics by project or namespace
- Identifies high-risk areas requiring immediate testing attention
- Calculates cyclomatic complexity for each type and method

**Output Information**:
- Overall type coverage percentage
- Overall member coverage percentage
- Coverage breakdown by project/namespace
- List of high-risk uncovered types
- Detailed member coverage for critical types
- Risk analysis summary

---

### 26. GetChangeImpact
**Description**: Analyze the impact of changing a symbol - identify all dependent code, assess risk level, and get actionable recommendations before refactoring

**Parameters**:
- `symbolName` (string): Symbol name to analyze (class, method, property, etc.)
- `solutionPath` (string): Path to solution file (.sln)
- `format` (string, optional): Output format: summary (key metrics), normal (balanced), detailed (comprehensive). Default: normal
- `maxDepth` (int, optional): Maximum depth for indirect dependency analysis (default: 3)
- `includeIndirectReferences` (bool, optional): Include indirect references (default: true)

**Example Usage**:
```
Use GetChangeImpact tool with:
- symbolName: "UserService"
- solutionPath: "D:\MyProject\MyProject.sln"
- format: "normal"
- maxDepth: 3
```

**Features**:
- Identifies all code that references the target symbol
- Distinguishes between direct and indirect dependencies
- Builds dependency chains showing how changes propagate
- Calculates impact radius (files, projects affected)
- Assesses risk level (Critical/High/Medium/Low) based on:
  - Public API exposure
  - Number of references
  - Cross-project dependencies
  - Interface/abstract class changes
- Detects breaking changes automatically
- Provides actionable recommendations for safe refactoring

**Risk Assessment Criteria**:
- **Critical**: Public API with 20+ references
- **High**: 50+ references or 5+ projects impacted
- **Medium**: 10+ references or 2+ projects impacted
- **Low**: Limited impact, internal use only

**Output Information**:
- Target symbol details (name, kind, accessibility, location)
- Impact statistics (direct/indirect references, impacted projects/files)
- Impact breakdown by project
- Dependency chains (showing propagation paths)
- Risk level with detailed reasoning
- Breaking change detection and reasons
- Specific recommendations based on impact analysis
- Code locations for all impacted symbols

**Recommendations Include**:
- Versioning and deprecation strategies for public APIs
- Migration guide suggestions for high-impact changes
- Team coordination for cross-project changes
- Testing strategies for breaking changes
- Alternative approaches (extension methods, default implementations)

---

## 🚀 Utility Tools

### 27. FindTODOComments
**Description**: Find all TODO, FIXME, HACK, and NOTE comments across the solution

**Parameters**:
- `solutionPath` (string): Path to solution file (.sln)
- `format` (string, optional): Output format: summary, normal, detailed (default: normal)
- `commentTypes` (string, optional): Comment types to find (comma-separated): TODO, FIXME, HACK, NOTE, BUG, XXX (default: all)
- `includeFilePath` (bool, optional): Include file paths in results (default: true)

---

### 28. FindLargeFiles
**Description**: Find large source files that may need refactoring

**Parameters**:
- `solutionPath` (string): Path to solution file (.sln)
- `format` (string, optional): Output format: summary, normal, detailed (default: normal)
- `minLines` (int, optional): Minimum lines to consider large (default: 500)
- `includeMetrics` (bool, optional): Include code metrics for large files (default: true)

---

### 29. FindDeprecatedAPIs
**Description**: Find usages of deprecated/obsolete APIs (both internal and .NET framework)

**Parameters**:
- `solutionPath` (string): Path to solution file (.sln)
- `format` (string, optional): Output format: summary, normal, detailed (default: normal)
- `includeFrameworkAPIs` (bool, optional): Include .NET framework obsolete APIs (default: true)
- `groupByAPI` (bool, optional): Group results by API instead of location (default: true)

---

### 30. GetFileStatistics
**Description**: Get detailed statistics for a specific C# source file

**Parameters**:
- `filePath` (string): Path to C# source file (.cs)
- `includeComplexity` (bool, optional): Include complexity metrics (default: true)
- `includeTypeInfo` (bool, optional): Include type and member counts (default: true)

---

### 31. BatchQuery
**Description**: Execute multiple queries in a single batch request

**Parameters**:
- `queriesJson` (string): JSON array of query specifications. Each query should have 'tool' (tool name) and 'parameters' (dict of parameters)
- `parallel` (bool, optional): Execute queries in parallel (default: true)

**Example queriesJson**:
```json
[
  {
    "tool": "SearchSymbols",
    "parameters": {
      "solutionPath": "D:\\MyProject\\MyProject.sln",
      "pattern": "User*"
    }
  },
  {
    "tool": "GetCodeMetrics",
    "parameters": {
      "solutionPath": "D:\\MyProject\\MyProject.sln"
    }
  }
]
```

### 32. FindPerformanceIssues
**Description**: Find common performance anti-patterns and issues in C# code - detects LINQ misuse, string concatenation in loops, sync-over-async patterns, IDisposable not disposed, and exception handling anti-patterns

**Parameters**:
- `solutionPath` (string): Path to solution file (.sln)
- `format` (string, optional): Output format: summary (key metrics), normal (balanced), detailed (comprehensive). Default: normal
- `issueTypes` (string, optional): Comma-separated issue types to check: LinqMisuse, StringConcatenation, SyncOverAsync, DisposableNotDisposed, ExceptionHandling. Default: all

**Example Usage**:
```
Use FindPerformanceIssues tool with:
- solutionPath: "D:\MyProject\MyProject.sln"
- format: "normal"
- issueTypes: "LinqMisuse,SyncOverAsync"
```

**Features**:
- Detects LINQ misuse patterns (Count() vs Any(), multiple ToList() calls, unnecessary materialization)
- Identifies string concatenation in loops (recommends StringBuilder)
- Finds sync-over-async anti-patterns (.Result, .Wait() in async methods)
- Detects IDisposable objects not properly disposed
- Identifies exception handling anti-patterns (empty catch blocks, catching base Exception)
- Provides severity levels (Critical, High, Medium, Low)
- Estimates performance impact (0-10 scale)
- Includes fix recommendations and code examples
- Groups issues by type, project, file, and severity
- Shows line numbers and code context

**Issue Types**:
- **LinqMisuse**: Inefficient LINQ patterns that enumerate collections unnecessarily
- **StringConcatenation**: String += in loops creating many intermediate objects
- **SyncOverAsync**: Blocking calls (.Result, .Wait) in async methods causing deadlocks
- **DisposableNotDisposed**: IDisposable objects without using statements causing resource leaks
- **ExceptionHandling**: Empty catch blocks and improper exception handling hiding bugs

**Output Formats**:
- **summary**: Key metrics, issue counts by severity, top issue types
- **normal**: Statistics, issues by type/project, top 10 critical and high severity issues
- **detailed**: Complete analysis with all issues grouped by severity and type, file statistics, recommendations

### 33. AnalyzeNamingConventions
**Description**: Analyze C# naming convention compliance and detect violations - checks interfaces, types, methods, properties, fields, parameters, and type parameters against C# naming standards

**Parameters**:
- `solutionPath` (string): Path to solution file (.sln)
- `format` (string, optional): Output format: summary (key metrics), normal (balanced), detailed (comprehensive). Default: normal
- `violationTypes` (string, optional): Comma-separated violation types to check: InterfaceNaming, TypeNaming, MethodNaming, PropertyNaming, FieldNaming, ParameterNaming, TypeParameterNaming. Default: all
- `scope` (string, optional): Analysis scope: all, public, internal. Default: all

**Example Usage**:
```
Use AnalyzeNamingConventions tool with:
- solutionPath: "D:\MyProject\MyProject.sln"
- format: "normal"
- violationTypes: "InterfaceNaming,FieldNaming"
- scope: "public"
```

**Features**:
- Checks interface naming (should start with 'I' followed by PascalCase)
- Validates type naming (PascalCase for classes, structs, enums, delegates)
- Verifies method naming (PascalCase)
- Checks property naming (PascalCase)
- Validates field naming (private/protected: _camelCase, public: PascalCase, constants: PascalCase or UPPER_CASE)
- Checks parameter naming (camelCase)
- Validates type parameter naming (TPascalCase - starts with 'T')
- Provides suggested names for violations
- Severity classification (High, Medium, Low)
- Calculates compliance score (percentage of symbols following conventions)
- Groups violations by type, symbol kind, project, and file
- Scope filtering (all/public/internal symbols)

**Violation Types**:
- **InterfaceNaming**: Interfaces not starting with 'I' (e.g., IUserService)
- **TypeNaming**: Types not using PascalCase (e.g., UserService, OrderProcessor)
- **MethodNaming**: Methods not using PascalCase (e.g., GetUser, ProcessOrder)
- **PropertyNaming**: Properties not using PascalCase (e.g., UserName, OrderDate)
- **PrivateFieldNaming**: Private/protected fields not using _camelCase (e.g., _userName, _orderDate)
- **PublicFieldNaming**: Public fields not using PascalCase
- **ConstantNaming**: Constants not using PascalCase or UPPER_CASE
- **ParameterNaming**: Parameters not using camelCase (e.g., userName, orderDate)
- **TypeParameterNaming**: Type parameters not using TPascalCase (e.g., TKey, TValue, TEntity)

**Output Formats**:
- **summary**: Key metrics, violation counts by severity, compliance score, top violation types
- **normal**: Statistics, violations by type/symbol kind, top 10 high and medium severity violations with suggestions
- **detailed**: Complete analysis with all violations grouped by severity and type, convention guidelines, file statistics

### 34. AnalyzeAPIChanges
**Description**: Analyze API changes between two versions of a solution - detect breaking changes, additions, removals, and get semantic versioning recommendations for proper version management

**Parameters**:
- `oldSolutionPath` (string): Path to old version solution file (.sln)
- `newSolutionPath` (string): Path to new version solution file (.sln)
- `format` (string, optional): Output format: summary (key metrics), normal (balanced), detailed (comprehensive). Default: normal
- `oldVersionLabel` (string, optional): Label for old version (e.g., 'v1.0.0', 'main'). Default: 'Old'
- `newVersionLabel` (string, optional): Label for new version (e.g., 'v2.0.0', 'develop'). Default: 'New'
- `includeInternal` (bool, optional): Include internal API changes (default: false)

**Example Usage**:
```
Use AnalyzeAPIChanges tool with:
- oldSolutionPath: "D:\MyProject\v1.0.0\MyProject.sln"
- newSolutionPath: "D:\MyProject\v2.0.0\MyProject.sln"
- format: "normal"
- oldVersionLabel: "v1.0.0"
- newVersionLabel: "v2.0.0"
- includeInternal: false
```

**Features**:
- Detects added symbols (new APIs)
- Identifies removed symbols (breaking changes)
- Tracks method signature changes (parameters, return types)
- Monitors accessibility changes (public/internal/protected/private)
- Detects type modifier changes (abstract, sealed)
- Tracks base type changes in inheritance hierarchies
- Monitors property type changes
- Classifies changes by impact level (Breaking/NonBreaking/Internal)
- Assigns severity levels (Critical/High/Medium/Low)
- Provides migration guidance for each change
- Calculates semantic versioning recommendations (Major/Minor/Patch)
- Groups changes by type, symbol kind, and namespace
- Identifies affected areas for each change
- Compares public API surface between versions
- Optional internal API comparison

**Change Types Detected**:
- **Added**: New symbols introduced in the new version
- **Removed**: Symbols deleted from the old version (breaking)
- **Modified**: General modifications to existing symbols
- **AccessibilityChanged**: Changes in public/internal/private access
- **SignatureChanged**: Method parameter or return type changes (breaking)

**Impact Levels**:
- **Breaking**: Requires major version bump - removes APIs, changes signatures, reduces accessibility
- **NonBreaking**: Requires minor version bump - adds new APIs without breaking existing ones
- **Internal**: Requires patch version bump - internal changes only

**Semantic Versioning Guidance**:
- **Major (X.0.0)**: Breaking changes detected - removed symbols, signature changes, accessibility reductions
- **Minor (x.X.0)**: New symbols added without breaking changes
- **Patch (x.x.X)**: Only internal changes, no public API modifications
- **None**: No API changes detected

**Output Formats**:
- **summary**: Key metrics, breaking/non-breaking counts, semantic versioning recommendation
- **normal**: Statistics, changes by type/symbol kind, top 10 breaking changes and additions with migration guidance
- **detailed**: Complete analysis with all changes grouped by impact level, full migration summary, comprehensive change details

---

## 📝 Tips for Using These Tools

### Best Practices

1. **Start with Structure**: Use `GetProjectStructure` to understand the codebase layout
2. **Search Before Modifying**: Use `SearchSymbols` to find relevant code before making changes
3. **Check Impact**: Use `FindReferences` to understand how changes will affect other code
4. **Quality Checks**: Run `AnalyzeCodeComplexity`, `FindUnusedCode`, and `FindDuplicateCode` regularly
5. **Security Audits**: Use `FindSecurityIssues` to identify potential vulnerabilities
6. **Documentation**: Use `AnalyzeDocumentationCoverage` to improve API documentation

### Performance Tips

1. Use **summary format** for quick overviews (saves tokens)
2. Use **normal format** for balanced information
3. Use **detailed format** only when you need complete information
4. Use `FindReferencesFiltered` with filters to reduce noise
5. Use `BatchQuery` to execute multiple queries efficiently

### Common Workflows

#### Understanding a New Codebase
```
1. GetProjectStructure → Understand overall structure
2. SearchSymbols → Find key classes/interfaces
3. GetClassHierarchy → Understand type relationships
4. GetCodeMetrics → Get quality overview
```

#### Refactoring a Class
```
1. FindReferences → Find all usages
2. GetClassHierarchy → Check inheritance
3. FindImplementations → Find interface implementations
4. FindTestsForType → Locate related tests
```

#### Quality Review
```
1. GetCompilationErrors → Fix build issues
2. AnalyzeCodeComplexity → Find complex methods
3. FindUnusedCode → Remove dead code
4. FindDuplicateCode → Identify refactoring opportunities
5. FindSecurityIssues → Check for vulnerabilities
6. AnalyzeDocumentationCoverage → Improve documentation
```

---

## 🔗 Related Documentation

- [Usage Examples](EXAMPLES.md) - Detailed examples for each tool
- [Testing Guide](TESTING.md) - How to test RoslynCSMCP
- [CLAUDE.md](../CLAUDE.md) - Development guidelines for contributors

---

**Note**: All tools require a valid `.sln` file path. Most tools support multiple output formats (summary/normal/detailed or compact/normal/detailed) to optimize token usage.
