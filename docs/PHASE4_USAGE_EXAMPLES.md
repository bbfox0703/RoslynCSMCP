# Phase 4: Diagnostics & File Analysis - Usage Examples

**Version**: 1.0
**Date**: 2026-01-09
**Status**: ✅ Implemented

---

## Overview

Phase 4 implements two high-value features that dramatically reduce token consumption by providing quick access to compilation issues and file structure without reading full implementation details.

### New Tools

| Tool | Description | Token Savings |
|------|-------------|---------------|
| **GetCompilationErrors** | Get compilation errors and warnings without running full build | 97.5% |
| **GetFileOutline** | Get file structure outline without reading full implementation | 95% |

---

## 🎯 GetCompilationErrors

### Purpose

Quickly identify compilation errors and warnings across the solution without running a full build. This is especially useful when analyzing unfamiliar codebases or debugging build issues.

### Parameters

```csharp
GetCompilationErrors(
    solutionPath: string,        // Path to .sln file
    severity: string,            // "Error" | "Warning" | "Info" | "All" (default: "All")
    projectFilter: string?,      // Project name filter (wildcards: *, ?)
    errorCodes: string[]?        // Specific error codes (e.g., ["CS0103", "CS0246"])
)
```

---

## 📚 GetCompilationErrors Usage Examples

### Example 1: Check All Compilation Issues ⭐ Most Common

**Scenario**: Quickly check if a solution has any build issues

```
Get all compilation errors in MySolution.sln
```

**Behind the scenes**:
```csharp
GetCompilationErrors(
    solutionPath: "MySolution.sln",
    severity: "All"
)
```

**Output**:
```
**Compilation Diagnostics** (Severity: All)

Found 8 issues:

## Error (5)

### MyProject.Core (3 issues)

**CS0103**: The name 'user' does not exist in the current context
  📄 UserService.cs:45:12
  ```csharp
  var name = user.Name;
  ```

**CS0246**: The type or namespace name 'InvalidType' could not be found
  📄 DataAccess.cs:23:5
  ```csharp
  InvalidType obj = new InvalidType();
  ```

**CS1061**: 'string' does not contain a definition for 'Lenght'
  📄 StringHelper.cs:67:20
  ```csharp
  return str.Lenght > 0;
  ```

### MyProject.WebAPI (2 issues)

**CS0029**: Cannot implicitly convert type 'int' to 'string'
  📄 UserController.cs:89:16
  ```csharp
  string id = user.Id;
  ```

**CS0103**: The name 'logger' does not exist in the current context
  📄 HomeController.cs:34:9
  ```csharp
  logger.LogInformation("test");
  ```

## Warning (3)

### MyProject.Core (2 issues)

**CS0169**: The field 'UserService._cache' is never used
  📄 UserService.cs:12:25
  ```csharp
  private Dictionary<int, User> _cache;
  ```

**CS0618**: 'OldMethod()' is obsolete
  📄 LegacyCode.cs:56:9
  ```csharp
  OldMethod();
  ```

### MyProject.Tests (1 issue)

**CS0219**: The variable 'result' is assigned but its value is never used
  📄 UserServiceTests.cs:78:13
  ```csharp
  var result = service.GetUser(1);
  ```

---
**Summary**: 5 Errors, 3 Warnings, 0 Info
```

**Token Comparison**:
- Reading all source files to find errors: ~50,000 tokens
- GetCompilationErrors output: ~1,200 tokens
- **Savings: 97.5%** 🎉

---

### Example 2: Check Only Errors

**Scenario**: Focus only on critical compilation errors

```
Get compilation errors from MySolution.sln, severity Error
```

**Behind the scenes**:
```csharp
GetCompilationErrors(
    solutionPath: "MySolution.sln",
    severity: "Error"
)
```

**Output**:
```
**Compilation Diagnostics** (Severity: Error)

Found 5 issues:

## Error (5)

### MyProject.Core (3 issues)
[... only errors shown ...]

---
**Summary**: 5 Errors, 0 Warnings, 0 Info
```

**Use Case**: Pre-commit check, CI/CD validation.

---

