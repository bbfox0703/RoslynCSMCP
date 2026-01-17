# Skill: Explore C# Codebase
Explore the structure of a C# solution, including projects, namespaces, and key service/interface types.

**Required Module**: `RoslynMcpServer.Navigation`
**Usage**: `/roslyn-explore <solution-path>`

**Steps**:
1. Call `GetProjectStructure` to see all projects and namespaces.
2. Call `SearchSymbols` with pattern `*Service` to find service classes.
3. Call `SearchSymbols` with pattern `I*` and `symbolTypes: "interface"` to find interfaces.
4. Present summary with project organization and key types.