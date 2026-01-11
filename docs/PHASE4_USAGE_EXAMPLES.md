# Phase 4 Token Optimization Usage Examples

**Date**: 2026-01-10  
**Status**: ✅ Implemented  
**Version**: 1.0

---

## 📊 Overview

Phase 4A introduces token-optimized parameters:

| Tool | New Parameters | Token Savings | Status |
|------|---------------|---------------|--------|
| GetFileOutline | `mode`, `maxMembers` | 60-75% | ✅ Implemented |
| GetSymbolInfo | `detailLevel` | 70-80% | ✅ Implemented |

---

## 🔷 GetFileOutline - Token Optimization

### Parameters

```csharp
string mode = "normal"              // compact | normal | detailed  
int maxMembers = 10                 // Max members per type (0=all)
bool includeMembers = true          // Legacy parameter
bool includeDocumentation = true    // Legacy parameter
```

### Mode Comparison

| Mode | Tokens | Use Case |
|------|--------|----------|
| compact | 200-500 | Quick overview |
| normal | 500-1000 | Daily development |
| detailed | 800-2000 | Deep analysis |

### Example 1: Compact Mode (75% savings)

```csharp
GetFileOutline(filePath: "UserService.cs", mode: "compact", maxMembers: 5)
```

**Output (~250 tokens)**:
```
File: UserService.cs (180 LOC, 15 usings)

UserService (Class, Public) @ MyProject.Services
  Constructors: 1
  Fields: 2 (0 public, 2 private)
  Methods: 8 (6 public, 2 private)
    GetUserAsync(int id) [async]
    GetAllUsersAsync() [async]
    CreateUserAsync(UserDto dto) [async]
    UpdateUserAsync(int id, UserDto dto) [async]
    DeleteUserAsync(int id) [async]
    ... and 3 more (use maxMembers=0 for all)
```

**Best for**: Initial exploration, quick reference

### Example 2: Normal Mode (50% savings)

```csharp
GetFileOutline(filePath: "UserService.cs", mode: "normal", maxMembers: 10)
```

**Output (~650 tokens)**:
```
**File Outline**: UserService.cs

📊 **Statistics**:
  • Lines: 180 code, 40 comments, 30 blank
  • Types: 2 (0 failed)
  • Members: 13 (0 failed)

📦 **Using** (15): System, System.Collections.Generic, System.Linq...
     ... and 10 more

📋 **Types** (2):

🔷 **UserService** (Class, Public)
   Line 15, Namespace: MyProject.Services

   📋 **Methods** (8):
     • GetUserAsync(int id)
       → Task<User?>
       Line 30, Public
     ... (limited to 10 members)
```

**Best for**: Code review, daily work

### Example 3: Detailed Mode (original)

```csharp
GetFileOutline(filePath: "UserService.cs", mode: "detailed", maxMembers: 0)
```

**Output (~1500 tokens)**: Full statistics, all usings, all members with docs

**Best for**: Documentation, comprehensive analysis

---

## 🔹 GetSymbolInfo - Token Optimization

### Parameters

```csharp
string detailLevel = "basic"  // summary | basic | full
```

### Detail Level Comparison

| Level | Tokens | Use Case |
|-------|--------|----------|
| summary | 30-50 | Quick lookup |
| basic | 80-120 | Daily use |
| full | 150-250 | Deep analysis |

### Example 4: Summary Mode (80% savings)

```csharp
GetSymbolInfo(
    symbolName: "GetUserAsync",
    solutionPath: "MyProject.sln",
    detailLevel: "summary"
)
```

**Output (~40 tokens)**:
```
GetUserAsync (Method, Public)
→ Task<User?> (int)
@ UserService.cs:30
```

**Best for**: Quick signature checks

### Example 5: Basic Mode (50% savings)

```csharp
GetSymbolInfo(
    symbolName: "GetUserAsync",
    solutionPath: "MyProject.sln",
    detailLevel: "basic"
)
```

**Output (~100 tokens)**:
```
**GetUserAsync** (Method)
Signature: Task<User?> GetUserAsync(int id)
Accessibility: Public
In: MyProject.Services.UserService
File: UserService.cs:30
Attributes: 1 (use detailLevel=full for details)
```

**Best for**: Understanding context

### Example 6: Full Mode (original)

```csharp
GetSymbolInfo(
    symbolName: "GetUserAsync",
    solutionPath: "MyProject.sln",
    detailLevel: "full"
)
```

**Output (~200 tokens)**: Full name, namespace, declaring type, all attributes, full path

**Best for**: API documentation, debugging

---

## 📈 Real-World Savings

### Scenario 1: Exploring Codebase

**Before**:
```
3x GetFileOutline (default)  = 5300 tokens
2x GetSymbolInfo (default)   = 460 tokens
Total: 5760 tokens
```

