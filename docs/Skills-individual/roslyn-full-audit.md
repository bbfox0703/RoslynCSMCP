# Skill: Full Code Audit
Perform a complete end-to-end audit of the codebase across all available domains.

**Required Module**: `RoslynMcpServer` (Full) or all 8 individual modules.
**Usage**: `/roslyn-full-audit <solution-path>`

**Steps**:
1. Call `GetProjectStructure` (Navigation).
2. Call `GetDependencyGraph` (Dependencies).
3. Call `GetCodeMetrics` (Metrics).
4. Call `FindCodeSmells` (Quality).
5. Call `FindSecurityIssues` (Security).
6. Call `GetTestCoverage` (Testing).
7. Present a comprehensive audit report covering structure, health, and security.