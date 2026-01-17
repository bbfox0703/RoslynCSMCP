# Skill: Code Quality Analysis
Perform a comprehensive quality check, including complexity analysis, code smells, and naming conventions.

**Required Module**: `RoslynMcpServer.Quality`
**Usage**: `/roslyn-quality <solution-path>`

**Steps**:
1. Call `AnalyzeCodeComplexity` with `threshold: 10`.
2. Call `FindCodeSmells` to detect anti-patterns.
3. Call `FindUnusedCode` with `format: "summary"`.
4. Call `AnalyzeNamingConventions` with `scope: "public"`.
5. Call `FindDuplicateCode` to detect copy-paste logic.
6. Call `FindMagicNumbers` to find hardcoded values.
7. Present quality report with recommendations.