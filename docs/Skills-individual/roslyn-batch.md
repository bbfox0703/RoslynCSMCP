# Skill: Batch Analysis
Run multiple diagnostic tools in a single batch to get a quick health check of the solution.

**Required Module**: `RoslynMcpServer.Advanced`
**Usage**: `/roslyn-batch <solution-path>`

**Steps**:
1. Call `BatchQuery` with tools: `GetCompilationErrors`, `FindTODOComments`, `FindLargeFiles`, `FindDeprecatedAPIs`, and `FindPerformanceIssues`.
2. Present a consolidated health report.