### Example 3: Filter by Project Pattern

**Scenario**: Check compilation issues in specific projects

```
Get compilation errors from MySolution.sln, filter projects matching *.WebAPI
```

**Behind the scenes**:
```csharp
GetCompilationErrors(
    solutionPath: "MySolution.sln",
    severity: "All",
    projectFilter: "*.WebAPI"
)
```

**Output**:
```
**Compilation Diagnostics** (Severity: All)

Found 2 issues:

## Error (2)

### MyProject.WebAPI (2 issues)
[... only WebAPI project issues ...]

---
**Summary**: 2 Errors, 0 Warnings, 0 Info
```

**Use Case**: Layer-specific analysis, debugging specific subsystem.

---

### Example 4: Find Specific Error Codes

**Scenario**: Track down specific types of errors (e.g., missing usings)

```
Get compilation errors from MySolution.sln with error codes CS0103 and CS0246
```

**Behind the scenes**:
```csharp
GetCompilationErrors(
    solutionPath: "MySolution.sln",
    severity: "All",
    errorCodes: ["CS0103", "CS0246"]
)
```

**Output**:
```
**Compilation Diagnostics** (Severity: All)

Found 3 issues:

## Error (3)

### MyProject.Core (3 issues)

**CS0103**: The name 'user' does not exist in the current context
  📄 UserService.cs:45:12
  ```csharp
  var name = user.Name;
  ```

**CS0246**: The type or namespace name 'InvalidType' could not be found
  📄 DataAccess.cs:23:5
  ```csharp
  InvalidType obj = new InvalidType();
  ```

**CS0103**: The name 'logger' does not exist in the current context
  📄 HomeController.cs:34:9
  ```csharp
  logger.LogInformation("test");
  ```

---
**Summary**: 3 Errors, 0 Warnings, 0 Info
```

**Use Case**: Systematic fixing of specific error categories.

---

### Example 5: Check Build Success

**Scenario**: Verify solution has no errors before proceeding

```
Check if MySolution.sln has any compilation errors
```

**Output** (when successful):
```
No compilation error issues found. Solution builds successfully!
```

**Use Case**: Pre-deployment verification, automated testing.

---

## 📄 GetFileOutline

### Purpose

Get the structural outline of a C# file showing types, members, and documentation without reading full implementation details. This is extremely useful for understanding file structure without consuming thousands of tokens on method bodies.

### Parameters

```csharp
GetFileOutline(
    filePath: string,               // Path to .cs file
    includeMembers: bool,           // Include member details (default: true)
    includeDocumentation: bool      // Include XML doc comments (default: true)
)
```

---

## 📚 GetFileOutline Usage Examples

### Example 1: Quick File Overview ⭐ Most Common

**Scenario**: Understand the structure of a file without reading implementation

```
Get outline of UserService.cs
```

**Behind the scenes**:
```csharp
GetFileOutline(
    filePath: "D:\\MyProject\\Services\\UserService.cs",
    includeMembers: true,
    includeDocumentation: true
)
```

