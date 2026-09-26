---
name: roslyn-navigate
description: Locate a C# symbol's definition, its references, and its implementations with the RoslynMcpServer Navigation tools. Use when the user asks where something is defined, where it is used, or what implements it.
---

# Navigate Symbols

**Required Module**: `RoslynMcpServer.Navigation`
**Usage**: `/roslyn-navigate <symbol-name> <solution-path>`

Tell the user where the symbol is defined, how widely and where it is used, and, for interfaces and abstract classes, what implements it.

`SearchSymbols` resolves a partial or ambiguous name to concrete symbols. `GetSymbolInfo` describes the definition. `FindReferences` lists usages at the detail level you need; `FindReferencesFiltered` narrows them (exclude tests, cross-project only, writes only, by project). `FindImplementations` lists implementations of an interface or abstract class.

Report the definition location, the usage count, the call sites that matter, and any implementations.
