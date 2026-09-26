---
name: roslyn-dependencies
description: Analyze a C# solution's project dependencies, circular references, NuGet packages, and dependency-injection setup with the RoslynMcpServer Dependencies tools. Use when the user asks about dependency health, package cleanup, or DI problems.
---

# Dependency Analysis

**Required Module**: `RoslynMcpServer.Dependencies`
**Usage**: `/roslyn-dependencies <solution-path>`

Give the user a picture of the solution's dependency health: how projects depend on each other (include the graph as a Mermaid diagram), circular references, removable packages and project references, outdated, conflicting, or vulnerable packages, and dependency-injection registration problems, with recommendations.

The Dependencies module provides `AnalyzeDependencies`, `GetDependencyGraph`, `FindUnusedDependencies`, `AnalyzePackages`, and `AnalyzeDIContainer`.
