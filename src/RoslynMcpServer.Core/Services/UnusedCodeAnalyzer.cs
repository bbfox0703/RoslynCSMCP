using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Core.Models;
using System.Collections.Concurrent;

namespace RoslynMcpServer.Core.Services
{
    /// <summary>
    /// Service for analyzing unused code in C# solutions
    /// </summary>
    public class UnusedCodeAnalyzer
    {
        private readonly ILogger<UnusedCodeAnalyzer> _logger;
        private readonly SecurityValidator _validator;

        public UnusedCodeAnalyzer(ILogger<UnusedCodeAnalyzer> logger, SecurityValidator validator)
        {
            _logger = logger;
            _validator = validator;
        }

        public async Task<UnusedCodeResults> AnalyzeUnusedCodeAsync(
            string solutionPath,
            string scope = "all",
            bool includeTests = false)
        {
            var results = new UnusedCodeResults();
            var unusedItems = new ConcurrentBag<UnusedItem>();

            try
            {
                // Validate solution path
                if (!_validator.ValidateSolutionPath(solutionPath))
                {
                    results.Warnings.Add(new OperationWarning
                    {
                        Context = "Validation",
                        Message = "Invalid solution path"
                    });
                    return results;
                }

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

                // Process projects in parallel
                var projectTasks = solution.Projects
                    .Where(p => includeTests || !IsTestProject(p))
                    .Select(async project =>
                    {
                        try
                        {
                            await AnalyzeProjectAsync(project, scope, unusedItems, solution);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to analyze project: {ProjectName}", project.Name);
                            results.FailedProjects++;
                            results.Warnings.Add(new OperationWarning
                            {
                                Context = project.Name,
                                Message = $"Failed to analyze: {ex.Message}"
                            });
                        }
                    });

                await Task.WhenAll(projectTasks);

                // Convert to list and sort
                results.UnusedItems = unusedItems
                    .OrderBy(i => i.Accessibility)
                    .ThenBy(i => i.Kind)
                    .ThenBy(i => i.DeclaringType)
                    .ThenBy(i => i.Name)
                    .ToList();

                // Calculate statistics
                CalculateStatistics(results);

                _logger.LogInformation("Unused code analysis complete: {Count} unused items found", results.UnusedItems.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing unused code");
                results.Warnings.Add(new OperationWarning
                {
                    Context = "Analysis",
                    Message = $"Unexpected error: {ex.Message}"
                });
            }

            return results;
        }

        private async Task AnalyzeProjectAsync(
            Project project,
            string scope,
            ConcurrentBag<UnusedItem> unusedItems,
            Solution solution)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) return;

            var isTestProject = IsTestProject(project);

            // Get all symbols to analyze
            var symbols = new List<ISymbol>();

            foreach (var syntaxTree in compilation.SyntaxTrees)
            {
                var semanticModel = compilation.GetSemanticModel(syntaxTree);
                var root = await syntaxTree.GetRootAsync();

                // Get all declared symbols in this tree
                var declaredSymbols = root.DescendantNodes()
                    .Select(node => semanticModel.GetDeclaredSymbol(node))
                    .Where(symbol => symbol != null)
                    .Cast<ISymbol>();

                symbols.AddRange(declaredSymbols);
            }

            // Filter symbols based on scope
            var filteredSymbols = symbols
                .Where(s => ShouldAnalyze(s, scope))
                .Distinct(SymbolEqualityComparer.Default)
                .ToList();

