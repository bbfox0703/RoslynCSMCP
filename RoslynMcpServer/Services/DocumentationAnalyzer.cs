using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Models;
using System.Collections.Concurrent;

namespace RoslynMcpServer.Services
{
    /// <summary>
    /// Service for analyzing documentation coverage in C# code
    /// </summary>
    public class DocumentationAnalyzer
    {
        private readonly ILogger<DocumentationAnalyzer> _logger;

        public DocumentationAnalyzer(ILogger<DocumentationAnalyzer> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Analyzes solution for documentation coverage
        /// </summary>
        public async Task<DocumentationCoverageResults> AnalyzeDocumentationCoverageAsync(
            string solutionPath,
            string scope = "public")
        {
            var results = new DocumentationCoverageResults();

            try
            {
                if (!File.Exists(solutionPath))
                {
                    results.Warnings.Add(new OperationWarning
                    {
                        Context = "Validation",
                        Message = "Invalid solution path"
                    });
                    return results;
                }

                // Normalize scope
                var scopeFilter = scope.ToLowerInvariant();

                // Load solution
                using var workspace = MSBuildWorkspace.Create();
                workspace.RegisterWorkspaceFailedHandler((e) =>
                {
                    if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
                    {
                        _logger.LogWarning("Workspace loading warning: {Message}", e.Diagnostic.Message);
                    }
                });

                var solution = await workspace.OpenSolutionAsync(solutionPath);
                results.AnalyzedProjects = solution.Projects.Count();

                // Collect all undocumented symbols
                var undocumentedSymbols = new ConcurrentBag<UndocumentedSymbol>();
                int totalSymbols = 0;
                int documentedSymbols = 0;

                var projectTasks = solution.Projects
                    .Where(p => p.SupportsCompilation)
                    .Select(async project =>
                    {
                        try
                        {
                            var compilation = await project.GetCompilationAsync();
                            if (compilation == null) return;

                            foreach (var syntaxTree in compilation.SyntaxTrees)
                            {
                                var semanticModel = compilation.GetSemanticModel(syntaxTree);
                                var root = await syntaxTree.GetRootAsync();
                                var filePath = syntaxTree.FilePath;
                                var fileName = Path.GetFileName(filePath);

                                results.AnalyzedFiles++;

                                // Get all type declarations
                                var types = root.DescendantNodes()
                                    .Where(n => n is Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax)
                                    .Cast<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>();

                                foreach (var type in types)
                                {
                                    var symbol = semanticModel.GetDeclaredSymbol(type) as INamedTypeSymbol;
                                    if (symbol == null) continue;

                                    // Check accessibility filter
                                    if (!MatchesScope(symbol, scopeFilter))
                                        continue;

                                    totalSymbols++;

                                    // Check if type has documentation
                                    var hasDoc = HasDocumentation(symbol);
                                    if (hasDoc)
                                    {
                                        documentedSymbols++;
                                    }
                                    else
                                    {
                                        undocumentedSymbols.Add(CreateUndocumentedSymbol(symbol, fileName, filePath, project.Name));
                                    }

                                    // Check members
                                    foreach (var member in symbol.GetMembers())
                                    {
                                        // Skip compiler-generated members
                                        if (member.IsImplicitlyDeclared)
                                            continue;

                                        // Check accessibility filter
                                        if (!MatchesScope(member, scopeFilter))
                                            continue;

                                        totalSymbols++;

                                        var memberHasDoc = HasDocumentation(member);
                                        if (memberHasDoc)
                                        {
                                            documentedSymbols++;
                                        }
                                        else
                                        {
                                            var location = member.Locations.FirstOrDefault();
                                            if (location?.SourceTree != null)
                                            {
                                                var memberFilePath = location.SourceTree.FilePath;
                                                var memberFileName = Path.GetFileName(memberFilePath);
                                                undocumentedSymbols.Add(CreateUndocumentedSymbol(member, memberFileName, memberFilePath, project.Name));
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to analyze project: {ProjectName}", project.Name);
                            results.FailedProjects++;
                        }
                    });

                await Task.WhenAll(projectTasks);

                results.TotalSymbols = totalSymbols;
                results.DocumentedSymbols = documentedSymbols;
                results.UndocumentedCount = totalSymbols - documentedSymbols;
                results.UndocumentedSymbols = undocumentedSymbols.ToList();

                CalculateStatistics(results);

                _logger.LogInformation(
                    "Documentation analysis complete: {Coverage}% coverage ({Documented}/{Total} symbols)",
                    results.CoveragePercentage.ToString("F1"),
                    results.DocumentedSymbols,
                    results.TotalSymbols);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing documentation coverage");
                results.Warnings.Add(new OperationWarning
                {
                    Context = "Analysis",
                    Message = $"Error: {ex.Message}"
                });
            }

            return results;
        }

        /// <summary>
        /// Checks if a symbol matches the scope filter
        /// </summary>
        private bool MatchesScope(ISymbol symbol, string scope)
        {
            if (scope == "all")
                return true;

            if (scope == "public")
                return symbol.DeclaredAccessibility == Accessibility.Public;

            return false;
        }

        /// <summary>
        /// Checks if a symbol has XML documentation
        /// </summary>
        private bool HasDocumentation(ISymbol symbol)
        {
            var docComment = symbol.GetDocumentationCommentXml();
            return !string.IsNullOrWhiteSpace(docComment);
        }

        /// <summary>
        /// Creates an UndocumentedSymbol from an ISymbol
        /// </summary>
        private UndocumentedSymbol CreateUndocumentedSymbol(ISymbol symbol, string fileName, string filePath, string projectName)
        {
            var lineSpan = symbol.Locations.FirstOrDefault()?.GetLineSpan();

            var undocumented = new UndocumentedSymbol
            {
                Name = symbol.Name,
                FullName = symbol.ToDisplayString(),
                Kind = symbol.Kind.ToString(),
                Accessibility = symbol.DeclaredAccessibility.ToString(),
                ContainingType = symbol.ContainingType?.Name ?? string.Empty,
                Namespace = symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty,
                FileName = fileName,
                FilePath = filePath,
                LineNumber = lineSpan?.StartLinePosition.Line + 1 ?? 0,
                ProjectName = projectName,
                Signature = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
            };

            // Generate suggested documentation
            undocumented.SuggestedDocumentation = GenerateDocumentationSuggestion(symbol);

            // For methods, capture parameters and return type
            if (symbol is IMethodSymbol method)
            {
                undocumented.Parameters = method.Parameters
                    .Select(p => $"{p.Type.Name} {p.Name}")
                    .ToList();
                undocumented.ReturnType = method.ReturnType.ToDisplayString();
            }

            return undocumented;
        }

        /// <summary>
        /// Generates a simple documentation suggestion for a symbol
        /// </summary>
        private string GenerateDocumentationSuggestion(ISymbol symbol)
        {
            var suggestion = new System.Text.StringBuilder();

            switch (symbol.Kind)
            {
                case SymbolKind.NamedType:
                    var type = (INamedTypeSymbol)symbol;
                    var typeKind = type.TypeKind == TypeKind.Interface ? "interface" :
                                  type.TypeKind == TypeKind.Class ? "class" :
                                  type.TypeKind == TypeKind.Struct ? "struct" : "type";
                    suggestion.AppendLine($"/// <summary>");
                    suggestion.AppendLine($"/// Represents {GetArticle(symbol.Name)} {symbol.Name} {typeKind}");
                    suggestion.AppendLine($"/// </summary>");
                    break;

                case SymbolKind.Method:
                    var method = (IMethodSymbol)symbol;
                    suggestion.AppendLine($"/// <summary>");
                    suggestion.AppendLine($"/// {GetMethodDescription(method)}");
                    suggestion.AppendLine($"/// </summary>");

                    foreach (var param in method.Parameters)
                    {
                        suggestion.AppendLine($"/// <param name=\"{param.Name}\">The {param.Name}</param>");
                    }

                    if (method.ReturnsVoid == false)
                    {
                        suggestion.AppendLine($"/// <returns>{method.ReturnType.ToDisplayString()}</returns>");
                    }
                    break;

                case SymbolKind.Property:
                    var prop = (IPropertySymbol)symbol;
                    var access = prop.IsReadOnly ? "Gets" : prop.IsWriteOnly ? "Sets" : "Gets or sets";
                    suggestion.AppendLine($"/// <summary>");
                    suggestion.AppendLine($"/// {access} the {CamelCaseToWords(symbol.Name).ToLower()}");
                    suggestion.AppendLine($"/// </summary>");
                    break;

                case SymbolKind.Field:
                    suggestion.AppendLine($"/// <summary>");
                    suggestion.AppendLine($"/// The {CamelCaseToWords(symbol.Name).ToLower()}");
                    suggestion.AppendLine($"/// </summary>");
                    break;

                case SymbolKind.Event:
                    suggestion.AppendLine($"/// <summary>");
                    suggestion.AppendLine($"/// Occurs when {CamelCaseToWords(symbol.Name).ToLower()}");
                    suggestion.AppendLine($"/// </summary>");
                    break;

                default:
                    suggestion.AppendLine($"/// <summary>");
                    suggestion.AppendLine($"/// TODO: Add documentation for {symbol.Name}");
                    suggestion.AppendLine($"/// </summary>");
                    break;
            }

            return suggestion.ToString().TrimEnd();
        }

        /// <summary>
        /// Gets method description based on naming convention
        /// </summary>
        private string GetMethodDescription(IMethodSymbol method)
        {
            var name = method.Name;

            // Check common method prefixes
            if (name.StartsWith("Get"))
                return $"Gets {CamelCaseToWords(name.Substring(3)).ToLower()}";
            if (name.StartsWith("Set"))
                return $"Sets {CamelCaseToWords(name.Substring(3)).ToLower()}";
            if (name.StartsWith("Create"))
                return $"Creates {GetArticle(name.Substring(6))} {CamelCaseToWords(name.Substring(6)).ToLower()}";
            if (name.StartsWith("Delete") || name.StartsWith("Remove"))
                return $"Deletes {CamelCaseToWords(name.Substring(6)).ToLower()}";
            if (name.StartsWith("Update"))
                return $"Updates {CamelCaseToWords(name.Substring(6)).ToLower()}";
            if (name.StartsWith("Is") || name.StartsWith("Has") || name.StartsWith("Can"))
                return $"Determines whether {CamelCaseToWords(name).ToLower()}";
            if (name.StartsWith("Calculate"))
                return $"Calculates {CamelCaseToWords(name.Substring(9)).ToLower()}";
            if (name.StartsWith("Validate"))
                return $"Validates {CamelCaseToWords(name.Substring(8)).ToLower()}";
            if (name.StartsWith("Process"))
                return $"Processes {CamelCaseToWords(name.Substring(7)).ToLower()}";

            // Default
            return $"{CamelCaseToWords(name)}";
        }

        /// <summary>
        /// Converts CamelCase to words
        /// </summary>
        private string CamelCaseToWords(string camelCase)
        {
            if (string.IsNullOrEmpty(camelCase))
                return camelCase;

            var result = System.Text.RegularExpressions.Regex.Replace(camelCase, "([A-Z])", " $1").Trim();
            return result;
        }

        /// <summary>
        /// Gets the appropriate article (a/an) for a word
        /// </summary>
        private string GetArticle(string word)
        {
            if (string.IsNullOrEmpty(word))
                return "a";

            var vowels = new[] { 'a', 'e', 'i', 'o', 'u', 'A', 'E', 'I', 'O', 'U' };
            return vowels.Contains(word[0]) ? "an" : "a";
        }

        /// <summary>
        /// Calculates statistics for the results
        /// </summary>
        private void CalculateStatistics(DocumentationCoverageResults results)
        {
            results.UndocumentedClasses = results.UndocumentedSymbols
                .Count(s => s.Kind == "NamedType");

            results.UndocumentedMethods = results.UndocumentedSymbols
                .Count(s => s.Kind == "Method");

            results.UndocumentedProperties = results.UndocumentedSymbols
                .Count(s => s.Kind == "Property");

            results.UndocumentedFields = results.UndocumentedSymbols
                .Count(s => s.Kind == "Field");

            results.UndocumentedEvents = results.UndocumentedSymbols
                .Count(s => s.Kind == "Event");
        }
    }
}
