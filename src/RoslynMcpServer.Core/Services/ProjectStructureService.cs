using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using System.Text;

namespace RoslynMcpServer.Core.Services
{
    public class ProjectStructureNode
    {
        public string Name { get; set; } = string.Empty;
        public List<NamespaceNode> Namespaces { get; set; } = new();
    }

    public class NamespaceNode
    {
        public string Name { get; set; } = string.Empty;
        public List<TypeNode> Types { get; set; } = new();
    }

    public class TypeNode
    {
        public string Name { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string Accessibility { get; set; } = string.Empty;
        public List<string> Members { get; set; } = new();
    }

    public class ProjectStructureService
    {
        private readonly CodeAnalysisService _codeAnalysisService;
        private readonly ILogger<ProjectStructureService> _logger;

        public ProjectStructureService(
            CodeAnalysisService codeAnalysisService,
            ILogger<ProjectStructureService> logger)
        {
            _codeAnalysisService = codeAnalysisService;
            _logger = logger;
        }

        public async Task<string> GetStructureAsync(
            string solutionPath,
            bool includeMembers = false,
            string? namespaceFilter = null,
            bool publicOnly = true)
        {
            try
            {
                var solution = await _codeAnalysisService.GetSolutionAsync(solutionPath);
                var structure = await BuildStructure(solution, includeMembers, namespaceFilter, publicOnly);
                return FormatStructure(structure, solution.FilePath ?? solutionPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting project structure");
                throw;
            }
        }

        private async Task<List<ProjectStructureNode>> BuildStructure(
            Solution solution,
            bool includeMembers,
            string? namespaceFilter,
            bool publicOnly)
        {
            var projects = new List<ProjectStructureNode>();

            foreach (var project in solution.Projects)
            {
                if (!project.SupportsCompilation)
                    continue;

                var projectNode = new ProjectStructureNode { Name = project.Name };

                var compilation = await project.GetCompilationAsync();
                if (compilation == null)
                    continue;

                // Get all types from the assembly
                var types = GetAllTypes(compilation.Assembly.GlobalNamespace)
                    .Where(t => !publicOnly || t.DeclaredAccessibility == Accessibility.Public)
                    .Where(t => string.IsNullOrEmpty(namespaceFilter) ||
                               t.ContainingNamespace.ToDisplayString().Contains(namespaceFilter, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // Group by namespace
                var typesByNamespace = types
                    .GroupBy(t => t.ContainingNamespace.ToDisplayString())
                    .OrderBy(g => g.Key);

                foreach (var nsGroup in typesByNamespace)
                {
                    var nsNode = new NamespaceNode { Name = nsGroup.Key };

                    foreach (var type in nsGroup.OrderBy(t => t.Name))
                    {
                        var typeNode = new TypeNode
                        {
                            Name = type.Name,
                            Kind = GetTypeKindString(type),
                            Accessibility = GetAccessibilityString(type.DeclaredAccessibility)
                        };

                        if (includeMembers)
                        {
                            typeNode.Members = type.GetMembers()
                                .Where(m => !publicOnly || m.DeclaredAccessibility == Accessibility.Public)
                                .Where(m => m.Kind != SymbolKind.NamedType) // Exclude nested types
                                .Where(m => !m.IsImplicitlyDeclared)
                                .Select(m => FormatMemberSignature(m))
                                .ToList();
                        }

                        nsNode.Types.Add(typeNode);
                    }

                    if (nsNode.Types.Any())
                    {
                        projectNode.Namespaces.Add(nsNode);
                    }
                }

                if (projectNode.Namespaces.Any())
                {
                    projects.Add(projectNode);
                }
            }

            return projects;
        }

        private IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceSymbol namespaceSymbol)
        {
            foreach (var type in namespaceSymbol.GetTypeMembers())
            {
                yield return type;

                // Get nested types
                foreach (var nestedType in GetNestedTypes(type))
                {
                    yield return nestedType;
                }
            }

            foreach (var childNamespace in namespaceSymbol.GetNamespaceMembers())
            {
                foreach (var type in GetAllTypes(childNamespace))
                {
                    yield return type;
                }
            }
        }

        private IEnumerable<INamedTypeSymbol> GetNestedTypes(INamedTypeSymbol type)
        {
            foreach (var nestedType in type.GetTypeMembers())
            {
                yield return nestedType;

                foreach (var deeplyNested in GetNestedTypes(nestedType))
                {
                    yield return deeplyNested;
                }
            }
        }

        private string FormatStructure(List<ProjectStructureNode> structure, string solutionPath)
        {
            var builder = new StringBuilder();
            var solutionName = Path.GetFileName(solutionPath);
            var totalProjects = structure.Count;

            builder.AppendLine($"Solution: {solutionName} ({totalProjects} project{(totalProjects > 1 ? "s" : "")})\n");

            foreach (var project in structure.OrderBy(p => p.Name))
            {
                builder.AppendLine($"📁 Project: {project.Name}");

                foreach (var ns in project.Namespaces.OrderBy(n => n.Name))
                {
                    builder.AppendLine($"  📦 Namespace: {ns.Name}");

                    foreach (var type in ns.Types.OrderBy(t => t.Name))
                    {
                        var icon = GetTypeIcon(type.Kind);
                        builder.AppendLine($"    {icon} {type.Name} ({type.Kind}, {type.Accessibility})");

                        if (type.Members.Any())
                        {
                            foreach (var member in type.Members)
                            {
                                builder.AppendLine($"      → {member}");
                            }
                        }
                    }
                    builder.AppendLine();
                }
            }

            // Add summary statistics
            var totalTypes = structure.Sum(p => p.Namespaces.Sum(ns => ns.Types.Count));
            var totalNamespaces = structure.Sum(p => p.Namespaces.Count);

            builder.AppendLine($"Summary:");
            builder.AppendLine($"  Total Projects: {totalProjects}");
            builder.AppendLine($"  Total Namespaces: {totalNamespaces}");
            builder.AppendLine($"  Total Types: {totalTypes}");

            return builder.ToString();
        }

        private string GetTypeKindString(INamedTypeSymbol type)
        {
            return type.TypeKind switch
            {
                TypeKind.Class => "Class",
                TypeKind.Interface => "Interface",
                TypeKind.Struct => "Struct",
                TypeKind.Enum => "Enum",
                TypeKind.Delegate => "Delegate",
                _ => "Type"
            };
        }

        private string GetAccessibilityString(Accessibility accessibility)
        {
            return accessibility switch
            {
                Accessibility.Public => "Public",
                Accessibility.Private => "Private",
                Accessibility.Protected => "Protected",
                Accessibility.Internal => "Internal",
                Accessibility.ProtectedOrInternal => "Protected Internal",
                Accessibility.ProtectedAndInternal => "Private Protected",
                _ => "Unknown"
            };
        }

        private string GetTypeIcon(string kind)
        {
            return kind switch
            {
                "Class" => "🔹",
                "Interface" => "🔸",
                "Struct" => "🔷",
                "Enum" => "🔶",
                "Delegate" => "🔺",
                _ => "▪"
            };
        }

        private string FormatMemberSignature(ISymbol member)
        {
            return member switch
            {
                IMethodSymbol method => FormatMethodSignature(method),
                IPropertySymbol property => FormatPropertySignature(property),
                IFieldSymbol field => FormatFieldSignature(field),
                IEventSymbol evt => FormatEventSignature(evt),
                _ => member.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
            };
        }

        private string FormatMethodSignature(IMethodSymbol method)
        {
            // Skip special methods like property getters/setters
            if (method.MethodKind != MethodKind.Ordinary && method.MethodKind != MethodKind.Constructor)
                return string.Empty;

            var builder = new StringBuilder();

            if (method.MethodKind == MethodKind.Constructor)
            {
                builder.Append(method.ContainingType.Name);
            }
            else
            {
                builder.Append(method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
                builder.Append(" ");
                builder.Append(method.Name);
            }

            builder.Append("(");
            builder.Append(string.Join(", ", method.Parameters.Select(p =>
                $"{p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {p.Name}")));
            builder.Append(")");

            return builder.ToString();
        }

        private string FormatPropertySignature(IPropertySymbol property)
        {
            var builder = new StringBuilder();
            builder.Append(property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
            builder.Append(" ");
            builder.Append(property.Name);
            builder.Append(" { ");

            if (property.GetMethod != null)
                builder.Append("get; ");
            if (property.SetMethod != null)
                builder.Append("set; ");

            builder.Append("}");

            return builder.ToString();
        }

        private string FormatFieldSignature(IFieldSymbol field)
        {
            var builder = new StringBuilder();
            builder.Append(field.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
            builder.Append(" ");
            builder.Append(field.Name);

            return builder.ToString();
        }

        private string FormatEventSignature(IEventSymbol evt)
        {
            var builder = new StringBuilder();
            builder.Append("event ");
            builder.Append(evt.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
            builder.Append(" ");
            builder.Append(evt.Name);

            return builder.ToString();
        }
    }
}
