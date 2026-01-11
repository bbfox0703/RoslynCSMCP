# Phase 4 Week 2: Navigation & Testing - Usage Examples

**Version**: 1.0
**Date**: 2026-01-09
**Status**: ✅ Implemented

---

## Overview

Phase 4 Week 2 implements two powerful navigation features that help understand code relationships and test coverage.

### New Tools

| Tool | Description | Token Savings |
|------|-------------|---------------|
| **FindImplementations** | Find all implementations of interfaces/abstract classes | 70-80% |
| **FindTestsForType** | Find test classes and methods for a given type | 90%+ |

---

## 🎯 FindImplementations

### Purpose

Find all concrete implementations of an interface or abstract class. Essential for understanding polymorphic code, dependency injection patterns, and design pattern implementations.

### Parameters

```csharp
FindImplementations(
    typeName: string,                        // Interface or abstract class name
    solutionPath: string,                    // Path to .sln file
    includeAbstractImplementations: bool     // Include abstract classes (default: false)
)
```

---

## 📚 FindImplementations Usage Examples

### Example 1: Find Interface Implementations ⭐ Most Common

**Scenario**: Find all classes implementing IUserRepository

```
Find implementations of IUserRepository in MySolution.sln
```

**Behind the scenes**:
```csharp
FindImplementations(
    typeName: "IUserRepository",
    solutionPath: "MySolution.sln",
    includeAbstractImplementations: false
)
```

**Output**:
```
**Implementations of 'IUserRepository'**

Found 3 implementations:

### MyProject.Data (2 implementations)

✅ **SqlUserRepository** (Public)
   📄 SqlUserRepository.cs:15
   📦 Namespace: MyProject.Data.Repositories
   💬 SQL Server implementation of IUserRepository
   ↗️ Base Class: RepositoryBase<User>
   🔹 Also Implements: IDisposable

✅ **InMemoryUserRepository** (Public)
   📄 InMemoryUserRepository.cs:23
   📦 Namespace: MyProject.Data.InMemory
   💬 In-memory implementation for testing

### MyProject.Services (1 implementation)

✅ **CachedUserRepository** (Public [sealed])
   📄 CachedUserRepository.cs:34
   📦 Namespace: MyProject.Services.Cache
   💬 Decorator that adds caching to IUserRepository
   🔹 Also Implements: IDisposable

---
**Summary**: 3 concrete, 0 abstract implementations across 2 projects
```

**Token Comparison**:
- Manual search (reading multiple files): ~5,000 tokens
- FindImplementations output: ~300 tokens
- **Savings: 94%** 🎉

---

### Example 2: Find Abstract Class Implementations

**Scenario**: Find all classes inheriting from ServiceBase

```
Find implementations of ServiceBase in MySolution.sln
```

**Behind the scenes**:
```csharp
FindImplementations(
    typeName: "ServiceBase",
    solutionPath: "MySolution.sln",
    includeAbstractImplementations: false
)
```

**Output**:
```
**Implementations of 'ServiceBase'**

Found 5 implementations:

### MyProject.Services (5 implementations)

✅ **UserService** (Public)
   📄 UserService.cs:12
   📦 Namespace: MyProject.Services
   💬 Service for managing user operations
   ↗️ Base Class: ServiceBase
   🔹 Also Implements: IUserService, IDisposable

✅ **OrderService** (Public)
   📄 OrderService.cs:18
   📦 Namespace: MyProject.Services
   ↗️ Base Class: ServiceBase
   🔹 Also Implements: IOrderService

✅ **PaymentService** (Public [sealed])
   📄 PaymentService.cs:25
   📦 Namespace: MyProject.Services
   💬 Handles payment processing
   ↗️ Base Class: ServiceBase
   🔹 Also Implements: IPaymentService

✅ **NotificationService** (Public)
   📄 NotificationService.cs:32
   📦 Namespace: MyProject.Services
   ↗️ Base Class: ServiceBase
   🔹 Also Implements: INotificationService

✅ **AuthenticationService** (Internal)
   📄 AuthenticationService.cs:45
   📦 Namespace: MyProject.Services.Auth
   ↗️ Base Class: ServiceBase
   🔹 Also Implements: IAuthenticationService

---
**Summary**: 5 concrete, 0 abstract implementations across 1 project
```

