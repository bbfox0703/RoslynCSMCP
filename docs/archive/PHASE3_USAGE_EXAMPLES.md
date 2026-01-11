# Phase 3: Advanced Filtering - Usage Examples

**Version**: 1.0
**Date**: 2026-01-09
**Status**: ✅ Implemented

---

## Overview

Phase 3 implements **FindReferencesFiltered**, an advanced filtering tool that reduces token consumption by 60-90% through intelligent reference filtering.

### New Tool

| Tool | Description | Token Savings |
|------|-------------|---------------|
| **FindReferencesFiltered** | Find references with advanced filtering options | 60-90% |

---

## 🎯 FindReferencesFiltered

### Purpose

Filter symbol references to focus on specific usage patterns, dramatically reducing noise and token consumption.

### Parameters

```csharp
FindReferencesFiltered(
    symbolName: string,           // Symbol name to search
    solutionPath: string,          // Path to .sln file
    detailLevel: string,           // "summary" | "locations" | "full"
    includeDefinition: bool,       // Include symbol definition
    publicOnly: bool,              // Only public API usage
    excludeTests: bool,            // Exclude test projects ⭐ Most useful
    crossProjectOnly: bool,        // Only cross-project references
    writesOnly: bool,              // Only write operations
    projectFilter: string          // Project name filter (wildcards: *, ?)
)
```

---

## 📚 Usage Examples

### Example 1: Exclude Test Projects ⭐ Most Common

**Scenario**: Find who uses `DeleteUser` in production code (exclude tests)

```
Find all references to DeleteUser in MySolution.sln, excluding test projects
```

**Behind the scenes**:
```csharp
FindReferencesFiltered(
    symbolName: "DeleteUser",
    solutionPath: "MySolution.sln",
    detailLevel: "summary",
    excludeTests: true
)
```

**Output**:
```
Filters applied: excluding tests

Found 12 references to 'DeleteUser' across 4 files:

📄 UserController.cs: 3 references
   Lines: 156, 234, 567

📄 UserService.cs: 1 reference (Definition)
   Lines: 89

📄 AdminController.cs: 5 references
   Lines: 45, 78, 112, 145, 189

📄 BackgroundJobs.cs: 3 references
   Lines: 67, 89, 102

Total: 12 references in 4 files across 3 projects
```

**Token Comparison**:
- Without filter: 100 references (incl. tests) = ~5,000 tokens
- With filter: 12 references (prod only) = ~600 tokens
- **Savings: 88%** 🎉

---

### Example 2: Cross-Project API Usage

**Scenario**: Find out which external projects depend on `UserService`

```
Show me which projects outside of MyProject.Services use UserService from MySolution.sln
```

**Behind the scenes**:
```csharp
FindReferencesFiltered(
    symbolName: "UserService",
    solutionPath: "MySolution.sln",
    detailLevel: "locations",
    crossProjectOnly: true,
    excludeTests: true
)
```

**Output**:
```
Filters applied: excluding tests, cross-project only

Found 8 references to 'UserService':

📄 UserController.cs (3 references)
  ✓ Line 23: Field Declaration
    private readonly UserService _userService;

  ✓ Line 45: Constructor Parameter
    public UserController(UserService userService)

  ✓ Line 156: Method Call
    var result = await _userService.GetUserAsync(id);

📄 AdminController.cs (5 references)
  ✓ Line 34: Field Declaration
    private readonly UserService _service;

  [... more references ...]
```

**Use Case**: Understanding API dependencies for refactoring or breaking changes.

---

### Example 3: Find Write Operations Only

**Scenario**: Find where `user.IsActive` property is being modified

```
Find all places where user.IsActive is written to in MySolution.sln
```

**Behind the scenes**:
```csharp
FindReferencesFiltered(
    symbolName: "IsActive",
    solutionPath: "MySolution.sln",
    detailLevel: "locations",
    writesOnly: true,
    excludeTests: true
)
```

**Output**:
```
Filters applied: excluding tests, writes only

Found 5 references to 'IsActive':

📄 UserService.cs (2 references)
  ✓ Line 89: Property Assignment
    user.IsActive = true;

  ✓ Line 145: Property Assignment
    user.IsActive = false;

📄 AdminService.cs (3 references)
  ✓ Line 67: Property Assignment
    user.IsActive = request.IsActive;

  [... more ...]
```

**Use Case**: Security audit, debugging state changes.

---

### Example 4: Filter by Project Pattern

**Scenario**: Find references only in WebAPI layer

```
Find references to ProcessPayment in WebAPI projects only from MySolution.sln
```

**Behind the scenes**:
```csharp
FindReferencesFiltered(
    symbolName: "ProcessPayment",
    solutionPath: "MySolution.sln",
    detailLevel: "summary",
    projectFilter: "*.WebAPI",
    excludeTests: true
)
```

