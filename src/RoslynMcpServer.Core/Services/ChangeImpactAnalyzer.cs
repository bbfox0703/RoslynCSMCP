using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Core.Models;

namespace RoslynMcpServer.Core.Services
{
    /// <summary>
    /// Service for analyzing the impact of changing a symbol
    /// </summary>
    public class ChangeImpactAnalyzer
    {
        /// <summary>
        /// Maximum number of members whose references are followed during indirect analysis
        /// </summary>
        public const int MaxFollowedSymbols = 200;

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

                // Analyze impacted symbols: one entry per reference location
                var impactedSymbols = new List<ImpactedSymbol>();
                var recordedLocations = new HashSet<string>();

                // Add direct references, remembering the member that encloses each one
                var directEnclosingSymbols = new List<ISymbol>();
                foreach (var reference in directReferences)
                {
                    var (impacted, enclosingSymbol) = await CreateImpactedSymbolAsync(reference, targetSymbol, "Direct", 0, "Usage");
                    if (impacted != null && recordedLocations.Add(GetLocationKey(reference)))
                    {
                        impactedSymbols.Add(impacted);
                    }
                    if (enclosingSymbol != null)
                    {
                        directEnclosingSymbols.Add(enclosingSymbol);
                    }
                }

                // Find indirect references if requested (maxDepth counts levels, direct references being level 1)
                if (includeIndirectReferences && maxDepth > 1)
                {
                    await FindIndirectReferencesAsync(
                        targetSymbol,
                        directEnclosingSymbols,
                        solution,
                        impactedSymbols,
                        recordedLocations,
                        maxDepth,
                        results.Warnings);
                }

