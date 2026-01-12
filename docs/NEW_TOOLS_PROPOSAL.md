# New Tool Opportunities for RoslynCSMCP

**Date**: 2026-01-12
**Current Tools**: 32

---

## 🆕 Latest Recommendations (2026-01-12)

### Priority Tool: AnalyzePackages
Based on the current tool coverage, the next high-value tool to implement is **AnalyzePackages** for NuGet package management analysis.

### Why AnalyzePackages?
1. **Gap in Coverage**: Current tools don't cover NuGet package management
2. **Practical Value**: Security vulnerability detection, version management
3. **Easy Implementation**: Parse .csproj files and use NuGet API
4. **DevOps Integration**: Useful for CI/CD pipelines
5. **Dependency Management**: Complements existing dependency analysis tools

### AnalyzePackages Tool Specification

**Description**: Comprehensive NuGet package analysis including version management, security audits, and usage tracking

**Parameters**:
```csharp
string solutionPath              // Solution file path
string format = "normal"         // summary | normal | detailed
bool checkUpdates = true         // Check for available updates
bool checkVulnerabilities = true // Check for security vulnerabilities
bool analyzeUsage = true         // Analyze if packages are actually used
```

**Output Example**:
```
Package Analysis: MySolution.sln

📦 Package Summary (25 packages across 5 projects):

Security Vulnerabilities (2 CRITICAL):
  ❌ Newtonsoft.Json 12.0.3 (MyProject.Api)
     → CVE-2024-1234: Deserialization vulnerability
     → Recommended: Update to 13.0.3+

  ⚠️ System.Text.Json 6.0.0 (MyProject.Core)
     → CVE-2024-5678: DoS vulnerability
     → Recommended: Update to 6.0.7+

Outdated Packages (8):
  MyProject.Api:
    - AutoMapper 10.1.1 → 12.0.1 available (2 major versions behind)
    - Serilog 2.10.0 → 3.1.1 available

Version Conflicts (1):
  Newtonsoft.Json:
    - MyProject.Api: 12.0.3
    - MyProject.Core: 13.0.1
    → Recommendation: Standardize to 13.0.3

Unused Packages (3):
  MyProject.Api:
    - Serilog.Sinks.Email (2.4.0) - No usings found

Up to Date (12 packages):
  ✓ Microsoft.EntityFrameworkCore 7.0.14
  ✓ Serilog.Sinks.Console 4.1.0
  ...
```

**Implementation Approach**:
1. Parse .csproj files to extract PackageReference elements
2. Use NuGet.Protocol APIs to check for updates
3. Query vulnerability databases (NuGet Audit API)
4. Analyze using statements to detect unused packages
5. Detect version conflicts across projects

**Estimated Effort**: 2-3 days

---

### Additional High-Value Tool Recommendations

Based on the current tool coverage analysis, here are other recommended tools in priority order:

#### 1. GetTestCoverage (High Priority) ✅ IMPLEMENTED
- **Why**: Current `FindTestsForType` only finds tests, but doesn't analyze coverage
- **Value**: Identifies untested code, especially high-risk areas (complex methods without tests)
- **Effort**: 2-3 days
- **Status**: Completed 2026-01-12

#### 2. GetChangeImpact (High Priority) ✅ IMPLEMENTED
- **Why**: Risk assessment before refactoring
- **Value**: Shows impact radius of changes (what breaks if you modify this symbol)
- **Effort**: 3-4 days
- **Status**: Completed 2026-01-12

#### 3. FindPerformanceIssues (Medium Priority) ✅ IMPLEMENTED
- **Why**: Detect common performance anti-patterns
- **Value**: LINQ misuse, string concatenation in loops, boxing issues, sync-over-async
- **Effort**: 2-3 days
- **Status**: Completed 2026-01-12

#### 4. AnalyzeNamingConventions (Medium Priority)
- **Why**: Code consistency enforcement
- **Value**: Automated code review for naming standards
- **Effort**: 1-2 days

#### 5. AnalyzeAPIChanges (Medium Priority)
- **Why**: Track breaking changes between versions
- **Value**: Semantic versioning guidance, migration planning
- **Effort**: 3-4 days

---

## 🎉 Implementation Status

**✅ Implemented**:

1. **FindUnusedCode** (2026-01-11)
   - Dead code detection with 3 format modes (summary/normal/detailed)
   - Scope filtering (private/internal/public/all)
   - Token optimization: 50-70% savings
   - Documentation: See PHASE4_USAGE_EXAMPLES.md

2. **FindUnusedDependencies** (2026-01-11)
   - NuGet package and project reference analysis
   - 3 format modes (summary/normal/detailed)
   - Intelligent namespace mapping for common packages
   - Token optimization: 50-70% savings

3. **FindSecurityIssues** (2026-01-11)
   - Security vulnerability detection and anti-pattern analysis
   - 5 categories: SQL injection, hardcoded secrets, weak crypto, path traversal, insecure deserialization
   - Severity filtering: Critical, High, Medium, Low
   - 3 format modes (summary/normal/detailed)
   - Token optimization: 40-60% savings

4. **FindDuplicateCode** (2026-01-11)
   - Code clone detection using syntax tree analysis
   - SHA256 hashing for exact duplicate detection
   - Configurable minimum line count and similarity threshold
   - 3 format modes (summary/normal/detailed)
   - Token optimization: 30-60% savings

5. **AnalyzeDocumentationCoverage** (2026-01-11)
   - XML documentation coverage analysis
   - Smart documentation suggestions based on naming conventions
   - Scope filtering (public/all)
   - 3 format modes (summary/normal/detailed)
   - Detailed view includes suggested XML documentation for each undocumented symbol
   - Token optimization: 40-60% savings

6. **AnalyzePackages** (2026-01-12)
   - NuGet package analysis and management
   - Check for package updates (with version gap analysis)
   - Detect version conflicts across projects
   - Identify unused packages (via namespace usage analysis)
   - Placeholder for security vulnerability checking
   - 3 format modes (summary/normal/detailed)
   - Token optimization: 40-70% savings

7. **GetTestCoverage** (2026-01-12)
   - Comprehensive test coverage analysis
   - Type-level and member-level coverage percentages
   - Risk assessment (Critical/High/Medium/Low based on complexity and coverage)
   - Groups by project or namespace
   - Identifies high-risk uncovered code
   - Calculates cyclomatic complexity
   - 3 format modes (summary/normal/detailed)
   - Token optimization: 40-70% savings

8. **GetChangeImpact** (2026-01-12)
   - Change impact analysis for symbols
   - Direct and indirect reference tracking
   - Dependency chain visualization
   - Risk assessment (Critical/High/Medium/Low)
   - Breaking change detection
   - Public API impact analysis
   - Cross-project dependency tracking
   - Actionable refactoring recommendations
   - 3 format modes (summary/normal/detailed)
   - Token optimization: 40-70% savings

9. **FindPerformanceIssues** (2026-01-12)
   - Performance anti-pattern detection
   - 5 issue types: LINQ misuse, string concatenation in loops, sync-over-async, IDisposable not disposed, exception handling
   - LINQ pattern analysis (Count() vs Any(), multiple ToList(), unnecessary materialization)
   - Sync-over-async detection (.Result, .Wait() in async methods)
   - Resource leak detection (IDisposable without using statements)
   - Exception handling anti-patterns (empty catch blocks)
   - Severity classification (Critical/High/Medium/Low)
   - Performance impact estimation (0-10 scale)
   - Fix recommendations with code examples
   - Filterable by issue type
   - Groups by type, project, file, and severity
   - 3 format modes (summary/normal/detailed)
   - Token optimization: 40-70% savings

---

## 📊 Current Tool Coverage

### ✅ Well Covered Areas

| Category | Tools | Coverage |
|----------|-------|----------|
| **Symbol Search** | SearchSymbols, FindReferences, FindReferencesFiltered, FindReferencesAcrossSolutions | ⭐⭐⭐⭐⭐ Excellent |
| **Code Structure** | GetProjectStructure, GetFileOutline, GetClassHierarchy, GetTypeSignature | ⭐⭐⭐⭐⭐ Excellent |
| **Analysis** | GetCodeMetrics, AnalyzeCodeComplexity, AnalyzeDependencies, GetCompilationErrors | ⭐⭐⭐⭐ Good |
| **Special Queries** | FindImplementations, FindTestsForType, FindAttributeUsages, GetCallHierarchy | ⭐⭐⭐⭐ Good |
| **Utilities** | GetDependencyGraph, BatchQuery, GetSymbolInfo | ⭐⭐⭐⭐ Good |