**Use Case**: Understanding service architecture, refactoring base classes.

---

### Example 3: Include Abstract Implementations

**Scenario**: Find all implementations including abstract ones

```
Find implementations of ICommand in MySolution.sln, include abstract implementations
```

**Behind the scenes**:
```csharp
FindImplementations(
    typeName: "ICommand",
    solutionPath: "MySolution.sln",
    includeAbstractImplementations: true
)
```

**Output**:
```
**Implementations of 'ICommand'**

Found 8 implementations:

### MyProject.Core (2 implementations)

🔷 **CommandBase** (Public [abstract])
   📄 CommandBase.cs:10
   📦 Namespace: MyProject.Core.Commands
   💬 Base implementation of ICommand with common logic

✅ **SimpleCommand** (Public)
   📄 SimpleCommand.cs:45
   📦 Namespace: MyProject.Core.Commands
   ↗️ Base Class: CommandBase

### MyProject.Commands (6 implementations)

✅ **CreateUserCommand** (Public)
   📄 CreateUserCommand.cs:12
   📦 Namespace: MyProject.Commands.Users
   ↗️ Base Class: CommandBase
   💬 Creates a new user account

✅ **UpdateUserCommand** (Public)
   📄 UpdateUserCommand.cs:28
   📦 Namespace: MyProject.Commands.Users
   ↗️ Base Class: CommandBase

✅ **DeleteUserCommand** (Public)
   📄 DeleteUserCommand.cs:34
   📦 Namespace: MyProject.Commands.Users
   ↗️ Base Class: CommandBase

✅ **SendEmailCommand** (Public [sealed])
   📄 SendEmailCommand.cs:42
   📦 Namespace: MyProject.Commands.Email
   ↗️ Base Class: CommandBase

✅ **ProcessPaymentCommand** (Public)
   📄 ProcessPaymentCommand.cs:56
   📦 Namespace: MyProject.Commands.Payments
   ↗️ Base Class: CommandBase

✅ **GenerateReportCommand** (Internal)
   📄 GenerateReportCommand.cs:67
   📦 Namespace: MyProject.Commands.Reports
   ↗️ Base Class: CommandBase

---
**Summary**: 7 concrete, 1 abstract implementations across 2 projects
```

**Use Case**: Understanding inheritance hierarchies, CQRS pattern analysis.

---

### Example 4: No Implementations Found

**Scenario**: Type is not an interface or has no implementations

```
Find implementations of UserService in MySolution.sln
```

**Output** (if UserService is a concrete class):
```
No implementations found for 'UserService'.
Note: UserService must be an interface or abstract class.
```

**Use Case**: Verification, debugging.

---

## 🧪 FindTestsForType

### Purpose

Find all test classes and test methods for a given type. Essential for TDD workflows, understanding test coverage, and ensuring code is properly tested.

### Parameters

```csharp
FindTestsForType(
    typeName: string,                    // Type name to find tests for
    solutionPath: string,                // Path to .sln file
    includePartialMatches: bool          // Include partial name matches (default: true)
)
```

**Naming Convention Detection**:
- `{TypeName}Tests` (e.g., UserServiceTests)
- `{TypeName}Test`
- `Test{TypeName}`
- `{TypeName}_Tests`
- `{TypeName}_Should`
- Partial matches: `*UserService*Tests`

**Framework Detection**:
- xUnit: `[Fact]`, `[Theory]`
- NUnit: `[Test]`, `[TestCase]`
- MSTest: `[TestMethod]`, `[DataTestMethod]`

---

## 📚 FindTestsForType Usage Examples

### Example 1: Find All Tests for a Type ⭐ Most Common

