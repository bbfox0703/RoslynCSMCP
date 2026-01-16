# RoslynCSMCP Tool Suggestions

建議添加的新工具，按優先級和類別組織。

## 🔥 High Priority (高優先級)

### 1. Code Generation & Refactoring (代碼生成與重構)

#### GenerateBoilerplate
**用途:** 生成常見的樣板代碼
**場景:**
- "生成一個 CRUD controller"
- "為這個實體創建 DTO"
- "生成 repository pattern 代碼"

**參數:**
- `templateType`: CRUD, Repository, Service, DTO, API
- `targetType`: 目標類型名稱
- `outputPath`: 輸出路徑
- `framework`: ASP.NET Core, MVC, etc.

**優點:**
- 加速開發
- 保證一致性
- 減少重複工作

---

#### RenameSymbolSafely
**用途:** 安全地重命名符號（類、方法、屬性等）
**場景:**
- "將 UserService 重命名為 AccountService"
- "重命名這個方法並更新所有引用"

**參數:**
- `symbolName`: 要重命名的符號
- `newName`: 新名稱
- `solutionPath`: 解決方案路徑
- `preview`: 是否僅預覽（不實際修改）

**功能:**
- 找到所有引用
- 生成修改預覽
- 驗證新名稱不衝突
- 可選擇實際執行重命名

**優點:**
- 自動化重構
- 避免手動錯誤
- 跨文件重命名

---

#### ExtractInterface
**用途:** 從類中提取介面
**場景:**
- "為 UserService 提取一個介面"
- "創建 IUserRepository 介面"

**參數:**
- `typeName`: 類名
- `interfaceName`: 介面名稱（可選，自動生成）
- `members`: 要包含的成員（可選，默認所有 public）
- `solutionPath`: 解決方案路徑

**優點:**
- 支持依賴注入
- 改善測試性
- 遵循 SOLID 原則

---

### 2. Architecture Analysis (架構分析)

#### AnalyzeLayerViolations
**用途:** 檢測架構層違規
**場景:**
- "UI 層是否直接調用了 Data 層？"
- "檢查分層架構是否正確"
- "驗證 Clean Architecture 規則"

**參數:**
- `solutionPath`: 解決方案路徑
- `layerDefinitions`: 層定義（JSON/YAML）
- `rulesFile`: 架構規則文件（可選）

**檢測:**
- Presentation → Data 層的直接依賴（違規）
- Core 層依賴 Infrastructure（違規）
- 循環依賴
- 違反依賴方向

**示例配置:**
```json
{
  "layers": [
    { "name": "Presentation", "projects": ["*.Web", "*.API"] },
    { "name": "Application", "projects": ["*.Application", "*.Services"] },
    { "name": "Domain", "projects": ["*.Domain", "*.Core"] },
    { "name": "Infrastructure", "projects": ["*.Data", "*.Infrastructure"] }
  ],
  "rules": [
    { "from": "Presentation", "to": "Application", "allowed": true },
    { "from": "Presentation", "to": "Infrastructure", "allowed": false },
    { "from": "Application", "to": "Domain", "allowed": true },
    { "from": "Domain", "to": "Infrastructure", "allowed": false }
  ]
}
```

---

#### DetectDesignPatterns
**用途:** 識別代碼中使用的設計模式
**場景:**
- "這個專案使用了哪些設計模式？"
- "找出所有 Singleton 實現"
- "識別 Factory Pattern"

**檢測模式:**
- Singleton
- Factory/Abstract Factory
- Builder
- Repository
- Strategy
- Observer
- Decorator
- Adapter
- Template Method

**輸出:**
- 模式類型
- 實現位置
- 是否正確實現
- 建議改進

---

### 3. Code Quality Enhancement (代碼質量增強)

#### FindCodeSmells
**用途:** 檢測代碼異味
**場景:**
- "這段代碼有什麼問題？"
- "找出所有代碼異味"
- "代碼審查準備"

**檢測異味:**
- Long Method (方法過長)
- Large Class (類過大)
- Long Parameter List (參數過多)
- Feature Envy (特性依賴)
- Data Clumps (數據泥團)
- Primitive Obsession (基本類型偏執)
- Switch Statements (過多的 switch)
- Speculative Generality (過度設計)
- Message Chains (消息鏈)
- Middle Man (中間人)

