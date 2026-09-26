---
name: roslyn-quality
description: Review the code quality of a C# solution (complexity, code smells, dead code, naming, duplication, magic numbers, concurrency patterns) with the RoslynMcpServer Quality tools. Use when the user asks for a quality review or refactoring candidates.
---

# Code Quality Analysis

**Required Module**: `RoslynMcpServer.Quality`
**Usage**: `/roslyn-quality <solution-path>`

Produce a quality review that tells the user which problems most deserve their attention, ranked, with locations and concrete fixes.

The Quality module covers complexity (`AnalyzeCodeComplexity`), code smells (`FindCodeSmells`), dead code (`FindUnusedCode`), naming (`AnalyzeNamingConventions`), copy-paste (`FindDuplicateCode`), hardcoded literals (`FindMagicNumbers`, `AnalyzeMagicNumbers`), and async/concurrency patterns (`AnalyzeConcurrencyPatterns`). Use the ones that fit the user's request.

The findings come from heuristic analyzers, so check the code behind anything you recommend changing.
