# .NET 10 升級評估報告

生成日期：2026-01-08
專案：RoslynCSMCP Server
當前目標框架：.NET 8.0
建議目標框架：.NET 10.0

---

## 📊 套件升級矩陣

| 套件名稱 | 當前版本 | 最新版本 | 版本差距 | .NET 10 相容 | 風險等級 |
|---------|---------|---------|---------|-------------|---------|
| **Microsoft.CodeAnalysis.CSharp** | 4.8.0 | 5.0.0 | 🟡 主版本 | ✅ 是 | 🟡 中 |
| **Microsoft.CodeAnalysis.CSharp.Workspaces** | 4.8.0 | 5.0.0 | 🟡 主版本 | ✅ 是 | 🟡 中 |
| **Microsoft.CodeAnalysis.Workspaces.MSBuild** | 4.8.0 | 5.0.0 | 🟡 主版本 | ✅ 是 | 🟡 中 |
| **ModelContextProtocol** | 0.3.0-preview.1 | 0.5.0-preview.1 | 🟢 次版本 | ✅ 是 | 🟡 中 |
| **Microsoft.Extensions.Hosting** | 8.0.0 | 10.0.1 | 🟡 主版本 | ✅ 原生 | 🟢 低 |
| **Microsoft.Extensions.Caching.Memory** | 9.0.6 | 10.0.1 | 🟢 次版本 | ✅ 原生 | 🟢 低 |
| **Microsoft.Extensions.Caching.StackExchangeRedis** | 9.0.6 | 10.0.1 | 🟢 次版本 | ✅ 原生 | 🟢 低 |
| **Microsoft.Build.Locator** | 1.6.10 | 1.11.2 | 🟢 次版本 | ✅ 是 | 🟢 低 |
| **System.Text.Json** | 9.0.6 | 10.0.1 | 🟢 次版本 | ✅ 原生 | 🟢 低 |

**圖例：**
- 🟢 低風險：向後相容，API 穩定
- 🟡 中風險：可能有 API 變更，需測試
- 🔴 高風險：重大變更，需要大量修改

---

## 🎯 相容性分析

### ✅ 完全相容的套件

#### Microsoft.Extensions.* 系列 (10.0.1)
- **來源**: Microsoft 官方 .NET 10 發布
- **相容性**: 原生 .NET 10 套件
- **向後相容**: 是
- **建議**: 強烈建議升級

#### System.Text.Json (10.0.1)
- **來源**: Microsoft 官方 .NET 10 發布
- **相容性**: 原生 .NET 10 套件
- **效能改進**: 是（.NET 10 包含序列化效能優化）
- **建議**: 強烈建議升級

#### Microsoft.Build.Locator (1.11.2)
- **來源**: Microsoft Roslyn 團隊
- **相容性**: 支援 .NET 8.0+
- **重大變更**: 無
- **建議**: 建議升級

### ⚠️ 需要測試的套件

#### Microsoft.CodeAnalysis.* 系列 (5.0.0)
- **主要變更**:
  - 支援 C# 14 新語法
  - 效能改進（更快的編譯速度）
  - 新增 API 用於處理 field-backed properties

- **目標框架**: .NET 8.0, .NET Standard 2.0
- **向後相容性**: ⚠️ 可能有破壞性變更

- **已知問題**:
  ```
  - API 簽章可能有小幅調整
  - 某些過時的 API 可能已移除
  - 語義模型行為可能有細微差異
  ```

