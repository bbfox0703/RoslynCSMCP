---
name: roslyn-explore
description: Map the structure of a C# solution (projects, namespaces, and the types that anchor its architecture) with the RoslynMcpServer Navigation tools. Use when the user wants an overview of, or orientation in, a .sln.
---

# Explore C# Codebase

**Required Module**: `RoslynMcpServer.Navigation`
**Usage**: `/roslyn-explore <solution-path>`

Give the user a working mental model of the solution: how it is split into projects and namespaces, which types carry the architecture (services, interfaces, entry points, domain models), and how they relate.

`GetProjectStructure` returns the project, namespace, and type layout. `SearchSymbols` finds types and members by wildcard pattern and kind; `FindImplementations` shows what implements a key interface or abstract class; `GetSymbolInfo` gives details on a single symbol.

Summarize the project organization and the key types, with file locations for anything the user is likely to open next.
