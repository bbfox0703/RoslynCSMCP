using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Models;

namespace RoslynMcpServer.Services
{
    public class AttributeSearchService
    {
        private readonly ILogger<AttributeSearchService> _logger;
        private readonly CodeAnalysisService _codeAnalysis;
        private readonly SecurityValidator _securityValidator;

        public AttributeSearchService(
            ILogger<AttributeSearchService> logger,
            CodeAnalysisService codeAnalysis,
            SecurityValidator securityValidator)
        {
            _logger = logger;
            _codeAnalysis = codeAnalysis;
            _securityValidator = securityValidator;
        }

        /// <summary>
        /// Find all usages of a specific attribute across the solution
        /// </summary>
        public async Task<List<AttributeUsageResult>> FindAttributeUsagesAsync(
            string attributeName,
            string solutionPath,
            string targetType = "all")
        {
            _logger.LogInformation("Finding attribute usages: {AttributeName}, TargetType: {TargetType}",
                attributeName, targetType);

            if (!_securityValidator.ValidateSolutionPath(solutionPath))
            {
                _logger.LogWarning("Invalid solution path: {SolutionPath}", solutionPath);
                throw new ArgumentException("Invalid solution path", nameof(solutionPath));
            }

            var solution = await _codeAnalysis.GetSolutionAsync(solutionPath);
            var results = new List<AttributeUsageResult>();

            // Normalize attribute name (remove "Attribute" suffix if present for comparison)
            var normalizedAttributeName = attributeName.EndsWith("Attribute")
                ? attributeName.Substring(0, attributeName.Length - "Attribute".Length)
                : attributeName;

            foreach (var project in solution.Projects.Where(p => p.SupportsCompilation))
            {
                _logger.LogDebug("Searching project: {ProjectName}", project.Name);

                var compilation = await project.GetCompilationAsync();
                if (compilation == null)
                    continue;

                foreach (var syntaxTree in compilation.SyntaxTrees)
                {
                    var semanticModel = compilation.GetSemanticModel(syntaxTree);
                    var root = await syntaxTree.GetRootAsync();

                    // Find all attribute syntax nodes
                    var attributeSyntaxes = root.DescendantNodes().OfType<AttributeSyntax>();

                    foreach (var attributeSyntax in attributeSyntaxes)
                    {
                        var attributeSymbol = semanticModel.GetSymbolInfo(attributeSyntax).Symbol;
                        if (attributeSymbol == null)
                            continue;

                        var attributeClass = attributeSymbol.ContainingType;
                        if (attributeClass == null)
                            continue;

                        // Check if this is the attribute we're looking for
                        var attrName = attributeClass.Name;
                        var attrNameWithoutSuffix = attrName.EndsWith("Attribute")
                            ? attrName.Substring(0, attrName.Length - "Attribute".Length)
                            : attrName;

                        if (!attrNameWithoutSuffix.Equals(normalizedAttributeName, StringComparison.OrdinalIgnoreCase))
                            continue;

                        // Get the target of this attribute
                        var target = GetAttributeTarget(attributeSyntax);
                        if (target == null)
                            continue;

                        var targetSymbol = semanticModel.GetDeclaredSymbol(target);
                        if (targetSymbol == null)
                            continue;

                        // Filter by target type if specified
                        if (!ShouldIncludeTarget(targetSymbol, targetType))
                            continue;

                        var usageResult = CreateAttributeUsageResult(
                            attributeSyntax,
                            attributeClass,
                            targetSymbol,
                            project,
                            syntaxTree,
                            semanticModel);

                        if (usageResult != null)
                        {
                            results.Add(usageResult);
                        }
                    }
                }
            }

            _logger.LogInformation("Found {Count} attribute usages", results.Count);
            return results.OrderBy(r => r.ProjectName).ThenBy(r => r.TargetName).ToList();
        }

        /// <summary>
        /// Get the syntax node that the attribute is applied to
        /// </summary>
        private SyntaxNode? GetAttributeTarget(AttributeSyntax attributeSyntax)
        {
            var attributeList = attributeSyntax.Parent as AttributeListSyntax;
            if (attributeList == null)
                return null;

            // The target is the parent of the attribute list
            var target = attributeList.Parent;
            return target;
        }

