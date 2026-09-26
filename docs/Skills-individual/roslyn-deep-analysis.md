---
name: roslyn-deep-analysis
description: In-depth analysis of one C# symbol (filtered references, call hierarchy, class hierarchy, API surface) with the RoslynMcpServer Advanced tools. Use when the user needs to understand how a symbol fits into and is used across a solution.
---

# Deep Symbol Analysis

**Required Module**: `RoslynMcpServer.Advanced`
**Usage**: `/roslyn-deep-analysis <symbol-name> <solution-path>`

Explain how the symbol fits into the solution: who uses it outside of tests (`FindReferencesFiltered`), what calls it and what it calls (`GetCallHierarchy`), for classes its ancestors and descendants (`GetClassHierarchy`), and its API surface (`GetTypeSignature`).
