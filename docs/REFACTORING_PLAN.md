# MCP Tools 模組化重構計畫

**建立日期**: 2026-01-16
**狀態**: 進行中
**目標**: 將 42 個 MCP 工具拆分成獨立的 MCP Server，實現運行時 Token 優化

---

## 目標架構

```
RoslynCSMCP.sln
│
├── src/
│   ├── RoslynMcpServer.Core/              # 共用程式庫 (類別庫)
│   │   ├── Services/                       # 所有分析服務
│   │   ├── Models/                         # 資料模型
│   │   ├── Configuration/                  # 配置類別
│   │   └── Utilities/                      # 工具類 (Security, Cache, Logging)
│   │
│   ├── RoslynMcpServer.Navigation/         # 導航工具 MCP (6 tools) ~1,050 tokens
│   │   ├── Program.cs
│   │   └── Tools/NavigationTools.cs
│   │
│   ├── RoslynMcpServer.Quality/            # 代碼品質 MCP (6 tools) ~1,050 tokens
│   │   ├── Program.cs
│   │   └── Tools/QualityTools.cs
│   │
│   ├── RoslynMcpServer.Security/           # 安全分析 MCP (3 tools) ~525 tokens
│   │   ├── Program.cs
│   │   └── Tools/SecurityTools.cs
│   │
│   ├── RoslynMcpServer.Dependencies/       # 依賴分析 MCP (5 tools) ~875 tokens
│   │   ├── Program.cs
│   │   └── Tools/DependencyTools.cs
│   │
│   ├── RoslynMcpServer.Refactoring/        # 重構工具 MCP (4 tools) ~700 tokens
│   │   ├── Program.cs
│   │   └── Tools/RefactoringTools.cs
│   │
│   ├── RoslynMcpServer.Testing/            # 測試分析 MCP (2 tools) ~350 tokens
│   │   ├── Program.cs
│   │   └── Tools/TestingTools.cs
│   │
│   ├── RoslynMcpServer.Metrics/            # 指標分析 MCP (3 tools) ~525 tokens
│   │   ├── Program.cs
│   │   └── Tools/MetricsTools.cs
│   │
│   ├── RoslynMcpServer.Advanced/           # 進階工具 MCP (13 tools) ~2,275 tokens
│   │   ├── Program.cs
│   │   └── Tools/AdvancedTools.cs
│   │
│   └── RoslynMcpServer/                    # 完整版 MCP (42 tools) - 保留相容性
│       └── (原有結構，引用 Core)
│
├── tests/
│   ├── RoslynMcpServer.Core.Tests/
│   └── RoslynMcpServer.Integration.Tests/
│
└── docs/
```

---

## 工具分組明細

### Navigation (6 tools)
| 工具名稱 | 描述 | 依賴服務 |
|----------|------|----------|
| SearchSymbols | 符號搜尋 | SymbolSearchService |
| FindReferences | 找參考 | SymbolSearchService |
| GetSymbolInfo | 符號資訊 | SymbolSearchService |
| GetProjectStructure | 專案結構 | ProjectStructureService |
| GetFileOutline | 檔案大綱 | FileAnalysisService |
| FindImplementations | 找實作 | SymbolSearchService |

### Quality (6 tools)
| 工具名稱 | 描述 | 依賴服務 |
|----------|------|----------|
| AnalyzeCodeComplexity | 複雜度分析 | CodeMetricsService |
| FindCodeSmells | 代碼異味 | Phase1AnalysisService |
| FindUnusedCode | 未使用代碼 | UnusedCodeAnalyzer |
| FindDuplicateCode | 重複代碼 | DuplicateCodeAnalyzer |
| FindMagicNumbers | 魔術數字 | Phase1AnalysisService |
| AnalyzeNamingConventions | 命名規範 | NamingConventionAnalyzer |

### Security (3 tools)
| 工具名稱 | 描述 | 依賴服務 |
|----------|------|----------|
| FindSecurityIssues | 安全問題 | SecurityIssueAnalyzer |
| FindThreadSafetyIssues | 線程安全 | Phase2AnalysisService |
| AnalyzeExceptionHandling | 例外處理 | Phase2AnalysisService |

### Dependencies (5 tools)
| 工具名稱 | 描述 | 依賴服務 |
|----------|------|----------|
| AnalyzeDependencies | 依賴分析 | CodeAnalysisService |
| GetDependencyGraph | 依賴圖 | DependencyGraphService |
| FindUnusedDependencies | 未使用依賴 | UnusedDependencyAnalyzer |
| AnalyzePackages | 套件分析 | PackageAnalysisService |
| AnalyzeDIContainer | DI 容器分析 | Phase2AnalysisService |

