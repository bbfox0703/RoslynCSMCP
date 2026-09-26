---
name: roslyn-outline
description: Show the structural outline (types and members) of a single C# source file with the RoslynMcpServer Navigation tools. Use when the user wants to see what a .cs file contains without reading all of it.
---

# File Outline

**Required Module**: `RoslynMcpServer.Navigation`
**Usage**: `/roslyn-outline <file-path>`

Show the user the file's structure (its classes, methods, and properties) using `GetFileOutline`.
