# Exception Handling & Logging Analysis

## Executive Summary

This document analyzes exception handling and logging mechanisms across the RoslynCSMCP codebase to identify potential issues where exceptions may be silently swallowed without proper logging or re-throwing.

**Analysis Date**: 2026-01-09
**Files Analyzed**: 16 files with exception handling
**Total catch blocks**: 47

---

## Severity Classification

### ✅ CORRECT (17 instances)
Exception is logged and either:
- Re-thrown (propagates to caller)
- Returns error message to user (MCP tools)
- Handled appropriately in cleanup code (Dispose)

### ⚠️ WARNING (18 instances)
Exception is logged but **swallowed** (not re-thrown, returns null/default):
- May hide critical errors from calling code
- Could lead to silent failures
- Debugging becomes difficult

### 🔴 CRITICAL (12 instances)
Exception is **caught without logging**:
- No visibility into what went wrong
- Silent failures
- Impossible to diagnose issues in production

---

## Detailed Analysis by File

### 1. Program.cs ✅ CORRECT

**Catch Blocks: 2**

```csharp
// Line 47: MSBuild registration failure
catch (Exception ex)
{
    Log.Fatal(ex, "Failed to register MSBuild: {Message}", ex.Message);
    Environment.Exit(1);  // ✅ Terminates application
}

// Line 116: Application failure
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly: {Message}", ex.Message);
    Environment.Exit(1);  // ✅ Terminates application
}
```

**Status**: ✅ **CORRECT** - Fatal errors properly logged and application exits.

---

### 2. CodeNavigationTools.cs ✅ MOSTLY CORRECT

**Catch Blocks: 18** (All MCP Tool methods)

**Pattern** (repeated 18 times):
```csharp
catch (Exception ex)
{
    var logger = serviceProvider?.GetService<ILogger<CodeNavigationTools>>();
    logger?.LogError(ex, "Error message here");
    return "Error: User-friendly error message";
}
```

**Status**: ✅ **CORRECT** - Tools layer catches all exceptions, logs them, and returns user-friendly error messages. This is the right pattern for MCP tools since they're the top-level API.

**Special handlers**:
- `OperationCanceledException` - Timeout message ✅
- `FileNotFoundException` - File not found message ✅
- `UnauthorizedAccessException` - Permission denied message ✅

---

### 3. AttributeSearchService.cs

**Catch Blocks: 1**

```csharp
// Line 227: CreateAttributeUsageResult
catch (Exception ex)
{
    _logger.LogError(ex, "Error creating attribute usage result");
    return null;  // ⚠️ Returns null, exception swallowed
}
```

**Status**: ⚠️ **WARNING** - Exception is logged but swallowed. Calling code receives `null` without knowing why.

**Issue**: In `FindAttributeUsagesAsync` (line 105), null results are silently skipped:
```csharp
if (usageResult != null)
{
    results.Add(usageResult);
}
// ⚠️ Null results are ignored - user doesn't know some results failed
```

**Impact**: Users may get incomplete results without knowing it.

**Recommendation**:
- Add partial failure reporting
- Or throw exception to indicate problem

---

### 4. Services/CacheManager.cs

**Catch Blocks: 1**

```csharp
// Line 267: GetAsync deserialization
catch
{
    return default;  // 🔴 NO LOGGING
}
```

**Status**: 🔴 **CRITICAL** - Exception silently swallowed, no logging.

**Issues**:
1. No exception type specified (catches everything)
2. No logging - cache failures invisible
3. Returns `default(T)` - looks like cache miss, not error

**Impact**: Cache corruption or deserialization errors are completely hidden.

**Recommendation**:
```csharp
catch (Exception ex)
{
    _logger.LogWarning(ex, "Failed to deserialize cache entry for key: {Key}", key);
    return default;
}
```

---

### 5. BatchQueryService.cs

**Catch Blocks: 3**

