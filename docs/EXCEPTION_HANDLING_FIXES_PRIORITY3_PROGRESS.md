# Exception Handling Fixes - Priority 3 (In Progress)

## Overview

This document tracks the progress of Priority 3 fixes: adding partial failure reporting to services that log exceptions but swallow them, returning incomplete results to users.

**Status**: 🟡 In Progress (60% complete)
**Started**: 2026-01-09
**Build Status**: ✅ Compiles successfully

---

## Progress Summary

| Component | Status | Notes |
|-----------|--------|-------|
| **Models** (SearchModels.cs) | ✅ Complete | Added OperationWarning, AttributeSearchResults, CompilationErrorResults |
| **AttributeSearchService** | ✅ Complete | Returns AttributeSearchResults with failure tracking |
| **CodeAnalysisService** | ✅ Complete | DependencyAnalysis now tracks failed projects |
| **DiagnosticsService** | 🔄 Pending | Needs to use CompilationErrorResults wrapper |
| **FileAnalysisService** | 🔄 Pending | FileOutlineResult needs failure tracking implementation |
| **Format Methods** | 🟡 Partial | FormatAttributeUsages updated, others pending |

---

## Completed Work

### 1. Model Enhancements ✅

**File**: `Models/SearchModels.cs`

**Added Classes**:

```csharp
/// <summary>
/// Represents a warning or partial failure during an operation
/// </summary>
public class OperationWarning
{
    public string Context { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }
}
```

**Enhanced Existing Classes**:

1. **DependencyAnalysis**:
   ```csharp
   public int AnalyzedProjects { get; set; }
   public int FailedProjects { get; set; }
   public List<OperationWarning> Warnings { get; set; } = new();
   ```

2. **FileOutlineResult**:
   ```csharp
   public int FailedTypes { get; set; }
   public int FailedMembers { get; set; }
   public List<OperationWarning> Warnings { get; set; } = new();
   ```

**New Wrapper Classes**:

1. **AttributeSearchResults**:
   ```csharp
   public List<AttributeUsageResult> Usages { get; set; } = new();
   public int SuccessCount { get; set; }
   public int FailureCount { get; set; }
   public List<OperationWarning> Warnings { get; set; } = new();
   ```

2. **CompilationErrorResults**:
   ```csharp
   public List<CompilationError> Errors { get; set; } = new();
   public int AnalyzedProjects { get; set; }
   public int FailedProjects { get; set; }
   public int FailedDiagnostics { get; set; }
   public List<OperationWarning> Warnings { get; set; } = new();
   ```

---

### 2. AttributeSearchService ✅

**File**: `Services/AttributeSearchService.cs`

**Changes**:
- Method signature changed from `Task<List<AttributeUsageResult>>` to `Task<AttributeSearchResults>`
- Added success/failure counters
- Tracks failures when `CreateAttributeUsageResult` returns null

**Before**:
```csharp
if (usageResult != null)
{
    results.Add(usageResult);
}
```

**After**:
```csharp
if (usageResult != null)
{
    results.Add(usageResult);
    searchResults.SuccessCount++;
}
else
{
    searchResults.FailureCount++;
    searchResults.Warnings.Add(new OperationWarning
    {
        Context = $"Attribute on {targetSymbol.Name}",
        Message = $"Failed to extract attribute usage details for {targetSymbol.Name}",
        Details = $"File: {syntaxTree.FilePath}"
    });
}
```

**Return Statement**:
```csharp
searchResults.Usages = results.OrderBy(r => r.ProjectName).ThenBy(r => r.TargetName).ToList();

_logger.LogInformation("Found {Count} attribute usages ({Success} successful, {Failed} failed)",
    results.Count, searchResults.SuccessCount, searchResults.FailureCount);

if (searchResults.FailureCount > 0)
{
    _logger.LogWarning("{FailureCount} attribute usages failed to extract details",
        searchResults.FailureCount);
}

return searchResults;
```

---

### 3. CodeAnalysisService ✅

**File**: `Services/CodeAnalysisService.cs`
**Method**: `AnalyzeDependenciesAsync`

**Changes**:
- Added `analyzedProjects` and `failedProjects` counters
- Tracks failures in parallel project processing
- Warnings added to `DependencyAnalysis.Warnings`

