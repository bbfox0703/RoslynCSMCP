# Token 優化 Phase 4 評估報告

**文檔版本**: 1.0
**建立日期**: 2026-01-10
**狀態**: 評估完成，待實作

---

## 📊 執行摘要

經過全面的程式碼分析，我們發現了 **12 個主要的 token 優化機會**，總計可節省 **30-75%** 的 token 使用量。

### 關鍵發現

| 類別 | 工具數量 | 平均節省 | 實作優先級 |
|------|---------|---------|-----------|
| **Critical** | 2 tools | 60-75% | 立即實作 |
| **High** | 4 tools | 40-70% | 第一階段 |
| **Medium** | 4 tools | 20-50% | 第二階段 |
| **Cross-cutting** | 全部工具 | 30-50% | 持續改進 |

**總體影響：** 預計可為常見查詢節省 **40-60%** 的 token 消耗。

---

## 🎯 Critical Priority（立即見效）

### 1. GetFileOutline - 檔案大綱壓縮 ⭐⭐⭐

**當前狀況：**
```csharp
// 輸出範例（800-2000+ tokens）
📊 **Statistics**:
  • Total Lines: 250
  • Code Lines: 180 (72.0%)
  • Comment Lines: 40 (16.0%)
  • Blank Lines: 30 (12.0%)

📦 **Using Statements** (15):
  • System
  • System.Collections.Generic
  • System.Linq
  • System.Threading.Tasks
  • Microsoft.Extensions.Logging
  • Microsoft.CodeAnalysis
  ... (顯示全部 15 個)

🔷 **UserService** (Class)
  Type: Class
  Accessibility: Public
  Namespace: MyProject.Services
  📄 UserService.cs:15

  📋 **Constructors** (1):
    • UserService(IUserRepository repository, ILogger<UserService> logger)
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

  📋 **Methods** (8):
    • GetUserAsync(int id)
      Line: 30
      Return: Task<User?>
      Accessibility: Public
      Modifiers: async
      /// <summary>
      /// Retrieves a user by ID
      /// </summary>
    ... (所有 8 個方法的完整資訊)
```

**問題分析：**
1. ❌ 百分比計算重複（Total, Code, Comment, Blank 都有 %）
2. ❌ 顯示所有 using statements（10-20 個）
3. ❌ 每個成員都有完整的簽名 + 行號 + modifiers + 文檔
4. ❌ 大量 emoji 使用（📊 📦 🔷 📋）
5. ❌ 每個 field/property/method 都獨立一行

**優化方案：**

```csharp
[McpServerTool, Description("Get structural outline of a C# file")]
public static async Task<string> GetFileOutline(
    [Description("Path to C# source file (.cs)")] string filePath,
    [Description("Output mode: compact, normal, detailed (default: normal)")]
    string mode = "normal",
    [Description("Maximum members to show per type (default: 10, 0=all)")]
    int maxMembers = 10,
    [Description("Include documentation comments (default: false)")]
    bool includeDocumentation = false,
    IServiceProvider? serviceProvider = null)
```

**Compact Mode 輸出範例（200-500 tokens，節省 60-75%）：**
```
File: UserService.cs (180 LOC, 15 usings)

UserService (Class, Public) @ MyProject.Services
  Constructors: 1
  Fields: 2 (IUserRepository, ILogger)
  Properties: 0
  Methods: 8 (6 public, 2 private)
    GetUserAsync(int) → Task<User?> [async]
    GetAllUsersAsync() → Task<IEnumerable<User>> [async]
    CreateUserAsync(UserDto) → Task<User> [async]
    DeleteUserAsync(int) → Task<bool> [async]
    ValidateUserAsync(User) → Task<bool> [private,async]
    ... and 3 more (use maxMembers=0 for all)
```

