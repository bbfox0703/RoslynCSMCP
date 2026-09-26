---
name: roslyn-api-diff
description: Compare the public API of two versions of a C# solution and recommend a semantic version bump with the RoslynMcpServer Advanced tools. Use when the user asks about breaking changes between versions or releases.
---

# API Change Analysis

**Required Module**: `RoslynMcpServer.Advanced`
**Usage**: `/roslyn-api-diff <old-solution> <new-solution>`

Use `AnalyzeAPIChanges` on the two solutions and report the breaking changes, additions, and removals, with the semantic version bump they imply.
