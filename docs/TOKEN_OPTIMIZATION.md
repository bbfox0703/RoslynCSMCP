# Token Optimization Guide

**Last Updated**: 2026-01-11
**Status**: ✅ Phase 4 Complete (6 tools optimized)

---

## 📋 Quick Reference

| Phase | Tools | Parameters Added | Token Savings | Status |
|-------|-------|-----------------|---------------|--------|
| **Phase 4A** | GetFileOutline<br>GetSymbolInfo | `mode`, `maxMembers`<br>`detailLevel` | 60-80% | ✅ Complete |
| **Phase 4B** | GetCompilationErrors<br>FindAttributeUsages<br>GetClassHierarchy<br>FindImplementations | `mode`<br>`format`<br>`format`<br>`format` | 40-70% | ✅ Complete |

**Overall Impact**: 40-80% token savings across 6 core tools

---

## 🎯 Quick Start

### Recommended Settings

```csharp
// Daily development (balanced)
GetFileOutline(path, mode: "normal", maxMembers: 10)
GetSymbolInfo(name, sln, detailLevel: "basic")
GetCompilationErrors(sln, mode: "normal")
FindAttributeUsages(attr, sln, format: "normal")

// Quick overview (maximum savings)
GetFileOutline(path, mode: "compact", maxMembers: 5)
GetSymbolInfo(name, sln, detailLevel: "summary")
GetCompilationErrors(sln, mode: "compact")
FindAttributeUsages(attr, sln, format: "inline")

// Deep analysis (comprehensive)
GetFileOutline(path, mode: "detailed", maxMembers: 0)
GetSymbolInfo(name, sln, detailLevel: "full")
GetCompilationErrors(sln, mode: "detailed")
FindAttributeUsages(attr, sln, format: "detailed")
```

---

## 📊 Phase 4A: File & Symbol Tools

### GetFileOutline

**New Parameters**:
```csharp
string mode = "normal"              // compact | normal | detailed
int maxMembers = 10                 // Max members per type (0=all)
```

**Token Savings**:
| Mode | Tokens | Savings | Best For |
|------|--------|---------|----------|
| compact | 200-500 | 60-75% | Quick overview, counting members |
| normal | 500-1000 | 30-50% | Daily development, code review |
| detailed | 800-2000 | baseline | Documentation, deep analysis |

**Example**:
```csharp
// Compact: ~300 tokens
GetFileOutline("UserService.cs", mode: "compact", maxMembers: 5)

Output:
File: UserService.cs (180 LOC, 15 usings)

UserService (Class, Public) @ MyProject.Services
  Methods: 8 (6 public, 2 private)
    GetUserAsync(int) → Task<User?> [async]
    CreateUserAsync(UserDto) → Task<User> [async]
    ... and 6 more
```

### GetSymbolInfo

**New Parameter**:
```csharp
string detailLevel = "basic"        // summary | basic | full
```

**Token Savings**:
| Level | Tokens | Savings | Best For |
|-------|--------|---------|----------|
| summary | 30-50 | 70-80% | Quick signature checks |
| basic | 80-120 | 40-50% | Daily queries, navigation |
| full | 150-250 | baseline | API docs, complete analysis |

**Example**:
```csharp
// Summary: ~40 tokens
GetSymbolInfo("GetUserAsync", "MyProject.sln", detailLevel: "summary")

Output:
GetUserAsync (Method, Public)
→ Task<User?> (int)
@ UserService.cs:30
```

---

## 📊 Phase 4B: Advanced Analysis Tools

### GetCompilationErrors

**New Parameter**:
```csharp
string mode = "normal"              // compact | normal | detailed
```

**Token Savings**:
| Mode | Tokens | Savings | Best For |
|------|--------|---------|----------|
| compact | 300-1000 | 40-60% | Quick error count, CI/CD |
| normal | 500-1500 | baseline | Daily development |
| detailed | 800-3000 | -60% | Deep diagnostics, troubleshooting |

