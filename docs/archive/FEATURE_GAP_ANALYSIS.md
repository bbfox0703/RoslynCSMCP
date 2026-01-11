# Feature Gap Analysis - VS Extension vs RoslynCSMCP

**Date**: 2026-01-09
**Current Tools**: 13 MCP tools implemented

---

## 📊 Implementation Status Matrix

### Navigation (7 features)

| Feature | Status | Current Tool | Notes |
|---------|--------|--------------|-------|
| find_references | ✅ **完整實現** | FindReferences, FindReferencesFiltered | 3 detail levels + 6 filters |
| find_definition | ⚠️ **部分實現** | FindReferences (includeDefinition) | 混在引用中，未獨立 |
| find_callers | ✅ **完整實現** | GetCallHierarchy (direction="callers") | 支援深度控制 |
| find_callees | ✅ **完整實現** | GetCallHierarchy (direction="callees") | 支援深度控制 |
| find_implementations | ❌ **未實現** | - | 🔥 高價值功能 |
| find_overrides | ❌ **未實現** | - | 中價值 |
| find_base_members | ❌ **未實現** | - | 低價值 |

**已實現**: 4/7 (57%)

---

### Understanding (6 features)

| Feature | Status | Current Tool | Notes |
|---------|--------|--------------|-------|
| understand_type | ✅ **完整實現** | GetTypeSignature | 包含成員、文檔 |
| understand_method | ⚠️ **部分實現** | GetSymbolInfo | 缺少方法體分析 |
| get_type_info | ✅ **完整實現** | GetSymbolInfo | 完整符號資訊 |
| get_type_members | ✅ **完整實現** | GetTypeSignature (includeMembers) | 可選包含成員 |
| get_method_body | ❌ **未實現** | - | ⚠️ 讀檔案更快 |
| get_class_hierarchy | ⚠️ **部分實現** | GetSymbolInfo | 有基類，缺完整階層 |

**已實現**: 4/6 (67%)

---

### Structure (6 features)

| Feature | Status | Current Tool | Notes |
|---------|--------|--------------|-------|
| get_solution_structure | ✅ **完整實現** | GetProjectStructure | 完整解決方案結構 |
| get_project_structure | ✅ **完整實現** | GetProjectStructure | 可過濾 namespace |
| get_file_outline | ❌ **未實現** | - | 🔥 高價值功能 |
| get_types_in_file | ⚠️ **間接實現** | SearchSymbols | 需手動過濾 |
| find_entry_points | ❌ **未實現** | - | 中價值（Main, endpoints） |
| get_dependency_graph | ✅ **完整實現** | GetDependencyGraph | 3 種格式輸出 |

**已實現**: 3/6 (50%)

---

### Diagnostics (6 features)

| Feature | Status | Current Tool | Notes |
|---------|--------|--------------|-------|
| get_errors | ❌ **未實現** | - | 🔥🔥🔥 **最高價值！** |
| get_warnings | ❌ **未實現** | - | 🔥🔥 高價值 |
| validate_text | ❌ **未實現** | - | 低價值（編輯器功能） |
| find_async_issues | ❌ **未實現** | - | 低價值（複雜分析） |
| find_performance_issues | ❌ **未實現** | - | 低價值（需要 profiler） |
| find_unused_code | ❌ **未實現** | - | 中價值（代碼清理） |

**已實現**: 0/6 (0%) ⚠️ **最大缺口！**

---

### Refactoring (6 features)

| Feature | Status | Current Tool | Notes |
|---------|--------|--------------|-------|
| preview_rename | ❌ **無法實現** | - | ⛔ MCP 協議限制（只讀） |
| apply_rename | ❌ **無法實現** | - | ⛔ MCP 協議限制（只讀） |
| extract_interface | ❌ **無法實現** | - | ⛔ MCP 協議限制（只讀） |
| organize_usings | ❌ **無法實現** | - | ⛔ MCP 協議限制（只讀） |
| impact_analysis | ⚠️ **部分實現** | FindReferences + GetCallHierarchy | 可組合使用 |
| preview_extract_method | ❌ **無法實現** | - | ⛔ MCP 協議限制（只讀） |