**實作要點：**
```csharp
private static string FormatFileOutlineCompact(FileOutline outline, int maxMembers)
{
    var output = new StringBuilder();

    // 簡化的統計資訊（一行）
    output.AppendLine($"File: {Path.GetFileName(outline.FilePath)} " +
                     $"({outline.CodeLines} LOC, {outline.UsingStatements.Count} usings)\n");

    foreach (var type in outline.Types)
    {
        // 型別標題（一行）
        output.AppendLine($"{type.Name} ({type.Kind}, {type.Accessibility}) " +
                         $"@ {type.Namespace}");

        // 成員摘要（計數）
        var memberGroups = type.Members.GroupBy(m => m.Kind);
        foreach (var group in memberGroups)
        {
            output.AppendLine($"  {group.Key}s: {group.Count()}");
        }

        // 方法列表（壓縮格式，最多 maxMembers 個）
        var methods = type.Members.Where(m => m.Kind == "Method").ToList();
        if (methods.Any())
        {
            output.AppendLine($"  Methods:");
            foreach (var method in methods.Take(maxMembers))
            {
                var modifiers = GetModifiersShorthand(method); // [async,static]
                output.AppendLine($"    {method.Name}{method.Parameters} → {method.ReturnType} {modifiers}");
            }
            if (methods.Count > maxMembers)
            {
                output.AppendLine($"    ... and {methods.Count - maxMembers} more (use maxMembers=0 for all)");
            }
        }
        output.AppendLine();
    }

    return output.ToString();
}
```

**Token 節省估算：**
- 當前模式：800-2000 tokens（Normal）
- Compact 模式：200-500 tokens
- **節省：60-75%（400-1500 tokens）**

**開發工作量：** 1-2 天

---

### 2. GetSymbolInfo - 詳細度控制 ⭐⭐⭐

**當前狀況：**
```csharp
// 輸出範例（150-250 tokens）
Symbol Information:
  Name: GetUserAsync
  Full Name: MyProject.Services.UserService.GetUserAsync
  Kind: Method
  Accessibility: Public
  Namespace: MyProject.Services
  Declaring Type: UserService
  Return Type: System.Threading.Tasks.Task<MyProject.Models.User?>
  Parameters:
    • id (int)
  Attributes:
    • AsyncStateMachineAttribute
  Documentation:
    Retrieves a user by their unique identifier.
    Returns null if the user is not found.
  Source Location: D:\Projects\MyProject\Services\UserService.cs:45
```

**問題分析：**
1. ❌ 總是顯示所有欄位（即使只需要名稱和類型）
2. ❌ 完整路徑佔用大量 tokens
3. ❌ Full Name 和 Declaring Type 重複資訊
4. ❌ 文檔常常很長

**優化方案：**

```csharp
[McpServerTool, Description("Get detailed information about a specific symbol")]
public static async Task<string> GetSymbolInfo(
    [Description("Exact symbol name or full qualified name")] string symbolName,
    [Description("Path to solution file (.sln)")] string solutionPath,
    [Description("Detail level: summary, basic, full (default: basic)")]
    string detailLevel = "basic",
    IServiceProvider? serviceProvider = null)
```

**Summary Mode 輸出（30-50 tokens，節省 70-80%）：**
```
GetUserAsync (Method, Public)
→ Task<User?> (int id)
@ UserService.cs:45
```

**Basic Mode 輸出（80-120 tokens，節省 40-50%）：**
```
GetUserAsync (Method)
Type: Task<User?>
Parameters: (int id)
Location: MyProject.Services.UserService
File: UserService.cs:45
```

**Full Mode 輸出（150-250 tokens，當前行為）：**
```
（保持現有的完整格式）
```

**實作要點：**
```csharp
private static string FormatSymbolInfo(ISymbol symbol, string detailLevel)
{
    switch (detailLevel.ToLower())
    {
        case "summary":
            return FormatSymbolSummary(symbol);
        case "basic":
            return FormatSymbolBasic(symbol);
        case "full":
        default:
            return FormatSymbolFull(symbol);
    }
}

private static string FormatSymbolSummary(ISymbol symbol)
{
    var output = new StringBuilder();

    // 一行格式
    if (symbol is IMethodSymbol method)
    {
        var parameters = string.Join(", ", method.Parameters.Select(p => p.Type.Name));
        output.AppendLine($"{symbol.Name} ({symbol.Kind}, {symbol.DeclaredAccessibility})");
        output.AppendLine($"→ {method.ReturnType.Name} ({parameters})");
    }
    else
    {
        output.AppendLine($"{symbol.Name} ({symbol.Kind}, {symbol.DeclaredAccessibility})");
        if (symbol is IFieldSymbol field)
            output.AppendLine($"Type: {field.Type.Name}");
        else if (symbol is IPropertySymbol prop)
            output.AppendLine($"Type: {prop.Type.Name}");
    }

    // 簡化的位置
    var location = symbol.Locations.FirstOrDefault();
    if (location != null)
    {
        var fileName = Path.GetFileName(location.SourceTree?.FilePath ?? "");
        output.AppendLine($"@ {fileName}:{location.GetLineSpan().StartLinePosition.Line + 1}");
    }

    return output.ToString();
}
```