**Output**:
```
**File Outline**: UserService.cs

📊 **Statistics**:
  • Total Lines: 245
  • Code Lines: 180 (73.5%)
  • Comment Lines: 45 (18.4%)
  • Blank Lines: 20 (8.2%)

📦 **Using Statements** (8):
  • System
  • System.Collections.Generic
  • System.Linq
  • System.Threading.Tasks
  • Microsoft.Extensions.Logging
  • MyProject.Domain.Entities
  • MyProject.Domain.Interfaces
  • MyProject.Infrastructure.Data

🏷️ **Namespaces**: MyProject.Services

📋 **Types** (1):

🔷 **UserService** (Class, Public)
   Line 12
   💬 Service for managing user operations including CRUD and authentication
   ↗️ Inherits/Implements: IUserService
   📌 **Members** (15):
      📦 Fields (2):
         • Private readonly ILogger<UserService> _logger [static]
           Line 14
         • Private readonly IUserRepository _repository
           Line 15
      🏗️ Constructors (1):
         • Public UserService(ILogger<UserService> logger, IUserRepository repository)
           Line 17
           💬 Initializes a new instance of the UserService class
      ⚙️ Methods (10):
         • Public Task<User> GetUserAsync(int userId) [async]
           Line 28
           💬 Gets a user by ID
         • Public Task<IEnumerable<User>> GetAllUsersAsync() [async]
           Line 45
           💬 Gets all users from the repository
         • Public Task<User> CreateUserAsync(CreateUserRequest request) [async]
           Line 67
           💬 Creates a new user
         • Public Task<User> UpdateUserAsync(int userId, UpdateUserRequest request) [async]
           Line 89
         • Public Task<bool> DeleteUserAsync(int userId) [async]
           Line 112
         • Public Task<bool> ValidateUserAsync(string username, string password) [async]
           Line 134
         • Public Task<User> GetUserByEmailAsync(string email) [async]
           Line 156
         • Public Task<IEnumerable<User>> SearchUsersAsync(string query) [async]
           Line 178
         • Private Task<bool> ValidateEmailAsync(string email) [async]
           Line 201
         • Private void LogUserAction(string action, int userId)
           Line 223
      🔧 Properties (2):
         • Public int TotalUsers { get; }
           Line 238
         • Private bool IsInitialized { get; set; }
           Line 240
```

**Token Comparison**:
- Reading full file with implementation: ~8,000 tokens
- GetFileOutline output: ~400 tokens
- **Savings: 95%** 🎉

---

### Example 2: Members Only (No Documentation)

**Scenario**: Quick scan of available methods and properties

```
Get outline of UserController.cs without documentation
```

**Behind the scenes**:
```csharp
GetFileOutline(
    filePath: "D:\\MyProject\\Controllers\\UserController.cs",
    includeMembers: true,
    includeDocumentation: false
)
```

**Output**:
```
**File Outline**: UserController.cs

📊 **Statistics**:
  • Total Lines: 189
  • Code Lines: 145 (76.7%)
  • Comment Lines: 28 (14.8%)
  • Blank Lines: 16 (8.5%)

📦 **Using Statements** (6):
  • Microsoft.AspNetCore.Mvc
  • System.Threading.Tasks
  • MyProject.Services
  • MyProject.Models.Requests
  • MyProject.Models.Responses
  • Microsoft.AspNetCore.Authorization

🏷️ **Namespaces**: MyProject.Controllers

📋 **Types** (1):

🔷 **UserController** (Class, Public)
   Line 10
   ↗️ Inherits/Implements: ControllerBase
   🏷️ Attributes: [ApiController, Route, Authorize]
   📌 **Members** (7):
      📦 Fields (1):
         • Private readonly IUserService _userService
           Line 15
      🏗️ Constructors (1):
         • Public UserController(IUserService userService)
           Line 17
      ⚙️ Methods (5):
         • Public Task<ActionResult<UserResponse>> GetUser(int id) [async]
           Line 25
         • Public Task<ActionResult<IEnumerable<UserResponse>>> GetAllUsers() [async]
           Line 45
         • Public Task<ActionResult<UserResponse>> CreateUser(CreateUserRequest request) [async]
           Line 67
         • Public Task<ActionResult<UserResponse>> UpdateUser(int id, UpdateUserRequest request) [async]
           Line 89
         • Public Task<ActionResult> DeleteUser(int id) [async]
           Line 112
```

**Use Case**: Quick API surface scan, finding available methods.

---

### Example 3: Type Structure Only

**Scenario**: Just want to see what types are in the file

```
Get outline of Models.cs without members
```

**Behind the scenes**:
```csharp
GetFileOutline(
    filePath: "D:\\MyProject\\Models\\Models.cs",
    includeMembers: false
)
```

