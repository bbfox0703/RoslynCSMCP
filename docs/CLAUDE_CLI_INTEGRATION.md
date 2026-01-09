# Claude CLI (Claude Code) 集成評估

---

### RoslynCSMCP 伺服器特性
- **傳輸協議**: stdio (標準輸入/輸出)
- **啟動命令**: `dotnet run --project <path>`
- **環境變數**: `DOTNET_ENVIRONMENT`, `LOG_LEVEL`
- **MCP 協議版本**: 2024-11-05 (via ModelContextProtocol 0.5.0)

### Claude Desktop vs Claude CLI

| 特性 | Claude Desktop | Claude CLI | 相容性 |
|------|----------------|------------|--------|
| **配置文件** | `claude_desktop_config.json` | `.mcp.json` 或 `~/.claude.json` | ✅ 都支援 |
| **Stdio 傳輸** | ✅ 支援 | ✅ 支援 | ✅ 完全相容 |
| **環境變數** | ✅ 支援 | ✅ 支援 | ✅ 完全相容 |
| **動態配置** | 手動編輯 JSON | `claude mcp` 命令 | ✅ 都可用 |
| **MCP 工具** | 5 個工具 | 5 個工具 | ✅ 完全相同 |

---

## 🎯 集成方案

### 方案 A：專案範圍配置（推薦給團隊）⭐

適合多人協作的專案，配置會被 Git 追蹤並共享。

**優點**:
- ✅ 團隊成員自動獲得配置
- ✅ 版本控制追蹤變更
- ✅ 統一的開發環境

**步驟**:

1. **建立 `.mcp.json` 配置文件**（已自動生成）

2. **團隊成員首次使用**:
   ```bash
   cd /path/to/c-sharp-project
   claude  # Claude CLI 會自動偵測 .mcp.json
   # 系統會提示批准使用 roslyn MCP 伺服器
   ```

3. **手動註冊**（如果需要）:
   ```bash
   claude mcp add --transport stdio roslyn --scope project \
     -- dotnet run --project /absolute/path/to/RoslynMcpServer
   ```

---

### 方案 B：用戶範圍配置（推薦給個人使用）

適合個人開發者，在所有專案中都可使用。

**優點**:
- ✅ 一次配置，隨處可用
- ✅ 不需要在每個專案中設置
- ✅ 個人化配置

**步驟**:

```bash
# 使用絕對路徑註冊到用戶範圍
claude mcp add --transport stdio roslyn --scope user \
  --env DOTNET_ENVIRONMENT=Production \
  --env LOG_LEVEL=Information \
  -- dotnet run --project D:\Github\RoslynMCP\RoslynMcpServer

# 驗證配置
claude mcp list

# 查看詳細資訊
claude mcp get roslyn
```

**配置位置**: `~/.claude.json` (Windows: `%USERPROFILE%\.claude.json`)

---

### 方案 C：本地範圍配置（快速測試）

適合在特定目錄下測試，不會提交到版本控制。

**優點**:
- ✅ 快速測試
- ✅ 不影響其他專案
- ✅ 私密配置

**步驟**:

```bash
cd /path/to/test-project

# 本地範圍是預設值
claude mcp add --transport stdio roslyn \
  -- dotnet run --project D:\Github\RoslynMCP\RoslynMcpServer
```

**配置位置**: `~/.claude.json` (標記為 local scope)

---

## 📝 配置文件格式

### .mcp.json (專案範圍)

```json
{
  "mcpServers": {
    "roslyn": {
      "command": "dotnet",
      "args": ["run", "--project", "${ROSLYN_MCP_PATH:-../../RoslynMCP/RoslynMcpServer}"],
      "env": {
        "DOTNET_ENVIRONMENT": "Production",
        "LOG_LEVEL": "Information"
      }
    }
  }
}
```

**使用環境變數**:
- `${ROSLYN_MCP_PATH}` - 可自定義路徑
- `${ROSLYN_MCP_PATH:-default}` - 提供預設值

---

## 🚀 使用範例

### 在 Claude CLI 中使用 RoslynMCP

```bash
# 1. 開啟 Claude CLI
cd /path/to/your-csharp-project
claude

# 2. 在對話中使用 MCP 工具
> Search for all classes implementing IRepository in MySolution.sln

> Find all references to UserService in MySolution.sln

> Analyze code complexity in src/Services/UserService.cs

> Show me the dependency graph for this solution

> Get symbol information for MyNamespace.MyClass
```

### 查看 MCP 狀態

