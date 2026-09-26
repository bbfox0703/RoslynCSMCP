# RoslynCSMCP Tools Reference

This document details the core MCP tools provided by RoslynCSMCP server for C# code analysis.

> **Last Updated**: 2026-01-12
> **Total Tools**: 51 (34 detailed below; see the [README](../README.md) for the full tool list including the Interop, performance, and modernization tools)
>
> **Recent Additions**: FindTODOComments, FindLargeFiles, FindDeprecatedAPIs, GetFileStatistics, AnalyzePackages, GetTestCoverage, GetChangeImpact, FindPerformanceIssues, AnalyzeNamingConventions, AnalyzeAPIChanges
>
> **Solution paths**: every `solutionPath` must be an existing absolute path to a `.sln` or `.slnx` file; project files (`.csproj`) are rejected. When projects fail to load or analysis fails partway, tools append a `⚠️ Warnings` section instead of reporting a clean result.

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
**Description**: Find source references to the symbols declared in the solution that match a name, plus their declaration sites. One entry per file line. A name that matches no declared symbol is reported as not found (a symbol-not-found error in the Navigation module); a declared symbol with no references reports "No references found".

**Parameters**:
- `symbolName` (string): Simple (`Save`) or qualified (`UserService.Save`, `MyApp.Services.UserService.Save`) name; generic arguments and parameter lists are ignored. Only symbols declared in the solution's source match (never framework or package members); exact-case matches are preferred, otherwise case is ignored. A simple name combines every match (overloads, same-named members of different types).
- `solutionPath` (string): Path to solution file (.sln)
- `detailLevel` (string, optional): Detail level: summary (file stats only), locations (with code lines), full (with 5-line context). Default: locations
- `includeDefinition` (bool, optional): Also return each matching symbol's declaration sites (every part of a partial declaration), marked as definitions (default: true)

**Example Usage**:
```
Use FindReferences tool with:
- symbolName: "UserService"
- solutionPath: "D:\MyProject\MyProject.sln"
- detailLevel: "full"
```

---

### 3. FindReferencesAcrossSolutions
**Description**: Run the FindReferences search in each listed solution and merge the results into one entry per file line (a line in a file shared by several solutions appears once, attributed to the first solution listed). The Advanced module groups its output by solution; the Full build groups by file.

**Parameters**:
- `symbolName` (string): Simple (`Save`) or qualified (`UserService.Save`, `MyApp.Services.UserService.Save`) name; generic arguments and parameter lists are ignored. Only symbols declared in the solution's source match (never framework or package members); exact-case matches are preferred, otherwise case is ignored. A simple name combines every match (overloads, same-named members of different types).
- `solutionPaths` (string): Comma-separated list of solution file paths (.sln)
- `detailLevel` (string, optional, Full build only): Detail level: summary, locations, full (default: locations)
- `includeDefinition` (bool, optional): Also return each matching symbol's declaration sites (every part of a partial declaration), marked as definitions (default: true)

---

### 4. FindReferencesFiltered
**Description**: Find references the way FindReferences does, then narrow them. Filters are applied to each reference before lines are merged, so a line that both reads and writes the symbol counts as a write. Declaration sites are subject to `projectFilter`, `excludeTests`, and `publicOnly`, and are dropped by `crossProjectOnly` and `writesOnly`.

