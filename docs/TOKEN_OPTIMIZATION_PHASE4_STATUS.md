# Phase 4A 工具現狀分析

**建立日期**: 2026-01-10
**狀態**: 工具已存在，但缺少 token 優化參數

---

## 📊 GetFileOutline 現狀

### ✅ 已實作

**當前參數：**
```csharp
[McpServerTool, Description("Get structural outline of a C# file")]
public static async Task<string> GetFileOutline(
    [Description("Path to C# source file (.cs)")] string filePath,
    [Description("Include member details (default: true)")] bool includeMembers = true,
    [Description("Include documentation comments (default: true)")] bool includeDocumentation = true,
    IServiceProvider? serviceProvider = null)
```

### ❌ 缺少的優化參數

1. **沒有 `mode` 或 `detailLevel` 參數**
   - 當前只有 `includeMembers` (bool) 和 `includeDocumentation` (bool)
   - 缺少多級別控制（compact/normal/detailed）

2. **沒有 `maxMembers` 參數**
   - 無法限制每個類型顯示的成員數量
   - 大型類別會輸出所有成員（可能 50+ 個）

### 📈 當前輸出分析

**當前格式（Lines 1189-1322）：**
```
**File Outline**: UserService.cs

📊 **Statistics**:
  • Total Lines: 250
  • Code Lines: 180 (72.0%)          ← 百分比計算
  • Comment Lines: 40 (16.0%)        ← 百分比計算
  • Blank Lines: 30 (12.0%)          ← 百分比計算
  • Types Found: 3 (0 failed)
  • Members Found: 25 (0 failed)

⚠️ **Warnings:**
   - Context: warning message
   ... and 2 more warnings

📦 **Using Statements** (15):       ← 顯示前 10 個
  • System
  • System.Collections.Generic
  • System.Linq
  • System.Threading.Tasks
  • Microsoft.Extensions.Logging
  • Microsoft.CodeAnalysis
  • Microsoft.CodeAnalysis.CSharp
  • Microsoft.CodeAnalysis.FindSymbols
  • RoslynMcpServer.Models
  • RoslynMcpServer.Services
  ... and 5 more

🏷️ **Namespaces**: MyProject.Services

📋 **Types** (3):

🔷 **UserService** (Class, Public)    ← Emoji
   Line 15
   💬 Handles user-related operations  ← Documentation
   ↗️ Inherits/Implements: IUserService

   📋 **Constructors** (1):            ← 成員分組
     • UserService(IUserRepository, ILogger<UserService>)
       Line: 20
       Accessibility: Public

   📋 **Fields** (2):
     • _repository
       Line: 17
       Type: IUserRepository
       Accessibility: Private
     • _logger
       Line: 18
       Type: ILogger<UserService>
       Accessibility: Private

   📋 **Methods** (8):                 ← 顯示所有 8 個方法
     • GetUserAsync(int id)
       Line: 30
       Return: Task<User?>
       Accessibility: Public
       Modifiers: async
       💬 Retrieves a user by ID
     • GetAllUsersAsync()
       Line: 45
       Return: Task<IEnumerable<User>>
       Accessibility: Public
       Modifiers: async
     ... (所有 8 個方法完整列出)

   📋 **Properties** (0):

   📋 **Events** (0):
```

**Token 估算：** 800-2000 tokens（取決於成員數量）

### 🎯 問題點

1. ❌ **百分比計算**：`(72.0%)`、`(16.0%)`、`(12.0%)` - 重複資訊
2. ❌ **Emoji 過多**：📊 📦 🏷️ 📋 🔷 💬 ↗️ - 每個 ~2 tokens
3. ❌ **Using Statements**：顯示前 10 個（常見專案有 15-30 個）
4. ❌ **顯示所有成員**：即使 `includeMembers=false`，仍會顯示成員計數和分組標題
5. ❌ **多行格式**：每個成員 3-5 行（Name, Line, Type/Return, Accessibility, Modifiers, Docs）

### 💡 建議改進

**添加參數：**
```csharp
[Description("Output mode: compact, normal, detailed (default: normal)")]
string mode = "normal",

[Description("Maximum members to show per type (default: 10, 0=all)")]
int maxMembers = 10
```

**Compact Mode 輸出應該是：**
```
File: UserService.cs (180 LOC, 15 usings)

UserService (Class, Public) @ MyProject.Services
  Constructors: 1
  Fields: 2 (IUserRepository, ILogger)
  Methods: 8 (6 public, 2 private)
    GetUserAsync(int) → Task<User?> [async]
    GetAllUsersAsync() → Task<IEnumerable<User>> [async]
    CreateUserAsync(UserDto) → Task<User> [async]
    DeleteUserAsync(int) → Task<bool> [async]
    ... and 4 more (use maxMembers=0 for all)
```

**Token 估算：** 200-500 tokens
**節省：** 60-75%（400-1500 tokens）

---

## 📊 GetSymbolInfo 現狀

### ✅ 已實作

**當前參數：**
```csharp
[McpServerTool, Description("Get detailed information about a specific symbol")]
public static async Task<string> GetSymbolInfo(
    [Description("Exact symbol name or full qualified name")] string symbolName,
    [Description("Path to solution file (.sln)")] string solutionPath,
    IServiceProvider? serviceProvider = null)
```

### ❌ 缺少的優化參數

**完全沒有任何詳細度控制參數！**

### 📈 當前輸出分析