        /// <summary>
        /// Check if target symbol should be included based on target type filter
        /// </summary>
        private bool ShouldIncludeTarget(ISymbol targetSymbol, string targetType)
        {
            if (targetType.Equals("all", StringComparison.OrdinalIgnoreCase))
                return true;

            var symbolKind = targetSymbol.Kind.ToString().ToLowerInvariant();

            return targetType.ToLowerInvariant() switch
            {
                "class" => targetSymbol is INamedTypeSymbol { TypeKind: TypeKind.Class },
                "interface" => targetSymbol is INamedTypeSymbol { TypeKind: TypeKind.Interface },
                "struct" => targetSymbol is INamedTypeSymbol { TypeKind: TypeKind.Struct },
                "enum" => targetSymbol is INamedTypeSymbol { TypeKind: TypeKind.Enum },
                "method" => targetSymbol is IMethodSymbol,
                "property" => targetSymbol is IPropertySymbol,
                "field" => targetSymbol is IFieldSymbol,
                "parameter" => targetSymbol is IParameterSymbol,
                "event" => targetSymbol is IEventSymbol,
                _ => symbolKind.Contains(targetType.ToLowerInvariant())
            };
        }

        /// <summary>
        /// Create AttributeUsageResult from attribute and target
        /// </summary>
        private AttributeUsageResult? CreateAttributeUsageResult(
            AttributeSyntax attributeSyntax,
            INamedTypeSymbol attributeClass,
            ISymbol targetSymbol,
            Project project,
            SyntaxTree syntaxTree,
            SemanticModel semanticModel)
        {
            try
            {
                var location = attributeSyntax.GetLocation();
                var lineSpan = location.GetLineSpan();

                // Extract attribute arguments
                var attributeArguments = new List<string>();
                var namedArguments = new Dictionary<string, string>();

                if (attributeSyntax.ArgumentList != null)
                {
                    foreach (var argument in attributeSyntax.ArgumentList.Arguments)
                    {
                        if (argument.NameEquals != null)
                        {
                            // Named argument
                            var name = argument.NameEquals.Name.Identifier.Text;
                            var value = argument.Expression.ToString();
                            namedArguments[name] = value;
                        }
                        else
                        {
                            // Positional argument
                            attributeArguments.Add(argument.Expression.ToString());
                        }
                    }
                }

                // Get target type (for members)
                var declaringType = targetSymbol.ContainingType?.ToDisplayString() ?? "";

                // Build signature
                string signature = targetSymbol switch
                {
                    IMethodSymbol method => BuildMethodSignature(method),
                    IPropertySymbol property => $"{property.Type.ToDisplayString()} {property.Name}",
                    IFieldSymbol field => $"{field.Type.ToDisplayString()} {field.Name}",
                    IParameterSymbol parameter => $"{parameter.Type.ToDisplayString()} {parameter.Name}",
                    IEventSymbol eventSymbol => $"event {eventSymbol.Type.ToDisplayString()} {eventSymbol.Name}",
                    INamedTypeSymbol type => type.ToDisplayString(),
                    _ => targetSymbol.ToDisplayString()
                };

                return new AttributeUsageResult
                {
                    AttributeName = attributeClass.Name,
                    TargetName = targetSymbol.Name,
                    TargetFullName = targetSymbol.ToDisplayString(),
                    TargetType = GetTargetTypeString(targetSymbol),
                    FilePath = syntaxTree.FilePath,
                    FileName = Path.GetFileName(syntaxTree.FilePath),
                    ProjectName = project.Name,
                    LineNumber = lineSpan.StartLinePosition.Line + 1,
                    Namespace = targetSymbol.ContainingNamespace?.ToDisplayString() ?? "",
                    DeclaringType = declaringType,
                    AttributeArguments = attributeArguments,
                    NamedArguments = namedArguments,
                    Signature = signature
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating attribute usage result");
                return null;
            }
        }

        /// <summary>
        /// Get target type as string
        /// </summary>
        private string GetTargetTypeString(ISymbol symbol)
        {
            return symbol switch
            {
                INamedTypeSymbol namedType => namedType.TypeKind switch
                {
                    TypeKind.Class => "Class",
                    TypeKind.Interface => "Interface",
                    TypeKind.Struct => "Struct",
                    TypeKind.Enum => "Enum",
                    TypeKind.Delegate => "Delegate",
                    _ => "Type"
                },
                IMethodSymbol => "Method",
                IPropertySymbol => "Property",
                IFieldSymbol => "Field",
                IParameterSymbol => "Parameter",
                IEventSymbol => "Event",
                _ => symbol.Kind.ToString()
            };
        }

        /// <summary>
        /// Build method signature string
        /// </summary>
        private string BuildMethodSignature(IMethodSymbol method)
        {
            var parameters = string.Join(", ",
                method.Parameters.Select(p => $"{p.Type.ToDisplayString()} {p.Name}"));

            if (method.MethodKind == MethodKind.Constructor)
            {
                return $"{method.ContainingType.Name}({parameters})";
            }

            return $"{method.ReturnType.ToDisplayString()} {method.Name}({parameters})";
        }
    }
}