**Parameters**:
- `symbolName` (string): Simple (`Save`) or qualified (`UserService.Save`, `MyApp.Services.UserService.Save`) name; generic arguments and parameter lists are ignored. Only symbols declared in the solution's source match (never framework or package members); exact-case matches are preferred, otherwise case is ignored. A simple name combines every match (overloads, same-named members of different types).
- `solutionPath` (string): Path to solution file (.sln)
- `detailLevel` (string, optional): Detail level: summary, locations, full (default: locations)
- `includeDefinition` (bool, optional): Also return each matching symbol's declaration sites (every part of a partial declaration), marked as definitions (default: true)
- `projectFilter` (string, optional): Project name wildcard pattern (* and ?), matched case-insensitively against the whole name
- `excludeTests` (bool, optional): Drop references in projects whose name contains "test" or "spec" (default: false)
- `writesOnly` (bool, optional): Keep only references that write the symbol: assignment or compound-assignment target (including object initializers and deconstruction), `++`/`--` operand, or `out`/`ref` argument. Right-hand-side reads are not writes (default: false)
- `publicOnly` (bool, optional): Keep only locations inside a type or member visible outside its assembly (it and every containing type are public, protected, or protected internal; an accessor's own modifier counts) (default: false)
- `crossProjectOnly` (bool, optional): Keep only references in a project other than the one declaring the referenced symbol, checked per symbol (default: false)

---

### 5. GetSymbolInfo
**Description**: Describe one type or member declared in the solution's source: kind, accessibility, namespace or declaring type, source file and line, and for methods the return type and parameters or for properties the type (which of these appear depends on `detailLevel` and the build). The name is resolved as described in [Resolving a type or member name](#resolving-a-type-or-member-name), accepting any type or member. If several declarations still match (overloads, the same name in different types), nothing is described and the candidates are listed. A name with no source declaration returns "Symbol not found." (also for framework-only names such as `Exception`).

**Parameters**:
- `symbolName` (string): Simple (`Save`), qualified (`UserService.Save`, `MyApp.Services.UserService.Save`), or nested-type (`Outer.Inner`, `Outer+Inner`) name, matched case-insensitively. Type arguments (`Result<T>`, ``Result`1``) select a generic type; a parameter list (`Save(User, bool)`, `Save()`) selects an overload.
- `solutionPath` (string): Path to solution file (.sln)
- `detailLevel` (string, optional): Detail level: summary, basic, full (default: basic)

#### Resolving a type or member name

GetSymbolInfo, FindImplementations, and GetClassHierarchy resolve their name argument the same way:

1. **Source declarations only.** Types and members declared in the solution's projects are matched, including nested types and their members. Types and members of referenced assemblies (the framework, NuGet packages) are never matched, so `Timer` finds your `MyApp.Timer` rather than `System.Threading.Timer`, and `IDisposable` or `Exception` cannot be the target.
2. **Name and qualifier.** The simple name must match, ignoring case. A qualified name must match whole trailing segments of the namespace and containing types, so `Geometry.Circle` matches `Lib.Geometry.Circle` and `App.Geometry.Circle`, but `Service.Save` does not match `UserService.Save`.
3. **Kind, type arguments, parameters.** Only the kinds the tool works on are kept (any type or member for GetSymbolInfo, any type for GetClassHierarchy, interfaces and abstract classes for FindImplementations). Type arguments keep only declarations with that many type parameters; a parameter list keeps only methods and indexers whose parameter types match. Parameter types may be written with keywords or type names, qualified or not (`int`, `Int32`, `System.Int32`); modifiers, parameter names, and default values are ignored.
4. **Preferences.** Exact-case matches win over case-insensitive ones. Without type arguments, a non-generic type wins over generic types of the same name (`Result` means `Result`, not `Result<T>`), as in C#.
5. **Outcome.** One remaining declaration is used. If several remain, the tool lists them (full name, kind, file:line, project) instead of guessing; each listed name can be passed back as is (only declarations with identical full names, such as the same namespace and type declared in two projects, cannot be told apart). If declarations with the name exist but none survives step 3 (for example FindImplementations on a concrete class, or a parameter list that fits no overload), the tool lists those declarations and says why none was used.

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
**Description**: Show the inheritance tree of one type declared in the solution's source: ancestors (base-class chain excluding `System.Object`, plus declared interfaces, recursively, including framework types) and descendants (derived classes, including through constructed generic bases such as `Base<int>`). Each descendant is listed once per direct parent, under the project that declares it. The type is resolved as described in [Resolving a type or member name](#resolving-a-type-or-member-name); a framework type such as `Exception` cannot be the starting point. If several types match, the candidates are listed instead.

**Parameters**:
- `typeName` (string): Simple (`Shape`), qualified (`MyApp.Geometry.Shape`), or nested-type (`Outer.Inner`) type name, matched case-insensitively. Type arguments (`Repository<T>`) select a generic type.
- `solutionPath` (string): Path to solution file (.sln)
- `direction` (string, optional): Direction: ancestors, descendants, or both, case-insensitive; other values return an error (default: both)
- `format` (string, optional): Output format: compact, normal, detailed (default: normal)
- `maxDepth` (int, optional): Maximum depth to traverse (default: 10)

---

### 10. FindImplementations
**Description**: Find the source types that implement an interface (directly, through a base class, or through an inherited interface) or derive from an abstract class at any depth, including through constructed generics (`IRepo<int>` counts for `IRepo<T>`). Each type is listed once, under the project that declares it. The target is resolved as described in [Resolving a type or member name](#resolving-a-type-or-member-name) and must be an interface or abstract class declared in the solution's source, so framework types such as `IDisposable` cannot be the target. If several interfaces or abstract classes match, the candidates are listed instead. An unknown name, and a name that only matches other kinds of declaration (such as a concrete class), are reported as such, so "No implementations found" always means the target exists. For subclasses of a concrete class, use GetClassHierarchy.

**Parameters**:
- `typeName` (string): Simple (`IRepository`), qualified (`MyApp.Data.IRepository`), or nested-type name of an interface or abstract class, matched case-insensitively. Type arguments (`IRepository<T>`) select a generic type.
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
- `groupBy` (string, optional): Breakdowns to append, comma-separated: project, namespace, type, or none (default: project)

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
**Description**: Find methods whose bodies are copies or near-copies across the solution

**Parameters**:
- `solutionPath` (string): Path to solution file (.sln)
- `format` (string, optional): Output format: summary, normal, detailed (default: normal)
- `minLines` (int, optional): Minimum line span of a method declaration to compare; values below 3 become 5 (default: 5)
- `similarity` (int, optional): Minimum body similarity percentage 70-100; 100 finds only exact copies after normalization (default: 90)

**Detection notes**:
- Only method **bodies** are compared, as token sequences: comments, whitespace, literal values, and names declared inside the method (parameters, locals, loop/catch/lambda variables) are normalized away, so a copy with a different method name, signature, or local names still matches. Called methods, members, and types must agree.
- Similarity is `2 × LCS / (tokens of both bodies)`. Each group collects the methods at or above the threshold around its largest member and reports the lowest similarity in the group.
- Files compiled into several projects (multi-targeting, linked files) are analyzed once. Duplicated fragments inside otherwise different methods are not detected.

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

**Detection notes** (the package rule is shared with `AnalyzePackages`):
- A package counts as used when a `using` directive — global usings included, such as `global using global::X;` generated from csproj `<Using>` items — imports the package ID, the ID minus its last segment (for IDs with three or more segments), or a sub-namespace of either.
- Packages without compile assets (`IncludeAssets` without `compile`, or `ExcludeAssets` with `compile`) and known build, test, and analyzer packages are never flagged. Packages whose namespaces differ from their IDs can still be false positives.
- A project reference counts as used when any identifier binds to a symbol from that project in any target framework.

---

### 19. AnalyzePackages
**Description**: Comprehensive NuGet package analysis including version management, update detection, security audits, and usage tracking

**Parameters**:
- `solutionPath` (string): Path to solution file (.sln)
- `format` (string, optional): Output format: summary (key metrics), normal (balanced), detailed (comprehensive). Default: normal
- `checkUpdates` (bool, optional): Check for available package updates (default: true)
- `checkVulnerabilities` (bool, optional): Report known advisories, direct and transitive, via `dotnet list package --vulnerable` (network access; the solution must already be restored) (default: true)
- `analyzeUsage` (bool, optional): Analyze package usage to detect unused packages (default: true)
- `checkConflicts` (bool, optional, split Dependencies module only): Report packages referenced at different versions across projects (default: true)

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
- Finds unused packages with the same heuristic as `FindUnusedDependencies`
- Checks for known vulnerabilities by running `dotnet list <solution> package --vulnerable --include-transitive --format json --no-restore`; problems (for example an unrestored project) are reported as warnings
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
**Description**: Get call hierarchy showing callers and callees for a method, as trees up to `maxDepth` levels (callers of callers, callees of callees). Each method is expanded once; repeats and recursive calls are marked, and each direction stops after 200 entries.

**Parameters**:
- `solutionPath` (string): Path to solution file (.sln)
- `methodName` (string): Method name to analyze
- `direction` (string, optional): Direction: both, callers, callees, case-insensitive; other values return an error (default: both)
- `maxDepth` (int, optional): Number of levels to follow, 1–10; 1 lists only direct callers and callees (default: 3)

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
- `maxDepth` (int, optional): Number of reference levels to follow, counting direct references as level 1 (default: 3)
- `includeIndirectReferences` (bool, optional): Follow references to the members that contain each reference (for example, callers of the callers), up to `maxDepth` levels (default: true)

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
**Description**: Find TODO, FIXME, HACK, NOTE, BUG, XXX, OPTIMIZE, and REFACTOR markers in comments across the solution

**Parameters**:
- `solutionPath` (string): Path to solution file (.sln)
- `format` (string, optional): Output format: summary, normal, detailed (default: normal)
- `types` (string, optional): Marker types to find (comma-separated): TODO, FIXME, HACK, NOTE, BUG, XXX, OPTIMIZE, REFACTOR (default: all)

**Detection notes**: Markers must be whole words (`debug` is not BUG, `notes` is not NOTE). A marker in any case is accepted at the start of a comment line; elsewhere only upper case counts, so prose such as "works around a bug" is ignored. Each comment line yields at most one marker, the first one of a requested type.

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
**Description**: Execute multiple read-only queries in a single batch request. Supports only `SearchSymbols` (`solutionPath`, `searchPattern`, `symbolKind`, `ignoreCase`), `FindReferences` (`solutionPath`, `symbolName`, `includeDefinition`), `GetSymbolInfo` (`solutionPath`, `symbolName`), `GetCodeMetrics` (`solutionPath`, `groupBy`), `GetDependencyGraph` (`solutionPath`, `format`, `includePackages`), `GetCallHierarchy` (`solutionPath`, `methodName`, `direction`, `maxDepth`), and `AnalyzeDependencies` (`solutionPath`, `maxDepth`). Other tool names fail for that query only.

**Parameters**:
- `queriesJson` (string): JSON array of objects, each with a `"tool"` name and a `"parameters"` object. Tool names are matched case-insensitively against the PascalCase names above; snake_case names are not recognized.
- `parallel` (bool, optional): Execute queries in parallel (default: true; full version only)

**Example queriesJson**:
```json
[
  {
    "tool": "SearchSymbols",
    "parameters": {
      "solutionPath": "D:\\MyProject\\MyProject.sln",
      "searchPattern": "User*"
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
- Detects IDisposable objects the code creates and then loses (locals and owned fields)
- Identifies empty catch blocks (each reported once)
- Provides severity levels (Critical, High, Medium, Low)
- Estimates performance impact (0-10 scale)
- Includes fix recommendations and code examples
- Groups issues by type, project, file, and severity
- Shows line numbers and code context

**Issue Types**:
- **LinqMisuse**: Inefficient LINQ patterns that enumerate collections unnecessarily
- **StringConcatenation**: `s += x` or `s = s + x` on a string (checked by type) inside a loop, reported once per assignment; strings declared inside the loop are skipped
- **SyncOverAsync**: Blocking calls (.Result, .Wait) in async methods causing deadlocks
- **DisposableNotDisposed**: Locals created with `new`, a static factory, or a Create/Open/Begin call that are never disposed, returned, stored, or passed on (using declarations are fine), and instance fields a type creates but never disposes
- **ExceptionHandling**: Empty catch blocks hiding bugs

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
- `includeInternal` (bool, optional): Also compare internal symbols; changes invisible outside the assembly are classified as Internal and call for at most a Patch bump (default: false). Public and protected members are always compared, and symbols are matched by documentation comment ID, so each overload is compared separately.

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
