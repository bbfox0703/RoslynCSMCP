# Phase 2 Implementation Plan

**Start Date:** 2026-01-16
**Status:** Planning
**Target:** 4 tools for refactoring and code quality analysis

---

## 📋 Overview

Phase 2 focuses on advanced refactoring operations and .NET-specific analysis tools that complement Phase 1's code quality tools.

---

## 🎯 Goals

**Primary Objectives:**
1. ✅ Provide advanced refactoring capabilities (ExtractInterface)
2. ✅ Add .NET Core-specific analysis (DI Container)
3. ✅ Enhance code quality analysis (Exception Handling)
4. ✅ Detect threading issues (Thread Safety)

**Success Criteria:**
- All 4 tools production-ready
- Comprehensive documentation
- Zero build errors
- Integration with existing Phase 1 architecture

---

## 🛠️ Tools to Implement

### 1. ExtractInterface (High Priority)
**Purpose:** Extract interface from a class for better testability and SOLID principles

**Parameters:**
- `solutionPath`: Path to .sln file
- `typeName`: Class name to extract interface from
- `interfaceName`: Target interface name (optional, auto-generated)
- `members`: Members to include (optional, default all public)
- `targetNamespace`: Namespace for interface (optional, same as class)

**Features:**
- Analyze class public members
- Generate interface definition
- Support property, method, event extraction
- Auto-generate interface name (I + ClassName)
- Preview mode (default)
- Optional execution mode

**Output:**
- Interface definition code
- Member list to extract
- Suggested file path
- Preview of changes

**Estimated Effort:** 3-4 hours

---

### 2. AnalyzeDIContainer (High Priority)
**Purpose:** Analyze dependency injection configuration for common issues

**Parameters:**
- `solutionPath`: Path to .sln file
- `checkLifetimes`: Check service lifetime issues (default: true)
- `checkCircular`: Check circular dependencies (default: true)
- `checkCaptive`: Check captive dependencies (default: true)

**Detections:**
- Unregistered dependencies (constructor parameters not in DI)
- Lifetime mismatches (Singleton → Scoped/Transient)
- Circular dependencies in DI graph
- Captive dependencies (Singleton capturing Scoped)
- Multiple registrations for same interface

**Output:**
- Issue type and severity
- Service name and lifetime
- Recommendation for fix
- Code location

**Estimated Effort:** 4-5 hours

---

### 3. AnalyzeExceptionHandling (Medium Priority)
**Purpose:** Analyze exception handling patterns and detect anti-patterns

**Parameters:**
- `solutionPath`: Path to .sln file
- `checkEmptyCatch`: Check empty catch blocks (default: true)
- `checkSwallowedExceptions`: Check swallowed exceptions (default: true)
- `checkMissingUsing`: Check missing using statements (default: true)

**Detections:**
- Empty catch blocks
- Generic catch (Exception) without handling
- Swallowed exceptions (no log, no rethrow)
- Missing finally/using for IDisposable
- Over-broad exception catching
- Improper async exception handling

**Output:**
- Anti-pattern type
- Severity (High/Medium/Low)
- Location and code snippet
- Recommendation (add logging, use specific exception, etc.)

**Estimated Effort:** 3-4 hours

---

### 4. FindThreadSafetyIssues (Medium Priority)
**Purpose:** Detect common thread safety issues and race conditions

**Parameters:**
- `solutionPath`: Path to .sln file
- `checkStaticFields`: Check mutable static fields (default: true)
- `checkSharedState`: Check unsynchronized shared state (default: true)
- `checkCollections`: Check unsafe collection operations (default: true)

**Detections:**
- Mutable static fields without synchronization
- Shared state access without locking
- Non-thread-safe collection usage
- Double-checked locking issues
- Race conditions in property setters
- Async/await anti-patterns (ConfigureAwait, deadlock risks)

**Output:**
- Issue type and severity
- Code location and snippet
- Thread safety violation reason
- Fix recommendation (lock, Concurrent collections, etc.)

**Estimated Effort:** 5-6 hours

---

## 📦 Architecture Plan

### Models (SearchModels.cs)
New models to add:
- `InterfaceExtractionResult` - Interface extraction preview
- `DIContainerIssue` - DI configuration issue
- `DIContainerResults` - DI analysis results
- `ExceptionHandlingIssue` - Exception handling anti-pattern
- `ExceptionHandlingResults` - Exception analysis results
- `ThreadSafetyIssue` - Thread safety violation
- `ThreadSafetyResults` - Thread safety analysis results