                results.ImpactedSymbols = impactedSymbols;
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
        /// Gets all types (nested types included) and their members declared in a compilation's own source.
        /// Referenced assemblies such as the framework are not searched.
        /// </summary>
        private List<ISymbol> GetAllSymbols(Compilation compilation)
        {
            var symbols = new List<ISymbol>();

            void VisitType(INamedTypeSymbol type)
            {
                symbols.Add(type);
                foreach (var typeMember in type.GetMembers())
                {
                    if (typeMember is INamedTypeSymbol nestedType)
                    {
                        VisitType(nestedType);
                    }
                    else
                    {
                        symbols.Add(typeMember);
                    }
                }
            }

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
                        VisitType(type);
                    }
                }
            }

            VisitNamespace(compilation.Assembly.GlobalNamespace);
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
        /// Finds all direct references to a symbol, one per source location
        /// </summary>
        private async Task<List<ReferenceLocation>> FindDirectReferencesAsync(ISymbol symbol, Solution solution)
        {
            var references = await SymbolFinder.FindReferencesAsync(symbol, solution);
            var locations = new List<ReferenceLocation>();
            var seenLocations = new HashSet<string>();

            foreach (var reference in references)
            {
                foreach (var location in reference.Locations)
                {
                    // A location can be reported for several cascaded symbols (e.g. a type and its
                    // constructor in 'new Foo()'), and once per target framework of a multi-targeted project
                    if (location.Document != null && seenLocations.Add(GetLocationKey(location)))
                    {
                        locations.Add(location);
                    }
                }
            }

            return locations;
        }

        /// <summary>
        /// Finds indirect references level by level: references to the members that enclose the previous
        /// level's references (for example the callers of the methods that use the target). Each member is
        /// followed once, and each location is recorded once across all levels.
        /// </summary>
        private async Task FindIndirectReferencesAsync(
            ISymbol targetSymbol,
            List<ISymbol> directEnclosingSymbols,
            Solution solution,
            List<ImpactedSymbol> impactedSymbols,
            HashSet<string> recordedLocations,
            int maxDepth,
            List<OperationWarning> warnings)
        {
            var followedSymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default) { targetSymbol };
            var followedCount = 0;
            var frontier = directEnclosingSymbols;

            // Direct references are level 1 (Distance 0); each further level is one step farther away
            for (var distance = 1; distance < maxDepth && frontier.Count > 0; distance++)
            {
                var nextFrontier = new List<ISymbol>();

                foreach (var symbol in frontier)
                {
                    if (!followedSymbols.Add(symbol))
                        continue;

                    if (followedCount == MaxFollowedSymbols)
                    {
                        warnings.Add(new OperationWarning
                        {
                            Context = "Indirect References",
                            Message = $"Stopped after following references to {MaxFollowedSymbols} members; indirect results are partial. Lower maxDepth for a complete result."
                        });
                        return;
                    }
                    followedCount++;

                    try
                    {
                        foreach (var reference in await FindDirectReferencesAsync(symbol, solution))
                        {
                            var (impacted, enclosingSymbol) = await CreateImpactedSymbolAsync(reference, symbol, "Indirect", distance, "Usage");

                            if (impacted != null && recordedLocations.Add(GetLocationKey(reference)))
                            {
                                impactedSymbols.Add(impacted);
                            }
                            if (enclosingSymbol != null)
                            {
                                nextFrontier.Add(enclosingSymbol);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to analyze indirect references of {Symbol}", symbol.ToDisplayString());
                    }
                }

                frontier = nextFrontier;
            }
        }

        /// <summary>
        /// Creates an ImpactedSymbol from a reference location, and returns the member that encloses the
        /// reference (null for references outside any member, such as in a using directive)
        /// </summary>
        private async Task<(ImpactedSymbol? Impacted, ISymbol? EnclosingMember)> CreateImpactedSymbolAsync(
            ReferenceLocation reference,
            ISymbol referencedSymbol,
            string referenceKind,
            int distance,
            string impactType)
        {
            try
            {
                var document = reference.Document;
                if (document == null)
                    return (null, null);

                var semanticModel = await document.GetSemanticModelAsync();
                if (semanticModel == null)
                    return (null, null);

                var root = await document.GetSyntaxRootAsync();
                if (root == null)
                    return (null, null);

                var node = root.FindNode(reference.Location.SourceSpan);
                var enclosingSymbol = semanticModel.GetEnclosingSymbol(node.SpanStart);

                if (enclosingSymbol == null)
                    return (null, null);

                var enclosingMember = GetEnclosingMember(enclosingSymbol);
                var symbol = enclosingMember ?? enclosingSymbol;

                // Get code context
                var lineSpan = reference.Location.GetLineSpan();
                var lines = (await document.GetTextAsync()).Lines;
                var lineNumber = lineSpan.StartLinePosition.Line;
                var codeLine = lineNumber < lines.Count ? lines[lineNumber].ToString() : string.Empty;

                var impacted = new ImpactedSymbol
                {
                    SymbolName = symbol.Name,
                    FullSymbolName = symbol.ToDisplayString(),
                    SymbolKind = symbol.Kind.ToString(),
                    FilePath = document.FilePath ?? string.Empty,
                    FileName = Path.GetFileName(document.FilePath ?? string.Empty),
                    ProjectName = document.Project.Name,
                    LineNumber = lineNumber + 1,
                    ReferenceKind = referenceKind,
                    ReferencedSymbol = referencedSymbol.ToDisplayString(),
                    Distance = distance,
                    ImpactType = impactType,
                    CodeContext = codeLine.Trim()
                };

                return (impacted, enclosingMember);
            }
            catch
            {
                return (null, null);
            }
        }

        /// <summary>
        /// Maps the symbol enclosing a reference to the member whose own references carry the impact further:
        /// lambdas and local functions map to their containing member, and accessors to their property or event.
        /// Returns null for namespaces, which are not followed.
        /// </summary>
        private static ISymbol? GetEnclosingMember(ISymbol symbol)
        {
            for (var current = symbol; current != null; current = current.ContainingSymbol)
            {
                switch (current)
                {
                    case IMethodSymbol { MethodKind: MethodKind.AnonymousFunction or MethodKind.LocalFunction }:
                        continue;
                    case IMethodSymbol
                    {
                        MethodKind: MethodKind.PropertyGet or MethodKind.PropertySet
                            or MethodKind.EventAdd or MethodKind.EventRemove or MethodKind.EventRaise,
                        AssociatedSymbol: { } associated
                    }:
                        return associated;
                    case IMethodSymbol or IPropertySymbol or IFieldSymbol or IEventSymbol or INamedTypeSymbol:
                        return current;
                    default:
                        return null;
                }
            }

            return null;
        }

        private static string GetLocationKey(ReferenceLocation location)
        {
            var span = location.Location.SourceSpan;
            return $"{location.Document.FilePath ?? location.Document.Name}|{span.Start}|{span.Length}";
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
        /// Builds dependency chains by following real links: each step is a member that references the
        /// previous one (the first step references the target directly)
        /// </summary>
        private List<DependencyChain> BuildDependencyChains(
            List<ImpactedSymbol> impactedSymbols,
            string startSymbol,
            int maxLength)
        {
            var chains = new List<DependencyChain>();

            // Index impacted locations by the symbol they reference
            var usersByReferencedSymbol = impactedSymbols
                .GroupBy(s => s.ReferencedSymbol)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Create representative chains (limit to avoid too many), one per directly impacted member
            var directSymbols = impactedSymbols
                .Where(s => s.Distance == 0)
                .GroupBy(s => s.FullSymbolName)
                .Select(g => g.First())
                .Take(5);

            foreach (var direct in directSymbols)
            {
                var chain = new DependencyChain
                {
                    Chain = new List<string> { startSymbol, direct.SymbolName },
                    ProjectsInvolved = new List<string> { direct.ProjectName }
                };

                // Extend the chain with a member that references the current end of the chain
                var visited = new HashSet<string> { direct.FullSymbolName };
                var current = direct;
                for (int i = 1; i < maxLength; i++)
                {
                    var next = usersByReferencedSymbol.TryGetValue(current.FullSymbolName, out var users)
                        ? users.FirstOrDefault(u => !visited.Contains(u.FullSymbolName))
                        : null;

                    if (next == null)
                        break;

                    chain.Chain.Add(next.SymbolName);
                    if (!chain.ProjectsInvolved.Contains(next.ProjectName))
                    {
                        chain.ProjectsInvolved.Add(next.ProjectName);
                    }

                    visited.Add(next.FullSymbolName);
                    current = next;
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
