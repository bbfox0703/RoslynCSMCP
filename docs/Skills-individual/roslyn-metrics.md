# Skill: Code Metrics
Generate a report on code statistics, complexity, and documentation coverage.

**Required Module**: `RoslynMcpServer.Metrics`
**Usage**: `/roslyn-metrics <solution-path>`

**Steps**:
1. Call `GetCodeMetrics` for overall solution statistics.
2. Call `GetFileStatistics` for file-level details.
3. Call `AnalyzeDocumentationCoverage` to check XML comments.
4. Present metrics including LOC, cyclomatic complexity, and documentation percentage.