**Output**:
```
Filters applied: excluding tests, project: *.WebAPI

Found 7 references to 'ProcessPayment' across 2 files:

📄 PaymentController.cs: 4 references
   Lines: 45, 78, 112, 145

📄 CheckoutController.cs: 3 references
   Lines: 234, 267, 289

Total: 7 references in 2 files across 1 project
```

**Use Case**: Layer-specific analysis, architectural validation.

---

### Example 5: Public API Usage Only

**Scenario**: Find only public API calls (exclude internal usage)

```
Find references to UserRepository in MySolution.sln, public API usage only
```

**Behind the scenes**:
```csharp
FindReferencesFiltered(
    symbolName: "UserRepository",
    solutionPath: "MySolution.sln",
    detailLevel: "summary",
    publicOnly: true,
    excludeTests: true
)
```

**Output**:
- If `UserRepository` is public: Shows all usage
- If `UserRepository` is internal/private: Returns empty (not part of public API)

**Use Case**: API documentation, public surface area analysis.

---

### Example 6: Combined Filters

**Scenario**: Complex filter - cross-project public API calls in non-test Web projects

```
Find cross-project references to AuthService from Web projects, excluding tests, in MySolution.sln
```

**Behind the scenes**:
```csharp
FindReferencesFiltered(
    symbolName: "AuthService",
    solutionPath: "MySolution.sln",
    detailLevel: "locations",
    publicOnly: true,
    excludeTests: true,
    crossProjectOnly: true,
    projectFilter: "*Web*"
)
```

**Output**:
```
Filters applied: excluding tests, cross-project only, public API only, project: *Web*

Found 4 references to 'AuthService':

📄 LoginController.cs (2 references)
  ✓ Line 34: Constructor Parameter
    public LoginController(AuthService authService)

  ✓ Line 67: Method Call
    await _authService.AuthenticateAsync(credentials);

[... more ...]
```

**Token Savings**: 95%+ in complex scenarios

---

## 🎯 Best Practices

### 1. Start with Summary

Always start with `detailLevel: "summary"` to get an overview:

```
Find references to DeleteUser in MySolution.sln with summary detail
```

If you need more detail, then use `locations` or `full`.

### 2. Exclude Tests by Default

For production code analysis, always use `excludeTests: true`:

```
Find references to PaymentService in MySolution.sln, excluding tests
```

### 3. Use Cross-Project for Dependencies

To understand external dependencies:

```
Find cross-project references to MyApiClient in MySolution.sln, excluding tests
```

### 4. Combine Filters for Precision

Don't hesitate to combine multiple filters:

```
Find write operations to Configuration in non-test WebAPI projects from MySolution.sln
```

---

## 📊 Token Optimization Comparison

### Scenario: Finding references to a popular method

**Unfiltered FindReferences**:
```
FindReferences("DeleteUser", "MySolution.sln", "full")
→ 100 references (80 in tests + 20 in prod)
→ Token usage: ~5,000 tokens
```

**Filtered with excludeTests**:
```
FindReferencesFiltered("DeleteUser", "MySolution.sln", excludeTests: true, detailLevel: "summary")
→ 20 references (prod only)
→ Token usage: ~300 tokens
→ Savings: 94% 🎉
```

**Further filtered with crossProjectOnly**:
```
FindReferencesFiltered("DeleteUser", "MySolution.sln", excludeTests: true, crossProjectOnly: true, detailLevel: "summary")
→ 5 references (external API usage only)
→ Token usage: ~100 tokens
→ Savings: 98% 🚀
```

---

## 🔍 Common Use Cases

### Security Audit
```
Find all write operations to SecurityToken in MySolution.sln, excluding tests
```

### API Impact Analysis
```
Find cross-project references to IUserService in MySolution.sln, excluding tests
```

### Layer Validation
```
Find references to DatabaseContext in WebAPI projects from MySolution.sln
```

### Refactoring Impact
```
Find references to LegacyPaymentService in non-test projects from MySolution.sln
```

---

## 💡 Tips & Tricks

### Tip 1: Wildcard Patterns

Project filter supports wildcards:
- `*.WebAPI` - Matches MyProject.WebAPI, MyProject.Admin.WebAPI
- `Test*` - Matches TestProject, TestHelpers, etc.
- `*Service*` - Matches MyProject.Services, ServiceTests, etc.

### Tip 2: Detail Level Strategy

1. **Summary**: Quick overview, file distribution
2. **Locations**: See code lines, understand context
3. **Full**: Deep dive with 5-line context

### Tip 3: Iterate Filters

Start broad, then narrow down:
```
1. excludeTests: true          → Still too many results?
2. + crossProjectOnly: true    → Still too many?
3. + projectFilter: "*.WebAPI" → Focused!
```

---

## 🚀 Next Steps

- Try FindReferencesFiltered on your codebase
- Experiment with different filter combinations
- Compare token usage with unfiltered FindReferences
- Share feedback for improvements

---

## 📞 Feedback

Phase 3 is designed to maximize productivity while minimizing token costs. Your feedback helps us improve!