**Token 節省估算：**
- Full Mode：150-250 tokens（當前）
- Basic Mode：80-120 tokens（節省 40-50%）
- Summary Mode：30-50 tokens（節省 70-80%）

**開發工作量：** 1 天

---

## 🔥 High Priority（第一階段）

### 3. GetCompilationErrors - 錯誤報告壓縮

**當前狀況：** 800-3000+ tokens（包含完整錯誤訊息、程式碼片段、markdown fence）

**優化方案：**
```csharp
[Description("Output mode: compact, normal, detailed (default: normal)")]
string mode = "normal"
```

**Compact Mode：**
```
Errors: 15 | Warnings: 42 | Projects: 3

MyProject.WebAPI (8 errors):
  CS0103: 'User' does not exist (UserController.cs:45)
  CS0246: Type 'ILogger' not found (Startup.cs:23)
  ... and 6 more

MyProject.Services (7 errors):
  CS1061: 'GetUserAsync' not defined (UserService.cs:67)
  ... and 6 more
```

**Token 節省：** 40-60%（300-1000 tokens → 500-1800 tokens saved）

---

### 4. FindAttributeUsages - 屬性使用內聯格式

**當前狀況：** 500-1500 tokens（完整格式，每個使用都有多行）

**優化方案：**
```csharp
[Description("Output format: inline, normal, detailed (default: normal)")]
string format = "normal"
```

**Inline Format：**
```
[Obsolete] usages (15):

Classes (3):
  OldUserService (Services.cs:45) ["Use NewUserService instead"]
  LegacyController (Controllers/Old.cs:12)
  DeprecatedModel (Models.cs:34) [error=true]

Methods (12):
  GetUser (UserService.cs:67), CreateUser (UserService.cs:89), ...
```

**Token 節省：** 50-70%（250-450 tokens → 250-1050 tokens saved）

---

### 5. GetClassHierarchy - 樹狀結構壓縮

**當前狀況：** 400-2000 tokens（完整樹狀結構，每個節點多行）

**優化方案：**
```csharp
[Description("Output format: compact, tree, detailed (default: tree)")]
string format = "tree"
```

**Compact Format：**
```
UserService (Class)

Ancestors (3):
  Object → ServiceBase → UserServiceBase → UserService

Descendants (2):
  UserService → [CachedUserService, MockUserService]

Files: 4 total
```

**Token 節省：** 50-70%（150-600 tokens → 200-1400 tokens saved）

---

### 6. FindImplementations - 實作清單摘要

**當前狀況：** 300-1200 tokens（完整資訊包含 namespace, docs, interfaces）

**優化方案：**
```csharp
[Description("Include details (namespace, docs, interfaces)")]
bool includeDetails = false
```

**Without Details：**
```
IUserService implementations (5):

UserService (MyProject.Services.cs:45)
CachedUserService (MyProject.Caching.cs:23)
MockUserService (MyProject.Tests.cs:12)
ProxyUserService (MyProject.Proxy.cs:67)
TestUserService (MyProject.Tests.cs:89)
```

**Token 節省：** 50-70%（100-300 tokens → 120-720 tokens saved）

---

## ⚡ Medium Priority（第二階段）

### 7. GetCallHierarchy - 呼叫層次簡化

**優化：** 添加 `compactMode` 參數，合併 containing type 和 method name

**Token 節省：** 20-30%（40-180 tokens）

---

### 8. GetDependencyGraph - 依賴圖內聯

**優化：** Text format 使用內聯顯示 `Project → [Dep1, Dep2]`

**Token 節省：** 30-40%（90-320 tokens）

---

### 9. FindReferences - Ultra-Compact 模式

**優化：** 添加 "ultra-compact" detailLevel，只顯示行號範圍

**Token 節省：** 20-40%（80-800 tokens）

---

### 10. GetCodeMetrics - 度量摘要

**優化：** 添加 summary mode，只顯示 top-level 統計

