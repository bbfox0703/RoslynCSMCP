using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Models;
using System.Collections.Concurrent;

namespace RoslynMcpServer.Services
{
    public class CodeAnalysisService : IDisposable
    {
        private readonly ILogger<CodeAnalysisService> _logger;
        private readonly IMemoryCache _cache;
        private readonly ConcurrentDictionary<string, MSBuildWorkspace> _workspaces;
        private readonly SemaphoreSlim _workspaceLock;

        public CodeAnalysisService(ILogger<CodeAnalysisService> logger, IMemoryCache cache)
        {
            _logger = logger;
            _cache = cache;
            _workspaces = new ConcurrentDictionary<string, MSBuildWorkspace>();
            _workspaceLock = new SemaphoreSlim(1, 1);
        }

        public async Task<Solution> GetSolutionAsync(string solutionPath)
        {
            var cacheKey = $"solution:{solutionPath}";

            // Check cache first
            if (_cache.TryGetValue(cacheKey, out Solution? cachedSolution) && cachedSolution != null)
            {
                _logger.LogDebug("Returning cached solution for {SolutionPath}", solutionPath);
                return cachedSolution;
            }

            await _workspaceLock.WaitAsync();
            try
            {
                // Double-check after acquiring lock
                if (_cache.TryGetValue(cacheKey, out cachedSolution) && cachedSolution != null)
                {
                    return cachedSolution;
                }

                _logger.LogInformation("Loading solution from {SolutionPath}", solutionPath);

                // Create or reuse workspace
                var workspace = _workspaces.GetOrAdd(solutionPath, _ => MSBuildWorkspace.Create());

                // Handle workspace failures
                workspace.WorkspaceFailed += (sender, args) =>
                {
                    if (args.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
                    {
                        _logger.LogWarning("Workspace failure: {Message}", args.Diagnostic.Message);
                    }
                };

                // Load solution
                var solution = await workspace.OpenSolutionAsync(solutionPath);

                // Cache the solution for 5 minutes
                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromMinutes(5))
                    .SetSize(1)
                    .RegisterPostEvictionCallback((key, value, reason, state) =>
                    {
                        _logger.LogDebug("Solution cache evicted for {Key}, reason: {Reason}", key, reason);
                    });

                _cache.Set(cacheKey, solution, cacheOptions);

                _logger.LogInformation("Solution loaded successfully with {ProjectCount} projects", solution.Projects.Count());

                return solution;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load solution from {SolutionPath}", solutionPath);
                throw;
            }
            finally
            {
                _workspaceLock.Release();
            }
        }

        public async Task<DependencyAnalysis> AnalyzeDependenciesAsync(string solutionPath, int maxDepth = 3)
        {
            var solution = await GetSolutionAsync(solutionPath);

            var analysis = new DependencyAnalysis
            {
                ProjectName = Path.GetFileNameWithoutExtension(solutionPath),
                Dependencies = new List<ProjectDependency>(),
                NamespaceUsages = new List<NamespaceUsage>()
            };

            var namespaceUsageMap = new ConcurrentDictionary<string, NamespaceUsageInfo>();
            var dependencyMap = new ConcurrentDictionary<string, DependencyUsageInfo>();

            // Thread-safe counters
            var totalSymbols = 0;
            var publicSymbols = 0;
            var internalSymbols = 0;

            // Analyze all projects in parallel
            var tasks = solution.Projects
                .Where(p => p.SupportsCompilation)
                .Select(async project =>
                {
                    try
                    {
                        var (total, pubCount, intCount) = await AnalyzeProjectAsync(project, namespaceUsageMap, dependencyMap);
                        Interlocked.Add(ref totalSymbols, total);
                        Interlocked.Add(ref publicSymbols, pubCount);
                        Interlocked.Add(ref internalSymbols, intCount);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error analyzing project {ProjectName}", project.Name);
                    }
                });

            await Task.WhenAll(tasks);

            // Set symbol counts
            analysis.TotalSymbols = totalSymbols;
            analysis.PublicSymbols = publicSymbols;
            analysis.InternalSymbols = internalSymbols;

            // Convert namespace usage map to list
            analysis.NamespaceUsages = namespaceUsageMap
                .Select(kvp => new NamespaceUsage
                {
                    Namespace = kvp.Key,
                    UsageCount = kvp.Value.Count,
                    UsedTypes = kvp.Value.Types.Take(20).ToList()
                })
                .OrderByDescending(n => n.UsageCount)
                .ToList();

            // Convert dependency map to list
            analysis.Dependencies = dependencyMap
                .Select(kvp => new ProjectDependency
                {
                    Name = kvp.Key,
                    Version = kvp.Value.Version,
                    Type = kvp.Value.Type,
                    UsageCount = kvp.Value.Count
                })
                .OrderByDescending(d => d.UsageCount)
                .ToList();

            return analysis;
        }