```bash
# 在 Claude CLI 對話中
> /mcp

# 或使用命令列
claude mcp list
claude mcp get roslyn
```

---

## 🔧 進階配置

### 1. 設置 MCP 超時時間

```bash
# 預設 5 秒，大型解決方案可能需要更長時間
MCP_TIMEOUT=30000 claude
```

### 2. 增加輸出限制

```bash
# 預設 15000 tokens，大型分析結果可能需要更多
MAX_MCP_OUTPUT_TOKENS=50000 claude
```

### 3. 多個 MCP 伺服器

```json
{
  "mcpServers": {
    "roslyn": {
      "command": "dotnet",
      "args": ["run", "--project", "path/to/RoslynMcpServer"]
    },
    "filesystem": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-filesystem", "/path/to/allowed"]
    },
    "github": {
      "type": "http",
      "url": "https://api.github.com/mcp"
    }
  }
}
```

---

### 🔧 可選的改進

以下是可選的改進項目，非必需：

#### 1. 添加 CLI 設置腳本

建立 `setup-claude-cli.sh` (Linux/macOS) 和 `setup-claude-cli.ps1` (Windows):

```bash
#!/bin/bash
# setup-claude-cli.sh

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROSLYN_PATH="$SCRIPT_DIR/RoslynMcpServer"

echo "Setting up RoslynMCP for Claude CLI..."

# 詢問範圍
echo "Select scope:"
echo "1) User (available in all projects)"
echo "2) Project (team-shared, requires .mcp.json)"
read -p "Enter choice [1/2]: " choice

case $choice in
  1)
    claude mcp add --transport stdio roslyn --scope user \
      --env DOTNET_ENVIRONMENT=Production \
      --env LOG_LEVEL=Information \
      -- dotnet run --project "$ROSLYN_PATH"
    ;;
  2)
    claude mcp add --transport stdio roslyn --scope project \
      --env DOTNET_ENVIRONMENT=Production \
      --env LOG_LEVEL=Information \
      -- dotnet run --project "$ROSLYN_PATH"
    ;;
  *)
    echo "Invalid choice"
    exit 1
    ;;
esac

echo "✅ RoslynMCP configured successfully!"
echo "   Run 'claude mcp list' to verify"
```

#### 2. 添加 .gitignore 更新

確保不會意外提交本地配置：

```gitignore
# Claude CLI 本地配置
~/.claude.json

# 但保留專案配置
# .mcp.json 應該被提交
```

#### 3. 建立快速測試腳本

`test-cli-integration.sh`:

```bash
#!/bin/bash
# Quick test of CLI integration

echo "Testing RoslynMCP with Claude CLI..."

# 檢查 Claude CLI 是否安裝
if ! command -v claude &> /dev/null; then
    echo "❌ Claude CLI not found. Please install it first."
    exit 1
fi

# 檢查 MCP 伺服器配置
if ! claude mcp get roslyn &> /dev/null; then
    echo "⚠️  RoslynMCP not configured. Run setup-claude-cli.sh first."
    exit 1
fi

echo "✅ RoslynMCP is configured"
echo ""
echo "Configured servers:"
claude mcp list

echo ""
echo "RoslynMCP details:"
claude mcp get roslyn
```

---

## 🎯 推薦配置策略

### 個人開發者
1. 使用**用戶範圍**配置
2. 一次設置，隨處使用
3. 不需要在每個專案中重複配置

```bash
claude mcp add --transport stdio roslyn --scope user \
  -- dotnet run --project /path/to/RoslynMcpServer
```

### 團隊協作
1. 提交 `.mcp.json` 到版本控制
2. 使用相對路徑或環境變數
3. 在 README 中說明首次使用步驟

```json
{
  "mcpServers": {
    "roslyn": {
      "command": "dotnet",
      "args": ["run", "--project", "${ROSLYN_MCP_PATH}"],
      "env": {
        "DOTNET_ENVIRONMENT": "Production"
      }
    }
  }
}
```

團隊成員設置環境變數：
```bash
# Linux/macOS
export ROSLYN_MCP_PATH=/path/to/RoslynMcpServer

# Windows
$env:ROSLYN_MCP_PATH = "D:\path\to\RoslynMcpServer"
```

---

## 🧪 測試計劃

### 手動測試檢查清單

- [ ] **配置測試**
  - [ ] 用戶範圍配置成功
  - [ ] 專案範圍配置成功
  - [ ] `claude mcp list` 顯示 roslyn 伺服器
  - [ ] `claude mcp get roslyn` 顯示詳細資訊

