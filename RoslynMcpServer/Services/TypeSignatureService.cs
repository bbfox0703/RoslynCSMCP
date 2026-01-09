using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Xml.Linq;

namespace RoslynMcpServer.Services
{
    public class TypeSignatureService
    {
        private readonly CodeAnalysisService _codeAnalysisService;
        private readonly ILogger<TypeSignatureService> _logger;

        public TypeSignatureService(
            CodeAnalysisService codeAnalysisService,
            ILogger<TypeSignatureService> logger)
        {
            _codeAnalysisService = codeAnalysisService;
            _logger = logger;
        }

        public async Task<string> GetTypeSignatureAsync(
            string typeName,
            string solutionPath,
            bool includePrivate = false,
            bool includeDocumentation = true)
        {
            try
            {
                var solution = await _codeAnalysisService.GetSolutionAsync(solutionPath);
                var type = await FindTypeSymbol(typeName, solution);

                if (type == null)
                {
                    return $"Type '{typeName}' not found in solution.";
                }

                return FormatTypeSignature(type, includePrivate, includeDocumentation);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting type signature for: {TypeName}", typeName);
                throw;
            }
        }

        private async Task<INamedTypeSymbol?> FindTypeSymbol(string typeName, Solution solution)
        {
            foreach (var project in solution.Projects)
            {
                if (!project.SupportsCompilation)
                    continue;

                var compilation = await project.GetCompilationAsync();
                if (compilation == null)
                    continue;

                // Try exact match first
                var type = compilation.GetTypeByMetadataName(typeName);
                if (type != null)
                    return type;

                // Try finding in all types
                var allTypes = GetAllTypes(compilation.Assembly.GlobalNamespace);
                type = allTypes.FirstOrDefault(t =>
                    t.Name == typeName ||
                    t.ToDisplayString() == typeName ||
                    t.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) == typeName);

                if (type != null)
                    return type;
            }

            return null;
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

        private string FormatTypeSignature(
            INamedTypeSymbol type,
            bool includePrivate,
            bool includeDocumentation)
        {
            var builder = new StringBuilder();

            // Add namespace
            if (!type.ContainingNamespace.IsGlobalNamespace)
            {
                builder.AppendLine($"namespace {type.ContainingNamespace.ToDisplayString()}");
                builder.AppendLine("{");
            }

            // Add XML documentation if available
            if (includeDocumentation)
            {
                var xmlDoc = type.GetDocumentationCommentXml();
                if (!string.IsNullOrEmpty(xmlDoc))
                {
                    var formattedDoc = FormatXmlDocumentation(xmlDoc, "    ");
                    if (!string.IsNullOrEmpty(formattedDoc))
                    {
                        builder.AppendLine(formattedDoc);
                    }
                }
            }

            // Add type declaration
            var indent = type.ContainingNamespace.IsGlobalNamespace ? "" : "    ";
            builder.AppendLine($"{indent}{FormatTypeDeclaration(type)}");
            builder.AppendLine($"{indent}{{");

            // Group and format members
            FormatMembers(builder, type, includePrivate, includeDocumentation, indent + "    ");

            builder.AppendLine($"{indent}}}");

            if (!type.ContainingNamespace.IsGlobalNamespace)
            {
                builder.AppendLine("}");
            }

            return builder.ToString();
        }

        private string FormatTypeDeclaration(INamedTypeSymbol type)
        {
            var builder = new StringBuilder();

            // Accessibility
            builder.Append(FormatAccessibility(type.DeclaredAccessibility));
            builder.Append(" ");

            // Modifiers
            if (type.IsStatic)
                builder.Append("static ");
            if (type.IsAbstract && type.TypeKind == TypeKind.Class)
                builder.Append("abstract ");
            if (type.IsSealed && type.TypeKind == TypeKind.Class)
                builder.Append("sealed ");

            // Type kind
            builder.Append(type.TypeKind switch
            {
                TypeKind.Class => "class",
                TypeKind.Interface => "interface",
                TypeKind.Struct => "struct",
                TypeKind.Enum => "enum",
                TypeKind.Delegate => "delegate",
                _ => "type"
            });
            builder.Append(" ");

            // Name
            builder.Append(type.Name);

            // Generic parameters
            if (type.TypeParameters.Length > 0)
            {
                builder.Append("<");
                builder.Append(string.Join(", ", type.TypeParameters.Select(tp => tp.Name)));
                builder.Append(">");
            }

            // Base types and interfaces
            var baseTypes = new List<string>();
            if (type.BaseType != null && type.BaseType.SpecialType != SpecialType.System_Object)
            {
                baseTypes.Add(type.BaseType.ToDisplayString());
            }
            baseTypes.AddRange(type.Interfaces.Select(i => i.ToDisplayString()));

            if (baseTypes.Any())
            {
                builder.Append(" : ");
                builder.Append(string.Join(", ", baseTypes));
            }

            return builder.ToString();
        }

