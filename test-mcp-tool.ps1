# 即時監控 MCP server 日誌
# 使用方式: .\test-mcp-tool.ps1

$logFile = "$env:TEMP\RoslynCSMCP\logs\debug-$(Get-Date -Format yyyyMMdd).log"

Write-Host "監控日誌文件: $logFile" -ForegroundColor Cyan
Write-Host "按 Ctrl+C 停止監控" -ForegroundColor Yellow
Write-Host ""

if (Test-Path $logFile) {
    Get-Content $logFile -Wait -Tail 50
} else {
    Write-Host "日誌文件不存在，MCP server 可能還沒啟動" -ForegroundColor Red
    Write-Host "請先在 Claude CLI 中使用 MCP 功能" -ForegroundColor Yellow
}
