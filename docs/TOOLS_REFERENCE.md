# RoslynCSMCP Tools Reference

This document lists all available MCP tools provided by RoslynCSMCP server for C# code analysis.

> **Last Updated**: 2026-01-12
> **Total Tools**: 29
>
> **Recent Additions**: FindTODOComments, FindLargeFiles, FindDeprecatedAPIs, GetFileStatistics, AnalyzePackages

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

## 🚀 Utility Tools

### 25. FindTODOComments
**Description**: Find all TODO, FIXME, HACK, and NOTE comments across the solution

**Parameters**:
- `solutionPath` (string): Path to solution file (.sln)
- `format` (string, optional): Output format: summary, normal, detailed (default: normal)
- `commentTypes` (string, optional): Comment types to find (comma-separated): TODO, FIXME, HACK, NOTE, BUG, XXX (default: all)
- `includeFilePath` (bool, optional): Include file paths in results (default: true)

---

### 26. FindLargeFiles
**Description**: Find large source files that may need refactoring

**Parameters**:
- `solutionPath` (string): Path to solution file (.sln)
- `format` (string, optional): Output format: summary, normal, detailed (default: normal)
- `minLines` (int, optional): Minimum lines to consider large (default: 500)
- `includeMetrics` (bool, optional): Include code metrics for large files (default: true)

---

### 27. FindDeprecatedAPIs
**Description**: Find usages of deprecated/obsolete APIs (both internal and .NET framework)

**Parameters**:
- `solutionPath` (string): Path to solution file (.sln)
- `format` (string, optional): Output format: summary, normal, detailed (default: normal)
- `includeFrameworkAPIs` (bool, optional): Include .NET framework obsolete APIs (default: true)
- `groupByAPI` (bool, optional): Group results by API instead of location (default: true)

---

### 28. GetFileStatistics
**Description**: Get detailed statistics for a specific C# source file

**Parameters**:
- `filePath` (string): Path to C# source file (.cs)
- `includeComplexity` (bool, optional): Include complexity metrics (default: true)
- `includeTypeInfo` (bool, optional): Include type and member counts (default: true)

---

### 29. BatchQuery
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

- [Usage Examples](PHASE4_USAGE_EXAMPLES.md) - Detailed examples for each tool
- [NEW_TOOLS_PROPOSAL.md](NEW_TOOLS_PROPOSAL.md) - Upcoming features and tools
- [CLAUDE.md](../CLAUDE.md) - Development guidelines for contributors

---

**Note**: All tools require a valid `.sln` file path. Most tools support multiple output formats (summary/normal/detailed or compact/normal/detailed) to optimize token usage.