        private void FormatMembers(
            StringBuilder builder,
            INamedTypeSymbol type,
            bool includePrivate,
            bool includeDocumentation,
            string indent)
        {
            var members = type.GetMembers()
                .Where(m => includePrivate || IsPublicOrProtected(m))
                .Where(m => m.Kind != SymbolKind.NamedType) // Exclude nested types for now
                .ToList();

            // Fields
            var fields = members.OfType<IFieldSymbol>()
                .Where(f => !f.IsImplicitlyDeclared)
                .OrderBy(f => f.Name)
                .ToList();
            if (fields.Any())
            {
                builder.AppendLine($"{indent}// Fields");
                foreach (var field in fields)
                {
                    FormatMember(builder, field, includeDocumentation, indent);
                }
                builder.AppendLine();
            }

            // Constructors
            var constructors = members.OfType<IMethodSymbol>()
                .Where(m => m.MethodKind == MethodKind.Constructor)
                .Where(m => !m.IsImplicitlyDeclared)
                .OrderBy(m => m.Parameters.Length)
                .ToList();
            if (constructors.Any())
            {
                builder.AppendLine($"{indent}// Constructors");
                foreach (var ctor in constructors)
                {
                    FormatMember(builder, ctor, includeDocumentation, indent);
                }
                builder.AppendLine();
            }

            // Properties
            var properties = members.OfType<IPropertySymbol>()
                .OrderBy(p => p.Name)
                .ToList();
            if (properties.Any())
            {
                builder.AppendLine($"{indent}// Properties");
                foreach (var property in properties)
                {
                    FormatMember(builder, property, includeDocumentation, indent);
                }
                builder.AppendLine();
            }

            // Events
            var events = members.OfType<IEventSymbol>()
                .OrderBy(e => e.Name)
                .ToList();
            if (events.Any())
            {
                builder.AppendLine($"{indent}// Events");
                foreach (var evt in events)
                {
                    FormatMember(builder, evt, includeDocumentation, indent);
                }
                builder.AppendLine();
            }

            // Methods
            var methods = members.OfType<IMethodSymbol>()
                .Where(m => m.MethodKind == MethodKind.Ordinary)
                .OrderBy(m => m.Name)
                .ToList();
            if (methods.Any())
            {
                builder.AppendLine($"{indent}// Methods");
                foreach (var method in methods)
                {
                    FormatMember(builder, method, includeDocumentation, indent);
                }
                builder.AppendLine();
            }

            // Show private member count if not included
            if (!includePrivate)
            {
                var privateCount = type.GetMembers().Count(m =>
                    m.DeclaredAccessibility == Accessibility.Private &&
                    !m.IsImplicitlyDeclared &&
                    m.Kind != SymbolKind.NamedType);

                if (privateCount > 0)
                {
                    builder.AppendLine($"{indent}// [{privateCount} private member{(privateCount > 1 ? "s" : "")} hidden - use includePrivate: true to show]");
                }
            }
        }

        private void FormatMember(
            StringBuilder builder,
            ISymbol member,
            bool includeDocumentation,
            string indent)
        {
            // Add documentation
            if (includeDocumentation)
            {
                var xmlDoc = member.GetDocumentationCommentXml();
                if (!string.IsNullOrEmpty(xmlDoc))
                {
                    var formattedDoc = FormatXmlDocumentation(xmlDoc, indent);
                    if (!string.IsNullOrEmpty(formattedDoc))
                    {
                        builder.AppendLine(formattedDoc);
                    }
                }
            }

            // Format member based on type
            builder.Append(indent);
            builder.Append(member switch
            {
                IFieldSymbol field => FormatField(field),
                IPropertySymbol property => FormatProperty(property),
                IMethodSymbol method => FormatMethod(method),
                IEventSymbol evt => FormatEvent(evt),
                _ => member.ToDisplayString()
            });
            builder.AppendLine(";");
        }