**After**:
```
3x GetFileOutline (compact)  = 900 tokens
2x GetSymbolInfo (summary)   = 85 tokens  
Total: 985 tokens
```

**Savings: 4775 tokens (83%)**

### Scenario 2: Code Review

**Before**: 1800 tokens  
**After**: 885 tokens  
**Savings: 915 tokens (51%)**

### Scenario 3: Quick Lookup

**Before**: 230 tokens  
**After**: 38 tokens  
**Savings: 192 tokens (83%)**

---

## 🎯 Best Practices

### When to Use Each Mode

**GetFileOutline**:
- `compact`: Exploring, counting members
- `normal`: Code review, daily work
- `detailed`: Documentation, deep analysis

**GetSymbolInfo**:
- `summary`: Quick lookup, signatures
- `basic`: Daily queries, navigation
- `full`: API docs, complete analysis

### Recommended Combos

```csharp
// Ultra-light exploration
GetFileOutline(path, mode: "compact", maxMembers: 3)

// Daily development
GetFileOutline(path, mode: "normal", maxMembers: 10)

// Quick method check
GetSymbolInfo(name, sln, detailLevel: "summary")

// Standard lookup
GetSymbolInfo(name, sln, detailLevel: "basic")
```

---

## 💡 Migration Guide

**GetFileOutline**:
```csharp
// Old (1500-2000 tokens)
GetFileOutline(path, includeMembers: true)

// New - balanced (600-800 tokens, 60% savings)
GetFileOutline(path, mode: "normal", maxMembers: 10)

// New - minimal (250-350 tokens, 80% savings)  
GetFileOutline(path, mode: "compact", maxMembers: 5)
```

**GetSymbolInfo**:
```csharp
// Old (200-250 tokens)
GetSymbolInfo(name, sln)

// New - balanced (100-120 tokens, 50% savings)
GetSymbolInfo(name, sln, detailLevel: "basic")

// New - minimal (40-50 tokens, 80% savings)
GetSymbolInfo(name, sln, detailLevel: "summary")
```

---

## 📚 Token Estimates

### GetFileOutline

| File Size | Compact | Normal | Detailed |
|-----------|---------|--------|----------|
| Small (<100 LOC) | 100-200 | 300-500 | 500-800 |
| Medium (100-500) | 200-400 | 500-800 | 800-1500 |
| Large (500+) | 300-600 | 800-1200 | 1500-2500 |

### GetSymbolInfo

| Complexity | Summary | Basic | Full |
|------------|---------|-------|------|
| Simple field | 25-35 | 60-80 | 100-150 |
| Property | 30-40 | 70-90 | 120-170 |
| Simple method | 35-50 | 90-120 | 150-250 |
| Complex method | 40-60 | 110-140 | 200-300 |

---

## 🔶 Phase 4B Token Optimization

Phase 4B adds token optimization to 4 additional tools:

| Tool | New Parameters | Token Savings | Status |
|------|---------------|---------------|--------|
| GetCompilationErrors | `mode` | 40-60% | ✅ Implemented |
| FindAttributeUsages | `format` | 50-70% | ✅ Implemented |
| GetClassHierarchy | `format` | 50-70% | ✅ Implemented |
| FindImplementations | `format` | 50-70% | ✅ Implemented |

---

## 🔷 GetCompilationErrors - Token Optimization

### Parameters

```csharp
string mode = "normal"              // compact | normal | detailed
string severity = "All"             // Error | Warning | Info | All
```

### Mode Comparison

| Mode | Tokens | Use Case |
|------|--------|----------|
| compact | 300-1000 | Quick error count |
| normal | 500-1500 | Daily development |
| detailed | 800-3000 | Deep diagnostics |

### Example 7: Compact Mode (60% savings)

```csharp
GetCompilationErrors(
    solutionPath: "MyProject.sln",
    mode: "compact",
    severity: "Error"
)
```

**Output (~400 tokens)**:
```
Issues: 15 (3 projects, 1 failed)

Error: 15
  MyProject.Core: 8 issues
    CS0103 (5x): Program.cs:45
    CS0246 (2x): UserService.cs:30
    CS0029 (1x): Calculator.cs:12
  MyProject.Web: 7 issues
    CS1061 (4x): HomeController.cs:78
    CS0246 (3x): Startup.cs:25

Total: 15 errors, 0 warnings
```

**Best for**: Quick error overview, CI/CD checks

### Example 8: Detailed Mode (comprehensive)

Shows all errors with code snippets, full paths, and complete analysis.

---

## 🔹 FindAttributeUsages - Token Optimization

### Parameters

```csharp
string format = "normal"            // inline | normal | detailed
string targetType = "all"           // class | interface | method | property | all
```

### Format Comparison

| Format | Tokens | Use Case |
|--------|--------|----------|
| inline | 250-450 | Quick attribute scan |
| normal | 500-1000 | Daily use |
| detailed | 800-1500 | Full analysis |