**已實現**: 0/6 (0%) - **MCP 協議限制，只讀模式**

---

### Search (5 features)

| Feature | Status | Current Tool | Notes |
|---------|--------|--------------|-------|
| find_attribute_usages | ❌ **未實現** | - | 🔥 高價值（特別是測試） |
| find_event_subscribers | ❌ **未實現** | - | 中價值 |
| find_extension_methods | ❌ **未實現** | - | 低價值 |
| find_tests_for_type | ❌ **未實現** | - | 🔥 高價值（TDD 工作流） |
| text_search | ⚠️ **間接實現** | SearchSymbols | 符號搜索，非文字搜索 |

**已實現**: 0/5 (0%) ⚠️ **重要缺口！**

---

## 🎯 優先級評估

### 🔥🔥🔥 最高優先級（立即實施）

#### 1. **GetCompilationErrors** (Diagnostics)

**為什麼是 #1**:
- Claude 最常見需求：「有什麼編譯錯誤？」
- 目前方式：讀取整個檔案 → 浪費大量 tokens
- 新方式：直接返回錯誤列表

**Token 節省**:
```
當前：讀取 5 個檔案找錯誤 = ~8,000 tokens
新工具：錯誤列表 = ~200 tokens
節省：97.5% 🎉
```

**API 設計**:
```csharp
GetCompilationErrors(
    solutionPath: string,
    severity: "error" | "warning" | "all",  // 嚴重程度
    projectFilter: string?,                  // 專案過濾
    errorCodes: string[]?                    // 特定錯誤代碼 (e.g., "CS0103")
)
```

**輸出範例**:
```
Found 5 errors and 12 warnings in MySolution.sln:

🔴 ERRORS (5):
  MyProject.WebAPI/UserController.cs:
    Line 45 [CS0103]: The name 'usre' does not exist (did you mean 'user'?)
    Line 67 [CS1061]: 'int' does not contain a definition for 'ToStirng'

  MyProject.Services/UserService.cs:
    Line 123 [CS0029]: Cannot implicitly convert type 'string' to 'int'

⚠️ WARNINGS (12):
  MyProject.Data/Repository.cs:
    Line 34 [CS0168]: Variable 'temp' is declared but never used
    [... more ...]
```

**開發時間**: 2-3 天
**使用頻率**: 🔥🔥🔥 極高（每次編譯後）

---

#### 2. **GetFileOutline** (Structure)

**為什麼重要**:
- 快速了解檔案結構，不需讀取完整內容
- 類似 GetTypeSignature 但針對單一檔案
- 支援多個 types 的檔案

**Token 節省**:
```
當前：讀取 500 行檔案 = ~3,000 tokens
新工具：檔案大綱 = ~150 tokens
節省：95% 🎉
```

**API 設計**:
```csharp
GetFileOutline(
    filePath: string,
    solutionPath: string,
    includeMembers: bool = false,      // 包含成員簽名
    includePrivate: bool = false,      // 包含私有成員
    includeDocumentation: bool = true  // 包含文檔註解
)
```

**輸出範例**:
```
File: MyProject.Services/UserService.cs (234 lines)

📦 Namespace: MyProject.Services

🔹 IUserService (Interface, Public)
  → GetUserAsync(int id)
  → CreateUserAsync(UserDto dto)
  → DeleteUserAsync(int id)

🔹 UserService (Class, Public) : IUserService
  Fields: 2 private
  Constructors: 1 public

  Public Methods:
    → Task<User?> GetUserAsync(int id)
    → Task<User> CreateUserAsync(UserDto dto)
    → Task<bool> DeleteUserAsync(int id)

  Private Methods: [3 methods - use includePrivate: true to show]

Using Statements (8):
  - System, System.Linq, System.Threading.Tasks
  - Microsoft.EntityFrameworkCore
  - MyProject.Data, MyProject.Models
```