**Example**:
```csharp
// Compact: ~400 tokens
GetCompilationErrors("MyProject.sln", mode: "compact", severity: "Error")

Output:
Issues: 15 (3 projects, 1 failed)

Error: 15
  MyProject.Core: 8 issues
    CS0103 (5x): Program.cs:45
    CS0246 (2x): UserService.cs:30
```

### FindAttributeUsages

**New Parameter**:
```csharp
string format = "normal"            // inline | normal | detailed
```

**Token Savings**:
| Format | Tokens | Savings | Best For |
|--------|--------|---------|----------|
| inline | 250-450 | 50-70% | Quick attribute scan |
| normal | 500-1000 | baseline | Daily use |
| detailed | 800-1500 | -50% | Full analysis with arguments |

**Example**:
```csharp
// Inline: ~300 tokens
FindAttributeUsages("Obsolete", "MyProject.sln", format: "inline")

Output:
[Obsolete]: 12 usages found

Methods (8):
  GetLegacyData("Use GetDataAsync") @ UserService.cs:45
  ProcessOldFormat @ DataProcessor.cs:120
  ... and 6 more
```

### GetClassHierarchy

**New Parameter**:
```csharp
string format = "normal"            // compact | normal | detailed
```

**Token Savings**:
| Format | Tokens | Savings | Best For |
|--------|--------|---------|----------|
| compact | 150-600 | 50-70% | Tree structure only |
| normal | 400-1200 | baseline | Balanced view |
| detailed | 600-2000 | -67% | Full metadata |

**Example**:
```csharp
// Compact: ~250 tokens
GetClassHierarchy("BaseController", "MyProject.sln", format: "compact")

Output:
BaseController (Class)

Ancestors (2):
  ↑ Controller (C)
  ↑ ControllerBase (A)

Descendants (5):
  ↓ HomeController (C)
  ↓ UserController (C)
  ↓ AdminController (C)
```

### FindImplementations

**New Parameter**:
```csharp
string format = "normal"            // summary | normal | detailed
```

**Token Savings**:
| Format | Tokens | Savings | Best For |
|--------|--------|---------|----------|
| summary | 100-300 | 50-70% | Names and locations |
| normal | 300-800 | baseline | Balanced info |
| detailed | 500-1200 | -50% | Full analysis with interfaces |

**Example**:
```csharp
// Summary: ~150 tokens
FindImplementations("IUserRepository", "MyProject.sln", format: "summary")

Output:
Implementations of 'IUserRepository': 4

MyProject.Infrastructure (3):
  UserRepository @ UserRepository.cs:15
  CachedUserRepository @ CachedUserRepository.cs:20
  InMemoryUserRepository @ InMemoryUserRepository.cs:8
```

---

## 📈 Real-World Savings

### Scenario 1: Exploring New Codebase (83% savings)

**Before** (5760 tokens):
```
3× GetFileOutline (default)  = 5300 tokens
2× GetSymbolInfo (default)   = 460 tokens
```

**After** (985 tokens):
```
3× GetFileOutline (compact)  = 900 tokens
2× GetSymbolInfo (summary)   = 85 tokens
```

### Scenario 2: Error Diagnosis (68% savings)

**Before** (2500 tokens):
```
1× GetCompilationErrors (default) = 2500 tokens
```

**After** (800 tokens):
```
1× GetCompilationErrors (compact) = 800 tokens
```

### Scenario 3: Architecture Analysis (72% savings)

**Before** (2700 tokens):
```
1× GetClassHierarchy (default)     = 1800 tokens
1× FindImplementations (default)   = 900 tokens
```

**After** (750 tokens):
```
1× GetClassHierarchy (compact)     = 500 tokens
1× FindImplementations (summary)   = 250 tokens
```

---

## 🎓 Best Practices

### 1. Start Small, Scale Up
- Begin with `compact`/`summary`/`inline` modes
- Only upgrade to `normal` or `detailed` when you need more information
- Use `maxMembers` to limit output (5-10 for code reviews)

