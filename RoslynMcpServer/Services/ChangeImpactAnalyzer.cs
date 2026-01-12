using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Models;
using System.Collections.Concurrent;

namespace RoslynMcpServer.Services
{
    /// <summary>
    /// Service for analyzing the impact of changing a symbol
    /// </summary>
    public class ChangeImpactAnalyzer
    {
        private readonly ILogger<ChangeImpactAnalyzer> _logger;
        private readonly CodeAnalysisService _codeAnalysis;
        private readonly SymbolSearchService _symbolSearch;

        public ChangeImpactAnalyzer(
            ILogger<ChangeImpactAnalyzer> logger,
            CodeAnalysisService codeAnalysis,
            SymbolSearchService symbolSearch)
        {
            _logger = logger;
            _codeAnalysis = codeAnalysis;
            _symbolSearch = symbolSearch;
        }

        /// <summary>
        /// Analyzes the impact of changing a symbol
        /// </summary>
        public async Task<ChangeImpactResults> AnalyzeChangeImpactAsync(
            string symbolName,
            string solutionPath,
            int maxDepth = 3,
            bool includeIndirectReferences = true)
        {
            var results = new ChangeImpactResults
            {
                TargetSymbol = symbolName
            };

            try
            {
                var solution = await _codeAnalysis.GetSolutionAsync(solutionPath);

                // Find the target symbol
                var targetSymbol = await FindSymbolAsync(symbolName, solution);
                if (targetSymbol == null)
                {
                    results.Warnings.Add(new OperationWarning
                    {
                        Context = "Symbol Search",
                        Message = $"Symbol '{symbolName}' not found in solution"
                    });
                    return results;
                }

                // Populate symbol information
                PopulateSymbolInfo(results, targetSymbol);

                // Find all references
                var directReferences = await FindDirectReferencesAsync(targetSymbol, solution);
                results.DirectReferences = directReferences.Count;

                // Analyze impacted symbols
                var impactedSymbols = new ConcurrentBag<ImpactedSymbol>();
                var processedSymbols = new HashSet<string>();

                // Add direct references
                foreach (var reference in directReferences)
                {
                    var impacted = await CreateImpactedSymbolAsync(reference, "Direct", 0, "Usage", solution);
                    if (impacted != null)
                    {
                        impactedSymbols.Add(impacted);
                        processedSymbols.Add(impacted.FullSymbolName);
                    }
                }

                // Find indirect references if requested
                if (includeIndirectReferences && maxDepth > 1)
                {
                    await FindIndirectReferencesAsync(
                        directReferences,
                        solution,
                        impactedSymbols,
                        processedSymbols,
                        maxDepth,
                        1);
                }

                results.ImpactedSymbols = impactedSymbols.ToList();
                results.IndirectReferences = results.ImpactedSymbols.Count(s => s.ReferenceKind == "Indirect");

                // Calculate statistics
                CalculateStatistics(results);

                // Build dependency chains
                results.DependencyChains = BuildDependencyChains(results.ImpactedSymbols, targetSymbol.Name, maxDepth);

                // Assess risk
                AssessRisk(results, targetSymbol);

                // Generate recommendations
                GenerateRecommendations(results, targetSymbol);

                _logger.LogInformation(
                    "Change impact analysis complete for '{SymbolName}': {TotalImpacted} symbols impacted across {ProjectCount} projects",
                    symbolName,
                    results.TotalImpactedSymbols,
                    results.ImpactedProjects);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing change impact for symbol: {SymbolName}", symbolName);
                results.Warnings.Add(new OperationWarning
                {
                    Context = "Analysis",
                    Message = $"Error: {ex.Message}"
                });
            }

            return results;
        }

        /// <summary>
        /// Finds a symbol by name in the solution
        /// </summary>
        private async Task<ISymbol?> FindSymbolAsync(string symbolName, Solution solution)
        {
            foreach (var project in solution.Projects.Where(p => p.SupportsCompilation))
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation == null)
                    continue;

                // Search in all types
                var symbols = GetAllSymbols(compilation);
                var symbol = symbols.FirstOrDefault(s =>
                    s.Name == symbolName ||
                    s.ToDisplayString() == symbolName ||
                    s.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) == symbolName);