- [ ] **功能測試**
  - [ ] SearchSymbols - 搜尋類別/方法
  - [ ] FindReferences - 查找引用
  - [ ] GetSymbolInfo - 獲取符號資訊
  - [ ] AnalyzeDependencies - 依賴分析
  - [ ] AnalyzeCodeComplexity - 複雜度分析

- [ ] **環境測試**
  - [ ] Windows 環境
  - [ ] Linux/macOS 環境
  - [ ] WSL 環境

- [ ] **錯誤處理**
  - [ ] 無效的 .sln 路徑
  - [ ] 超時情況
  - [ ] 大型解決方案 (>50 專案)

### 自動化測試（未來）

建議建立 E2E 測試：

```bash
# test-e2e.sh
#!/bin/bash

# 1. 配置 MCP 伺服器
claude mcp add --transport stdio roslyn-test --scope local \
  -- dotnet run --project ./RoslynMcpServer

# 2. 測試各項功能
echo "Testing SearchSymbols..." # 使用測試 solution

# 3. 清理
claude mcp remove roslyn-test
```

---

## 📈 效益分析

### 對 Claude CLI 用戶的優勢

| 功能 | 無 RoslynMCP | 有 RoslynMCP | 改進 |
|------|-------------|--------------|------|
| **符號搜尋** | 使用 Grep（文字） | 語義搜尋 | 🚀 更精確 |
| **找引用** | 手動搜尋 | 自動追蹤 | ⏱️ 省時 |
| **理解架構** | 讀多個檔案 | 一鍵分析 | 📊 全面 |
| **複雜度分析** | 無 | 自動計算 | ✨ 新功能 |
| **依賴關係** | 手動整理 | 圖形化呈現 | 🎯 清晰 |

### 使用場景

1. **重構大型專案** - 快速找出所有引用
2. **理解陌生代碼** - 分析架構和依賴
3. **程式碼審查** - 識別高複雜度方法
4. **文件生成** - 快速獲取符號資訊
5. **技術債務評估** - 依賴分析和複雜度報告

---

## 🚦 實施建議

### 階段 1: 基礎集成（立即可行）

1. ✅ 建立 `.mcp.json` 配置文件
2. ✅ 更新 README 說明 CLI 使用方式
3. ✅ 建立本文件（評估報告）
4. ⏰ 手動測試 5 個 MCP 工具

**預計時間**: 已完成配置文件，剩餘文檔更新 ~30 分鐘

### 階段 2: 完善文檔（建議）

1. 更新 CLAUDE.md 添加 CLI 章節
2. 建立 setup-claude-cli 腳本
3. 錄製示範影片或 GIF
4. 撰寫部落格文章

**預計時間**: 1-2 小時

### 階段 3: 最佳化（可選）

1. 建立 E2E 自動化測試
2. 效能優化（大型解決方案）
3. 錯誤訊息改進
4. 添加進度指示器

**預計時間**: 4-6 小時

---

## 💡 最佳實踐

### 1. 路徑配置

❌ **不推薦** - 硬編碼絕對路徑:
```json
{
  "command": "dotnet",
  "args": ["run", "--project", "D:\\Users\\Andy\\Projects\\RoslynMCP"]
}
```

✅ **推薦** - 使用環境變數和相對路徑:
```json
{
  "command": "dotnet",
  "args": ["run", "--project", "${ROSLYN_MCP_PATH:-./RoslynMcpServer}"]
}
```

### 2. 效能調整

對於大型解決方案 (>30 專案)：

```bash
# 增加超時時間
MCP_TIMEOUT=60000 claude

# 增加輸出限制
MAX_MCP_OUTPUT_TOKENS=100000 claude
```

### 3. 安全性考量

- ✅ 使用專案範圍時，團隊成員需手動批准
- ✅ 敏感資訊使用環境變數 `${API_KEY}`
- ✅ 不要在 .mcp.json 中硬編碼密碼或 token
- ✅ 定期更新 MCP 套件到最新版本

---

## 📚 相關資源

### Claude CLI 文檔
- [MCP 伺服器配置](https://docs.anthropic.com/claude/docs/mcp)
- [claude mcp 命令參考](https://docs.anthropic.com/claude/docs/cli-mcp-commands)

### RoslynMCP 文檔
- `CLAUDE.md` - 專案架構指南
- `UPGRADE_COMPLETE.md` - .NET 10 升級文件
- `README.md` - 基本使用說明

### MCP 協議
- [ModelContextProtocol 官方文檔](https://modelcontextprotocol.io/)
- [MCP TypeScript SDK](https://github.com/modelcontextprotocol/typescript-sdk)
