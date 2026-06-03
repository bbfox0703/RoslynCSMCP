using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
            string groupBy = "project",
            CancellationToken cancellationToken = default)
        {
            var results = new TestCoverageResults();

            try
            {
                var solution = await _codeAnalysis.GetSolutionAsync(solutionPath);
                var allTypeCoverages = new ConcurrentBag<TypeCoverage>();

                // Build a single reverse-reference index of all symbols referenced from
                // test projects. This replaces a per-member full-solution
                // SymbolFinder.FindReferencesAsync search (which was O(members × solution))
                // with one pass over the test code and O(1) lookups per member.
                var testReferenceIndex = await BuildTestReferenceIndexAsync(solution, cancellationToken);

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
                        var projectCoverages = await AnalyzeProjectCoverageAsync(project, solution, scope, testReferenceIndex, cancellationToken);
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
            string scope,
            IReadOnlyDictionary<string, List<string>> testReferenceIndex,
            CancellationToken cancellationToken)
        {
            var coverages = new List<TypeCoverage>();

            var compilation = await project.GetCompilationAsync(cancellationToken);
            if (compilation == null)
                return coverages;

            // Get all types in the project
            var allTypes = GetTypesInCompilation(compilation, scope);

            foreach (var typeSymbol in allTypes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var coverage = await AnalyzeTypeCoverageAsync(typeSymbol, solution, project, testReferenceIndex, cancellationToken);
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
            Project project,
            IReadOnlyDictionary<string, List<string>> testReferenceIndex,
            CancellationToken cancellationToken)
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
                var memberCoverage = await AnalyzeMemberCoverageAsync(member, testReferenceIndex);

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
        private async Task<MemberCoverage> AnalyzeMemberCoverageAsync(
            ISymbol member,
            IReadOnlyDictionary<string, List<string>> testReferenceIndex)
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

            // Check if member has tests (simplified - checks if any test references this member).
            // Looked up against the pre-built reverse-reference index instead of running a
            // full-solution reference search per member.
            var memberKey = GetReferenceKey(member);
            if (memberKey != null && testReferenceIndex.TryGetValue(memberKey, out var testReferences) && testReferences.Count > 0)
            {
                memberCoverage.HasTest = true;
                memberCoverage.TestMethods = testReferences.Take(5).ToList();
            }
            else
            {
                memberCoverage.HasTest = false;
            }

            return memberCoverage;
        }

        /// <summary>
        /// Builds a reverse-reference index mapping a symbol key to the test-project
        /// locations that reference it. Walks every test-project document's syntax/semantic
        /// model exactly once, so member coverage can be resolved with O(1) lookups instead
        /// of an O(members × solution) reference search.
        /// </summary>
        private async Task<IReadOnlyDictionary<string, List<string>>> BuildTestReferenceIndexAsync(
            Solution solution,
            CancellationToken cancellationToken)
        {
            var index = new ConcurrentDictionary<string, ConcurrentBag<string>>();

            var testDocuments = solution.Projects
                .Where(p => p.SupportsCompilation && IsTestProject(p.Name))
                .SelectMany(p => p.Documents)
                .ToList();

            if (testDocuments.Count == 0)
                return new Dictionary<string, List<string>>();

            // Bound parallelism so a very large solution can't spawn unbounded
            // semantic-model work all at once.
            var maxConcurrency = Math.Max(1, Environment.ProcessorCount / 2);
            using var throttle = new SemaphoreSlim(maxConcurrency);

            var tasks = testDocuments.Select(async document =>
            {
                await throttle.WaitAsync(cancellationToken);
                try
                {
                    await IndexTestDocumentAsync(document, index, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to index test document: {DocumentName}", document.Name);
                }
                finally
                {
                    throttle.Release();
                }
            });

            await Task.WhenAll(tasks);

            return index.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Distinct().ToList());
        }

        /// <summary>
        /// Indexes every symbol referenced from a single test document.
        /// </summary>
        private static async Task IndexTestDocumentAsync(
            Document document,
            ConcurrentDictionary<string, ConcurrentBag<string>> index,
            CancellationToken cancellationToken)
        {
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            var root = await document.GetSyntaxRootAsync(cancellationToken);
            if (semanticModel == null || root == null)
                return;

            foreach (var node in root.DescendantNodes())
            {
                if (node is not (IdentifierNameSyntax or GenericNameSyntax))
                    continue;

                cancellationToken.ThrowIfCancellationRequested();

                var symbol = semanticModel.GetSymbolInfo(node, cancellationToken).Symbol;
                if (symbol == null)
                    continue;

                var key = GetReferenceKey(symbol);
                if (key == null)
                    continue;

                var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                index.GetOrAdd(key, _ => new ConcurrentBag<string>())
                     .Add($"{document.Name}:{line}");
            }
        }

        /// <summary>
        /// Produces a stable, cross-compilation key for a symbol so that references resolved
        /// in a test project's compilation match definitions in the analyzed project's
        /// compilation. Returns null for symbols that have no documentation comment id.
        /// </summary>
        private static string? GetReferenceKey(ISymbol symbol)
        {
            // Map reduced extension method invocations back to their original definition.
            if (symbol is IMethodSymbol { ReducedFrom: { } reducedFrom })
                symbol = reducedFrom;

            return symbol.OriginalDefinition.GetDocumentationCommentId();
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

            // Use the source assembly's global namespace rather than compilation.GlobalNamespace:
            // the latter merges in every referenced assembly (the entire BCL), which would make
            // us analyze tens of thousands of metadata types that can never have test coverage.
            VisitNamespace(compilation.Assembly.GlobalNamespace);
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