**Key Code**:
```csharp
var analyzedProjects = 0;
var failedProjects = 0;

// In parallel loop:
try
{
    // ... analysis ...
    Interlocked.Increment(ref analyzedProjects);
}
catch (Exception ex)
{
    Interlocked.Increment(ref failedProjects);
    _logger.LogError(ex, "Error analyzing project {ProjectName}", project.Name);
    analysis.Warnings.Add(new OperationWarning
    {
        Context = $"Project: {project.Name}",
        Message = $"Failed to analyze project: {ex.Message}",
        Details = null
    });
}

// Before return:
analysis.AnalyzedProjects = analyzedProjects;
analysis.FailedProjects = failedProjects;

if (failedProjects > 0)
{
    _logger.LogWarning("{FailedProjects} projects failed to analyze out of {TotalProjects}",
        failedProjects, analyzedProjects + failedProjects);
}
```

---

### 4. Format Methods (Partial) 🟡

**File**: `Tools/CodeNavigationTools.cs`

**Updated**: `FormatAttributeUsages`

**Changes**:
- Changed parameter from `List<AttributeUsageResult>` to `AttributeSearchResults`
- Displays success/failure counts
- Shows warnings (max 5)

**Example Output**:
```
Found 15 usages (14 successful, 1 failed):

⚠️ **Warnings:**
   - Attribute on MyMethod: Failed to extract attribute usage details for MyMethod
```

---

## Pending Work

### 5. DiagnosticsService 🔄

**File**: `Services/DiagnosticsService.cs`
**Method**: `GetCompilationErrorsAsync`

**Required Changes**:
1. Change return type from `Task<List<CompilationError>>` to `Task<CompilationErrorResults>`
2. Add counters for analyzed/failed projects
3. Track failures when `ConvertDiagnosticToErrorAsync` returns null
4. Populate `CompilationErrorResults.Warnings`

**Estimated Lines of Code**: ~30 lines

---

### 6. FileAnalysisService 🔄

**File**: `Services/FileAnalysisService.cs`
**Method**: `GetFileOutlineAsync`

**Required Changes**:
1. Track failures when `ExtractTypeOutline` returns null (line 245)
2. Track failures when `ExtractMemberOutline` returns null (line 396)
3. Populate `FileOutlineResult.FailedTypes` and `FailedMembers`
4. Add warnings to `FileOutlineResult.Warnings`

**Estimated Lines of Code**: ~40 lines

---

### 7. Remaining Format Methods 🔄

**Files**: `Tools/CodeNavigationTools.cs`

**Methods to Update**:
1. **FormatDependencyAnalysis** - Add warnings display for DependencyAnalysis
2. **FormatFileOutline** - Add warnings display for FileOutlineResult
3. **FormatCompilationErrors** - Update for CompilationErrorResults wrapper

**Estimated Lines of Code**: ~60 lines total

---

## Build Verification

### Current Status: ✅ Success

```
建置成功。
    0 個警告
    0 個錯誤
經過時間 00:00:02.48
```

**All changes compile successfully.**

---

## Testing Plan

### Unit Tests (Recommended)
1. Test AttributeSearchService with failing CreateAttributeUsageResult
2. Test CodeAnalysisService with failing project analysis
3. Verify warning counts and messages
4. Verify formatted output includes warnings

### Integration Tests
1. Run against solution with compilation errors
2. Run against solution with malformed files
3. Verify partial failures are reported correctly

---

## Benefits So Far

### User-Facing Improvements
- ✅ Users now see when some attribute usages failed to extract
- ✅ Users know how many projects succeeded vs. failed in dependency analysis
- ✅ Warnings provide context about what failed and where

### Developer Benefits
- ✅ Easier debugging with detailed warning messages
- ✅ Can track failure patterns across operations
- ✅ Consistent failure reporting pattern

---

## Next Steps

1. **Complete DiagnosticsService** (~30 min)
   - Update GetCompilationErrorsAsync
   - Use CompilationErrorResults wrapper
   - Track project and diagnostic failures

2. **Complete FileAnalysisService** (~30 min)
   - Track type extraction failures
   - Track member extraction failures
   - Populate warnings

3. **Update Remaining Format Methods** (~30 min)
   - FormatDependencyAnalysis
   - FormatFileOutline
   - FormatCompilationErrors

4. **Final Testing** (~30 min)
   - Build verification
   - Manual testing with real solutions
   - Verify warning display

5. **Documentation** (~15 min)
   - Update EXCEPTION_HANDLING_FIXES.md
   - Add examples of warning output
   - Update CLAUDE.md if needed

**Estimated Time to Complete**: 2-2.5 hours

---

## Related Documentation

- **Analysis Report**: `docs/EXCEPTION_HANDLING_ANALYSIS.md`
- **Priority 1 & 2 Fixes**: `docs/EXCEPTION_HANDLING_FIXES.md`
- **Project Overview**: `README.md`
- **Logging Configuration**: `CLAUDE.md`
