---
name: roslyn-batch
description: Quick health check of a C# solution (compilation errors, TODO/FIXME comments, oversized files, deprecated API usage, performance anti-patterns) with the RoslynMcpServer Advanced tools. Use when the user wants a fast overall status of a .sln.
---

# Batch Health Check

**Required Module**: `RoslynMcpServer.Advanced`
**Usage**: `/roslyn-batch <solution-path>`

Give the user one consolidated health report built from `GetCompilationErrors`, `FindTODOComments`, `FindLargeFiles`, `FindDeprecatedAPIs`, and `FindPerformanceIssues`. The five calls are independent of each other. Call these tools directly: `BatchQuery` does not support them.