**Token 節省：** 30-50%

---

## 🔧 Cross-Cutting Improvements（通用改進）

### 11. 路徑優化（所有工具）

**問題：** 所有工具都顯示完整檔案路徑
```
D:\Github\RoslynCSMCP\RoslynMcpServer\Services\UserService.cs
```

**優化方案：**
```csharp
// 新增全域參數（通過 serviceProvider 傳遞）
public class PathFormattingOptions
{
    public bool UseFullPaths { get; set; } = false;
    public string? SolutionRoot { get; set; }
}

// 在所有工具中使用
private static string FormatPath(string fullPath, PathFormattingOptions? options)
{
    if (options?.UseFullPaths ?? false)
        return fullPath;

    // 使用相對路徑或只顯示檔名
    if (options?.SolutionRoot != null)
        return Path.GetRelativePath(options.SolutionRoot, fullPath);

    return Path.GetFileName(fullPath);
}
```

**影響：** 所有顯示路徑的工具
**Token 節省：** 30-50%（路徑部分）

---

### 12. Emoji 優化（所有工具）

**問題：** 大量使用 emoji（📄 🔷 📊 ✓ 等），每個 emoji ≈ 2 tokens

**優化方案：**
```csharp
public enum OutputFormat
{
    Unicode,  // 當前（使用 emoji）
    ASCII,    // 純文字（: 、C、M、|、->）
    Minimal   // 最小化（無符號）
}

// 範例
Unicode: 📄 UserService.cs:45
ASCII:   : UserService.cs:45
Minimal: UserService.cs:45
```

**影響：** 所有工具的格式化輸出
**Token 節省：** 2-5%（總體）

---

## 📊 優化效益總結

| 工具 | 當前 Tokens | Compact Tokens | Full Tokens | 節省 % | 優先級 |
|------|------------|---------------|-------------|-------|--------|
| **GetFileOutline** | 800-2000 | 200-500 | 800-2000 | 60-75% | 🔴 Critical |
| **GetSymbolInfo** | 150-250 | 30-50 | 150-250 | 70-80% | 🔴 Critical |
| **GetCompilationErrors** | 800-3000 | 300-1000 | 800-3000 | 40-60% | 🟠 High |
| **FindAttributeUsages** | 500-1500 | 250-450 | 500-1500 | 50-70% | 🟠 High |
| **GetClassHierarchy** | 400-2000 | 150-600 | 400-2000 | 50-70% | 🟠 High |
| **FindImplementations** | 300-1200 | 100-300 | 300-1200 | 50-70% | 🟠 High |
| **GetCallHierarchy** | 200-600 | 100-200 | 200-600 | 20-30% | 🟡 Medium |
| **GetDependencyGraph** | 300-800 | 150-300 | 300-800 | 30-40% | 🟡 Medium |
| **FindReferences** | 200-2000 | 100-300 | 400-2000 | 20-40% | 🟡 Medium |

**總體預期效益：**
- **常見查詢場景：** 40-60% token 節省
- **深度分析場景：** 50-75% token 節省
- **檔案大綱查詢：** 60-75% token 節省

---

## 🚀 實作建議

### Phase 4A: Critical Priority（第 7 週，5 天）

**Week 7 (Days 1-3):**
- ✅ GetFileOutline compact mode
  - 添加 `mode` 參數（compact/normal/detailed）
  - 實作 `maxMembers` 參數
  - 壓縮 using statements 顯示
  - 測試 token 節省效果

**Week 7 (Days 4-5):**
- ✅ GetSymbolInfo detail levels
  - 添加 `detailLevel` 參數（summary/basic/full）
  - 實作三種格式化函數
  - 測試各模式的正確性

**預期成果：**
- 2 個 critical 工具優化完成
- Token 節省：60-80%（這兩個工具）
- 使用文檔更新

---

### Phase 4B: High Priority（第 8-9 週，10 天）

**Week 8:**
- ✅ GetCompilationErrors compact mode（2 天）
- ✅ FindAttributeUsages inline format（2 天）
- ✅ GetClassHierarchy compact tree（2 天）

**Week 9:**
- ✅ FindImplementations summary mode（2 天）
- ✅ 整合測試和文檔（3 天）

**預期成果：**
- 4 個 high priority 工具優化完成
- Token 節省：40-70%（這些工具）