**Scenario**: Find all tests for UserService

```
Find tests for UserService in MySolution.sln
```

**Behind the scenes**:
```csharp
FindTestsForType(
    typeName: "UserService",
    solutionPath: "MySolution.sln",
    includePartialMatches: true
)
```

**Output**:
```
**Test Classes for 'UserService'**

Found 3 test classes with 28 tests total:

### MyProject.UnitTests (18 tests)

🧪 **UserServiceTests** - 15 tests
   📄 UserServiceTests.cs:12
   🔬 Framework: xUnit
   💬 Unit tests for UserService functionality
   📋 **Test Methods**:
      ✓ GetUserAsync_WithValidId_ReturnsUser [Fact]
        Line 25
      ✓ GetUserAsync_WithInvalidId_ReturnsNull [Fact]
        Line 45
      ✓ GetUserAsync_WithNegativeId_ThrowsArgumentException [Fact]
        Line 67
      ✓ CreateUserAsync_WithValidDto_CreatesUser [Fact]
        Line 89
      ✓ CreateUserAsync_WithNullDto_ThrowsArgumentNullException [Fact]
        Line 112
      ✓ CreateUserAsync_WithDuplicateEmail_ThrowsInvalidOperationException [Fact]
        Line 134
      ✓ UpdateUserAsync_WithValidData_UpdatesUser [Fact]
        Line 156
      ✓ UpdateUserAsync_WithNonExistentUser_ThrowsNotFoundException [Fact]
        Line 178
      ✓ DeleteUserAsync_WithValidId_DeletesUser [Fact]
        Line 201
      ✓ DeleteUserAsync_WithNonExistentId_ReturnsFalse [Fact]
        Line 223
      ... and 5 more tests

🧪 **UserServiceCacheTests** - 3 tests
   📄 UserServiceCacheTests.cs:89
   🔬 Framework: xUnit
   💬 Tests for UserService caching behavior
   📋 **Test Methods**:
      ✓ GetUserAsync_CachesResult [Fact]
        Line 98
      ✓ UpdateUserAsync_InvalidatesCache [Fact]
        Line 123
      ✓ DeleteUserAsync_InvalidatesCache [Fact]
        Line 145

### MyProject.IntegrationTests (10 tests)

🧪 **UserServiceIntegrationTests** - 10 tests
   📄 UserServiceIntegrationTests.cs:15
   🔬 Framework: xUnit
   💬 Integration tests with real database
   📋 **Test Methods**:
      ✓ CreateAndRetrieveUser_Success [Fact]
        Line 28
      ✓ UpdateUser_WithConcurrentModification_HandlesConflict [Fact]
        Line 56
      ✓ DeleteUser_CascadesRelatedData [Fact]
        Line 89
      ✓ GetUsersByRole_FiltersCorrectly [Fact]
        Line 112
      ✓ SearchUsers_WithWildcard_ReturnsMatches [Fact]
        Line 134
      ✓ CreateUser_WithTransaction_RollsBackOnError [Fact]
        Line 167
      ✓ BulkCreateUsers_PerformanceTest [Fact]
        Line 201
      ✓ GetUser_WithIncludeOptions_LoadsRelatedEntities [Fact]
        Line 234
      ✓ UpdateUser_WithOptimisticLocking_PreventsConflicts [Fact]
        Line 267
      ✓ DeleteUser_SoftDeletesInsteadOfHardDelete [Fact]
        Line 289

---
**Summary by Framework**:
  • xUnit: 3 classes, 28 tests
```

**Token Comparison**:
- Reading test files manually: ~10,000 tokens
- FindTestsForType output: ~800 tokens
- **Savings: 92%** 🎉

---

### Example 2: Exact Match Only

**Scenario**: Find tests with exact naming convention only

```
Find tests for PaymentService in MySolution.sln, exact match only
```

**Behind the scenes**:
```csharp
FindTestsForType(
    typeName: "PaymentService",
    solutionPath: "MySolution.sln",
    includePartialMatches: false
)
```