### Refactoring (4 tools)
| 工具名稱 | 描述 | 依賴服務 |
|----------|------|----------|
| RenameSymbolSafely | 安全重命名 | Phase1AnalysisService |
| ExtractInterface | 提取介面 | Phase2AnalysisService |
| GetChangeImpact | 變更影響 | ChangeImpactAnalyzer |
| AnalyzeLayerViolations | 層級違規 | Phase1AnalysisService |

### Testing (2 tools)
| 工具名稱 | 描述 | 依賴服務 |
|----------|------|----------|
| FindTestsForType | 找測試 | TestDiscoveryService |
| GetTestCoverage | 測試覆蓋 | TestCoverageAnalyzer |

### Metrics (3 tools)
| 工具名稱 | 描述 | 依賴服務 |
|----------|------|----------|
| GetCodeMetrics | 代碼指標 | CodeMetricsService |
| GetFileStatistics | 檔案統計 | FileStatisticsAnalyzer |
| AnalyzeDocumentationCoverage | 文檔覆蓋 | DocumentationAnalyzer |

### Advanced (13 tools)
| 工具名稱 | 描述 | 依賴服務 |
|----------|------|----------|
| BatchQuery | 批次查詢 | BatchQueryService |
| FindReferencesFiltered | 過濾參考 | SymbolSearchService |
| FindReferencesAcrossSolutions | 跨方案參考 | SymbolSearchService |
| GetCompilationErrors | 編譯錯誤 | DiagnosticsService |
| GetCallHierarchy | 呼叫層級 | CallHierarchyService |
| GetClassHierarchy | 類別層級 | SymbolSearchService |
| GetTypeSignature | 類型簽名 | TypeSignatureService |
| FindAttributeUsages | 屬性用法 | AttributeSearchService |
| FindDeprecatedAPIs | 棄用 API | DeprecatedAPIAnalyzer |
| FindTODOComments | TODO 註解 | TODOCommentAnalyzer |
| FindLargeFiles | 大型檔案 | LargeFileAnalyzer |
| AnalyzeAPIChanges | API 變更 | APIChangeAnalyzer |
| FindPerformanceIssues | 效能問題 | PerformanceIssueAnalyzer |

---

## 實施階段

### Phase 1: 建立 Core 共用庫 ✅ 完成
**預估時間**: 3-4 小時
**風險**: 中

**工作項目**:
- [x] 建立 `src/RoslynMcpServer.Core/` 專案
- [x] 遷移 `Services/` 目錄
- [x] 遷移 `Models/` 目錄
- [x] 遷移 `Configuration/` 目錄
- [x] 遷移工具類 (SecurityValidator, DiagnosticLogger, Cache)
- [x] 更新命名空間
- [x] 原 RoslynMcpServer 引用 Core
- [x] 驗證建置成功

**檔案遷移清單**:
```
RoslynMcpServer/
├── Services/           → Core/Services/
│   ├── CodeAnalysisService.cs
│   ├── SymbolSearchService.cs
│   ├── ... (所有 *Service.cs, *Analyzer.cs)
├── Models/             → Core/Models/
│   └── SearchModels.cs
├── Configuration/      → Core/Configuration/
│   ├── ToolProfileConfig.cs
│   └── McpToolFilterExtensions.cs
└── (保留 Program.cs, Tools/)
```

---

### Phase 2: 建立 Navigation MCP (試點) ✅ 完成
**預估時間**: 2 小時
**風險**: 低

**工作項目**:
- [x] 建立 `src/RoslynMcpServer.Navigation/` 專案
- [x] 建立 Program.cs (MCP 入口)
- [x] 建立 Tools/NavigationTools.cs
- [x] 從 CodeNavigationTools.cs 遷移 6 個工具
- [x] 配置 MCP Server
- [x] 測試獨立運行
- [ ] 驗證 Claude Desktop 整合

---

### Phase 3: 建立 Quality + Security MCP ✅ 完成
**預估時間**: 2-3 小時
**風險**: 低

**工作項目**:
- [x] 建立 `src/RoslynMcpServer.Quality/` (6 tools)
- [x] 建立 `src/RoslynMcpServer.Security/` (3 tools)
- [x] 遷移對應工具
- [x] 測試運行

---

### Phase 4: 建立 Dependencies + Refactoring MCP ✅ 完成
**預估時間**: 2-3 小時
**風險**: 低

**工作項目**:
- [x] 建立 `src/RoslynMcpServer.Dependencies/` (5 tools)
- [x] 建立 `src/RoslynMcpServer.Refactoring/` (4 tools)
- [x] 遷移對應工具
- [x] 測試運行

