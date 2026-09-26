---
name: roslyn-full-audit
description: End-to-end audit of a C# solution across structure, dependencies, metrics, quality, security, and test coverage with the full RoslynMcpServer. Use when the user asks for a complete or comprehensive code audit.
---

# Full Code Audit

**Required Module**: `RoslynMcpServer` (Full) or all 9 individual modules
**Usage**: `/roslyn-full-audit <solution-path>`

Produce one audit report, organized by area and led by the most important findings, covering structure (`GetProjectStructure`, `GetDependencyGraph`), size and complexity (`GetCodeMetrics`), code quality (`FindCodeSmells`), security (`FindSecurityIssues`), and test coverage (`GetTestCoverage`). Use the other available tools to dig into the problems these surface.