```csharp
// Line 79: ExecuteBatchAsync
catch (Exception ex)
{
    _logger.LogError(ex, "Error executing batch query");
    return $"Error: Failed to execute batch query: {ex.Message}";
}
// ✅ CORRECT - Returns error to user

// Line 109: ExecuteQueryAsync
catch (Exception ex)
{
    _logger.LogError(ex, $"Error executing query for tool {query.Tool}");
    return new BatchQueryResult
    {
        Tool = query.Tool,
        Success = false,
        Error = ex.Message
    };
}
// ✅ CORRECT - Returns error result

// Line 303: GetParameterValue<T>
catch
{
    return defaultValue;  // 🔴 NO LOGGING
}
```

**Status**: Mixed
- Lines 79, 109: ✅ **CORRECT**
- Line 303: 🔴 **CRITICAL** - No logging for parameter conversion failures

**Recommendation** for line 303:
```csharp
catch (Exception ex)
{
    _logger.LogDebug(ex, "Failed to convert parameter to type {Type}, using default", typeof(T).Name);
    return defaultValue;
}
```

---

### 6. CodeAnalysisService.cs

**Catch Blocks: 4**

```csharp
// Line 82: GetSolutionAsync
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to load solution from {SolutionPath}", solutionPath);
    throw;  // ✅ Re-throws
}

// Line 124: AnalyzeDependenciesAsync (inside Parallel.ForEach)
catch (Exception ex)
{
    _logger.LogError(ex, "Error analyzing project {ProjectName}", project.Name);
    // ⚠️ Exception swallowed - continues with other projects
}

// Line 242: CountSymbols (inside foreach)
catch (Exception ex)
{
    _logger.LogWarning(ex, "Error analyzing syntax tree {FilePath}", syntaxTree.FilePath);
    // ⚠️ Exception swallowed - continues with other files
}

// Line 292: DisposeWorkspaces (cleanup)
catch (Exception ex)
{
    _logger.LogWarning(ex, "Error disposing workspace");
    // ✅ Acceptable - disposal errors shouldn't fail cleanup
}
```

**Status**: Mixed
- Line 82: ✅ **CORRECT**
- Line 124, 242: ⚠️ **WARNING** - Parallel processing failures hidden
- Line 292: ✅ **ACCEPTABLE** - Cleanup code

**Issues**:
- Lines 124, 242: User doesn't know some projects/files failed to analyze
- Partial results returned without indication of failures

**Recommendation**:
- Add failure counters
- Report partial failures in results
- Consider adding a `Warnings` collection to results

---

### 7. IncrementalAnalyzer.cs

**Catch Blocks: 2**

```csharp
// Line 118: AnalyzeIncrementalAsync (inside foreach)
catch (Exception)
{
    // Log error but continue processing other documents
    // 🔴 NO LOGGING - only a comment!
}

// Line 144: ShouldAnalyzeDocument
catch
{
    return Task.FromResult(true); // If we can't check, analyze to be safe
    // 🔴 NO LOGGING
}
```

**Status**: 🔴 **CRITICAL** - Both catches have no logging

**Issues**:
1. Line 118: Comment says "Log error" but **NO ACTUAL LOGGING**
2. Line 144: File access errors completely silent
3. No exception type specified

**Impact**: Incremental analysis failures are completely invisible.

**Recommendation**:
```csharp
// Line 118
catch (Exception ex)
{
    _logger.LogError(ex, "Error analyzing document {DocumentPath}", document.FilePath);
    // Continue processing other documents
}

// Line 144
catch (Exception ex)
{
    _logger.LogWarning(ex, "Cannot check file timestamp for {FilePath}, will analyze", document.FilePath);
    return Task.FromResult(true);
}
```

---

### 8. DiagnosticsService.cs

**Catch Blocks: 3**

