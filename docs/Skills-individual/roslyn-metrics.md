---
name: roslyn-metrics
description: Report size, complexity, and documentation-coverage metrics for a C# solution with the RoslynMcpServer Metrics tools. Use when the user asks for code metrics, statistics, or documentation coverage.
---

# Code Metrics

**Required Module**: `RoslynMcpServer.Metrics`
**Usage**: `/roslyn-metrics <solution-path>`

Report the solution's lines of code, cyclomatic complexity, and documentation coverage, and call out the outliers.

`GetCodeMetrics` gives solution-wide statistics. `GetFileStatistics` takes a single `.cs` file path and gives that file's details. `AnalyzeDocumentationCoverage` measures XML documentation coverage. `AnalyzeMemoryAllocation` finds allocation hotspots when the user also cares about performance.