---

## 🎯 High-Priority New Tool Opportunities

### 1. 🔍 FindUnusedCode (Dead Code Detection)

**Priority**: ⭐⭐⭐⭐⭐ **VERY HIGH**

**Description**: Find unused types, methods, properties, and fields in the solution

**Use Cases**:
- Code cleanup and refactoring
- Identifying technical debt
- Reducing codebase size
- Finding dead code after refactoring

**Parameters**:
```csharp
string solutionPath              // Solution file path
string scope = "all"             // all | private | internal | public
bool includeTests = false        // Include test projects
string format = "normal"         // summary | normal | detailed
```

**Output Example**:
```
Unused Code Analysis: MySolution.sln

Private Members (78):
  MyProject.Services:
    - UserService.ValidateUserInternal() @ UserService.cs:145
    - UserService._oldCache (field) @ UserService.cs:23
    ... and 76 more

Internal Types (5):
  MyProject.Helpers.OldHelper @ OldHelper.cs:10
  MyProject.Utils.DeprecatedUtil @ DeprecatedUtil.cs:15

Total: 83 unused items (can safely remove)
```

**Implementation Complexity**: Medium (use SymbolFinder.FindReferencesAsync, filter by accessibility)

**Token Savings**: 40-70% with format parameter

---

### 2. 🔄 FindDuplicateCode (Code Clone Detection)

**Priority**: ⭐⭐⭐⭐ **HIGH**

**Description**: Detect duplicate or similar code blocks across the solution

**Use Cases**:
- Identifying refactoring opportunities
- Reducing code duplication
- Finding copy-paste code
- Improving maintainability

**Parameters**:
```csharp
string solutionPath              // Solution file path
int minLines = 5                 // Minimum lines to consider duplicate
int similarity = 90              // Similarity threshold (70-100%)
string scope = "all"             // all | methods | classes
```

**Output Example**:
```
Duplicate Code Detection: MySolution.sln (5+ lines, 90% similarity)

High Similarity (95%+):
  1. UserService.ValidateUser() @ UserService.cs:45-60
     OrderService.ValidateOrder() @ OrderService.cs:78-93
     → 15 lines, 96% similar

  2. DataProcessor.ProcessItems() @ DataProcessor.cs:120-135
     ReportProcessor.ProcessReports() @ ReportProcessor.cs:200-215
     → 16 lines, 94% similar

Total: 12 duplicate code blocks detected
```

**Implementation Complexity**: High (requires syntax tree comparison, hashing)

**Token Savings**: 30-60% with summary mode

---

### 3. 📦 FindUnusedDependencies (NuGet Package Analysis)

**Priority**: ⭐⭐⭐⭐ **HIGH**

**Description**: Find unused NuGet packages and project references

**Use Cases**:
- Cleaning up dependencies
- Reducing build time
- Security audits (removing unused packages)
- Package optimization

**Parameters**:
```csharp
string solutionPath              // Solution file path
bool includeProjectReferences = true
bool includeNuGetPackages = true
string format = "normal"         // summary | normal | detailed
```

**Output Example**:
```
Unused Dependencies: MySolution.sln

Unused NuGet Packages (8):
  MyProject.Api:
    - Newtonsoft.Json (12.0.3) - No usings found
    - Serilog.Sinks.Email (2.4.0) - No usings found

  MyProject.Core:
    - AutoMapper (12.0.1) - No usings found

Unused Project References (2):
  MyProject.Api → MyProject.Legacy (no types used)

Total: 10 unused dependencies (can remove)
```

**Implementation Complexity**: Medium (analyze using statements, check references)

**Token Savings**: 50-70% with summary mode

---

### 4. 🔐 FindSecurityIssues (Basic Security Scanner)

**Priority**: ⭐⭐⭐⭐ **HIGH**

**Description**: Detect common security issues and anti-patterns

**Use Cases**:
- Security audits
- Finding SQL injection vulnerabilities
- Detecting hardcoded secrets
- Identifying insecure patterns

**Parameters**:
```csharp
string solutionPath              // Solution file path
string[] categories = null       // sql-injection | secrets | crypto | all
string severity = "all"          // critical | high | medium | all
```

