---
name: roslyn-security
description: Audit a C# solution for security vulnerabilities, thread-safety risks, and exception-handling gaps with the RoslynMcpServer Security tools. Use when the user asks for a security review of a .sln.
---

# Security Audit

**Required Module**: `RoslynMcpServer.Security`
**Usage**: `/roslyn-security <solution-path>`

Produce a security report the user can act on: each finding with its location, why it is exploitable or risky, and how to remediate it, led by the High and Critical findings.

`FindSecurityIssues` covers SQL injection, hardcoded secrets, weak cryptography, path traversal, and unsafe deserialization. `FindThreadSafetyIssues` covers shared-state race conditions. `AnalyzeExceptionHandling` covers empty catches, swallowed exceptions, and missing disposal.