**Output**:
```
**File Outline**: Models.cs

📊 **Statistics**:
  • Total Lines: 312
  • Code Lines: 245 (78.5%)
  • Comment Lines: 42 (13.5%)
  • Blank Lines: 25 (8.0%)

📦 **Using Statements** (3):
  • System
  • System.Collections.Generic
  • System.ComponentModel.DataAnnotations

🏷️ **Namespaces**: MyProject.Models

📋 **Types** (8):

🔷 **User** (Class, Public)
   Line 8
   💬 Represents a user entity
   🏷️ Attributes: [Table]

🔷 **CreateUserRequest** (Class, Public)
   Line 45
   💬 Request model for creating a new user

🔷 **UpdateUserRequest** (Class, Public)
   Line 67
   💬 Request model for updating an existing user

🔷 **UserResponse** (Class, Public)
   Line 89
   💬 Response model for user operations

📝 **UserSummary** (Record, Public)
   Line 123
   💬 Lightweight summary of user information

🔹 **IUserValidator** (Interface, Public)
   Line 145
   💬 Validator interface for user operations

🔢 **UserRole** (Enum, Public)
   Line 167
   💬 User role enumeration

🔸 **UserStats** (Struct, Public)
   Line 189
   💬 User statistics structure
```

**Use Case**: Understanding file organization, refactoring planning.

---

## 🎯 Best Practices

### For GetCompilationErrors:

1. **Start with Overview**: Always check all issues first
   ```
   Get all compilation errors in MySolution.sln
   ```

2. **Focus on Errors**: Filter by severity to prioritize
   ```
   Get compilation errors from MySolution.sln, severity Error
   ```

3. **Use Project Filters**: Narrow down to specific subsystems
   ```
   Get compilation errors from MySolution.sln, filter by *.Tests
   ```

4. **Track Specific Issues**: Use error codes for systematic fixes
   ```
   Get errors CS0103 and CS0246 from MySolution.sln
   ```

### For GetFileOutline:

1. **Quick Overview**: Include members for complete picture
   ```
   Get outline of MyClass.cs
   ```

2. **API Discovery**: Skip documentation for faster scanning
   ```
   Get outline of MyService.cs without documentation
   ```

3. **Structure Analysis**: Exclude members to see type organization
   ```
   Get outline of Models.cs without members
   ```

---

## 📊 Token Optimization Impact

### Scenario 1: Finding Build Errors

**Traditional Approach**:
```
1. Read multiple files to find errors
2. Parse code manually
3. Identify issues
→ Token usage: ~50,000 tokens
```

**With GetCompilationErrors**:
```
GetCompilationErrors("MySolution.sln", "Error")
→ Token usage: ~1,000 tokens
→ Savings: 98% 🚀
```

---

### Scenario 2: Understanding File Structure

**Traditional Approach**:
```
Read entire file (245 lines with implementation)
→ Token usage: ~8,000 tokens
```

**With GetFileOutline**:
```
GetFileOutline("UserService.cs")
→ Token usage: ~400 tokens
→ Savings: 95% 🎉
```

---

## 🔍 Common Use Cases

### Pre-Commit Checks
```
Get all compilation errors in MySolution.sln
```

### API Discovery
```
Get outline of MyApiClient.cs
```

### Bug Investigation
```
Get compilation errors from MySolution.sln with error code CS0103
```

### Code Review Preparation
```
Get outline of NewFeature.cs with members
```

### Layer Violation Detection
```
Get compilation errors from *.Domain projects in MySolution.sln
```

---

## 💡 Tips & Tricks

### Tip 1: Combine with FindReferences

First, get file outline to understand structure:
```
Get outline of UserService.cs
```

Then find specific member references:
```
Find references to DeleteUserAsync in MySolution.sln, excluding tests
```

### Tip 2: Error Code Lookup

Common C# error codes to filter:
- **CS0103**: Name does not exist (missing using/variable)
- **CS0246**: Type not found (missing using/reference)
- **CS0029**: Type conversion errors
- **CS1061**: Missing member definition
- **CS0618**: Obsolete API usage

### Tip 3: Progressive Detail Levels

1. Start with type structure only (no members)
2. Add members if needed
3. Include documentation for deep understanding

---

## 🚀 Next Steps

- Try GetCompilationErrors on your solution
- Use GetFileOutline to explore unfamiliar files
- Combine with Phase 3 filtering tools for maximum efficiency
- Share feedback for improvements

---

## 📞 Feedback

Phase 4 provides critical diagnostic and structure information with minimal token cost. Your feedback helps us improve!