**Output Example**:
```
Security Issues: MySolution.sln

Critical (3):
  SQL Injection Risk:
    - UserRepository.FindByEmail() @ UserRepository.cs:45
      → String concatenation in SQL query
    - OrderRepository.GetOrders() @ OrderRepository.cs:89
      → String interpolation in SQL query

  Hardcoded Secrets:
    - AppSettings.ConnectionString @ AppSettings.cs:12
      → Hardcoded connection string detected

High (5):
  Weak Cryptography:
    - PasswordHasher.HashPassword() @ PasswordHasher.cs:23
      → Using MD5 (deprecated)

Total: 8 security issues found
```

**Implementation Complexity**: Medium (pattern matching, syntax analysis)

**Token Savings**: 40-60% with summary mode

---

### 5. 📝 GenerateDocumentation (XML Doc Generator)

**Priority**: ⭐⭐⭐ **MEDIUM**

**Description**: Generate XML documentation for undocumented types/methods

**Use Cases**:
- Documentation generation
- Finding undocumented APIs
- Documentation coverage reports
- IntelliSense improvement

**Parameters**:
```csharp
string solutionPath              // Solution file path
string scope = "public"          // public | all
bool includeParameters = true    // Include parameter docs
bool includeReturns = true       // Include return docs
```

**Output Example**:
```
Documentation Coverage: MySolution.sln

Undocumented Public APIs (45):
  MyProject.Services.UserService:
    - GetUserAsync(int userId)
      Suggested: /// <summary>Gets user by ID asynchronously</summary>
                 /// <param name="userId">The user identifier</param>
                 /// <returns>User object or null</returns>

  MyProject.Models.User:
    - Email (property)
      Suggested: /// <summary>Gets or sets the user email address</summary>

Coverage: 65% (145/220 public APIs documented)
```

**Implementation Complexity**: Medium (analyze symbols, generate docs)

**Token Savings**: 30-50% with summary mode

---

### 6. 🔀 FindBranchingComplexity (Decision Point Analysis)

**Priority**: ⭐⭐⭐ **MEDIUM**

**Description**: Analyze branching complexity and decision points

**Use Cases**:
- Code quality assessment
- Finding complex conditional logic
- Refactoring candidates
- Test coverage planning

**Parameters**:
```csharp
string solutionPath              // Solution file path
int threshold = 10               // Decision point threshold
bool includeNestedLoops = true   // Include nested loops
```

**Output Example**:
```
Branching Complexity: MySolution.sln (10+ decision points)

High Complexity Methods (15):
  UserService.ValidateUser() @ UserService.cs:45
    → 23 decision points (8 if/else, 5 switch, 10 && operators)
    → Deeply nested (max depth: 5)

  OrderProcessor.ProcessOrder() @ OrderProcessor.cs:120
    → 18 decision points (12 if/else, 6 null checks)
    → Nested loops (depth: 3)

Total: 15 methods with high branching complexity
```

**Implementation Complexity**: Medium (syntax tree analysis)

**Token Savings**: 40-60% with summary mode

---

### 7. 🔧 FindDeprecatedAPIs (Obsolete Usage Finder)

**Priority**: ⭐⭐⭐ **MEDIUM**

**Description**: Find usages of deprecated/obsolete APIs (already have FindAttributeUsages, but this is specialized)

**Use Cases**:
- Migration planning
- API upgrade preparation
- Technical debt tracking
- Deprecation warnings

**Parameters**:
```csharp
string solutionPath              // Solution file path
bool includeFrameworkAPIs = true // Include .NET framework obsolete APIs
bool groupByAPI = true           // Group by API instead of location
```

**Output Example**:
```
Deprecated API Usages: MySolution.sln

.NET Framework Obsolete APIs (12):
  BinaryFormatter.Serialize()
    → 8 usages across 3 files
    → Deprecated in .NET 5.0, removed in .NET 9.0
    → Suggested: Use System.Text.Json

  WebRequest.Create()
    → 4 usages in HttpHelper.cs
    → Deprecated in .NET 6.0
    → Suggested: Use HttpClient

Internal Obsolete APIs (5):
  [Obsolete] UserService.GetUser()
    → 5 usages (use GetUserAsync instead)

Total: 17 usages of deprecated APIs
```