**當前格式（Lines 967-1004）：**
```
**GetUserAsync** (Method)
Full Name: `MyProject.Services.UserService.GetUserAsync`
Accessibility: Public
Namespace: MyProject.Services
Declaring Type: UserService
Return Type: Task<User?>
Parameters:
  • id (int)
Attributes:
  • AsyncStateMachineAttribute
Location: D:\Projects\MyProject\Services\UserService.cs:45
```

**Token 估算：** 150-250 tokens

### 🎯 問題點

1. ❌ **總是顯示所有欄位**：即使只需要基本資訊（名稱、類型）
2. ❌ **Full Name 和 Declaring Type 重複**：`MyProject.Services.UserService.GetUserAsync` 包含了 Declaring Type 資訊
3. ❌ **完整路徑**：`D:\Projects\MyProject\Services\UserService.cs:45` - 路徑部分佔用大量 tokens
4. ❌ **無法只獲取簽名**：有時只需要 `GetUserAsync(int) → Task<User?>`

### 💡 建議改進

**添加參數：**
```csharp
[Description("Detail level: summary, basic, full (default: basic)")]
string detailLevel = "basic"
```

**三種模式輸出：**

**Summary Mode（30-50 tokens，節省 70-80%）：**
```
GetUserAsync (Method, Public)
→ Task<User?> (int id)
@ UserService.cs:45
```

**Basic Mode（80-120 tokens，節省 40-50%）：**
```
GetUserAsync (Method)
Type: Task<User?>
Parameters: (int id)
Location: MyProject.Services.UserService
File: UserService.cs:45
```

**Full Mode（150-250 tokens，當前行為）：**
```
（保持現有完整格式）
```

---

## 📊 總結對比

| 工具 | 狀態 | 當前參數 | 缺少的參數 | 當前 Tokens | 優化後 Tokens | 節省 |
|------|------|---------|-----------|------------|--------------|------|
| **GetFileOutline** | ✅ 已實作 | `includeMembers` (bool)<br>`includeDocumentation` (bool) | `mode` (string)<br>`maxMembers` (int) | 800-2000 | 200-500<br>(compact) | 60-75% |
| **GetSymbolInfo** | ✅ 已實作 | 無 | `detailLevel` (string) | 150-250 | 30-50<br>(summary) | 70-80% |

---

## 🎯 結論

### 工具已存在，設計合理，但缺少 token 優化

**不是設計不良，而是：**
1. ✅ **功能完整**：兩個工具都正確實作了核心功能
2. ✅ **參數合理**：現有參數（includeMembers, includeDocumentation）有意義
3. ❌ **缺少優化**：沒有針對 token 消耗進行優化設計
4. ❌ **單一詳細度**：只有開/關，沒有多級別控制

### 需要做的是「增強」而非「重寫」

**Phase 4A 實作任務：**
1. **GetFileOutline**：添加 `mode` 和 `maxMembers` 參數
2. **GetSymbolInfo**：添加 `detailLevel` 參數
3. 實作對應的格式化函數（compact/summary 版本）
4. 保持向後相容（預設參數保持當前行為或使用 basic/normal）

### 為什麼我建議這樣做？

**當前行為分析：**
- `includeMembers=true`（預設）：顯示所有成員的完整資訊
- `includeMembers=false`：只顯示類型資訊，但仍然很冗長（統計、using statements、emoji）

**問題：**
- 沒有中間選項：「顯示成員但限制數量」或「緊湊格式」
- 用戶想要概覽時，必須完全關閉成員顯示
- 無法說「只顯示前 5 個方法」

**解決方案：**
```csharp
// 更靈活的控制
mode: "compact"          → 極度簡化（200-500 tokens）
mode: "normal"           → 適度顯示（500-1000 tokens）
mode: "detailed"         → 完整資訊（800-2000 tokens，當前預設）

maxMembers: 5            → 每個類型最多 5 個成員
maxMembers: 0            → 顯示全部（當前行為）
```

---

## 📋 下一步建議

### Option 1: 直接實作 Phase 4A（推薦）

**理由：** 工具功能正確，只需添加參數和新的格式化邏輯

**工作量：**
- GetFileOutline：1 天（添加參數 + compact formatter）
- GetSymbolInfo：1 天（添加參數 + summary/basic formatters）
- 測試和文檔：1 天
- **總計：3 天**

### Option 2: 評估當前 includeMembers=false 的效果

**理由：** 也許 `includeMembers=false` 已經足夠緊湊？

**測試：**
```csharp
// 測試當前參數的組合
await GetFileOutline(filePath, includeMembers: false, includeDocumentation: false);
```

如果這個組合已經很緊湊，我們可以：
1. 更新文檔，推薦使用 `includeMembers=false` 來節省 tokens
2. 只添加 `maxMembers` 參數（更小的改動）
3. 跳過 GetSymbolInfo 優化（因為它已經相對簡潔）

### Option 3: 先實作 GetFileOutline，評估效果後再決定

**理由：** GetFileOutline 的 token 消耗更高（800-2000 vs 150-250）

**優先順序：**
1. GetFileOutline maxMembers 參數（最小改動，最大效益）
2. GetFileOutline mode 參數（更徹底的優化）
3. GetSymbolInfo detailLevel 參數（較低優先級）

---

## ❓ 您的決定

請告訴我您希望：

1. **直接實作 Phase 4A**（3 天，添加新參數和格式化函數）
2. **測試當前參數組合**（評估 includeMembers=false 是否已經足夠）
3. **只添加 maxMembers 參數**（最小改動，最快實作）
4. **暫不實作**（Phase 4B/4C 有更高優先級）

或者有其他想法？