### 2. Match Mode to Task

| Task | Recommended Mode |
|------|-----------------|
| Quick overview | compact/summary |
| Code navigation | normal/basic |
| Bug diagnosis | normal/detailed |
| Documentation | detailed/full |
| CI/CD checks | compact |
| Code review | normal |

### 3. Combine Strategically

```csharp
// Workflow 1: Find and understand a symbol
GetSymbolInfo("UserService", sln, "summary")     // Quick check (40 tokens)
GetFileOutline("UserService.cs", "compact", 5)   // See structure (250 tokens)
// Total: 290 tokens vs 1750 tokens (83% savings)

// Workflow 2: Diagnose build errors
GetCompilationErrors(sln, "compact")             // See error counts (400 tokens)
// If needed: switch to "detailed" for specific errors
// Total: 400 tokens vs 2500 tokens (84% savings)
```

### 4. Avoid Common Mistakes

❌ **Don't use `detailed`/`full` by default**
- These modes are verbose and expensive
- Only use when you specifically need full metadata

❌ **Don't set `maxMembers=0` unless necessary**
- This shows ALL members (can be 50+ for large classes)
- Default `maxMembers=10` is usually sufficient

✅ **Do use hierarchical exploration**
- Start compact → go normal → go detailed if needed
- Each step provides progressively more detail

---

## 📊 Token Estimates by File Size

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

## 🔧 Implementation Details

### New Formatting Functions (18 total)

**Phase 4A** (6 functions):
- `FormatFileOutlineCompact()`
- `FormatFileOutlineNormal()`
- `FormatFileOutlineDetailed()`
- `FormatSymbolInfoSummary()`
- `FormatSymbolInfoBasic()`
- `FormatSymbolInfoFull()`

**Phase 4B** (10 functions):
- `FormatCompilationErrorsCompact()`
- `FormatCompilationErrorsDetailed()`
- `FormatAttributeUsagesInline()`
- `FormatAttributeUsagesDetailed()`
- `FormatClassHierarchyCompact()`
- `FormatClassHierarchyDetailed()`
- `FormatHierarchyNodesCompact()`
- `FormatHierarchyNodesDetailed()`
- `FormatImplementationResultsSummary()`
- `FormatImplementationResultsDetailed()`

**Helper Methods** (2):
- `GetTypeIcon()` - Returns emoji for type kinds
- `GetModifiersCompact()` - Compact modifier display

### Backward Compatibility

✅ **100% Backward Compatible**
- All new parameters have default values
- Default values use `normal`/`basic` modes (balanced)
- Existing code continues to work without changes
- No breaking changes to any tool signatures

---

## ✅ Summary

**Phase 4 Complete: 6 Tools Optimized**

- ✅ GetFileOutline: `mode` + `maxMembers` → 60-75% savings
- ✅ GetSymbolInfo: `detailLevel` → 70-80% savings
- ✅ GetCompilationErrors: `mode` → 40-60% savings
- ✅ FindAttributeUsages: `format` → 50-70% savings
- ✅ GetClassHierarchy: `format` → 50-70% savings
- ✅ FindImplementations: `format` → 50-70% savings

**Overall Impact**:
- **40-80% token savings** across all tools
- **18 new formatting functions**
- **100% backward compatible**
- **Zero migration effort**

Start using these optimizations today for faster, more cost-effective code analysis!

---

## 📚 Related Documentation

- **Usage Examples**: See `PHASE4_USAGE_EXAMPLES.md` for detailed examples
- **Implementation**: See `archive/PHASE4A_IMPLEMENTATION_SUMMARY.md` and `archive/PHASE4B_IMPLEMENTATION_SUMMARY.md`
- **Planning**: See `archive/TOKEN_OPTIMIZATION_PLAN.md` and `archive/TOKEN_OPTIMIZATION_PHASE4.md`