**參數:**
- `severity`: 嚴重程度過濾
- `categories`: 要檢測的異味類別
- `thresholds`: 自定義閾值

---

#### FindMagicNumbers
**用途:** 找出魔術數字和硬編碼值
**場景:**
- "找出所有硬編碼的數字"
- "哪些常量應該提取？"

**檢測:**
- 魔術數字（除了 0, 1, -1 外的數字字面量）
- 硬編碼字符串
- 應該是常量的值

**建議:**
- 提取為常量
- 建議的常量名稱
- 作用域（類常量 vs 靜態字段）

---

#### AnalyzeMethodLength
**用途:** 分析方法長度並建議拆分
**場景:**
- "哪些方法太長了？"
- "這個方法應該如何拆分？"

**分析:**
- 方法行數
- 方法職責數量
- 建議的拆分點
- 提取方法建議

**閾值:**
- 20 行：建議檢查
- 50 行：應該拆分
- 100+ 行：必須重構

---

### 4. Dependency Injection Analysis (依賴注入分析)

#### AnalyzeDIContainer
**用途:** 分析依賴注入配置
**場景:**
- "檢查 DI 容器配置"
- "找出未註冊的服務"
- "驗證服務生命週期"

**檢測:**
- 未註冊的依賴
- 生命週期不匹配（Singleton → Scoped）
- 循環依賴
- 多個實現註冊
- Captive Dependencies

**支持框架:**
- Microsoft.Extensions.DependencyInjection
- Autofac
- Ninject

---

#### FindUnregisteredDependencies
**用途:** 找到構造函數中未註冊的依賴
**場景:**
- "為什麼這個類無法注入？"
- "檢查所有服務是否已註冊"

**檢測:**
- 構造函數參數
- 對應的 DI 註冊
- 建議註冊代碼

---

### 5. Exception Handling Analysis (異常處理分析)

#### AnalyzeExceptionHandling
**用途:** 分析異常處理模式
**場景:**
- "異常處理是否正確？"
- "找出空的 catch 塊"
- "找出被吞掉的異常"

**檢測:**
- 空 catch 塊
- catch (Exception) 但沒有處理
- 異常被吞掉
- 過度使用 try-catch
- 缺少 finally 或 using
- 應該使用 using statement

**建議:**
- 添加日誌
- 重新拋出異常
- 使用更具體的異常類型
- 使用 using statement

---

#### FindSwallowedExceptions
**用途:** 找到被吞掉的異常
**場景:**
- "哪裡的異常被忽略了？"
- "找出靜默失敗"

**檢測模式:**
```csharp
catch (Exception ex)
{
    // 空塊
}

catch (Exception ex)
{
    return null; // 沒有記錄
}
```

---

## ⭐ Medium Priority (中優先級)

### 6. Threading & Concurrency (線程與並發)

#### FindThreadSafetyIssues
**用途:** 檢測線程安全問題
**場景:**
- "這段代碼線程安全嗎？"
- "找出競態條件"

**檢測:**
- 靜態可變字段訪問
- 沒有鎖的共享狀態
- 雙重檢查鎖定錯誤
- 不安全的集合操作

---

#### AnalyzeConcurrency
**用途:** 分析並發代碼模式
**場景:**
- "async/await 使用正確嗎？"
- "檢測死鎖風險"

**檢測:**
- ConfigureAwait(false) 缺失
- Async void 方法
- 阻塞的異步調用 (.Result, .Wait())
- 潛在的死鎖

---

### 7. LINQ & Performance (LINQ 與性能)

#### OptimizeLINQ
**用途:** LINQ 查詢優化建議
**場景:**
- "這個 LINQ 查詢可以優化嗎？"
- "如何提高查詢性能？"

**檢測:**
- 多次枚舉（已有，但可增強）
- 不必要的 ToList()
- 可以用 Any() 替代 Count() > 0
- 可以用 FirstOrDefault() 替代 Where().First()
- OrderBy 在 Take 之後

**建議:**
- 優化的查詢
- 性能影響估計

---

#### AnalyzeEntityFramework
**用途:** Entity Framework 查詢分析
**場景:**
- "檢測 N+1 查詢問題"
- "找出缺少 Include 的查詢"

**檢測:**
- N+1 查詢
- 缺少 Include/ThenInclude
- 過度 Include
- AsNoTracking 缺失
- 投影優化機會

