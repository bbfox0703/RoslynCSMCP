# Skill: Security Audit
Audit the codebase for security vulnerabilities, thread safety issues, and exception handling patterns.

**Required Module**: `RoslynMcpServer.Security`
**Usage**: `/roslyn-security <solution-path>`

**Steps**:
1. Call `FindSecurityIssues` with `severity: "High"`.
2. Call `FindThreadSafetyIssues` to detect concurrency problems.
3. Call `AnalyzeExceptionHandling` to review error handling strategy.
4. Present security report with vulnerabilities and remediation steps.