**開發時間**: 1-2 天
**使用頻率**: 🔥🔥 高

---

### 🔥🔥 高優先級（建議實施）

#### 3. **FindImplementations** (Navigation)

**為什麼重要**:
- 找出 interface/abstract class 的所有實現
- 常見問題：「誰實現了 IUserRepository？」

**Token 節省**: 70-80%

**API 設計**:
```csharp
FindImplementations(
    typeName: string,              // Interface 或 abstract class 名稱
    solutionPath: string,
    includeAbstractImplementations: bool = false  // 包含抽象實現
)
```

**輸出範例**:
```
Found 3 implementations of 'IUserRepository':

📄 SqlUserRepository (MyProject.Data)
   Location: Data/Repositories/SqlUserRepository.cs:15
   Accessibility: Public

📄 InMemoryUserRepository (MyProject.Tests)
   Location: Tests/Mocks/InMemoryUserRepository.cs:23
   Accessibility: Internal

📄 CachedUserRepository (MyProject.Services)
   Location: Services/Cache/CachedUserRepository.cs:34
   Accessibility: Public
   Wraps: SqlUserRepository (decorator pattern)
```

**開發時間**: 2-3 天
**使用頻率**: 🔥🔥 高

---

#### 4. **FindTestsForType** (Search)

**為什麼重要**:
- TDD 工作流：快速找到相關測試
- Claude 常問：「這個類別有測試嗎？」

**Token 節省**: 90%+

**API 設計**:
```csharp
FindTestsForType(
    typeName: string,              // 要找測試的類型
    solutionPath: string,
    includePartialMatches: bool = true  // 包含部分匹配（UserTests, UserServiceTests）
)
```

**輸出範例**:
```
Found 4 test classes for 'UserService':

📄 UserServiceTests (MyProject.UnitTests) - 15 tests
   Location: Tests/Unit/Services/UserServiceTests.cs
   Framework: xUnit
   Tests:
     - GetUserAsync_WithValidId_ReturnsUser
     - GetUserAsync_WithInvalidId_ReturnsNull
     - CreateUserAsync_WithValidDto_CreatesUser
     [... 12 more tests]

📄 UserServiceIntegrationTests (MyProject.IntegrationTests) - 8 tests
   Location: Tests/Integration/UserServiceIntegrationTests.cs
   Framework: xUnit

⚠️ Coverage analysis:
   - 15 unit tests + 8 integration tests
   - Public methods: 8
   - Methods without tests: DeleteUserAsync (no tests found)
```

**開發時間**: 2-3 天
**使用頻率**: 🔥🔥 高（TDD 開發）

---

### 🔥 中優先級（考慮實施）

#### 5. **GetClassHierarchy** (Understanding)

增強現有 GetSymbolInfo，提供完整繼承鏈。

**API 設計**:
```csharp
GetClassHierarchy(
    typeName: string,
    solutionPath: string,
    direction: "ancestors" | "descendants" | "both" = "both",
    maxDepth: int = 10
)
```

**輸出範例**:
```
Class Hierarchy for 'UserService':

⬆️ ANCESTORS (Inheritance Chain):
  UserService
    ↓ implements IUserService
    ↓ inherits ServiceBase
      ↓ inherits Object

⬇️ DESCENDANTS (Derived Classes):
  UserService
    ← CachedUserService (decorator)
    ← AdminUserService (extends)
      ← SuperAdminUserService
```

**開發時間**: 2 天
**使用頻率**: 🔥 中

---

#### 6. **FindAttributeUsages** (Search)

找出特定 Attribute 的所有使用。

**API 設計**:
```csharp
FindAttributeUsages(
    attributeName: string,  // e.g., "Obsolete", "Route", "Test"
    solutionPath: string,
    targetType: "class" | "method" | "property" | "all" = "all"
)
```

**使用場景**:
- 找所有 `[Obsolete]` 標記
- 找所有 API endpoints (`[Route]`, `[HttpGet]`)
- 找所有測試方法 (`[Fact]`, `[Test]`)