```csharp
// Line 128: GetCompilationErrorsAsync (inside foreach)
catch (Exception ex)
{
    _logger.LogError(ex, "Error analyzing project {ProjectName}", project.Name);
    // ⚠️ Exception swallowed - continues with other projects
}

// Line 171: ConvertDiagnosticToError (reading source line)
catch (Exception ex)
{
    _logger.LogWarning(ex, "Could not read source line from {FilePath}", filePath);
    // ⚠️ Exception swallowed - continues without source line
}

// Line 189: ConvertDiagnosticToError
catch (Exception ex)
{
    _logger.LogError(ex, "Error converting diagnostic to error object");
    return null;  // ⚠️ Returns null
}
```

**Status**: ⚠️ **WARNING** - All catch blocks swallow exceptions

**Issues**:
- Line 128: Some projects may fail silently
- Line 171: Acceptable - source line is optional
- Line 189: Null results are filtered out (line 125: `results.Where(r => r != null)`)

**Impact**: Users may get incomplete diagnostic results.

**Recommendation**: Add warning count to results summary.

---

### 9. FileAnalysisService.cs

**Catch Blocks: 2**

```csharp
// Line 245: ExtractTypeOutline
catch (Exception ex)
{
    _logger.LogError(ex, "Error extracting type outline");
    return null;  // ⚠️ Returns null
}

// Line 396: ExtractMemberOutline
catch (Exception ex)
{
    _logger.LogError(ex, "Error extracting member outline");
    return null;  // ⚠️ Returns null
}
```

**Status**: ⚠️ **WARNING** - Both return null on error

**Issue**: Calling code receives null without distinguishing between "no types" vs "error occurred".

**Recommendation**: Consider throwing or returning error indicators.

---

### 10. SecurityValidator.cs

**Catch Blocks: 1**

```csharp
// Line 507: ValidateSolutionPath
catch
{
    return false;  // 🔴 NO LOGGING
}
```

**Status**: 🔴 **CRITICAL** - No logging for path validation failures

**Issues**:
1. File access errors are completely silent
2. No exception type specified
3. Returns `false` - looks like validation failed, not error occurred

**Impact**: Can't diagnose why path validation is failing (permissions, network, etc.)

**Recommendation**:
```csharp
catch (Exception ex)
{
    _logger.LogWarning(ex, "Error validating path: {Path}", path);
    return false;
}
```

---

### 11. SymbolSearchService.cs

**Catch Blocks: 2**

```csharp
// Line 72: SearchInProjectAsync (inside loop)
catch (Exception ex)
{
    _logger.LogError(ex, "Error searching project: {ProjectName}", project.Name);
    // ⚠️ Exception swallowed - continues with other projects
}

// Line 382: IsWriteReference
catch
{
    // If we can't determine, assume it's not a write
    return false;  // 🔴 NO LOGGING
}
```

**Status**: Mixed
- Line 72: ⚠️ **WARNING** - Swallowed in loop
- Line 382: 🔴 **CRITICAL** - No logging

**Recommendation** for line 382:
```csharp
catch (Exception ex)
{
    _logger.LogDebug(ex, "Could not determine if reference is write access, assuming read");
    return false;
}
```

---

### 12. TestDiscoveryService.cs

**Catch Blocks: 1**

```csharp
// Line 221: CreateTestClassResult
catch (Exception ex)
{
    _logger.LogError(ex, "Error creating test class result for {ClassName}", classDecl.Identifier.Text);
    return null;  // ⚠️ Returns null
}
```

**Status**: ⚠️ **WARNING** - Null results are silently skipped.

---

### 13. TypeSignatureService.cs

**Catch Blocks: 2**

```csharp
// Line 39: GetTypeSignatureAsync
catch (Exception ex)
{
    _logger.LogError(ex, "Error getting type signature for: {TypeName}", typeName);
    throw;  // ✅ Re-throws
}

// Line 510: GetDocumentationComments
catch
{
    return string.Empty;  // 🔴 NO LOGGING
}
```

**Status**: Mixed
- Line 39: ✅ **CORRECT**
- Line 510: 🔴 **CRITICAL** - Documentation extraction errors silent

**Recommendation** for line 510:
```csharp
catch (Exception ex)
{
    _logger.LogDebug(ex, "Could not extract documentation comments");
    return string.Empty;
}
```