**Output**:
```
**Test Classes for 'PaymentService'**

Found 1 test class with 12 tests total:

### MyProject.Tests (12 tests)

🧪 **PaymentServiceTests** - 12 tests
   📄 PaymentServiceTests.cs:20
   🔬 Framework: NUnit
   📋 **Test Methods**:
      ✓ ProcessPayment_WithValidCard_Succeeds [Test]
        Line 34
      ✓ ProcessPayment_WithInsufficientFunds_ThrowsException [Test]
        Line 56
      ✓ ProcessPayment_WithExpiredCard_ThrowsException [Test]
        Line 78
      ... and 9 more tests

---
**Summary by Framework**:
  • NUnit: 1 class, 12 tests
```

**Use Case**: Strict naming convention adherence.

---

### Example 3: Multiple Test Frameworks

**Scenario**: Type tested with multiple frameworks

```
Find tests for AuthService in MySolution.sln
```

**Output**:
```
**Test Classes for 'AuthService'**

Found 4 test classes with 35 tests total:

### MyProject.UnitTests (15 tests)

🧪 **AuthServiceTests** - 15 tests
   📄 AuthServiceTests.cs:10
   🔬 Framework: xUnit
   💬 Unit tests for authentication logic

### MyProject.IntegrationTests (10 tests)

🧪 **AuthServiceIntegrationTests** - 10 tests
   📄 AuthServiceIntegrationTests.cs:45
   🔬 Framework: xUnit
   💬 Integration tests with identity server

### MyProject.LegacyTests (7 tests)

🧪 **AuthServiceTest** - 7 tests
   📄 AuthServiceTest.cs:123
   🔬 Framework: MSTest
   💬 Legacy test suite (MSTest)

### MyProject.BehaviorTests (3 tests)

🧪 **AuthServiceBehaviorTests** - 3 tests
   📄 AuthServiceBehaviorTests.cs:67
   🔬 Framework: NUnit
   💬 Behavior-driven tests

---
**Summary by Framework**:
  • xUnit: 2 classes, 25 tests
  • MSTest: 1 class, 7 tests
  • NUnit: 1 class, 3 tests
```

**Use Case**: Mixed framework projects, migration planning.

---

### Example 4: No Tests Found

**Scenario**: Type has no tests

```
Find tests for LegacyUtility in MySolution.sln
```

**Output**:
```
No test classes found for 'LegacyUtility'.
Note: Searched test projects for classes matching naming conventions (e.g., LegacyUtilityTests, LegacyUtilityTest).
```

**Use Case**: Test coverage analysis, identifying untested code.

---

### Example 5: Test Method Details

**Scenario**: Detailed test information with display names

```
Find tests for Calculator in MySolution.sln
```

**Output**:
```
**Test Classes for 'Calculator'**

Found 1 test class with 8 tests total:

### MyProject.Tests (8 tests)

🧪 **CalculatorTests** - 8 tests
   📄 CalculatorTests.cs:5
   🔬 Framework: xUnit
   📋 **Test Methods**:
      ✓ Add_WithPositiveNumbers_ReturnsSum [Fact] - "2 + 2 should equal 4"
        Line 15
      ✓ Add_WithNegativeNumbers_ReturnsSum [Fact] - "(-1) + (-1) should equal -2"
        Line 28
      ✓ Subtract_WithPositiveNumbers_ReturnsDifference [Fact]
        Line 41
      ✓ Multiply_WithZero_ReturnsZero [Fact] - "Any number × 0 = 0"
        Line 54
      ✓ Divide_WithZero_ThrowsDivideByZeroException [Fact]
        Line 67
      ✓ Divide_WithValidNumbers_ReturnsQuotient [Theory]
        Line 80
      ✓ Add_ParameterizedTests [Theory] - "Parameterized addition tests"
        Line 93
      ✓ SquareRoot_WithNegativeNumber_ThrowsException [Fact]
        Line 106

---
**Summary by Framework**:
  • xUnit: 1 class, 8 tests
```