**開發時間**: 1-2 天
**使用頻率**: 🔥 中

---

## 📋 建議實作順序

基於 **ROI**（投資回報）、**開發難度**、**使用頻率**：

### Phase 4 建議（2-3 週）

```
Week 1:
1. ✅ GetCompilationErrors (2-3 天) - 🔥🔥🔥 最高價值
2. ✅ GetFileOutline (1-2 天) - 🔥🔥 快速實現

Week 2:
3. ✅ FindImplementations (2-3 天) - 🔥🔥 常用功能
4. ✅ FindTestsForType (2-3 天) - 🔥🔥 TDD 工作流

Week 3:
5. ✅ GetClassHierarchy (2 天) - 🔥 補充功能
6. ✅ FindAttributeUsages (1-2 天) - 🔥 特定場景
7. ✅ 文檔與測試 (2 天)
```

**預期成果**:
- 新增 6 個 MCP tools (13 → 19 tools)
- Token 節省：平均 70-90%
- 覆蓋最常見開發工作流

---

## ❌ 不建議實作

### Refactoring 類別（全部）

**原因**: MCP 協議限制（只讀模式）
- preview_rename
- apply_rename
- extract_interface
- organize_usings
- preview_extract_method

**替代方案**:
- 提供影響分析（已有 FindReferences + GetCallHierarchy）
- 建議重構策略（文字回應）

---

### 低價值 Diagnostics

- **validate_text**: 編輯器即時功能，MCP 無優勢
- **find_async_issues**: 需要複雜靜態分析，開發成本高
- **find_performance_issues**: 需要 profiler 數據，無法從 Roslyn 獲得

---

### 其他低價值功能

- **get_method_body**: 直接讀檔案更快，token 節省不明顯
- **find_extension_methods**: 使用頻率低
- **find_event_subscribers**: C# 現代代碼少用 events

---

## 💡 Token 節省潛力總結

| 功能 | 場景 | Token 節省 | 使用頻率 |
|------|------|-----------|---------|
| GetCompilationErrors | 查錯誤 | 95-98% | 🔥🔥🔥 極高 |
| GetFileOutline | 了解檔案 | 90-95% | 🔥🔥 高 |
| FindImplementations | 找實現 | 70-80% | 🔥🔥 高 |
| FindTestsForType | 找測試 | 90%+ | 🔥🔥 高（TDD） |
| GetClassHierarchy | 繼承鏈 | 60-70% | 🔥 中 |
| FindAttributeUsages | 找標註 | 80-90% | 🔥 中 |

---

## 🚀 建議行動

### 立即行動（Phase 4 Week 1）

實施最高優先級的兩個功能：
1. **GetCompilationErrors** - 解決最痛點
2. **GetFileOutline** - 快速見效

預計 **3-4 天**開發時間，**95%+ token 節省**。

### 短期計劃（Phase 4 Week 2-3）

實施剩餘 4 個功能，完成 Phase 4。

### 長期考慮

根據用戶反饋和使用數據，決定是否實施：
- GetClassHierarchy
- FindAttributeUsages
- 其他創新功能

---

## 📊 實現狀態總覽

**當前**:
- 總功能：36 個 VS extension 功能
- 已實現：13/36 (36%)
- 部分實現：6/36 (17%)
- 未實現可行：11/36 (31%)
- 無法實現：6/36 (17%, MCP 限制)

**Phase 4 後**:
- 總工具數：13 → 19 (+46%)
- 實現率：36% → 53%
- 核心功能覆蓋：80%+

---

## 🎯 結論

**最大價值機會**:
1. **Diagnostics 類** - 當前 0% 實現，最高價值
2. **Search 類特定功能** - 針對性強，token 節省大

**建議**:
- 立即啟動 Phase 4
- 優先實施 GetCompilationErrors + GetFileOutline
- 根據用戶反饋迭代優化

**預期影響**:
- 用戶工作效率提升 **50-70%**
- Token 消耗減少 **70-95%**（特定場景）
- 覆蓋 **80%+** 常見開發工作流