### Example 9: Inline Format (70% savings)

```csharp
FindAttributeUsages(
    attributeName: "Obsolete",
    solutionPath: "MyProject.sln",
    format: "inline"
)
```

**Output (~300 tokens)**:
```
[Obsolete]: 12 usages found

Methods (8):
  GetLegacyData("Use GetDataAsync instead") @ UserService.cs:45
  ProcessOldFormat @ DataProcessor.cs:120
  CalculateOldWay @ Calculator.cs:67
  ... and 5 more

Properties (4):
  OldPropertyName @ Settings.cs:30
  LegacyConnection @ DbContext.cs:15
  ... and 2 more
```

**Best for**: Finding all usages quickly, attribute audits

---

## 🔸 GetClassHierarchy - Token Optimization

### Parameters

```csharp
string format = "normal"            // compact | normal | detailed
string direction = "both"           // ancestors | descendants | both
int maxDepth = 10
```

### Format Comparison

| Format | Tokens | Use Case |
|--------|--------|----------|
| compact | 150-600 | Tree structure only |
| normal | 400-1200 | Balanced view |
| detailed | 600-2000 | Full metadata |

### Example 10: Compact Format (70% savings)

```csharp
GetClassHierarchy(
    typeName: "BaseController",
    solutionPath: "MyProject.sln",
    format: "compact"
)
```

**Output (~250 tokens)**:
```
BaseController (Class)

Ancestors (2):
  ↑ Controller (C)
  ↑ ControllerBase (A)

Descendants (5):
  ↓ HomeController (C)
  ↓ UserController (C)
  ↓ AdminController (C)
    ↓ SuperAdminController (C)
  ↓ ApiController (C)
```

**Legend**: C=Concrete, A=Abstract, I=Interface

**Best for**: Understanding hierarchy structure quickly

---

## 🔻 FindImplementations - Token Optimization

### Parameters

```csharp
string format = "normal"            // summary | normal | detailed
bool includeAbstractImplementations = false
```

### Format Comparison

| Format | Tokens | Use Case |
|--------|--------|----------|
| summary | 100-300 | Names and locations |
| normal | 300-800 | Balanced info |
| detailed | 500-1200 | Full analysis |

### Example 11: Summary Format (70% savings)

```csharp
FindImplementations(
    typeName: "IUserRepository",
    solutionPath: "MyProject.sln",
    format: "summary"
)
```

**Output (~150 tokens)**:
```
Implementations of 'IUserRepository': 4

MyProject.Infrastructure (3):
  UserRepository @ UserRepository.cs:15
  CachedUserRepository @ CachedUserRepository.cs:20
  InMemoryUserRepository @ InMemoryUserRepository.cs:8

MyProject.Tests (1):
  MockUserRepository @ MockUserRepository.cs:12
```

**Best for**: Finding all implementations quickly

---

## 📈 Phase 4B Real-World Savings

### Scenario 1: Error Diagnostics

**Before**:
```
GetCompilationErrors (default)  = 2500 tokens
```

**After**:
```
GetCompilationErrors (compact)  = 800 tokens
```

**Savings: 1700 tokens (68%)**

### Scenario 2: Attribute Audit

**Before**:
```
FindAttributeUsages (default)   = 1200 tokens
```

**After**:
```
FindAttributeUsages (inline)    = 400 tokens
```

**Savings: 800 tokens (67%)**

### Scenario 3: Architecture Analysis

**Before**:
```
GetClassHierarchy (default)     = 1800 tokens
FindImplementations (default)   = 900 tokens
Total: 2700 tokens
```

**After**:
```
GetClassHierarchy (compact)     = 500 tokens
FindImplementations (summary)   = 250 tokens
Total: 750 tokens
```

**Savings: 1950 tokens (72%)**

---

## 🎓 Tips

1. Start with `compact`/`summary`, upgrade only when needed
2. Use `maxMembers` to limit output (5-10 for reviews)
3. Avoid `full` mode unless you need attributes/full paths
4. Combine strategically for different workflows

---

## ✅ Summary

### Phase 4 Complete: 6 Tools Optimized

**Phase 4A (2 tools)**:
- GetFileOutline: `mode` + `maxMembers` → 60-75% savings
- GetSymbolInfo: `detailLevel` → 70-80% savings

**Phase 4B (4 tools)**:
- GetCompilationErrors: `mode` → 40-60% savings
- FindAttributeUsages: `format` → 50-70% savings
- GetClassHierarchy: `format` → 50-70% savings
- FindImplementations: `format` → 50-70% savings

**Overall Impact**:
- **Total impact**: 40-80% token savings across 6 core tools
- **Migration effort**: Zero (100% backward compatible)
- **User experience**: Dramatically improved (faster responses, focused results)
- **Total formatting modes**: 18 new formatting functions

Start using these optimizations today!