        private string FormatField(IFieldSymbol field)
        {
            var builder = new StringBuilder();
            builder.Append(FormatAccessibility(field.DeclaredAccessibility));
            builder.Append(" ");

            if (field.IsStatic)
                builder.Append("static ");
            if (field.IsReadOnly)
                builder.Append("readonly ");
            if (field.IsConst)
                builder.Append("const ");

            builder.Append(field.Type.ToDisplayString());
            builder.Append(" ");
            builder.Append(field.Name);

            return builder.ToString();
        }

        private string FormatProperty(IPropertySymbol property)
        {
            var builder = new StringBuilder();
            builder.Append(FormatAccessibility(property.DeclaredAccessibility));
            builder.Append(" ");

            if (property.IsStatic)
                builder.Append("static ");
            if (property.IsVirtual)
                builder.Append("virtual ");
            if (property.IsOverride)
                builder.Append("override ");
            if (property.IsAbstract)
                builder.Append("abstract ");

            builder.Append(property.Type.ToDisplayString());
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

        private string FormatMethod(IMethodSymbol method)
        {
            var builder = new StringBuilder();
            builder.Append(FormatAccessibility(method.DeclaredAccessibility));
            builder.Append(" ");

            if (method.IsStatic)
                builder.Append("static ");
            if (method.IsVirtual)
                builder.Append("virtual ");
            if (method.IsOverride)
                builder.Append("override ");
            if (method.IsAbstract)
                builder.Append("abstract ");
            if (method.IsAsync)
                builder.Append("async ");

            builder.Append(method.ReturnType.ToDisplayString());
            builder.Append(" ");
            builder.Append(method.Name);

            // Generic parameters
            if (method.TypeParameters.Length > 0)
            {
                builder.Append("<");
                builder.Append(string.Join(", ", method.TypeParameters.Select(tp => tp.Name)));
                builder.Append(">");
            }

            builder.Append("(");
            builder.Append(string.Join(", ", method.Parameters.Select(p =>
                $"{p.Type.ToDisplayString()} {p.Name}")));
            builder.Append(")");

            return builder.ToString();
        }

        private string FormatEvent(IEventSymbol evt)
        {
            var builder = new StringBuilder();
            builder.Append(FormatAccessibility(evt.DeclaredAccessibility));
            builder.Append(" ");

            if (evt.IsStatic)
                builder.Append("static ");
            if (evt.IsVirtual)
                builder.Append("virtual ");
            if (evt.IsOverride)
                builder.Append("override ");

            builder.Append("event ");
            builder.Append(evt.Type.ToDisplayString());
            builder.Append(" ");
            builder.Append(evt.Name);

            return builder.ToString();
        }

        private string FormatAccessibility(Accessibility accessibility)
        {
            return accessibility switch
            {
                Accessibility.Public => "public",
                Accessibility.Private => "private",
                Accessibility.Protected => "protected",
                Accessibility.Internal => "internal",
                Accessibility.ProtectedOrInternal => "protected internal",
                Accessibility.ProtectedAndInternal => "private protected",
                _ => ""
            };
        }

        private bool IsPublicOrProtected(ISymbol symbol)
        {
            return symbol.DeclaredAccessibility == Accessibility.Public ||
                   symbol.DeclaredAccessibility == Accessibility.Protected ||
                   symbol.DeclaredAccessibility == Accessibility.ProtectedOrInternal;
        }

        private string FormatXmlDocumentation(string xmlDoc, string indent)
        {
            if (string.IsNullOrWhiteSpace(xmlDoc))
                return string.Empty;

            try
            {
                var doc = XDocument.Parse(xmlDoc);
                var summary = doc.Descendants("summary").FirstOrDefault();

                if (summary == null)
                    return string.Empty;

                var summaryText = summary.Value.Trim();
                if (string.IsNullOrWhiteSpace(summaryText))
                    return string.Empty;

                var lines = summaryText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Trim())
                    .Where(line => !string.IsNullOrWhiteSpace(line));

                if (!lines.Any())
                    return string.Empty;

                var result = new StringBuilder();
                result.AppendLine($"{indent}/// <summary>");
                foreach (var line in lines)
                {
                    result.AppendLine($"{indent}/// {line}");
                }
                result.Append($"{indent}/// </summary>");

                return result.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not extract documentation comments");
                return string.Empty;
            }
        }
    }
}
