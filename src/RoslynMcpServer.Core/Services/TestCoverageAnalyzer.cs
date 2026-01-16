using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Core.Models;
using System.Collections.Concurrent;

namespace RoslynMcpServer.Core.Services
{
    /// <summary>
    /// Service for analyzing test coverage across a solution
    /// </summary>
    public class TestCoverageAnalyzer
    {
        private readonly ILogger<TestCoverageAnalyzer> _logger;
        private readonly CodeAnalysisService _codeAnalysis;
        private readonly TestDiscoveryService _testDiscovery;

        public TestCoverageAnalyzer(
            ILogger<TestCoverageAnalyzer> logger,
            CodeAnalysisService codeAnalysis,
            TestDiscoveryService testDiscovery)
        {
            _logger = logger;
            _codeAnalysis = codeAnalysis;
            _testDiscovery = testDiscovery;
        }

        /// <summary>
        /// Analyzes test coverage for all types in a solution
        /// </summary>
        public async Task<TestCoverageResults> AnalyzeTestCoverageAsync(
            string solutionPath,
            string scope = "public",
            string groupBy = "project")
        {
            var results = new TestCoverageResults();

            try
            {
                var solution = await _codeAnalysis.GetSolutionAsync(solutionPath);
                var allTypeCoverages = new ConcurrentBag<TypeCoverage>();

                // Get non-test projects
                var nonTestProjects = solution.Projects
                    .Where(p => p.SupportsCompilation && !IsTestProject(p.Name))
                    .ToList();

                results.AnalyzedProjects = nonTestProjects.Count;

                // Analyze each project
                var projectTasks = nonTestProjects.Select(async project =>
                {
                    try
                    {
                        var projectCoverages = await AnalyzeProjectCoverageAsync(project, solution, scope);
                        foreach (var coverage in projectCoverages)
                        {
                            allTypeCoverages.Add(coverage);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to analyze project: {ProjectName}", project.Name);
                        results.FailedProjects++;
                    }
                });

                await Task.WhenAll(projectTasks);

                results.TypeCoverages = allTypeCoverages.ToList();

                // Calculate statistics
                CalculateStatistics(results, groupBy);

                _logger.LogInformation(
                    "Test coverage analysis complete: {TotalTypes} types analyzed, {TestedTypes} tested ({Coverage:F1}%)",
                    results.TotalTypes,
                    results.TestedTypes,
                    results.OverallTypeCoverage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing test coverage");
                results.Warnings.Add(new OperationWarning
                {
                    Context = "Analysis",
                    Message = $"Error: {ex.Message}"
                });
            }

            return results;
        }

        /// <summary>
        /// Analyzes test coverage for a single project
        /// </summary>
        private async Task<List<TypeCoverage>> AnalyzeProjectCoverageAsync(
            Project project,
            Solution solution,
            string scope)
        {
            var coverages = new List<TypeCoverage>();

            var compilation = await project.GetCompilationAsync();
            if (compilation == null)
                return coverages;

            // Get all types in the project
            var allTypes = GetTypesInCompilation(compilation, scope);

            foreach (var typeSymbol in allTypes)
            {
                try
                {
                    var coverage = await AnalyzeTypeCoverageAsync(typeSymbol, solution, project);
                    coverages.Add(coverage);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to analyze type: {TypeName}", typeSymbol.Name);
                }
            }

            return coverages;
        }

        /// <summary>
        /// Analyzes test coverage for a single type
        /// </summary>
        private async Task<TypeCoverage> AnalyzeTypeCoverageAsync(
            INamedTypeSymbol typeSymbol,
            Solution solution,
            Project project)
        {
            var coverage = new TypeCoverage
            {
                TypeName = typeSymbol.Name,
                FullTypeName = typeSymbol.ToDisplayString(),
                Namespace = typeSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty,
                ProjectName = project.Name,
                Accessibility = typeSymbol.DeclaredAccessibility.ToString(),
                IsAbstract = typeSymbol.IsAbstract,
                IsSealed = typeSymbol.IsSealed
            };

            // Get location information
            var location = typeSymbol.Locations.FirstOrDefault();
            if (location != null && location.IsInSource)
            {
                coverage.FilePath = location.SourceTree?.FilePath ?? string.Empty;
                coverage.LineNumber = location.GetLineSpan().StartLinePosition.Line + 1;
            }

            // Find tests for this type
            try
            {
                var tests = await _testDiscovery.FindTestsForTypeAsync(
                    typeSymbol.Name,
                    solution.FilePath ?? string.Empty,
                    includePartialMatches: true);

                coverage.HasTests = tests.Any();
                coverage.TestClasses = tests.Select(t => t.TestClassName).ToList();
                coverage.TestCount = tests.Sum(t => t.TestCount);
            }
            catch
            {
                coverage.HasTests = false;
            }

            // Analyze member coverage
            var publicMembers = GetPublicMembers(typeSymbol);
            coverage.TotalPublicMembers = publicMembers.Count;

            var uncoveredMembers = new List<MemberCoverage>();
            int totalComplexity = 0;
            int maxComplexity = 0;

            foreach (var member in publicMembers)
            {
                var memberCoverage = await AnalyzeMemberCoverageAsync(member, solution);

                totalComplexity += memberCoverage.CyclomaticComplexity;
                if (memberCoverage.CyclomaticComplexity > maxComplexity)
                    maxComplexity = memberCoverage.CyclomaticComplexity;

                if (!memberCoverage.HasTest)
                {
                    uncoveredMembers.Add(memberCoverage);
                }
                else
                {
                    coverage.TestedPublicMembers++;
                }
            }

            coverage.UncoveredMembers = uncoveredMembers;
            coverage.CyclomaticComplexity = totalComplexity;
            coverage.MaxMethodComplexity = maxComplexity;

            // Assess risk level
            coverage.RiskLevel = AssessRiskLevel(coverage);

            return coverage;
        }

        /// <summary>
        /// Analyzes test coverage for a single member
        /// </summary>
        private async Task<MemberCoverage> AnalyzeMemberCoverageAsync(ISymbol member, Solution solution)
        {
            var memberCoverage = new MemberCoverage
            {
                MemberName = member.Name,
                MemberKind = member.Kind.ToString(),
                Signature = member.ToDisplayString(),
                DeclaringType = member.ContainingType?.Name ?? string.Empty,
                IsPublic = member.DeclaredAccessibility == Accessibility.Public,
                IsVirtual = member.IsVirtual,
                IsAbstract = member.IsAbstract
            };

            var location = member.Locations.FirstOrDefault();
            if (location != null && location.IsInSource)
            {
                memberCoverage.LineNumber = location.GetLineSpan().StartLinePosition.Line + 1;
            }

            // Calculate cyclomatic complexity for methods
            if (member is IMethodSymbol method)
            {
                memberCoverage.CyclomaticComplexity = await CalculateMethodComplexityAsync(method);
            }

            // Check if member has tests (simplified - checks if any test references this member)
            try
            {
                var references = await SymbolFinder.FindReferencesAsync(member, solution);
                var testReferences = references
                    .SelectMany(r => r.Locations)
                    .Where(loc => loc.Document != null && IsTestProject(loc.Document.Project.Name))
                    .ToList();

                memberCoverage.HasTest = testReferences.Any();
                memberCoverage.TestMethods = testReferences
                    .Select(loc => $"{loc.Document?.Name}:{loc.Location.GetLineSpan().StartLinePosition.Line + 1}")
                    .Distinct()
                    .Take(5)
                    .ToList();
            }
            catch
            {
                memberCoverage.HasTest = false;
            }

            return memberCoverage;
        }

        /// <summary>
        /// Gets all public members of a type
        /// </summary>
        private List<ISymbol> GetPublicMembers(INamedTypeSymbol typeSymbol)
        {
            var members = new List<ISymbol>();

            // Get public methods (excluding constructors, getters, setters)
            members.AddRange(typeSymbol.GetMembers()
                .Where(m => m.DeclaredAccessibility == Accessibility.Public &&
                           m.Kind == SymbolKind.Method &&
                           m is IMethodSymbol method &&
                           method.MethodKind == MethodKind.Ordinary));

            // Get public properties
            members.AddRange(typeSymbol.GetMembers()
                .Where(m => m.DeclaredAccessibility == Accessibility.Public &&
                           m.Kind == SymbolKind.Property));

            // Get public events
            members.AddRange(typeSymbol.GetMembers()
                .Where(m => m.DeclaredAccessibility == Accessibility.Public &&
                           m.Kind == SymbolKind.Event));

            return members;
        }

        /// <summary>
        /// Calculates cyclomatic complexity for a method
        /// </summary>
        private async Task<int> CalculateMethodComplexityAsync(IMethodSymbol method)
        {
            var location = method.Locations.FirstOrDefault();
            if (location == null || !location.IsInSource || location.SourceTree == null)
                return 1;

            var root = await location.SourceTree.GetRootAsync();
            var methodNode = root.FindNode(location.SourceSpan) as MethodDeclarationSyntax;

            if (methodNode == null)
                return 1;

            int complexity = 1;

            // Count decision points
            var descendantNodes = methodNode.DescendantNodes();

            // If statements
            complexity += descendantNodes.OfType<IfStatementSyntax>().Count();

            // While loops
            complexity += descendantNodes.OfType<WhileStatementSyntax>().Count();

            // For loops
            complexity += descendantNodes.OfType<ForStatementSyntax>().Count();

            // Foreach loops
            complexity += descendantNodes.OfType<ForEachStatementSyntax>().Count();

            // Switch cases
            complexity += descendantNodes.OfType<SwitchSectionSyntax>().Count();

            // Catch clauses
            complexity += descendantNodes.OfType<CatchClauseSyntax>().Count();

            // Logical operators (&&, ||)
            complexity += descendantNodes.OfType<BinaryExpressionSyntax>()
                .Count(b => b.IsKind(SyntaxKind.LogicalAndExpression) ||
                           b.IsKind(SyntaxKind.LogicalOrExpression));

            // Null-coalescing operator (??)
            complexity += descendantNodes.OfType<BinaryExpressionSyntax>()
                .Count(b => b.IsKind(SyntaxKind.CoalesceExpression));

            // Conditional expressions (ternary operator)
            complexity += descendantNodes.OfType<ConditionalExpressionSyntax>().Count();

            return complexity;
        }

        /// <summary>
        /// Gets all types in a compilation based on scope
        /// </summary>
        private List<INamedTypeSymbol> GetTypesInCompilation(Compilation compilation, string scope)
        {
            var types = new List<INamedTypeSymbol>();

            void VisitNamespace(INamespaceSymbol ns)
            {
                foreach (var member in ns.GetMembers())
                {
                    if (member is INamespaceSymbol childNs)
                    {
                        VisitNamespace(childNs);
                    }
                    else if (member is INamedTypeSymbol type &&
                             (type.TypeKind == TypeKind.Class || type.TypeKind == TypeKind.Interface))
                    {
                        // Filter by scope
                        if (scope.ToLower() == "public" && type.DeclaredAccessibility != Accessibility.Public)
                            continue;

                        types.Add(type);
                    }
                }
            }

            VisitNamespace(compilation.GlobalNamespace);
            return types;
        }

        /// <summary>
        /// Assesses risk level based on complexity and test coverage
        /// </summary>
        private string AssessRiskLevel(TypeCoverage coverage)
        {
            // Calculate average complexity
            double avgComplexity = coverage.TotalPublicMembers > 0
                ? coverage.CyclomaticComplexity / (double)coverage.TotalPublicMembers
                : 0;

            bool highComplexity = avgComplexity > 10 || coverage.MaxMethodComplexity > 20;
            bool mediumComplexity = avgComplexity > 5 || coverage.MaxMethodComplexity > 10;
            bool hasTests = coverage.HasTests;
            bool fullyCovered = coverage.CoveragePercentage >= 80;

            if (highComplexity && !hasTests)
                return "Critical";
            if (mediumComplexity && !hasTests)
                return "High";
            if (!hasTests || coverage.CoveragePercentage < 50)
                return "Medium";
            if (fullyCovered)
                return "Low";

            return "Medium";
        }

        /// <summary>
        /// Checks if a project is a test project
        /// </summary>
        private bool IsTestProject(string projectName)
        {
            var testIndicators = new[] { "test", "tests", "testing", "spec", "specs" };
            return testIndicators.Any(indicator =>
                projectName.Contains(indicator, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Calculates overall statistics
        /// </summary>
        private void CalculateStatistics(TestCoverageResults results, string groupBy)
        {
            results.TotalTypes = results.TypeCoverages.Count;
            results.TestedTypes = results.TypeCoverages.Count(t => t.HasTests);
            results.TotalPublicMembers = results.TypeCoverages.Sum(t => t.TotalPublicMembers);
            results.TestedPublicMembers = results.TypeCoverages.Sum(t => t.TestedPublicMembers);

            // Risk analysis
            results.CriticalRiskTypes = results.TypeCoverages.Count(t => t.RiskLevel == "Critical");
            results.HighRiskTypes = results.TypeCoverages.Count(t => t.RiskLevel == "High");
            results.MediumRiskTypes = results.TypeCoverages.Count(t => t.RiskLevel == "Medium");
            results.LowRiskTypes = results.TypeCoverages.Count(t => t.RiskLevel == "Low");

            // Group by project or namespace
            if (groupBy.ToLower() == "project")
            {
                results.ProjectStatistics = results.TypeCoverages
                    .GroupBy(t => t.ProjectName)
                    .ToDictionary(
                        g => g.Key,
                        g => new CoverageStatistics
                        {
                            Name = g.Key,
                            TotalTypes = g.Count(),
                            TestedTypes = g.Count(t => t.HasTests),
                            UncoveredTypes = g.Count(t => !t.HasTests),
                            TotalPublicMembers = g.Sum(t => t.TotalPublicMembers),
                            TestedPublicMembers = g.Sum(t => t.TestedPublicMembers),
                            UncoveredPublicMembers = g.Sum(t => t.UncoveredMembers.Count)
                        });
            }
            else if (groupBy.ToLower() == "namespace")
            {
                results.NamespaceStatistics = results.TypeCoverages
                    .GroupBy(t => t.Namespace)
                    .ToDictionary(
                        g => g.Key,
                        g => new CoverageStatistics
                        {
                            Name = g.Key,
                            TotalTypes = g.Count(),
                            TestedTypes = g.Count(t => t.HasTests),
                            UncoveredTypes = g.Count(t => !t.HasTests),
                            TotalPublicMembers = g.Sum(t => t.TotalPublicMembers),
                            TestedPublicMembers = g.Sum(t => t.TestedPublicMembers),
                            UncoveredPublicMembers = g.Sum(t => t.UncoveredMembers.Count)
                        });
            }
        }
    }
}
