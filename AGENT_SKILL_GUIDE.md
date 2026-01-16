# RoslynCSMCP Agent Skill Guide

This guide describes when and how to use RoslynCSMCP tools to help Claude analyze C# codebases effectively. These tools integrate with Microsoft's Roslyn compiler platform to provide deep code analysis capabilities.

## Table of Contents

1. [Code Navigation & Discovery](#code-navigation--discovery)
2. [Understanding Code Structure](#understanding-code-structure)
3. [Dependency Analysis](#dependency-analysis)
4. [Code Quality & Maintenance](#code-quality--maintenance)
5. [Security & Performance](#security--performance)
6. [Testing & Coverage](#testing--coverage)
7. [Refactoring Support](#refactoring-support)
8. [Advanced Scenarios](#advanced-scenarios)

---

## Code Navigation & Discovery

### When to Use These Tools

Use these tools when you need to **find, locate, or explore** symbols in a C# codebase.

### Available Tools

#### SearchSymbols
**When to use:**
- "Find all classes named UserService"
- "Where are the authentication methods?"
- "Show me all interfaces in this solution"
- Looking for specific patterns (e.g., all classes ending with "Controller")

**Best for:**
- Initial exploration of unfamiliar codebases
- Finding candidates for refactoring
- Locating specific types, methods, or properties

**Example scenarios:**
```
❓ "Find all repository classes"
→ Use SearchSymbols with pattern "*Repository"

❓ "Where are the validation methods?"
→ Use SearchSymbols with pattern "*Validat*" and symbolTypes="method"

❓ "List all public interfaces"
→ Use SearchSymbols with symbolTypes="interface"
```

#### FindReferences
**When to use:**
- "Where is this method called?"
- "Find all usages of the UserService class"
- "What code depends on this property?"
- Understanding impact before making changes

**Best for:**
- Impact analysis before refactoring
- Understanding code dependencies
- Finding dead code (zero references)

**Detail levels:**
- `summary`: Quick overview with file counts
- `locations`: See each reference with code line (recommended for most cases)
- `full`: Include 5-line context around each reference (for detailed analysis)

#### FindReferencesAcrossSolutions
**When to use:**
- Working with multi-solution repositories
- Analyzing shared libraries used by multiple solutions
- Cross-repository dependency analysis

#### GetProjectStructure
**When to use:**
- "What is the overall architecture of this solution?"
- Getting a bird's-eye view of the codebase
- Understanding namespace organization
- Documenting project structure

**Parameters:**
- `includeMembers=true`: Show methods and properties (for detailed view)
- `namespaceFilter`: Focus on specific areas (e.g., "MyApp.Services")
- `publicOnly=true`: Show only public API surface

---

## Understanding Code Structure

### When to Use These Tools

Use these tools when you need to **understand how code is organized** and what it contains.

### Available Tools

#### GetFileOutline
**When to use:**
- "What's in this file?"
- Quick overview of classes and members without full implementation
- Understanding file organization before editing

**Best for:**
- Initial file exploration
- Code reviews
- Generating documentation

**Output modes:**
- `compact`: Minimal info, good for large files
- `normal`: Balanced view (recommended)
- `detailed`: Comprehensive with attributes and modifiers

#### GetTypeSignature
**When to use:**
- "What's the API surface of this class?"
- Understanding interfaces and contracts
- Documenting public APIs
- Comparing type signatures

**Best for:**
- API documentation
- Understanding contracts before implementation
- Interface design review

#### GetSymbolInfo
**When to use:**
- "Tell me everything about this method"
- Deep dive into specific symbol details
- Understanding documentation and attributes
- Examining method signatures

**Detail levels:**
- `summary`: Name, kind, accessibility
- `basic`: Add parameters, return type, documentation (recommended)
- `full`: Include all attributes and detailed metadata

#### GetClassHierarchy
**When to use:**
- "What are all the implementations of IRepository?"
- "What does this class inherit from?"
- Understanding OOP relationships
- Planning inheritance refactoring

**Best for:**
- Understanding polymorphism
- Finding all derived classes
- Analyzing inheritance depth

#### FindImplementations
**When to use:**
- "What classes implement IUserService?"
- Finding concrete implementations of abstractions
- Understanding dependency injection registrations

---

## Dependency Analysis

### When to Use These Tools

Use these tools when you need to **understand dependencies** between projects, packages, and code components.

### Available Tools

#### GetDependencyGraph
**When to use:**
- "How are these projects connected?"
- Visualizing solution architecture
- Finding circular dependencies
- Planning deployment order

**Output formats:**
- `text`: Simple text output
- `dot`: GraphViz format for visualization tools
- `mermaid`: Mermaid diagram for documentation

#### AnalyzeDependencies
**When to use:**
- "What are the dependencies for this project?"
- Analyzing namespace usage patterns
- Finding circular dependencies
- Understanding symbol visibility (public vs internal)

**Detects:**
- Circular dependency chains
- Project reference relationships
- Most-used namespaces
- Public vs internal API usage

#### FindUnusedDependencies
**When to use:**
- Cleaning up project files
- Reducing dependency bloat
- Optimizing build times
- Preparing for package updates

**Checks:**
- Unused NuGet packages
- Unused project references
- Provides expected namespaces for verification

#### AnalyzePackages
**When to use:**
- "Are our NuGet packages up to date?"
- Security vulnerability scanning
- Finding version conflicts
- Package cleanup recommendations

**Provides:**
- Available updates with version comparison
- Version conflicts across projects
- Unused packages
- Security vulnerability warnings (if available)

#### GetCallHierarchy
**When to use:**
- "What methods call this function?"
- "What does this method call?"
- Understanding execution flow
- Debugging complex call chains

**Directions:**
- `both`: Show callers and callees (comprehensive)
- `callers`: Who calls this method?
- `callees`: What does this method call?

---

## Code Quality & Maintenance

### When to Use These Tools

Use these tools when **maintaining code quality** and identifying improvement opportunities.

### Available Tools

#### AnalyzeCodeComplexity
**When to use:**
- "What are the most complex methods?"
- Code review preparation
- Identifying refactoring candidates
- Setting complexity budgets

**Metrics:**
- Cyclomatic complexity (decision points)
- Cognitive complexity (readability)
- Maximum nesting depth

**Thresholds:**
- 1-5: Low complexity (good)
- 6-10: Moderate complexity (consider refactoring)
- 11+: High complexity (should refactor)

#### FindUnusedCode
**When to use:**
- "What code can we safely delete?"
- Codebase cleanup
- Reducing maintenance burden
- Before major refactoring

**Scopes:**
- `private`: Only private members (safest to remove)
- `internal`: Internal members (safe within assembly)
- `public`: Public members (breaking change warning)
- `all`: Everything

#### FindDuplicateCode
**When to use:**
- "Is this code duplicated elsewhere?"
- Finding refactoring opportunities
- Improving code reuse
- Reducing bug duplication

**Settings:**
- `minLines`: Minimum line count (default: 5)
- `threshold`: Similarity percentage (default: 85%)

#### FindLargeFiles
**When to use:**
- "Which files are too large?"
- Identifying files that should be split
- Code organization improvements
- Refactoring planning

**Threshold:** Default 500 lines (configurable)

#### AnalyzeDocumentationCoverage
**When to use:**
- "How well is our code documented?"
- Preparing for API release
- Documentation improvement planning
- Code review standards

**Scopes:**
- `public`: Only public APIs (for library authors)
- `all`: All symbols (for comprehensive documentation)

**Provides:**
- Coverage percentage
- List of undocumented symbols
- AI-generated documentation suggestions

#### FindTODOComments
**When to use:**
- "What technical debt exists?"
- Sprint planning
- Finding incomplete work
- Code cleanup initiatives

**Comment types:**
- TODO: Planned work
- FIXME: Known issues
- HACK: Temporary solutions
- BUG: Known bugs
- OPTIMIZE: Performance improvements needed

#### FindMagicNumbers 🆕
**When to use:**
- "Find all hardcoded values"
- "What numbers should be constants?"
- Code quality audits
- Preparing for internationalization
- Configuration extraction

**Detects:**
- Numeric literals (excluding common: 0, 1, -1, 2)
- String literals (configurable minimum length)
- Excludes attributes and test assertions
- Context-aware filtering

**Features:**
- Priority categorization (High/Medium/Low based on occurrence)
- Suggested constant names
- Grouped by value with counts
- File locations and code context

**Output formats:**
- `summary`: Counts only
- `normal`: Grouped list with first few occurrences
- `detailed`: All occurrences with suggested names

**Parameters:**
- `includeStrings`: Include string literals (default: true)
- `includeNumbers`: Include numeric literals (default: true)
- `minStringLength`: Minimum string length (default: 3)

**Example scenarios:**
```
❓ "Find all magic numbers in my solution"
→ Use FindMagicNumbers with default settings

❓ "Find hardcoded strings only"
→ Use FindMagicNumbers with includeNumbers=false

❓ "Show me all numeric constants that should be extracted"
→ Use FindMagicNumbers with format=detailed, includeStrings=false
```

#### FindCodeSmells 🆕 ✅
**When to use:**
- "What code smells exist in my project?"
- Code review preparation
- Identifying refactoring candidates
- Learning code quality practices
- Technical debt assessment

**Detects (10 patterns) - All Implemented:**
1. **Long Method** - Methods with too many lines (20-29: Low, 30-49: Medium, 50+: High)
2. **Large Class** - Classes with too many members or lines (300+ lines/20+ members: Medium, 500+ lines/30+ members: High)
3. **Long Parameter List** - Excessive parameters (4: Low, 5: Medium, 6+: High)
4. **Feature Envy** - Methods accessing other classes more than their own
5. **Data Clumps** - Repeated parameter patterns across methods
6. **Primitive Obsession** - 3+ parameters of same primitive type
7. **Switch Statements** - Large switches (5-7 cases: Medium, 8+: High)
8. **Message Chains** - Long method chains (3+ levels: Medium, 5+: High)
9. **Middle Man** - Classes that just delegate (>50% methods delegate: Medium, >75%: High)
10. **Speculative Generality** - Unused abstractions (abstract classes, single-member interfaces)

**Output formats:**
- `summary`: Counts by type and severity
- `normal`: Top 10 of each severity level with locations (recommended)
- `detailed`: All smells with full metrics, recommendations, and code snippets

**Parameters:**
- `smellTypes`: "all" or comma-separated list (e.g., "LongMethod,LargeClass")
- `severity`: "All", "High", "Medium", or "Low"
- `format`: "summary", "normal", or "detailed"

**Example scenarios:**
```
❓ "Find all code smells in my solution"
→ Use FindCodeSmells with format=normal

❓ "Show only critical issues"
→ Use FindCodeSmells with severity=High, format=detailed

❓ "Check for long methods and large classes"
→ Use FindCodeSmells with smellTypes="LongMethod,LargeClass"

❓ "Quick overview of code quality issues"
→ Use FindCodeSmells with format=summary
```

**Output includes:**
- Grouped by smell type and severity
- File locations and line numbers
- Specific metrics (line count, parameter count, etc.)
- Tailored recommendations for each smell
- Quick recommendations summary (detailed mode)

**Status:** ✅ **Production Ready** (Fully implemented and tested)

#### AnalyzeNamingConventions
**When to use:**
- Code review enforcement
- Ensuring consistent naming
- Onboarding new developers
- Style guide compliance

**Checks:**
- Interface naming (IUserService)
- PascalCase for types and methods
- camelCase for parameters
- Private field naming (_field vs field)

---

## Security & Performance

### When to Use These Tools

Use these tools when **identifying security vulnerabilities** and **performance issues**.

### Available Tools

#### FindSecurityIssues
**When to use:**
- Security audits
- Pre-release security review
- Compliance requirements
- Learning security best practices

**Detects:**
- SQL injection vulnerabilities
- Hardcoded secrets (passwords, API keys)
- Weak cryptography
- Path traversal vulnerabilities
- Unsafe deserialization

**Categories:**
- `sql-injection`: SQL concatenation patterns
- `secrets`: Hardcoded credentials
- `crypto`: Weak algorithms (MD5, SHA1, DES)
- `path-traversal`: Unsafe file path handling
- `deserialization`: BinaryFormatter usage

**Severity levels:**
- Critical: Immediate fix required
- High: Fix before release
- Medium: Fix soon
- Low: Consider improving

#### FindPerformanceIssues
**When to use:**
- Performance optimization
- Code review
- Before production deployment
- Learning performance best practices

**Detects:**
- LINQ misuse (multiple enumeration)
- String concatenation in loops
- Sync-over-async patterns
- Disposable not disposed
- Exception handling anti-patterns

**Provides:**
- Performance impact score (1-10)
- Fix recommendations
- Code examples

---

## Testing & Coverage

### When to Use These Tools

Use these tools when **working with tests** and **analyzing test coverage**.

### Available Tools

#### FindTestsForType
**When to use:**
- "Does this class have tests?"
- Understanding test organization
- Code coverage review
- Test-driven development

**Detects:**
- xUnit tests ([Fact], [Theory])
- NUnit tests ([Test])
- MSTest tests ([TestMethod])

#### GetTestCoverage
**When to use:**
- "What code lacks tests?"
- Test coverage analysis
- Identifying high-risk untested code
- Test planning

**Provides:**
- Type coverage percentage
- Member coverage percentage
- Risk assessment (complexity + no tests = high risk)
- List of uncovered members

**Risk levels:**
- Critical: High complexity, no tests
- High: Medium complexity, no tests
- Medium: Low complexity, no tests OR high complexity, partial tests
- Low: Fully tested

**Scopes:**
- `public`: Only public APIs (recommended)
- `all`: All types (comprehensive)

---

## Refactoring Support

### When to Use These Tools

Use these tools when **planning and executing refactoring** work.

### Available Tools

#### RenameSymbolSafely ✅
**When to use:**
- "Rename this class safely"
- "What would break if I rename this method?"
- Before performing renames
- Understanding rename impact

**Features:**
- Finds all references to symbol
- Preview mode (default - no changes made)
- Conflict detection (new name already exists)
- Risk assessment
- Cross-file renaming support
- Atomic operations (all or nothing)

**Safety Checks:**
- Validates new name doesn't conflict
- Checks accessibility constraints
- Warns about breaking changes
- Identifies impact scope

**Parameters:**
- `symbolName`: Current name
- `newName`: New name
- `solutionPath`: Solution file path
- `previewOnly`: Preview (true) or execute (false), default: true

**Modes:**
- `previewOnly=true`: Shows what would change (safe, recommended)
- `previewOnly=false`: Actually performs rename (use with caution)

**Output:**
- Files affected count
- Total locations to change
- Risk level assessment
- Conflict warnings
- Preview of changes

**Example scenarios:**
```
❓ "Can I safely rename UserService to AccountService?"
→ Use RenameSymbolSafely in preview mode (default)

❓ "Show me everywhere GetUser is referenced"
→ Use RenameSymbolSafely with previewOnly=true (safer than FindReferences for rename planning)

❓ "Rename ConfigManager to SettingsManager everywhere"
→ First use previewOnly=true to review, then previewOnly=false to execute
```

**Risk Levels:**
- **Low**: Private members, single project
- **Medium**: Internal members, few projects
- **High**: Public members, many references
- **Critical**: Public API, cross-project, breaking changes

**Status:** ✅ Production Ready - Safe symbol renaming with preview mode, conflict detection, and intelligent risk assessment

#### AnalyzeLayerViolations ✅
**When to use:**
- "Does my code follow Clean Architecture?"
- "Check layered architecture compliance"
- Enforcing architectural rules
- Preventing dependency violations

**Architecture Patterns Supported:**
- Clean Architecture (Presentation → Application → Domain → Infrastructure)
- Onion Architecture
- Hexagonal Architecture
- DDD (Domain-Driven Design) layers
- Custom layer definitions

**Features:**
- JSON-based layer definitions
- Pattern matching for project classification
- Dependency rule validation
- Circular dependency detection
- Compliance scoring
- Violation recommendations

**Layer Definition Format:**
```json
{
  "layers": [
    {"name": "Presentation", "projects": ["*.Web", "*.API"]},
    {"name": "Application", "projects": ["*.Application", "*.Services"]},
    {"name": "Domain", "projects": ["*.Domain", "*.Core"]},
    {"name": "Infrastructure", "projects": ["*.Data", "*.Infrastructure"]}
  ],
  "rules": [
    {"from": "Presentation", "to": "Application", "allowed": true},
    {"from": "Presentation", "to": "Infrastructure", "allowed": false},
    {"from": "Application", "to": "Domain", "allowed": true},
    {"from": "Domain", "to": "Infrastructure", "allowed": false}
  ]
}
```

**Violations Detected:**
- Direct dependency violations (e.g., Presentation → Infrastructure)
- Circular dependencies between layers
- Reverse dependencies (dependencies pointing "up" the stack)

**Output:**
- Total violations by severity
- Compliance score (percentage)
- Detailed violation list with recommendations
- Impacted projects and references

**Parameters:**
- `solutionPath`: Solution file path
- `layerDefinitionsJson`: JSON layer configuration
- `format`: Output format (summary/normal/detailed)

**Example scenarios:**
```
❓ "Check if my solution follows Clean Architecture"
→ Use AnalyzeLayerViolations with Clean Architecture layer definition

❓ "Find all places where UI directly accesses database"
→ Define layers with rule: Presentation → Data = not allowed

❓ "Validate DDD boundaries"
→ Define Domain layer rules preventing external dependencies
```

**Status:** ✅ Production Ready - Detects direct dependency violations, circular dependencies, and calculates compliance scores using JSON-based layer definitions

#### GetChangeImpact
**When to use:**
- Before renaming a class or method
- Before changing a method signature
- Before removing code
- Risk assessment for changes

**Provides:**
- Total impacted symbols
- Direct vs indirect references
- Impacted projects and files
- Risk assessment (Critical/High/Medium/Low)
- Breaking change analysis
- Recommendations

**When it shows "Critical" risk:**
- Public API with many external references
- Breaking signature changes
- Cross-project dependencies

#### AnalyzeAPIChanges
**When to use:**
- Comparing versions (v1 vs v2)
- Release planning
- Semantic versioning decisions
- Breaking change documentation

**Compares:**
- Two different solution versions
- Identifies added/removed/modified symbols
- Detects accessibility changes
- Suggests version bump (major/minor/patch)

**Change types:**
- Breaking: Method removed, signature changed
- Non-breaking: New methods, overloads added
- Internal: Private/internal changes

---

## Advanced Scenarios

### When to Use These Tools

Use these tools for **specialized analysis** and **complex queries**.

### Available Tools

#### BatchQuery
**When to use:**
- Running multiple analyses at once
- Comprehensive codebase audit
- Automated reporting
- CI/CD integration

**Example batch:**
```json
[
  {
    "tool": "AnalyzeCodeComplexity",
    "parameters": { "solutionPath": "...", "threshold": 10 }
  },
  {
    "tool": "FindUnusedCode",
    "parameters": { "solutionPath": "...", "scope": "private" }
  },
  {
    "tool": "FindSecurityIssues",
    "parameters": { "solutionPath": "..." }
  }
]
```

**Parameters:**
- `parallel=true`: Run queries concurrently (faster)
- `parallel=false`: Run sequentially (less resource intensive)

#### FindReferencesFiltered
**When to use:**
- Reducing noise in reference searches
- Focusing on specific usage patterns
- Excluding test code
- Project-specific analysis

**Filters:**
- Exclude projects (e.g., test projects)
- Exclude namespaces
- Exclude file patterns
- Include only specific reference kinds

#### GetCompilationErrors
**When to use:**
- Quick syntax/semantic error check
- Before running full build
- CI/CD health checks
- Understanding why code won't compile

**Severities:**
- `Error`: Build-blocking issues
- `Warning`: Potential problems
- `Info`: Informational messages
- `All`: Everything

**Modes:**
- `compact`: Error counts and key issues
- `normal`: Balanced detail
- `detailed`: Full error messages with context

#### GetCodeMetrics
**When to use:**
- Solution-wide statistics
- Codebase health dashboard
- Tracking metrics over time
- Architecture documentation

**Groups by:**
- `project`: Metrics per project
- `namespace`: Metrics per namespace
- `type`: Metrics per type

**Metrics include:**
- Lines of code
- Number of types, methods, properties
- Cyclomatic complexity
- Documentation coverage

#### GetFileStatistics
**When to use:**
- Single-file analysis
- Understanding file complexity
- Code review preparation
- Documentation

**Provides:**
- Line counts (total, code, comments, blank)
- Code elements (classes, methods, properties)
- Complexity metrics
- Documentation coverage
- Dependencies

#### FindAttributeUsages
**When to use:**
- "Where is [Obsolete] used?"
- Finding all REST API endpoints ([HttpGet], etc.)
- Dependency injection registrations ([Injectable])
- Validation rules ([Required], [MaxLength])

**Examples:**
- `[Authorize]` - Find all protected endpoints
- `[HttpPost]` - Find all POST APIs
- `[Fact]` - Find all unit tests
- `[JsonProperty]` - Find serialization mappings

#### FindDeprecatedAPIs
**When to use:**
- Migration planning
- Finding [Obsolete] usage
- Technical debt tracking
- Upgrade preparation

**Detects:**
- Internal obsolete APIs
- .NET Framework obsolete APIs
- Third-party library deprecations

---

## Best Practices

### Choosing Detail Levels

**Summary/Compact:**
- Quick overview needed
- Large result sets expected
- Performance is priority

**Normal (Recommended):**
- Balanced view for most scenarios
- Good detail without overwhelming output
- Default for interactive use

**Detailed/Full:**
- Deep analysis required
- Small result sets
- Documentation generation
- Investigation and debugging

### Solution Path Requirements

All tools require a **solution file path** (.sln). Ensure:
- Path is absolute or relative to current directory
- Solution file exists and is accessible
- Solution can be loaded by Roslyn (valid MSBuild format)

### Performance Considerations

**For large solutions:**
- Use specific filters (namespace, project)
- Choose appropriate detail levels
- Consider using `BatchQuery` for multiple analyses
- Tools use multi-level caching (memory, Redis, file)

**Caching:**
- L1 (Memory): 10-minute expiry
- L2 (Redis): 1-hour expiry (optional)
- L3 (File): 7-day expiry
- Cache invalidates on solution file changes

### Error Handling

Tools are designed to be resilient:
- Partial failures are captured as warnings
- Tools continue processing other projects/files
- Results include failure counts and warning messages

### Common Workflows

#### Initial Codebase Exploration
1. `GetProjectStructure` - Understand overall structure
2. `SearchSymbols` - Find interesting types
3. `GetTypeSignature` - Examine specific types
4. `FindReferences` - Understand usage

#### Pre-Refactoring Analysis
1. `FindReferences` - What uses this code?
2. `GetChangeImpact` - What's the risk?
3. `FindTestsForType` - Are there tests?
4. `GetCallHierarchy` - How is this called?

#### Code Quality Audit
1. `AnalyzeCodeComplexity` - Find complex code
2. `FindUnusedCode` - Find dead code
3. `FindDuplicateCode` - Find duplication
4. `AnalyzeDocumentationCoverage` - Check docs

#### Security Review
1. `FindSecurityIssues` - Security vulnerabilities
2. `FindDeprecatedAPIs` - Obsolete code
3. `AnalyzePackages` - Package vulnerabilities
4. `GetCompilationErrors` - Compilation issues

#### Test Coverage Analysis
1. `GetTestCoverage` - Overall coverage
2. `FindTestsForType` - Specific type tests
3. `AnalyzeCodeComplexity` - Prioritize high-complexity untested code

---

## Integration Tips

### With Claude Code

These tools are designed to be used by Claude (via MCP protocol):

**Natural language queries:**
- "Find all the authentication methods" → Use SearchSymbols
- "Where is UserService used?" → Use FindReferences
- "Is this code secure?" → Use FindSecurityIssues
- "What will break if I change this?" → Use GetChangeImpact

### With CI/CD

Tools can be integrated into build pipelines:
- `GetCompilationErrors` - Fast pre-build checks
- `FindSecurityIssues` - Security gates
- `AnalyzeCodeComplexity` - Quality gates
- `GetTestCoverage` - Coverage requirements

### Logging and Debugging

**Enable detailed logs:**
- Set `DOTNET_ENVIRONMENT=Development`
- Logs written to: `%TEMP%\RoslynCSMCP\logs\debug-YYYYMMDD.log`

**Tail logs in real-time:**
```powershell
# Windows PowerShell
Get-Content "$env:TEMP\RoslynCSMCP\logs\debug-$(Get-Date -Format yyyyMMdd).log" -Wait -Tail 50
```

---

## Conclusion

RoslynCSMCP provides comprehensive code analysis capabilities through Roslyn. By choosing the right tool for each scenario and understanding their strengths, you can effectively navigate, understand, maintain, and improve C# codebases.

**Quick decision tree:**
- Need to find code? → **SearchSymbols**, **FindReferences**
- Need to understand structure? → **GetProjectStructure**, **GetFileOutline**
- Need to assess quality? → **AnalyzeCodeComplexity**, **FindUnusedCode**
- Need to check security? → **FindSecurityIssues**, **AnalyzePackages**
- Need to plan refactoring? → **GetChangeImpact**, **FindReferences**
- Need to analyze tests? → **GetTestCoverage**, **FindTestsForType**

For detailed implementation guidance, see [CLAUDE.md](./CLAUDE.md).