        private async Task<(int totalSymbols, int publicSymbols, int internalSymbols)> AnalyzeProjectAsync(
            Project project,
            ConcurrentDictionary<string, NamespaceUsageInfo> namespaceUsageMap,
            ConcurrentDictionary<string, DependencyUsageInfo> dependencyMap)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) return (0, 0, 0);

            // Count symbols
            var symbols = GetAllSymbols(compilation.GlobalNamespace).ToList();
            var totalCount = symbols.Count;
            var publicCount = symbols.Count(s => s.DeclaredAccessibility == Accessibility.Public);
            var internalCount = symbols.Count(s => s.DeclaredAccessibility == Accessibility.Internal);

            // Analyze metadata references (NuGet packages and assemblies)
            foreach (var reference in compilation.References)
            {
                if (reference is PortableExecutableReference peRef)
                {
                    var assemblySymbol = compilation.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol;
                    if (assemblySymbol != null && !IsSystemAssembly(assemblySymbol.Name))
                    {
                        var dep = dependencyMap.GetOrAdd(assemblySymbol.Name, _ => new DependencyUsageInfo
                        {
                            Version = assemblySymbol.Identity.Version.ToString(),
                            Type = "Assembly"
                        });

                        Interlocked.Increment(ref dep.Count);
                    }
                }
            }

            // Analyze project references
            foreach (var projectRef in project.ProjectReferences)
            {
                var referencedProject = project.Solution.GetProject(projectRef.ProjectId);
                if (referencedProject != null)
                {
                    var dep = dependencyMap.GetOrAdd(referencedProject.Name, _ => new DependencyUsageInfo
                    {
                        Version = "",
                        Type = "ProjectReference"
                    });

                    Interlocked.Increment(ref dep.Count);
                }
            }

            // Analyze namespace usages from syntax trees
            foreach (var syntaxTree in compilation.SyntaxTrees)
            {
                try
                {
                    var root = await syntaxTree.GetRootAsync();
                    var semanticModel = compilation.GetSemanticModel(syntaxTree);

                    // Find all type references
                    var typeNodes = root.DescendantNodes()
                        .Where(n => n is Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax ||
                                   n is Microsoft.CodeAnalysis.CSharp.Syntax.QualifiedNameSyntax);

                    foreach (var typeNode in typeNodes)
                    {
                        var symbolInfo = semanticModel.GetSymbolInfo(typeNode);
                        var symbol = symbolInfo.Symbol;

                        if (symbol is INamedTypeSymbol namedType)
                        {
                            var ns = namedType.ContainingNamespace?.ToDisplayString();
                            if (!string.IsNullOrEmpty(ns) && !IsSystemNamespace(ns))
                            {
                                var usageInfo = namespaceUsageMap.GetOrAdd(ns, _ => new NamespaceUsageInfo());
                                Interlocked.Increment(ref usageInfo.Count);
                                usageInfo.Types.Add(namedType.Name);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error analyzing syntax tree {FilePath}", syntaxTree.FilePath);
                }
            }

            return (totalCount, publicCount, internalCount);
        }

        private IEnumerable<ISymbol> GetAllSymbols(INamespaceSymbol namespaceSymbol)
        {
            foreach (var member in namespaceSymbol.GetMembers())
            {
                yield return member;

                if (member is INamespaceSymbol nestedNamespace)
                {
                    foreach (var nested in GetAllSymbols(nestedNamespace))
                        yield return nested;
                }
                else if (member is INamedTypeSymbol namedType)
                {
                    foreach (var typeMember in namedType.GetMembers())
                        yield return typeMember;
                }
            }
        }

        private bool IsSystemAssembly(string assemblyName)
        {
            var systemPrefixes = new[] { "System", "Microsoft", "mscorlib", "netstandard" };
            return systemPrefixes.Any(prefix => assemblyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsSystemNamespace(string namespaceName)
        {
            var systemPrefixes = new[] { "System", "Microsoft" };
            return systemPrefixes.Any(prefix => namespaceName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        public void Dispose()
        {
            _workspaceLock?.Dispose();

            foreach (var workspace in _workspaces.Values)
            {
                try
                {
                    workspace.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error disposing workspace");
                }
            }

            _workspaces.Clear();
        }

        private class DependencyUsageInfo
        {
            public int Count;
            public string Version = string.Empty;
            public string Type = string.Empty;
        }

        private class NamespaceUsageInfo
        {
            public int Count;
            public ConcurrentBag<string> Types = new ConcurrentBag<string>();
        }
    }
}