---

### Phase 4C: Medium Priority（第 10 週，5 天）

**Week 10:**
- ✅ GetCallHierarchy, GetDependencyGraph, FindReferences 優化
- ✅ 通用路徑優化實作
- ✅ Emoji 優化選項

**預期成果：**
- 剩餘工具優化完成
- 全域選項生效
- Phase 4 完成文檔

---

## 📈 ROI 分析

### 開發成本
- **Phase 4A (Critical):** 5 天
- **Phase 4B (High):** 10 天
- **Phase 4C (Medium):** 5 天
- **總計：** 20 天（4 週）

### 收益估算

**場景 1: 日常程式碼探索**
```
當前：GetFileOutline (1500 tokens) + GetSymbolInfo x3 (600 tokens) = 2100 tokens
優化後：GetFileOutline compact (400 tokens) + GetSymbolInfo summary x3 (120 tokens) = 520 tokens
節省：1580 tokens (75%)
```

**場景 2: 錯誤分析**
```
當前：GetCompilationErrors (2000 tokens)
優化後：GetCompilationErrors compact (700 tokens)
節省：1300 tokens (65%)
```

**場景 3: 架構分析**
```
當前：GetClassHierarchy (1200 tokens) + FindImplementations (800 tokens) = 2000 tokens
優化後：GetClassHierarchy compact (400 tokens) + FindImplementations summary (300 tokens) = 700 tokens
節省：1300 tokens (65%)
```

**平均每次查詢節省：** 1000-1500 tokens
**每天 Claude 使用次數假設：** 20 次
**每天節省：** 20,000-30,000 tokens

### ROI 計算

**假設：**
- Claude API 成本：$3 per million input tokens (Sonnet)
- 開發成本：$500/day × 20 days = $10,000

**每日成本節省：**
```
25,000 tokens/day × $3/1M tokens = $0.075/day
月度節省：$0.075 × 30 = $2.25
年度節省：$2.25 × 12 = $27
```

**單用戶 ROI：** 53 年回收期（不理想）

**BUT... 考慮多用戶場景：**
- 100 個活躍用戶：1.9 年回收期
- 500 個活躍用戶：0.4 年回收期 ✅
- 1000 個活躍用戶：0.2 年回收期 ✅

**額外收益：**
1. ✅ **用戶體驗改善**（更快的響應，更少的滾動）
2. ✅ **降低 API rate limiting 風險**
3. ✅ **提升工具採用率**（更高效 = 更願意使用）
4. ✅ **競爭優勢**（比其他 MCP 工具更高效）

---

## ✅ 實作檢查清單

### Phase 4A: Critical
- [ ] GetFileOutline 添加 `mode` 參數
- [ ] 實作 FormatFileOutlineCompact 函數
- [ ] 實作 `maxMembers` 限制邏輯
- [ ] GetSymbolInfo 添加 `detailLevel` 參數
- [ ] 實作 FormatSymbolSummary 函數
- [ ] 實作 FormatSymbolBasic 函數
- [ ] 單元測試覆蓋（compact vs normal）
- [ ] 文檔更新（PHASE4_USAGE_EXAMPLES.md）

### Phase 4B: High
- [ ] GetCompilationErrors compact mode
- [ ] FindAttributeUsages inline format
- [ ] GetClassHierarchy compact tree
- [ ] FindImplementations summary mode
- [ ] 整合測試
- [ ] 效能基準測試

### Phase 4C: Medium
- [ ] GetCallHierarchy compact mode
- [ ] GetDependencyGraph inline format
- [ ] FindReferences ultra-compact
- [ ] 通用 PathFormattingOptions
- [ ] OutputFormat enum (Unicode/ASCII/Minimal)
- [ ] 全域設定支援

---

## 📝 測試策略

