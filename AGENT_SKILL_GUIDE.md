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