**Implementation Complexity**: Medium (combine FindAttributeUsages + framework API analysis)

**Token Savings**: 50-70% with summary mode

---

### 8. 🎨 FindNamingViolations (Naming Convention Checker)

**Priority**: ⭐⭐ **LOW-MEDIUM**

**Description**: Check for naming convention violations

**Use Cases**:
- Code style enforcement
- Consistency checking
- Onboarding new developers
- Code review automation

**Parameters**:
```csharp
string solutionPath              // Solution file path
string conventions = "default"   // default | pascalCase | camelCase | custom
string scope = "all"             // classes | methods | fields | all
```

**Output Example**:
```
Naming Violations: MySolution.sln (PascalCase for public, camelCase for private)

Classes (5):
  userService → UserService (PascalCase expected)
  order_processor → OrderProcessor

Methods (12):
  UserService.get_user() → GetUser()
  OrderService.process_Order() → ProcessOrder()

Fields (20):
  UserService.UserCache → _userCache (camelCase + underscore expected for private)

Total: 37 naming violations
```

**Implementation Complexity**: Low-Medium (pattern matching on symbol names)

**Token Savings**: 40-60% with summary mode

---

### 9. 🔗 FindCircularDependencies (Cycle Detection)

**Priority**: ⭐⭐⭐ **MEDIUM**

**Description**: Detect circular dependencies between projects or namespaces

**Use Cases**:
- Architecture validation
- Refactoring planning
- Dependency cleanup
- Build optimization

**Parameters**:
```csharp
string solutionPath              // Solution file path
string level = "project"         // project | namespace | type
bool includeIndirect = true      // Include indirect cycles
```

**Output Example**:
```
Circular Dependencies: MySolution.sln (Project level)

Direct Cycles (2):
  MyProject.Api → MyProject.Services → MyProject.Api
    Via: ApiController uses ServiceFactory, ServiceFactory uses ApiHelper

  MyProject.Data → MyProject.Business → MyProject.Data
    Via: Repository uses BusinessRules, BusinessRules uses DataContext

Indirect Cycles (3):
  MyProject.Core → MyProject.Utils → MyProject.Helpers → MyProject.Core

Total: 5 circular dependency chains detected
```

**Implementation Complexity**: Medium (graph analysis, cycle detection)

**Token Savings**: 30-50% with summary mode

---

### 10. 📈 GetTypeEvolution (Type Change History - Git Integration)

**Priority**: ⭐⭐ **LOW**

**Description**: Show how a type has evolved over git commits

**Use Cases**:
- Understanding code history
- Tracking refactorings
- Documentation
- Code archaeology

**Parameters**:
```csharp
string typeName                  // Type to analyze
string solutionPath              // Solution file path
int maxCommits = 20              // Max commits to analyze
```

**Output Example**:
```
Type Evolution: UserService (last 20 commits)

2026-01-10: Added async methods (3 methods added)
2026-01-05: Removed obsolete methods (2 methods removed)
2025-12-20: Refactored validation logic (4 methods changed)
2025-12-15: Added caching support (1 field added, 2 methods modified)

Total: 15 changes across 20 commits
Lines added: 250, Lines removed: 180
Contributors: 3 (Alice, Bob, Charlie)
```

**Implementation Complexity**: High (requires Git integration)

**Token Savings**: 40-60% with summary mode

---

## 📊 Priority Summary

### Tier 1 (Must Have) - Immediate Value

| Tool | Priority | Complexity | Value | Estimated Effort |
|------|----------|-----------|-------|-----------------|
| **FindUnusedCode** | ⭐⭐⭐⭐⭐ | Medium | Very High | 2-3 days |
| **FindDuplicateCode** | ⭐⭐⭐⭐ | High | High | 4-5 days |
| **FindUnusedDependencies** | ⭐⭐⭐⭐ | Medium | High | 2-3 days |
| **FindSecurityIssues** | ⭐⭐⭐⭐ | Medium | High | 3-4 days |

**Total Tier 1 Effort**: 11-15 days

### Tier 2 (Nice to Have) - Good Value