---

### Phase 5: 建立 Testing + Metrics + Advanced MCP ✅ 完成
**預估時間**: 3-4 小時
**風險**: 低

**工作項目**:
- [x] 建立 `src/RoslynMcpServer.Testing/` (2 tools)
- [x] 建立 `src/RoslynMcpServer.Metrics/` (3 tools)
- [x] 建立 `src/RoslynMcpServer.Advanced/` (13 tools)
- [x] 遷移對應工具
- [x] 測試運行

---

### Phase 6: 調整測試專案 ⏳
**預估時間**: 2-3 小時
**風險**: 中

**工作項目**:
- [ ] 建立 `tests/RoslynMcpServer.Core.Tests/`
- [ ] 遷移現有測試
- [ ] 建立整合測試
- [ ] 確保所有測試通過

---

### Phase 7: 文檔更新 ⏳
**預估時間**: 1-2 小時
**風險**: 低

**工作項目**:
- [ ] 更新 README.md
- [ ] 更新 CLAUDE.md
- [ ] 更新安裝腳本
- [ ] 建立 MCP 選擇指南
- [ ] 更新 claude_desktop_config.json 範例

---

## Claude Desktop 配置範例

### 最小配置 (Navigation only)
```json
{
  "mcpServers": {
    "roslyn-nav": {
      "command": "dotnet",
      "args": ["run", "--project", "D:/RoslynCSMCP/src/RoslynMcpServer.Navigation"]
    }
  }
}
```

### 標準配置 (Navigation + Quality + Security)
```json
{
  "mcpServers": {
    "roslyn-nav": {
      "command": "dotnet",
      "args": ["run", "--project", "D:/RoslynCSMCP/src/RoslynMcpServer.Navigation"]
    },
    "roslyn-quality": {
      "command": "dotnet",
      "args": ["run", "--project", "D:/RoslynCSMCP/src/RoslynMcpServer.Quality"]
    },
    "roslyn-security": {
      "command": "dotnet",
      "args": ["run", "--project", "D:/RoslynCSMCP/src/RoslynMcpServer.Security"]
    }
  }
}
```

### 完整配置 (All modules)
```json
{
  "mcpServers": {
    "roslyn-full": {
      "command": "dotnet",
      "args": ["run", "--project", "D:/RoslynCSMCP/src/RoslynMcpServer"]
    }
  }
}
```

---

## 技術決策記錄

### TD-001: 服務共享方式
**決策**: 所有服務放在 Core 庫，各 MCP 引用 Core
**原因**: 避免服務重複，維護方便

### TD-002: MSBuild 註冊
**決策**: 每個 MCP Server 獨立註冊 MSBuild
**原因**: 各 MCP 是獨立進程，需要各自初始化

### TD-003: 快取策略
**決策**: 各 MCP 獨立記憶體快取，可選共享 Redis
**原因**: 平衡簡單性和效能

### TD-004: 日誌配置
**決策**: 各 MCP 獨立日誌檔案，使用相同目錄
**原因**: 便於問題排查

### TD-005: 保留原 RoslynMcpServer
**決策**: 保留作為 "Full" 版本，引用 Core
**原因**: 向後相容，簡化升級

---

## 進度追蹤

| 階段 | 狀態 | 開始時間 | 完成時間 | 備註 |
|------|------|----------|----------|------|
| Phase 1 | ✅ 完成 | 2026-01-16 | 2026-01-16 | Core 庫建立完成，所有服務遷移成功 |
| Phase 2 | ✅ 完成 | 2026-01-16 | 2026-01-17 | Navigation MCP 建立完成，獨立運行測試通過 |
| Phase 3 | ✅ 完成 | 2026-01-17 | 2026-01-17 | Quality + Security MCP 建立完成 |
| Phase 4 | ✅ 完成 | 2026-01-17 | 2026-01-17 | Dependencies + Refactoring MCP 建立完成 |
| Phase 5 | ✅ 完成 | 2026-01-17 | 2026-01-17 | Testing + Metrics + Advanced MCP 建立完成 |
| Phase 6 | ⏳ 待開始 | | | |
| Phase 7 | ⏳ 待開始 | | | |

---

## 風險與緩解

| 風險 | 影響 | 緩解措施 |
|------|------|----------|
| 服務依賴複雜 | 中 | 仔細分析依賴關係，分階段遷移 |
| 建置時間增加 | 低 | 使用專案參考，增量建置 |
| 部署複雜度 | 中 | 提供安裝腳本，清晰文檔 |
| 測試覆蓋下降 | 中 | 先建立 Core 測試，再逐步補充 |

---

**最後更新**: 2026-01-17
