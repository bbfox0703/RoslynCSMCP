# Skill: Navigate Symbols
Locate specific symbols, their definitions, and find all references or implementations.

**Required Module**: `RoslynMcpServer.Navigation`
**Usage**: `/roslyn-navigate <symbol-name> <solution-path>`

**Steps**:
1. Call `SearchSymbols` to locate the symbol.
2. Call `GetSymbolInfo` with `detailLevel: "basic"` for details.
3. Call `FindReferences` with `detailLevel: "summary"` for usage count.
4. Call `FindImplementations` if the symbol is an interface.
5. Present location, usage count, and implementations.