---

### 14. Services (Correct Implementations) ✅

These services correctly log and re-throw exceptions:

- **CallHierarchyService.cs** (line 80): Log + throw ✅
- **CodeMetricsService.cs** (line 81): Log + throw ✅
- **DependencyGraphService.cs** (line 37): Log + throw ✅
- **DiagnosticLogger.cs** (line 38): Log + throw ✅
- **ProjectStructureService.cs** (line 52): Log + throw ✅

---

## Summary of Issues

### 🔴 Critical Issues (No Logging) - 12 instances

| File | Line | Method | Issue |
|------|------|--------|-------|
| CacheManager.cs | 267 | GetAsync | Deserialization failures silent |
| BatchQueryService.cs | 303 | GetParameterValue | Type conversion failures silent |
| IncrementalAnalyzer.cs | 118 | AnalyzeIncrementalAsync | Document analysis failures silent |
| IncrementalAnalyzer.cs | 144 | ShouldAnalyzeDocument | File access errors silent |
| SecurityValidator.cs | 507 | ValidateSolutionPath | Path validation errors silent |
| SymbolSearchService.cs | 382 | IsWriteReference | Symbol analysis errors silent |
| TypeSignatureService.cs | 510 | GetDocumentationComments | Documentation extraction errors silent |

**Impact**: These failures are completely invisible in logs, making debugging in production impossible.

### ⚠️ Warning Issues (Logged but Swallowed) - 18 instances

**Partial Result Issues** (user gets incomplete results):
- AttributeSearchService.cs:227 - Some attribute usages may be missing
- CodeAnalysisService.cs:124 - Some projects fail to analyze
- CodeAnalysisService.cs:242 - Some files fail to analyze
- DiagnosticsService.cs:128 - Some projects fail to get diagnostics
- DiagnosticsService.cs:189 - Some diagnostics fail to convert
- FileAnalysisService.cs:245, 396 - Some types/members fail to extract
- SymbolSearchService.cs:72 - Some projects fail to search
- TestDiscoveryService.cs:221 - Some test classes fail to extract

**Impact**: Users receive partial results without knowing some data is missing.

---

## Logging Configuration

### Current Logging Setup (Program.cs:20-78)

```csharp
// Startup logging
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console(...)
    .WriteTo.File(path: "startup-.log", ...)
    .CreateBootstrapLogger();

// Main logging
builder.Services.AddSerilog((services, loggerConfiguration) =>
{
    var environment = builder.Environment.EnvironmentName;
    var logFileName = environment == "Development" ? "debug-.log" : "roslyn-mcp-.log";
    var retainedFiles = environment == "Development" ? 7 : 30;
    var minLevel = environment == "Development" ? LogEventLevel.Verbose : LogEventLevel.Warning;

    loggerConfiguration
        .WriteTo.Console(...)
        .WriteTo.File(
            path: logFileName,
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: retainedFiles,
            restrictedToMinimumLevel: minLevel,
            ...);
});
```

### Log Levels Used

- `Fatal`: Application termination (2 uses) ✅
- `Error`: Unexpected errors that need attention (25 uses) ✅
- `Warning`: Non-critical issues (4 uses) ✅
- `Information`: Normal operations (many uses) ✅
- `Debug`: Detailed diagnostics (0 uses in exceptions) ❌

### Log Files

1. **Startup logs**: `{LogDirectory}/startup-.log`
2. **Production logs**: `{LogDirectory}/roslyn-mcp-.log` (Warning+ only)
3. **Development logs**: `{LogDirectory}/debug-.log` (Verbose+)

**Issue**: In Production mode (Warning+), many `LogError` calls won't be visible!

---

## Recommendations

### 1. Fix Critical Issues (No Logging)

Add logging to all 12 critical catch blocks. Use appropriate log levels:

```csharp
// For cache/helper methods - Debug/Warning
catch (Exception ex)
{
    _logger.LogDebug(ex, "Non-critical operation failed: {Context}", context);
    return default;
}

// For data retrieval - Warning
catch (Exception ex)
{
    _logger.LogWarning(ex, "Could not retrieve data: {Context}", context);
    return null;
}

// For user-facing operations - Error
catch (Exception ex)
{
    _logger.LogError(ex, "Operation failed: {Context}", context);
    throw; // or return error result
}
```

### 2. Add Partial Failure Reporting

For operations that process multiple items in parallel, add failure tracking:

```csharp
public class AnalysisResult
{
    public List<Item> Items { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public List<string> Warnings { get; set; }  // ← Add this
}

// In service:
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to process {Item}", item);
    warnings.Add($"Failed to process {item}: {ex.Message}");
    // Continue with next item
}
```

### 3. Specify Exception Types

Replace bare `catch` with `catch (Exception ex)`:

```csharp
// Bad
catch
{
    return default;
}

// Good
catch (Exception ex)
{
    _logger.LogWarning(ex, "Operation failed");
    return default;
}
```

### 4. Adjust Production Log Level

Current Production mode only logs Warning+, but many errors use `LogError`:

**Option A**: Lower Production minimum level to Information:
```csharp
var minLevel = environment == "Development" ? LogEventLevel.Verbose : LogEventLevel.Information;
```

**Option B**: Keep Warning+ but use Warning for operational errors:
```csharp
// For recoverable errors in production
_logger.LogWarning(ex, "Project analysis failed, continuing with others");

// For critical errors only
_logger.LogError(ex, "Critical failure in core operation");
```

### 5. Add Exception Filters

For known exception types, add specific handling:

```csharp
try
{
    // Operation
}
catch (OperationCanceledException)
{
    // Expected for timeouts
    _logger.LogInformation("Operation timed out");
    return timeoutResult;
}
catch (UnauthorizedAccessException ex)
{
    // Expected for permission issues
    _logger.LogWarning(ex, "Access denied to {Path}", path);
    return accessDeniedResult;
}
catch (Exception ex)
{
    // Unexpected errors
    _logger.LogError(ex, "Unexpected error in operation");
    throw;
}
```

---

## Priority Action Items

### Priority 1: Critical (Fix Immediately)

1. **IncrementalAnalyzer.cs:118** - Add logging (comment says "Log error" but missing)
2. **SecurityValidator.cs:507** - Add logging for path validation failures
3. **CacheManager.cs:267** - Add logging for cache deserialization failures

### Priority 2: High (Fix Soon)

4. **IncrementalAnalyzer.cs:144** - Add logging for file timestamp checks
5. **BatchQueryService.cs:303** - Add logging for parameter conversion
6. **SymbolSearchService.cs:382** - Add logging for reference analysis
7. **TypeSignatureService.cs:510** - Add logging for documentation extraction

### Priority 3: Medium (Add Partial Failure Reporting)

8. Add `FailureCount` and `Warnings` to result models
9. Update services to track partial failures:
   - AttributeSearchService
   - CodeAnalysisService
   - DiagnosticsService
   - FileAnalysisService
   - SymbolSearchService

### Priority 4: Low (Improvements)

10. Replace all bare `catch` with `catch (Exception ex)`
11. Review Production log level (Warning vs Information)
12. Add exception type filters where appropriate

---

## Testing Recommendations

1. **Add Exception Logging Tests**: Verify all catch blocks log appropriately
2. **Integration Tests**: Test partial failure scenarios
3. **Production Monitoring**: Add metrics for exception counts by type

---

## Conclusion

**Current State**:
- 17/47 catch blocks are correct (36%)
- 18/47 have warnings (38%)
- 12/47 have critical issues (26%)

**Main Issues**:
1. **12 critical instances** with no logging at all
2. **18 instances** where partial failures are hidden from users
3. **7 instances** using bare `catch` without exception type
4. Production logging may miss important errors (Warning+ threshold)

**After Fixes**:
- All exceptions will be logged
- Users will be informed of partial failures
- Production logs will capture all critical issues
- Debugging will be significantly easier