### Services (Phase2AnalysisService.cs)
New service methods:
- `ExtractInterfaceAsync` - Extract interface logic
- `AnalyzeDIContainerAsync` - DI analysis logic
- `AnalyzeExceptionHandlingAsync` - Exception analysis logic
- `FindThreadSafetyIssuesAsync` - Thread safety analysis logic

### Tools (CodeNavigationTools.cs)
New MCP tool registrations:
- `ExtractInterface`
- `AnalyzeDIContainer`
- `AnalyzeExceptionHandling`
- `FindThreadSafetyIssues`

Each with formatting methods.

---

## 🔍 Technical Approach

### ExtractInterface
**Algorithm:**
1. Find target class by name
2. Get all public members (methods, properties, events)
3. Generate interface syntax
4. Check for name conflicts
5. Preview or execute

**Roslyn APIs:**
- `INamedTypeSymbol` for class info
- `IMethodSymbol`, `IPropertySymbol` for members
- Syntax generation for interface

---

### AnalyzeDIContainer
**Algorithm:**
1. Find all DI registration calls (AddScoped, AddSingleton, etc.)
2. Build service registration map
3. Find all constructor injection points
4. Check each dependency is registered
5. Validate lifetimes (Singleton → Scoped = captive)
6. Detect circular dependencies using graph traversal

**Roslyn APIs:**
- `InvocationExpressionSyntax` for AddXxx calls
- `ConstructorDeclarationSyntax` for dependencies
- Semantic model for type resolution

---

### AnalyzeExceptionHandling
**Algorithm:**
1. Find all try-catch-finally blocks
2. Check catch blocks:
   - Empty body
   - No logging/rethrowing
   - Generic Exception catch
3. Check disposable types for using/try-finally
4. Validate async exception patterns

**Roslyn APIs:**
- `TryStatementSyntax` for try-catch blocks
- `CatchClauseSyntax` for catch analysis
- Control flow analysis for exception paths

---

### FindThreadSafetyIssues
**Algorithm:**
1. Find static mutable fields
2. Find shared state (instance fields accessed from multiple methods)
3. Check for lock statements protecting access
4. Find concurrent collection usage
5. Analyze async/await patterns for deadlock risks

**Roslyn APIs:**
- `FieldDeclarationSyntax` with static modifier
- Data flow analysis for shared state
- Pattern matching for lock statements

---

## 📊 Estimated Timeline

| Tool | Effort | Priority | Complexity |
|------|--------|----------|------------|
| ExtractInterface | 3-4 hrs | High | Medium |
| AnalyzeDIContainer | 4-5 hrs | High | High |
| AnalyzeExceptionHandling | 3-4 hrs | Medium | Medium |
| FindThreadSafetyIssues | 5-6 hrs | Medium | High |
| Documentation | 2-3 hrs | High | Low |
| **Total** | **17-22 hrs** | - | - |

**Recommended Order:**
1. ExtractInterface (simplest, high value)
2. AnalyzeExceptionHandling (moderate complexity, good learning)
3. AnalyzeDIContainer (complex but well-defined)
4. FindThreadSafetyIssues (most complex, requires advanced analysis)

---

## 🎯 Success Metrics

**Code Quality:**
- ✅ Zero build errors
- ✅ Consistent with Phase 1 architecture
- ✅ Comprehensive error handling
- ✅ Well-documented code

**Functionality:**
- ✅ All tools working with test cases
- ✅ Accurate detection algorithms
- ✅ Useful recommendations

**Documentation:**
- ✅ Updated AGENT_SKILL_GUIDE.md
- ✅ Individual tool documentation
- ✅ Usage examples
- ✅ Phase 2 progress report

---

## 📝 Notes

**Challenges:**
- DI container analysis requires understanding multiple DI frameworks
- Thread safety detection is inherently complex (false positives/negatives)
- Interface extraction needs careful syntax generation

**Dependencies:**
- Phase 1 infrastructure (models, services, tools)
- Existing Roslyn utilities
- MSBuildWorkspace setup

**Future Enhancements:**
- Support multiple DI frameworks (currently MS DI only)
- More sophisticated thread safety analysis
- Interface extraction with dependency updates
- Automated fixes for exception handling

---

**Plan Created:** 2026-01-16
**Ready to Begin Implementation**