            // Analyze each symbol in parallel (with batching to avoid overwhelming the system)
            var batchSize = 50;
            for (int i = 0; i < filteredSymbols.Count; i += batchSize)
            {
                var batch = filteredSymbols.Skip(i).Take(batchSize);
                var batchTasks = batch.Select(async symbol =>
                {
                    try
                    {
                        if (await IsUnusedAsync(symbol, solution))
                        {
                            var item = CreateUnusedItem(symbol, project, isTestProject);
                            if (item != null)
                            {
                                unusedItems.Add(item);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Error checking symbol: {SymbolName}", symbol.Name);
                    }
                });

                await Task.WhenAll(batchTasks);
            }
        }

        private bool ShouldAnalyze(ISymbol symbol, string scope)
        {
            // Skip null symbols
            if (symbol == null) return false;

            // Skip compiler-generated symbols
            if (symbol.IsImplicitlyDeclared) return false;

            // Skip extern symbols
            if (symbol.IsExtern) return false;

            // Only analyze these kinds
            var validKinds = new[]
            {
                SymbolKind.NamedType,
                SymbolKind.Method,
                SymbolKind.Property,
                SymbolKind.Field,
                SymbolKind.Event
            };

            if (!validKinds.Contains(symbol.Kind)) return false;

            // Filter by accessibility scope
            var accessibility = symbol.DeclaredAccessibility;

            return scope.ToLowerInvariant() switch
            {
                "private" => accessibility == Accessibility.Private,
                "internal" => accessibility == Accessibility.Internal,
                "public" => accessibility == Accessibility.Public,
                "all" => accessibility == Accessibility.Private ||
                         accessibility == Accessibility.Internal ||
                         accessibility == Accessibility.Public,
                _ => true
            };
        }

        private async Task<bool> IsUnusedAsync(ISymbol symbol, Solution solution)
        {
            // Skip special cases that shouldn't be flagged as unused
            if (ShouldSkipSymbol(symbol)) return false;

            // Find all references
            var references = await SymbolFinder.FindReferencesAsync(symbol, solution);
            var referenceLocations = references
                .SelectMany(r => r.Locations)
                .Where(loc => !loc.IsImplicit) // Skip implicit references
                .ToList();

            // Check if there are any references (excluding the definition itself)
            var hasExternalReferences = referenceLocations.Any(loc =>
            {
                // Get the syntax node at this location
                var syntaxTree = loc.Location.SourceTree;
                if (syntaxTree == null) return false;

                // Check if this is the definition location
                var symbolLocations = symbol.Locations;
                return !symbolLocations.Any(symLoc =>
                    symLoc.SourceTree == syntaxTree &&
                    symLoc.SourceSpan == loc.Location.SourceSpan);
            });

            return !hasExternalReferences;
        }

        private bool ShouldSkipSymbol(ISymbol symbol)
        {
            // Skip entry points (Main methods)
            if (symbol is IMethodSymbol method)
            {
                if (method.Name == "Main" && method.IsStatic)
                    return true;

                // Skip test methods
                if (HasTestAttribute(method))
                    return true;

                // Skip interface implementations
                if (method.ExplicitInterfaceImplementations.Any())
                    return true;

                // Skip override methods
                if (method.IsOverride)
                    return true;

                // Skip constructors (they might be used by DI or serialization)
                if (method.MethodKind == MethodKind.Constructor)
                    return true;
            }

            // Skip types with special attributes
            if (symbol is INamedTypeSymbol type)
            {
                // Skip types with serialization attributes
                if (HasSerializationAttribute(type))
                    return true;

                // Skip startup/program classes
                if (type.Name == "Program" || type.Name == "Startup")
                    return true;
            }

            // Skip properties/fields with serialization attributes
            var attributes = symbol.GetAttributes();
            foreach (var attr in attributes)
            {
                var attrName = attr.AttributeClass?.Name ?? "";
                if (attrName.Contains("JsonProperty") ||
                    attrName.Contains("DataMember") ||
                    attrName.Contains("XmlElement") ||
                    attrName.Contains("Required") ||
                    attrName.Contains("Key"))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasTestAttribute(IMethodSymbol method)
        {
            var attributes = method.GetAttributes();
            var testAttributeNames = new[]
            {
                "Test", "TestMethod", "Fact", "Theory",
                "TestCase", "TestCaseSource", "SetUp", "TearDown",
                "OneTimeSetUp", "OneTimeTearDown", "Before", "After"
            };

            return attributes.Any(attr =>
                testAttributeNames.Any(name =>
                    attr.AttributeClass?.Name.Contains(name) == true));
        }

        private bool HasSerializationAttribute(INamedTypeSymbol type)
        {
            var attributes = type.GetAttributes();
            var serializationAttributes = new[]
            {
                "DataContract", "Serializable", "JsonObject", "XmlRoot"
            };

            return attributes.Any(attr =>
                serializationAttributes.Any(name =>
                    attr.AttributeClass?.Name.Contains(name) == true));
        }

        private UnusedItem? CreateUnusedItem(ISymbol symbol, Project project, bool isTestMember)
        {
            var location = symbol.Locations.FirstOrDefault();
            if (location == null || !location.IsInSource) return null;

            var lineSpan = location.GetLineSpan();

            return new UnusedItem
            {
                Name = symbol.Name,
                FullName = symbol.ToDisplayString(),
                Kind = symbol.Kind.ToString(),
                Accessibility = symbol.DeclaredAccessibility.ToString(),
                DeclaringType = symbol.ContainingType?.Name ?? "",
                Namespace = symbol.ContainingNamespace?.ToDisplayString() ?? "",
                ProjectName = project.Name,
                FileName = Path.GetFileName(location.SourceTree?.FilePath ?? ""),
                FilePath = location.SourceTree?.FilePath ?? "",
                LineNumber = lineSpan.StartLinePosition.Line + 1,
                Signature = GetSignature(symbol),
                IsTestMember = isTestMember,
                Reason = "No references found"
            };
        }

        private string GetSignature(ISymbol symbol)
        {
            return symbol switch
            {
                IMethodSymbol method => $"{method.ReturnType.Name} {method.Name}({string.Join(", ", method.Parameters.Select(p => $"{p.Type.Name} {p.Name}"))})",
                IPropertySymbol property => $"{property.Type.Name} {property.Name}",
                IFieldSymbol field => $"{field.Type.Name} {field.Name}",
                IEventSymbol @event => $"event {@event.Type.Name} {@event.Name}",
                INamedTypeSymbol type => $"{type.TypeKind} {type.Name}",
                _ => symbol.Name
            };
        }

        private bool IsTestProject(Project project)
        {
            var testIndicators = new[] { "Test", "Tests", "Testing", "Spec", "Specs" };
            return testIndicators.Any(indicator =>
                project.Name.Contains(indicator, StringComparison.OrdinalIgnoreCase));
        }

        private void CalculateStatistics(UnusedCodeResults results)
        {
            results.AnalyzedSymbols = results.UnusedItems.Count;

            // By accessibility
            results.PrivateCount = results.UnusedItems.Count(i => i.Accessibility == "Private");
            results.InternalCount = results.UnusedItems.Count(i => i.Accessibility == "Internal");
            results.PublicCount = results.UnusedItems.Count(i => i.Accessibility == "Public");

            // By kind
            results.ClassCount = results.UnusedItems.Count(i => i.Kind == "NamedType");
            results.MethodCount = results.UnusedItems.Count(i => i.Kind == "Method");
            results.PropertyCount = results.UnusedItems.Count(i => i.Kind == "Property");
            results.FieldCount = results.UnusedItems.Count(i => i.Kind == "Field");
            results.EventCount = results.UnusedItems.Count(i => i.Kind == "Event");
        }
    }
}