                if (symbol != null)
                    return symbol;
            }

            return null;
        }

        /// <summary>
        /// Gets all symbols in a compilation
        /// </summary>
        private List<ISymbol> GetAllSymbols(Compilation compilation)
        {
            var symbols = new List<ISymbol>();

            void VisitNamespace(INamespaceSymbol ns)
            {
                foreach (var member in ns.GetMembers())
                {
                    if (member is INamespaceSymbol childNs)
                    {
                        VisitNamespace(childNs);
                    }
                    else if (member is INamedTypeSymbol type)
                    {
                        symbols.Add(type);
                        // Add type members
                        foreach (var typeMember in type.GetMembers())
                        {
                            symbols.Add(typeMember);
                        }
                    }
                }
            }

            VisitNamespace(compilation.GlobalNamespace);
            return symbols;
        }

        /// <summary>
        /// Populates symbol information in results
        /// </summary>
        private void PopulateSymbolInfo(ChangeImpactResults results, ISymbol symbol)
        {
            results.TargetSymbolFullName = symbol.ToDisplayString();
            results.SymbolKind = symbol.Kind.ToString();
            results.Accessibility = symbol.DeclaredAccessibility.ToString();
            results.DeclaringType = symbol.ContainingType?.Name ?? string.Empty;
            results.Namespace = symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;

            var location = symbol.Locations.FirstOrDefault();
            if (location != null && location.IsInSource)
            {
                results.FilePath = location.SourceTree?.FilePath ?? string.Empty;
                results.LineNumber = location.GetLineSpan().StartLinePosition.Line + 1;
            }

            // Get project name from containing assembly
            if (symbol.ContainingAssembly != null)
            {
                results.ProjectName = symbol.ContainingAssembly.Name;
            }

            results.IsPublicAPI = symbol.DeclaredAccessibility == Accessibility.Public;
        }

        /// <summary>
        /// Finds all direct references to a symbol
        /// </summary>
        private async Task<List<ReferenceLocation>> FindDirectReferencesAsync(ISymbol symbol, Solution solution)
        {
            var references = await SymbolFinder.FindReferencesAsync(symbol, solution);
            var locations = new List<ReferenceLocation>();

            foreach (var reference in references)
            {
                foreach (var location in reference.Locations)
                {
                    if (location.Document != null)
                    {
                        locations.Add(location);
                    }
                }
            }

            return locations;
        }

        /// <summary>
        /// Finds indirect references (references to symbols that reference the target)
        /// </summary>
        private async Task FindIndirectReferencesAsync(
            List<ReferenceLocation> directReferences,
            Solution solution,
            ConcurrentBag<ImpactedSymbol> impactedSymbols,
            HashSet<string> processedSymbols,
            int maxDepth,
            int currentDepth)
        {
            if (currentDepth >= maxDepth)
                return;

            var nextLevelReferences = new List<ReferenceLocation>();

            foreach (var reference in directReferences)
            {
                try
                {
                    // Get the containing symbol
                    var document = reference.Document;
                    if (document == null)
                        continue;

                    var semanticModel = await document.GetSemanticModelAsync();
                    if (semanticModel == null)
                        continue;

                    var root = await document.GetSyntaxRootAsync();
                    if (root == null)
                        continue;

                    var node = root.FindNode(reference.Location.SourceSpan);
                    var containingSymbol = semanticModel.GetEnclosingSymbol(node.SpanStart);

                    if (containingSymbol != null && !processedSymbols.Contains(containingSymbol.ToDisplayString()))
                    {
                        processedSymbols.Add(containingSymbol.ToDisplayString());

                        // Find references to this containing symbol
                        var secondaryRefs = await FindDirectReferencesAsync(containingSymbol, solution);

                        foreach (var secondaryRef in secondaryRefs)
                        {
                            var impacted = await CreateImpactedSymbolAsync(
                                secondaryRef,
                                "Indirect",
                                currentDepth,
                                "Usage",
                                solution);

                            if (impacted != null && !processedSymbols.Contains(impacted.FullSymbolName))
                            {
                                impactedSymbols.Add(impacted);
                                processedSymbols.Add(impacted.FullSymbolName);
                                nextLevelReferences.Add(secondaryRef);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to analyze indirect reference");
                }
            }

            // Recursively find next level
            if (nextLevelReferences.Any() && currentDepth + 1 < maxDepth)
            {
                await FindIndirectReferencesAsync(
                    nextLevelReferences,
                    solution,
                    impactedSymbols,
                    processedSymbols,
                    maxDepth,
                    currentDepth + 1);
            }
        }

        /// <summary>
        /// Creates an ImpactedSymbol from a reference location
        /// </summary>
        private async Task<ImpactedSymbol?> CreateImpactedSymbolAsync(
            ReferenceLocation reference,
            string referenceKind,
            int distance,
            string impactType,
            Solution solution)
        {
            try
            {
                var document = reference.Document;
                if (document == null)
                    return null;

                var semanticModel = await document.GetSemanticModelAsync();
                if (semanticModel == null)
                    return null;

                var root = await document.GetSyntaxRootAsync();
                if (root == null)
                    return null;

                var node = root.FindNode(reference.Location.SourceSpan);
                var symbol = semanticModel.GetEnclosingSymbol(node.SpanStart);

                if (symbol == null)
                    return null;

                // Get code context
                var lineSpan = reference.Location.GetLineSpan();
                var lines = (await document.GetTextAsync()).Lines;
                var lineNumber = lineSpan.StartLinePosition.Line;
                var codeLine = lineNumber < lines.Count ? lines[lineNumber].ToString() : string.Empty;

                return new ImpactedSymbol
                {
                    SymbolName = symbol.Name,
                    FullSymbolName = symbol.ToDisplayString(),
                    SymbolKind = symbol.Kind.ToString(),
                    FilePath = document.FilePath ?? string.Empty,
                    FileName = Path.GetFileName(document.FilePath ?? string.Empty),
                    ProjectName = document.Project.Name,
                    LineNumber = lineNumber + 1,
                    ReferenceKind = referenceKind,
                    Distance = distance,
                    ImpactType = impactType,
                    CodeContext = codeLine.Trim()
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Calculates impact statistics
        /// </summary>
        private void CalculateStatistics(ChangeImpactResults results)
        {
            results.TotalImpactedSymbols = results.ImpactedSymbols.Count;
            results.ImpactedFiles = results.ImpactedSymbols.Select(s => s.FilePath).Distinct().Count();
            results.ImpactedProjectNames = results.ImpactedSymbols.Select(s => s.ProjectName).Distinct().ToList();
            results.ImpactedProjects = results.ImpactedProjectNames.Count;

            // Impact by project
            results.ImpactByProject = results.ImpactedSymbols
                .GroupBy(s => s.ProjectName)
                .ToDictionary(g => g.Key, g => g.Count());

            // Impact by kind
            results.ImpactByKind = results.ImpactedSymbols
                .GroupBy(s => s.SymbolKind)
                .ToDictionary(g => g.Key, g => g.Count());
        }

        /// <summary>
        /// Builds dependency chains
        /// </summary>
        private List<DependencyChain> BuildDependencyChains(
            List<ImpactedSymbol> impactedSymbols,
            string startSymbol,
            int maxLength)
        {
            var chains = new List<DependencyChain>();

            // Group by distance and create chains
            var byDistance = impactedSymbols
                .OrderBy(s => s.Distance)
                .GroupBy(s => s.Distance)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Create representative chains (limit to avoid too many)
            var directSymbols = byDistance.ContainsKey(0) ? byDistance[0].Take(5) : Enumerable.Empty<ImpactedSymbol>();

            foreach (var direct in directSymbols)
            {
                var chain = new DependencyChain
                {
                    Chain = new List<string> { startSymbol, direct.SymbolName },
                    ProjectsInvolved = new List<string> { direct.ProjectName }
                };

                // Try to extend chain with indirect references
                for (int i = 1; i < maxLength && byDistance.ContainsKey(i); i++)
                {
                    var nextLevel = byDistance[i]
                        .Where(s => s.ProjectName == chain.ProjectsInvolved.Last() || !chain.ProjectsInvolved.Contains(s.ProjectName))
                        .FirstOrDefault();

                    if (nextLevel != null)
                    {
                        chain.Chain.Add(nextLevel.SymbolName);
                        if (!chain.ProjectsInvolved.Contains(nextLevel.ProjectName))
                        {
                            chain.ProjectsInvolved.Add(nextLevel.ProjectName);
                        }
                    }
                    else
                    {
                        break;
                    }
                }

                chains.Add(chain);
            }

            return chains.Take(10).ToList();
        }

        /// <summary>
        /// Assesses the risk level of the change
        /// </summary>
        private void AssessRisk(ChangeImpactResults results, ISymbol symbol)
        {
            var riskFactors = new List<string>();

            // Public API changes are higher risk
            if (results.IsPublicAPI)
            {
                riskFactors.Add("Public API change");
                results.IsBreakingChange = true;
                results.BreakingChangeReasons.Add("Changes to public API may break external consumers");
            }

            // High number of references
            if (results.TotalImpactedSymbols > 50)
            {
                riskFactors.Add($"High usage ({results.TotalImpactedSymbols} references)");
            }
            else if (results.TotalImpactedSymbols > 20)
            {
                riskFactors.Add($"Moderate usage ({results.TotalImpactedSymbols} references)");
            }

            // Cross-project impact
            if (results.ImpactedProjects > 3)
            {
                riskFactors.Add($"Impacts {results.ImpactedProjects} projects");
            }

            // Interface or abstract class changes
            if (symbol is INamedTypeSymbol namedType)
            {
                if (namedType.TypeKind == TypeKind.Interface)
                {
                    riskFactors.Add("Interface change (affects all implementations)");
                    results.IsBreakingChange = true;
                    results.BreakingChangeReasons.Add("Interface changes require updates to all implementing classes");
                }
                else if (namedType.IsAbstract)
                {
                    riskFactors.Add("Abstract class change (affects all derived classes)");
                }
            }

            // Determine risk level
            if (results.IsPublicAPI && results.TotalImpactedSymbols > 20)
            {
                results.RiskLevel = "Critical";
            }
            else if (results.TotalImpactedSymbols > 50 || results.ImpactedProjects > 5)
            {
                results.RiskLevel = "High";
            }
            else if (results.TotalImpactedSymbols > 10 || results.ImpactedProjects > 2)
            {
                results.RiskLevel = "Medium";
            }
            else
            {
                results.RiskLevel = "Low";
            }

            results.RiskReason = string.Join("; ", riskFactors);
        }

        /// <summary>
        /// Generates recommendations for the change
        /// </summary>
        private void GenerateRecommendations(ChangeImpactResults results, ISymbol symbol)
        {
            if (results.IsPublicAPI)
            {
                results.Recommendations.Add("✓ Consider versioning or deprecation strategy for public API changes");
                results.Recommendations.Add("✓ Update API documentation and release notes");
            }

            if (results.TotalImpactedSymbols > 20)
            {
                results.Recommendations.Add("✓ Review all impacted locations before committing");
                results.Recommendations.Add("✓ Consider creating a migration guide");
            }

            if (results.ImpactedProjects > 2)
            {
                results.Recommendations.Add($"✓ Coordinate with teams owning the {results.ImpactedProjects} affected projects");
            }

            if (results.IsBreakingChange)
            {
                results.Recommendations.Add("✓ Plan for major version bump");
                results.Recommendations.Add("✓ Create comprehensive tests for breaking changes");
            }

            if (symbol is INamedTypeSymbol namedType && namedType.TypeKind == TypeKind.Interface)
            {
                results.Recommendations.Add("✓ Consider using extension methods instead of adding interface members");
                results.Recommendations.Add("✓ Provide default implementations where possible (C# 8.0+)");
            }

            if (results.RiskLevel == "Low" && results.TotalImpactedSymbols < 5)
            {
                results.Recommendations.Add("✓ Low risk change - proceed with standard code review");
            }
        }
    }
}
