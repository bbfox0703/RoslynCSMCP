---
name: roslyn-refactor
description: Assess the impact and risk of refactoring a C# symbol before any code changes, with the RoslynMcpServer Refactoring tools. Use when the user is considering renaming, extracting an interface from, or otherwise changing a type or member.
---

# Refactoring Assessment

**Required Module**: `RoslynMcpServer.Refactoring`
**Usage**: `/roslyn-refactor <symbol-name> <solution-path>`

Tell the user what changing the symbol would affect, how risky it is, and the refactoring path you recommend. This skill assesses; it does not change code.

- `GetChangeImpact` lists dependent code and assesses risk.
- `RenameSymbolSafely` with `previewOnly: true` shows exactly which files and locations a rename would touch. Keep `previewOnly` true here: `false` writes the rename to disk.
- `ExtractInterface` returns generated interface code for a class that would benefit from an interface seam; it writes nothing.
- `AnalyzeLayerViolations` needs the solution's layer definition. Use it only when the user has one or gives you one, because violations measured against an invented architecture mean nothing.
