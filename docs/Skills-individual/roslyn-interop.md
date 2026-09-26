---
name: roslyn-interop
description: Review a C# solution's Native AOT/trimming readiness, P/Invoke usage, and unsafe code with the RoslynMcpServer Interop tools. Use when the user is preparing for Native AOT or auditing native interop or pointer code.
---

# Interop and AOT Readiness Review

**Required Module**: `RoslynMcpServer.Interop`
**Usage**: `/roslyn-interop <solution-path>`

Produce an interop report with migration and safety recommendations, covering AOT and trimming incompatibilities across C#, XAML, and `.csproj` settings (`AnalyzeAotCompatibility`), P/Invoke patterns including `[DllImport]` to `[LibraryImport]` migration (`AnalyzePInvoke`), and pointer, `fixed`, and `stackalloc` usage (`AnalyzeUnsafeCode`).
