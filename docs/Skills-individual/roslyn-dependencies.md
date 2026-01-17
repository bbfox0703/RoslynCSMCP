# Skill: Dependency Analysis
Analyze project dependencies, circular references, and Visualize the DI container setup.

**Required Module**: `RoslynMcpServer.Dependencies`
**Usage**: `/roslyn-dependencies <solution-path>`

**Steps**:
1. Call `AnalyzeDependencies` to check for circular references.
2. Call `GetDependencyGraph` with `format: "mermaid"`.
3. Call `FindUnusedDependencies` to find removable NuGet packages.
4. Call `AnalyzePackages` to check for outdated versions.
5. Call `AnalyzeDIContainer` to review dependency injection setup.
6. Present dependency diagram and optimization recommendations.