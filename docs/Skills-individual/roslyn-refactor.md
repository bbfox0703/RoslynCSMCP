# Skill: Refactoring Assessment
Assess the impact of potential refactoring and safely preview changes.

**Required Module**: `RoslynMcpServer.Refactoring`
**Usage**: `/roslyn-refactor <symbol-name> <solution-path>`

**Steps**:
1. Call `GetChangeImpact` to assess the risk of changing the symbol.
2. Call `RenameSymbolSafely` with `previewOnly: true` to preview name changes.
3. Call `AnalyzeLayerViolations` to ensure architectural integrity.
4. Call `ExtractInterface` with `previewOnly: true` if applicable.
5. Present impact report with risk assessment.