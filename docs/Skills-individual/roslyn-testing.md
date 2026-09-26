---
name: roslyn-testing
description: Find the tests that cover a C# type and analyze test coverage gaps with the RoslynMcpServer Testing tools. Use when the user asks which tests exercise a type or where coverage is missing.
---

# Test Coverage Analysis

**Required Module**: `RoslynMcpServer.Testing`
**Usage**: `/roslyn-testing <type-name> <solution-path>`

Give the user the tests related to the type (`FindTestsForType`) and the coverage gaps around it (`GetTestCoverage`), with the untested areas that carry the most risk first.