---

### 8. Resource Management (資源管理)

#### FindResourceLeaks
**用途:** 檢測資源洩漏
**場景:**
- "找出未 dispose 的資源"
- "檢測記憶體洩漏風險"

**檢測:**
- IDisposable 未 dispose
- Stream/File 未關閉
- HttpClient 誤用
- 事件訂閱未取消
- Timer 未 dispose

**建議:**
- 使用 using statement
- 實現 IDisposable
- 取消訂閱事件

---

### 9. String & Localization (字符串與本地化)

#### FindStringIssues
**用途:** 檢測字符串處理問題
**場景:**
- "找出文化相關的問題"
- "字符串比較是否正確？"

**檢測:**
- 不安全的字符串比較（==）
- 缺少 StringComparison 參數
- 應該使用 StringBuilder
- 硬編碼的日期/數字格式
- 文化相關的排序

---

#### AnalyzeLocalization
**用途:** 分析本地化問題
**場景:**
- "找出未本地化的字符串"
- "檢查資源文件使用"

**檢測:**
- UI 中的硬編碼字符串
- 缺少資源文件
- 未使用的資源
- 缺少翻譯

---

### 10. Code Similarity (代碼相似性)

#### FindSimilarCode
**用途:** 找到相似（但不完全重複）的代碼
**場景:**
- "找出可以統一的類似代碼"
- "檢測近似重複"

**與 FindDuplicateCode 的區別:**
- 不需要完全相同
- 檢測結構相似性
- 建議抽象和統一

**算法:**
- AST 相似度比較
- 結構模式匹配
- 語義相似度

---

## 🔧 Specialized Tools (專用工具)

### 11. Framework-Specific Analysis (特定框架分析)

#### AnalyzeASPNetCore
**用途:** ASP.NET Core 特定分析
**場景:**
- "檢查 API 最佳實踐"
- "驗證中間件配置"

**檢測:**
- Controller 設計
- Routing 問題
- Model validation
- CORS 配置
- Authentication/Authorization
- 缺少 [ApiController] 屬性
- 返回類型問題

---

#### AnalyzeXUnit
**用途:** xUnit 測試分析
**場景:**
- "測試是否遵循最佳實踐？"
- "檢測測試異味"

**檢測:**
- 缺少 Assert
- 多個 Assert（違反單一責任）
- 測試名稱不清晰
- Fixture 使用不當
- 異步測試問題

---

### 12. API Surface Analysis (API 表面分析)

#### AnalyzePublicAPI
**用途:** 分析公共 API 表面
**場景:**
- "檢查 API 設計"
- "驗證 API 一致性"

**檢測:**
- 命名一致性
- 返回類型一致性
- 參數驗證
- 文檔完整性
- 版本控制

---

#### GenerateAPIDocumentation
**用途:** 生成 API 文檔
**場景:**
- "生成 OpenAPI/Swagger 文檔"
- "創建 API 使用指南"

**輸出格式:**
- Markdown
- HTML
- OpenAPI (Swagger)
- Postman Collection

---

### 13. Git Integration (Git 整合)

#### AnalyzeCodeChurn
**用途:** 分析代碼變動頻率
**場景:**
- "哪些文件變動最頻繁？"
- "找出不穩定的代碼"

**需要:** Git repository

**分析:**
- 變動頻率
- 修改者數量
- 複雜度 vs 變動頻率（風險）

---

#### FindHotspots
**用途:** 找出代碼熱點
**場景:**
- "哪些代碼需要重點關注？"
- "找出高風險區域"

**組合因素:**
- 高複雜度
- 頻繁變更
- 多人修改
- 缺少測試

---

### 14. Configuration Analysis (配置分析)

#### AnalyzeConfiguration
**用途:** 分析配置文件
**場景:**
- "檢查 appsettings.json"
- "找出配置問題"

**檢測:**
- 缺少的配置項
- 敏感信息（密碼、密鑰）
- 環境特定配置
- 未使用的配置

---

### 15. Code Modernization (代碼現代化)

#### ModernizeCode
**用途:** 建議使用現代 C# 語法
**場景:**
- "升級到 C# 12"
- "使用新語法特性"

**建議:**
- String interpolation 替代 String.Format
- Pattern matching 替代 if-else
- Records 替代簡單類
- Primary constructors
- Collection expressions
- Using declarations