### Token 計數測試
```csharp
[TestClass]
public class TokenOptimizationPhase4Tests
{
    [TestMethod]
    public async Task GetFileOutline_CompactMode_Saves60PercentTokens()
    {
        // Arrange
        var testFile = "TestFiles/LargeService.cs"; // 2000 lines

        // Act
        var normalResult = await GetFileOutline(testFile, mode: "normal");
        var compactResult = await GetFileOutline(testFile, mode: "compact");

        var normalTokens = EstimateTokenCount(normalResult);
        var compactTokens = EstimateTokenCount(compactResult);
        var savings = (normalTokens - compactTokens) / (double)normalTokens;

        // Assert
        Assert.IsTrue(savings >= 0.60,
            $"Expected >= 60% savings, got {savings:P2} ({normalTokens} → {compactTokens} tokens)");
    }

    [TestMethod]
    public async Task GetSymbolInfo_SummaryMode_Saves70PercentTokens()
    {
        var fullResult = await GetSymbolInfo("GetUserAsync", testSln, detailLevel: "full");
        var summaryResult = await GetSymbolInfo("GetUserAsync", testSln, detailLevel: "summary");

        var fullTokens = EstimateTokenCount(fullResult);
        var summaryTokens = EstimateTokenCount(summaryResult);
        var savings = (fullTokens - summaryTokens) / (double)fullTokens;

        Assert.IsTrue(savings >= 0.70,
            $"Expected >= 70% savings, got {savings:P2}");
    }
}
```

### 功能完整性測試
```csharp
[TestMethod]
public async Task GetFileOutline_CompactMode_IncludesEssentialInfo()
{
    var result = await GetFileOutline(testFile, mode: "compact");

    // 確保 compact 模式仍包含關鍵資訊
    Assert.IsTrue(result.Contains("UserService"), "Should include type name");
    Assert.IsTrue(result.Contains("Public"), "Should include accessibility");
    Assert.IsTrue(result.Contains("GetUserAsync"), "Should include method names");
    Assert.IsTrue(result.Contains("Task<User?>"), "Should include return types");
}
```

---

## 📚 相關文檔

完成後更新以下文檔：

1. **PHASE4_USAGE_EXAMPLES.md** - Phase 4 使用範例
2. **TOKEN_OPTIMIZATION_PLAN.md** - 更新為已完成
3. **CLAUDE.md** - 更新工具參數說明
4. **README.md** - 更新 feature list

---

## 🎯 成功標準

Phase 4 視為成功，當：

1. ✅ **Token 節省達標**
   - Critical tools: >= 60% 節省
   - High priority tools: >= 40% 節省
   - Medium priority tools: >= 20% 節省

2. ✅ **功能完整性**
   - Compact mode 仍包含所有必要資訊
   - Claude 可以基於 compact output 做出正確決策
   - 不需要 follow-up 查詢來獲取基本資訊

3. ✅ **向後相容**
   - 預設參數保持當前行為（或 basic mode）
   - 現有工具調用不會中斷

4. ✅ **測試覆蓋**
   - 所有新參數有單元測試
   - Token 節省有基準測試
   - 文檔有使用範例

5. ✅ **用戶文檔**
   - 清楚說明何時使用哪種模式
   - 提供真實使用場景範例
   - Token 節省數據公開透明

---

## 💡 實作建議

### 參數命名統一

為了一致性，建議所有工具使用相同的參數名：

```csharp
// 輸出詳細度
[Description("Output detail level: summary, basic/normal, detailed/full")]
string detailLevel = "basic"

// 或者使用 mode（更短）
[Description("Output mode: compact, normal, detailed")]
string mode = "normal"

// 建議統一使用 "detailLevel" 因為更明確
```

### 預設值選擇

```csharp
// 建議預設值
detailLevel = "basic"    // 而非 "full"（節省 token）
maxMembers = 10          // 而非 0（避免過多輸出）
includeDocumentation = false  // 僅在需要時包含
```

### 漸進式遷移

```csharp
// 第一步：添加新參數但保持舊行為
public static async Task<string> GetFileOutline(
    string filePath,
    string mode = "normal",  // 新參數，預設保持舊行為
    bool includeMembers = true,     // 保留舊參數
    bool includeDocumentation = true)

// 第二步：在下個版本將 mode 預設改為 "compact"
// 第三步：標記舊參數為 [Obsolete]
```

---

## 結論

Phase 4 token 優化提供了顯著的效益，特別是針對高頻使用的工具（GetFileOutline, GetSymbolInfo）。雖然單用戶 ROI 較長，但在多用戶場景下非常有價值，且能顯著改善用戶體驗。

**建議：** 優先實作 Phase 4A (Critical Priority)，評估效果後再決定是否繼續 Phase 4B/4C。

**下一步：** 開始 Phase 4A 實作，預計 5 個工作天完成。