- **遷移建議**:
  1. 先升級到 4.12.0（如果有）進行漸進式測試
  2. 檢視 [Roslyn 5.0.0 Release Notes](https://github.com/dotnet/roslyn/releases/tag/v5.0.0)
  3. 特別測試 `SymbolSearchService` 中的語義分析邏輯

#### ModelContextProtocol (0.5.0-preview.1)
- **狀態**: Preview（預覽版）
- **版本跳躍**: 0.3 → 0.5（跳過 0.4）
- **目標框架**: .NET 8.0, .NET Standard 2.0

- **潛在變更**:
  ```
  - MCP 協議可能有版本更新
  - Server/Tool 註冊 API 可能有變更
  - Stdio transport 行為可能調整
  ```

- **風險評估**:
  - 🟡 中等風險 - Preview 版本可能不穩定
  - ⚠️ 需要測試 MCP 通訊是否正常
  - ⚠️ 需要測試 Claude Desktop 整合

- **遷移建議**:
  1. 查看 [ModelContextProtocol GitHub](https://github.com/modelcontextprotocol) 變更日誌
  2. 測試所有 5 個 MCP Tools 是否正常運作
  3. 驗證與 Claude Desktop 的整合

---

## 🚨 風險評估

### 高風險區域

#### 1. Roslyn API 使用
**影響檔案**:
- `Services/SymbolSearchService.cs`
- `Services/IncrementalAnalyzer.cs`
- `Services/CodeAnalysisService.cs`

**風險點**:
- `ISymbol` 介面行為變更
- `SemanticModel.GetSymbolInfo()` 回傳值變化
- `MSBuildWorkspace` 初始化差異

**測試重點**:
```csharp
// 需要特別測試的場景
1. SearchSymbols - 萬用字元搜尋準確性
2. FindReferences - 引用追蹤完整性
3. GetSymbolInfo - 符號資訊完整性
4. AnalyzeDependencies - 依賴圖正確性
5. AnalyzeCodeComplexity - 複雜度計算準確性
```

#### 2. MCP 協議整合
**影響檔案**:
- `Program.cs`
- `Tools/CodeNavigationTools.cs`

**風險點**:
- MCP Server 註冊 API 變更
- Tool 屬性 (`[McpServerTool]`) 行為變化
- Stdio transport 序列化問題

**測試重點**:
```bash
# 使用 MCP Inspector 測試
npx @modelcontextprotocol/inspector dotnet run --project ./RoslynMcpServer

# 測試每個 Tool
1. SearchSymbols - 測試萬用字元搜尋
2. FindReferences - 測試引用查找
3. GetSymbolInfo - 測試符號查詢
4. AnalyzeDependencies - 測試依賴分析
5. AnalyzeCodeComplexity - 測試複雜度分析
```

### 中風險區域

#### 快取機制
**影響檔案**:
- `Services/CacheManager.cs`
- `Services/CodeAnalysisService.cs`

**風險點**:
- `IMemoryCache` API 變更
- `IDistributedCache` 序列化行為

**測試重點**:
- 驗證多層快取正確性
- 確認快取過期策略
- 測試並行存取安全性

---

## 📋 建議的升級策略

### 方案 A：保守漸進式升級（推薦）⭐

**階段 1：框架升級**
```xml
<!-- 只修改 TargetFramework -->
<TargetFramework>net10.0</TargetFramework>
```
- 保持所有套件版本不變
- 測試基本功能是否正常
- 預期成功率：95%

**階段 2：低風險套件升級**
```bash
dotnet add package Microsoft.Build.Locator --version 1.11.2
dotnet add package Microsoft.Extensions.Hosting --version 10.0.1
dotnet add package Microsoft.Extensions.Caching.Memory --version 10.0.1
dotnet add package Microsoft.Extensions.Caching.StackExchangeRedis --version 10.0.1
dotnet add package System.Text.Json --version 10.0.1
```
- 升級 Microsoft.Extensions 和 System.Text.Json
- 執行完整測試
- 預期問題：極少

**階段 3：Roslyn 套件升級**
```bash
dotnet add package Microsoft.CodeAnalysis.CSharp --version 5.0.0
dotnet add package Microsoft.CodeAnalysis.CSharp.Workspaces --version 5.0.0
dotnet add package Microsoft.CodeAnalysis.Workspaces.MSBuild --version 5.0.0
```
- 升級所有 Roslyn 套件
- **重點測試所有 5 個 MCP Tools**
- 如果有問題，回退到 4.11.0 或 4.12.0

**階段 4：MCP 套件升級**
```bash
dotnet add package ModelContextProtocol --version 0.5.0-preview.1
```
- 最後升級 MCP 套件
- 使用 MCP Inspector 測試
- 測試 Claude Desktop 整合
- 如果有問題，保持在 0.3.0-preview.1

**預期時間**: 2-3 小時
**成功率**: 90%
**回退能力**: 高

---

### 方案 B：一次性全面升級

**一次升級所有套件到最新版本**

**優點**:
- 快速
- 獲得所有最新功能

**缺點**:
- 風險較高
- 難以定位問題來源
- 可能需要大量除錯

**預期時間**: 4-6 小時（含除錯）
**成功率**: 70%
**回退能力**: 中

---

### 方案 C：僅框架升級（最保守）

**只升級 TargetFramework，不動任何套件**

**優點**:
- 風險最低
- 立即享受 .NET 10 執行時期優化

**缺點**:
- 無法使用 C# 14 新功能
- 套件版本過舊可能有安全隱患

**預期時間**: 15 分鐘
**成功率**: 98%
**回退能力**: 極高

---

## ✅ 測試檢查清單

### 單元測試（建議補充）

目前專案缺少單元測試，建議升級前先建立：

```csharp
// 測試範例
[Fact]
public async Task SearchSymbols_WildcardPattern_ReturnsCorrectResults()
{
    var service = CreateSymbolSearchService();
    var results = await service.SearchSymbolsAsync(
        "*Service",
        "TestSolution.sln",
        "class",
        true
    );
    Assert.NotEmpty(results);
}
```

### 整合測試檢查清單

- [ ] **SearchSymbols**
  - [ ] 萬用字元 `*Service` 搜尋
  - [ ] 萬用字元 `Get*` 搜尋
  - [ ] 大小寫不敏感搜尋
  - [ ] 多種符號類型篩選

- [ ] **FindReferences**
  - [ ] 類別引用追蹤
  - [ ] 方法引用追蹤
  - [ ] 引用與定義區分
  - [ ] 跨專案引用

- [ ] **GetSymbolInfo**
  - [ ] 類別資訊查詢
  - [ ] 方法資訊查詢（含參數）
  - [ ] 屬性資訊查詢
  - [ ] 命名空間資訊

- [ ] **AnalyzeDependencies**
  - [ ] 專案引用分析
  - [ ] NuGet 套件分析
  - [ ] 命名空間使用統計
  - [ ] 符號可存取性統計

- [ ] **AnalyzeCodeComplexity**
  - [ ] 循環複雜度計算
  - [ ] 閾值篩選
  - [ ] 多檔案分析

- [ ] **MCP 整合**
  - [ ] MCP Inspector 連接
  - [ ] Claude Desktop 連接
  - [ ] 所有 Tools 可呼叫
  - [ ] 錯誤處理正確

- [ ] **效能測試**
  - [ ] 大型解決方案載入（>50 專案）
  - [ ] 快取機制運作
  - [ ] 記憶體使用合理

---

## 📝 升級前準備

### 1. 建立備份

```bash
# 建立 Git tag
git tag v1.0-net8-stable
git push origin v1.0-net8-stable

# 建立升級分支
git checkout -b upgrade/dotnet10
```

### 2. 記錄當前狀態

```bash
# 記錄當前套件版本
dotnet list package > packages-before-upgrade.txt

# 建置並確保成功
dotnet build -c Release
```

### 3. 準備測試環境

```bash
# 準備測試用 C# 解決方案
# 建議使用實際專案進行測試，而非範例專案
```

---

## 🎯 最終建議

### 推薦方案：方案 A（保守漸進式升級）

**理由**:
1. ✅ 風險可控，每階段可獨立驗證
2. ✅ 問題容易定位和解決
3. ✅ 有充分的回退選項
4. ✅ 符合生產環境最佳實踐

### 執行時機
- 建議在**非工作時間**進行
- 確保有**完整測試環境**
- 預留**2-3 小時**進行升級和測試

### 成功指標
- ✅ 所有 5 個 MCP Tools 運作正常
- ✅ MCP Inspector 測試通過
- ✅ Claude Desktop 整合正常
- ✅ 無編譯警告
- ✅ 無執行時期錯誤

---

## 📚 參考資源

### 官方文件
- [What's new in .NET 10](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/overview)
- [Announcing .NET 10](https://devblogs.microsoft.com/dotnet/announcing-dotnet-10/)
- [Microsoft.CodeAnalysis 5.0.0 on NuGet](https://www.nuget.org/packages/microsoft.codeanalysis.csharp/)
- [ModelContextProtocol 0.5.0 on NuGet](https://www.nuget.org/packages/ModelContextProtocol)

### 變更日誌
- [Roslyn Release Notes](https://github.com/dotnet/roslyn/releases)
- [Model Context Protocol GitHub](https://github.com/modelcontextprotocol)

### 遷移指南
- [Upgrade ASP.NET Core to .NET 10](https://learn.microsoft.com/en-us/aspnet/core/migration/)
- [Breaking Changes in .NET 10](https://learn.microsoft.com/en-us/dotnet/core/compatibility/10.0)

---

**報告結束**

如需協助執行升級，請告知要採用哪個方案。