---

#### ConvertToAsync
**用途:** 將同步代碼轉換為異步
**場景:**
- "將這個方法改為異步"
- "識別可以異步化的代碼"

**轉換:**
- 方法簽名（添加 async Task）
- 調用點（添加 await）
- 同步 API 替換為異步版本

---

## 📊 Metrics & Reporting (度量與報告)

### 16. Advanced Metrics (進階度量)

#### CalculateMaintainabilityIndex
**用途:** 計算可維護性指數
**場景:**
- "這段代碼可維護性如何？"
- "追踪代碼質量趨勢"

**計算因素:**
- 循環複雜度
- 代碼行數
- Halstead Volume
- 計算深度

**輸出:** 0-100 的分數
- 85-100: 優秀
- 65-84: 良好
- 50-64: 可接受
- <50: 需要重構

---

#### GetDependencyMetrics
**用途:** 計算依賴度量
**場景:**
- "分析模塊耦合度"
- "計算穩定性"

**度量:**
- Afferent Coupling (Ca) - 傳入依賴
- Efferent Coupling (Ce) - 傳出依賴
- Instability (I = Ce / (Ca + Ce))
- Abstractness (A)
- Distance from Main Sequence

---

#### GenerateQualityReport
**用途:** 生成綜合質量報告
**場景:**
- "生成代碼質量儀表板"
- "Sprint 結束報告"

**包含:**
- 複雜度統計
- 覆蓋率
- 安全問題
- 技術債務
- 趨勢圖表

---

## 🎯 Implementation Priority Recommendation

### Phase 1 (立即實現)
1. **RenameSymbolSafely** - 高需求，直接提升開發體驗
2. **FindCodeSmells** - 補充現有代碼質量工具
3. **AnalyzeLayerViolations** - 架構驗證是常見需求
4. **FindMagicNumbers** - 簡單實現，高價值

### Phase 2 (短期)
5. **ExtractInterface** - 常見重構操作
6. **AnalyzeDIContainer** - .NET Core 開發必需
7. **AnalyzeExceptionHandling** - 補充安全性分析
8. **FindThreadSafetyIssues** - 重要但實現複雜

### Phase 3 (中期)
9. **GenerateBoilerplate** - 需要模板系統
10. **DetectDesignPatterns** - 需要模式識別引擎
11. **OptimizeLINQ** - 性能優化熱點
12. **AnalyzeASPNetCore** - 特定框架支持

### Phase 4 (長期)
13. **ModernizeCode** - 需要語法轉換引擎
14. **ConvertToAsync** - 複雜的代碼轉換
15. **AnalyzeCodeChurn** - 需要 Git 整合
16. **CalculateMaintainabilityIndex** - 複雜計算

---

## 💡 Quick Wins (快速勝利)

這些工具實現相對簡單，但提供即時價值：

1. **FindMagicNumbers** - 簡單的語法樹遍歷
2. **FindSwallowedExceptions** - 模式匹配
3. **AnalyzeMethodLength** - 行數計算
4. **FindStringIssues** - 字符串比較檢查
5. **AnalyzePublicAPI** - 基於現有 symbol 分析

這些可以在 1-2 天內實現並帶來明顯的價值。

---

## 🔮 Future Considerations

### AI-Powered Tools
- **GenerateTestsWithAI** - 使用 AI 生成測試
- **SuggestRefactoring** - AI 驅動的重構建議
- **ExplainCode** - 代碼解釋
- **GenerateDocumentationWithAI** - AI 生成文檔

### IDE Integration
- **VS Code Extension** - 編輯器整合
- **Visual Studio Plugin** - IDE 插件
- **JetBrains Rider Support** - Rider 支持

### CI/CD Integration
- **GitHub Actions** - GitHub 工作流
- **Azure DevOps** - Azure Pipelines
- **Quality Gates** - 質量門檻驗證

---

## 📝 Notes

1. **工具設計原則:**
   - 保持工具單一職責
   - 提供多種輸出格式
   - 支持增量分析
   - 善用 Roslyn 緩存

2. **性能考量:**
   - 大型解決方案優化
   - 並行處理
   - 增量分析
   - 結果緩存

3. **可擴展性:**
   - 插件系統
   - 自定義規則
   - 配置文件支持
   - 模板引擎