| Tool | Priority | Complexity | Value | Estimated Effort |
|------|----------|-----------|-------|-----------------|
| **GenerateDocumentation** | ⭐⭐⭐ | Medium | Medium | 2-3 days |
| **FindBranchingComplexity** | ⭐⭐⭐ | Medium | Medium | 2-3 days |
| **FindDeprecatedAPIs** | ⭐⭐⭐ | Medium | Medium | 1-2 days |
| **FindCircularDependencies** | ⭐⭐⭐ | Medium | Medium | 2-3 days |

**Total Tier 2 Effort**: 7-11 days

### Tier 3 (Lower Priority) - Specialized

| Tool | Priority | Complexity | Value | Estimated Effort |
|------|----------|-----------|-------|-----------------|
| **FindNamingViolations** | ⭐⭐ | Low-Medium | Low-Medium | 1-2 days |
| **GetTypeEvolution** | ⭐⭐ | High | Low-Medium | 4-5 days |

**Total Tier 3 Effort**: 5-7 days

---

## 🎯 Recommended Implementation Order

### Phase 1: Code Quality & Cleanup (Most Requested)
1. ✅ **FindUnusedCode** - Everyone wants this
2. ✅ **FindUnusedDependencies** - High impact, relatively easy
3. ✅ **FindDuplicateCode** - Complex but very valuable

**Phase 1 Total**: ~8-11 days

### Phase 2: Security & Analysis
4. ✅ **FindSecurityIssues** - Important for production code
5. ✅ **FindDeprecatedAPIs** - Migration helper
6. ✅ **FindBranchingComplexity** - Code quality metric

**Phase 2 Total**: ~6-10 days

### Phase 3: Documentation & Architecture
7. ✅ **GenerateDocumentation** - API documentation
8. ✅ **FindCircularDependencies** - Architecture validation

**Phase 3 Total**: ~4-6 days

---

## 💡 Additional Tool Enhancement Ideas

### Existing Tool Enhancements

1. **GetCodeMetrics**: Add trend analysis (compare with previous version)
2. **FindReferences**: Add "Show only writes" filter
3. **AnalyzeCodeComplexity**: Add cognitive complexity (in addition to cyclomatic)
4. **GetCallHierarchy**: Add visualization (ASCII tree or Mermaid diagram)
5. **BatchQuery**: Add progress reporting for long-running queries

### New Utility Tools

- **CompareTypes**: Compare two types side-by-side
- **FindTODOComments**: Extract all TODO/FIXME/HACK comments
- **GetFileHistory**: Show file change statistics
- **FindLargeFiles**: Identify large source files (> threshold)
- **AnalyzeTestCoverage**: Show which types lack tests

---

## 🚀 Quick Wins (Low Effort, High Value)

These can be implemented quickly with existing infrastructure:

### 1. FindTODOComments (1 day)
```csharp
// Use syntax tree walker to find comments with TODO/FIXME/HACK
```

### 2. FindLargeFiles (0.5 days)
```csharp
// Enumerate files, count lines, return top N
```

### 3. CompareTypes (1-2 days)
```csharp
// Combine GetTypeSignature for 2 types, show diff
```

### 4. GetFileStatistics (0.5 days)
```csharp
// Extend GetFileOutline to show: LOC, complexity, dependency count
```

---

## 📝 Implementation Notes

### Common Infrastructure Needed

1. **Syntax Tree Walker**: For code pattern detection
2. **Graph Analysis**: For circular dependency detection
3. **Diff Algorithm**: For duplicate code detection
4. **Security Pattern Database**: For security issue detection
5. **Git Integration**: For type evolution tracking

### Token Optimization

All new tools should include `format` parameter:
- `summary`: Counts and top issues only (50-70% savings)
- `normal`: Balanced output (baseline)
- `detailed`: Full information with context

### Testing Strategy

Each new tool should have:
- Unit tests with sample code
- Integration tests with real solutions
- Performance tests (for large solutions)
- Token usage benchmarks

---

## 🎉 Summary

**Current Tools**: 22
**Proposed New Tools**: 10 (Tier 1-3)
**Quick Wins**: 4 additional utilities

**Most Valuable Tools** (implement first):
1. 🔍 **FindUnusedCode** - Dead code detection
2. 📦 **FindUnusedDependencies** - Dependency cleanup
3. 🔄 **FindDuplicateCode** - Clone detection
4. 🔐 **FindSecurityIssues** - Security scanning

These 4 tools would add tremendous value for code maintenance and quality improvement!