**Use Case**: Understanding test intent, documentation.

---

## 🎯 Best Practices

### For FindImplementations:

1. **Start with Interfaces**: Most valuable for dependency injection analysis
   ```
   Find implementations of IUserRepository in MySolution.sln
   ```

2. **Check Abstract Classes**: Understand inheritance hierarchies
   ```
   Find implementations of ServiceBase in MySolution.sln
   ```

3. **Include Abstract for Full Picture**: See complete inheritance chain
   ```
   Find implementations of ICommand in MySolution.sln, include abstract
   ```

4. **Verify Design Patterns**: Check decorator, strategy, factory patterns
   ```
   Find implementations of IPaymentProcessor in MySolution.sln
   ```

### For FindTestsForType:

1. **TDD Workflow**: Check existing tests before adding features
   ```
   Find tests for UserService in MySolution.sln
   ```

2. **Coverage Analysis**: Identify untested code
   ```
   Find tests for LegacyModule in MySolution.sln
   ```

3. **Test Refactoring**: See all related tests before refactoring
   ```
   Find tests for PaymentProcessor in MySolution.sln
   ```

4. **Framework Migration**: Understand test distribution across frameworks
   ```
   Find tests for AuthService in MySolution.sln
   ```

---

## 📊 Token Optimization Impact

### Scenario 1: Finding Implementations

**Traditional Approach**:
```
1. Search for interface definition
2. Search for "implements IUserRepository"
3. Read multiple implementation files
4. Manually track found implementations
→ Token usage: ~5,000 tokens
```

**With FindImplementations**:
```
FindImplementations("IUserRepository", "MySolution.sln")
→ Token usage: ~300 tokens
→ Savings: 94% 🚀
```

---

### Scenario 2: Finding Tests

**Traditional Approach**:
```
1. Search test projects
2. Find test classes by naming pattern
3. Read test files to count methods
4. Identify test frameworks
→ Token usage: ~10,000 tokens
```

**With FindTestsForType**:
```
FindTestsForType("UserService", "MySolution.sln")
→ Token usage: ~800 tokens
→ Savings: 92% 🎉
```

---

## 🔍 Common Use Cases

### Dependency Injection Analysis
```
Find implementations of IRepository in MySolution.sln
```

### Design Pattern Validation
```
Find implementations of ICommand in MySolution.sln
Find implementations of IObserver in MySolution.sln
```

### Test Coverage Check
```
Find tests for UserService in MySolution.sln
Find tests for PaymentProcessor in MySolution.sln
```

### Refactoring Planning
```
Find implementations of ServiceBase in MySolution.sln
Find tests for AuthService in MySolution.sln
```

### Architecture Review
```
Find implementations of IController in MySolution.sln
Find implementations of IValidator in MySolution.sln
```

---

## 💡 Tips & Tricks

### Tip 1: Combine with Other Tools

First find implementations:
```
Find implementations of IUserRepository in MySolution.sln
```

Then find references for specific implementation:
```
Find references to SqlUserRepository in MySolution.sln, excluding tests
```

### Tip 2: Test-Driven Development Workflow

1. Create interface: `IOrderService`
2. Check implementations: `Find implementations of IOrderService`
3. Check tests: `Find tests for OrderService`
4. Implement missing features based on gaps

### Tip 3: Understanding Inheritance

Use `includeAbstractImplementations: true` to see full inheritance chain:
```
Find implementations of ICommand in MySolution.sln, include abstract
```

This shows both:
- Abstract base classes (CommandBase)
- Concrete implementations (CreateUserCommand, etc.)

---

## 🚀 Next Steps

- Try FindImplementations on your interfaces
- Use FindTestsForType in your TDD workflow
- Combine with Phase 3 filtering for maximum efficiency
- Share feedback for improvements

---

## 📞 Feedback

Phase 4 Week 2 adds essential navigation features for understanding code relationships and test coverage. Your feedback helps us improve!
