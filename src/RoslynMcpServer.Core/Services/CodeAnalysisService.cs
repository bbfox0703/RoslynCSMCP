using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Core.Models;
using System.Collections.Concurrent;

namespace RoslynMcpServer.Core.Services
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
                var workspace = _workspaces.GetOrAdd(solutionPath, _ =>
                {
                    var ws = MSBuildWorkspace.Create();

                    // Handle workspace failures (Roslyn 5.0+ API)
                    ws.RegisterWorkspaceFailedHandler(args =>
                    {
                        if (args.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
                        {
                            _logger.LogWarning("Workspace failure: {Message}", args.Diagnostic.Message);
                        }
                    });

                    return ws;
                });

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
            var analyzedProjects = 0;
            var failedProjects = 0;

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
                        Interlocked.Increment(ref analyzedProjects);
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref failedProjects);
                        _logger.LogError(ex, "Error analyzing project {ProjectName}", project.Name);
                        analysis.Warnings.Add(new OperationWarning
                        {
                            Context = $"Project: {project.Name}",
                            Message = $"Failed to analyze project: {ex.Message}",
                            Details = null
                        });
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

            // Set project analysis counts
            analysis.AnalyzedProjects = analyzedProjects;
            analysis.FailedProjects = failedProjects;

            if (failedProjects > 0)
            {
                _logger.LogWarning("{FailedProjects} projects failed to analyze out of {TotalProjects}",
                    failedProjects, analyzedProjects + failedProjects);
            }

            // Detect circular dependencies
            analysis.CircularDependencies = DetectCircularDependencies(solution);

            if (analysis.HasCircularDependencies)
            {
                _logger.LogWarning("Detected {Count} circular dependencies in solution",
                    analysis.CircularDependencyCount);
            }

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

        /// <summary>
        /// Detects circular dependencies in the solution using Tarjan's algorithm
        /// </summary>
        private List<CircularDependency> DetectCircularDependencies(Solution solution)
        {
            var circularDependencies = new List<CircularDependency>();

            // Build dependency graph
            var graph = BuildProjectDependencyGraph(solution);

            if (graph.Count == 0)
            {
                return circularDependencies; // No projects or no dependencies
            }

            // Use Tarjan's algorithm to find strongly connected components
            var stronglyConnectedComponents = FindStronglyConnectedComponents(graph);

            // Filter out single-node components (not circular)
            foreach (var scc in stronglyConnectedComponents.Where(scc => scc.Count > 1))
            {
                // This is a circular dependency
                var cycle = new CircularDependency
                {
                    ProjectChain = scc,
                    CycleType = scc.Count == 2 ? "Direct" : "Indirect",
                    Description = $"Circular dependency detected: {string.Join(" → ", scc)} → {scc[0]}"
                };

                circularDependencies.Add(cycle);
            }

            // Also check for direct bidirectional dependencies (A → B and B → A)
            foreach (var kvp in graph)
            {
                var projectA = kvp.Key;
                var dependenciesOfA = kvp.Value;

                foreach (var projectB in dependenciesOfA)
                {
                    if (graph.ContainsKey(projectB) && graph[projectB].Contains(projectA))
                    {
                        // Found direct circular dependency
                        // Make sure we don't add duplicates
                        var existingCycle = circularDependencies.FirstOrDefault(c =>
                            c.ProjectChain.Count == 2 &&
                            c.ProjectChain.Contains(projectA) &&
                            c.ProjectChain.Contains(projectB));

                        if (existingCycle == null)
                        {
                            circularDependencies.Add(new CircularDependency
                            {
                                ProjectChain = new List<string> { projectA, projectB },
                                CycleType = "Direct",
                                Description = $"Direct circular dependency: {projectA} ↔ {projectB}"
                            });
                        }
                    }
                }
            }

            return circularDependencies.Distinct().ToList();
        }

        /// <summary>
        /// Builds a project dependency graph
        /// </summary>
        private Dictionary<string, List<string>> BuildProjectDependencyGraph(Solution solution)
        {
            var graph = new Dictionary<string, List<string>>();

            foreach (var project in solution.Projects)
            {
                var projectName = project.Name;

                if (!graph.ContainsKey(projectName))
                {
                    graph[projectName] = new List<string>();
                }

                // Add project references
                foreach (var projectRef in project.ProjectReferences)
                {
                    var referencedProject = solution.GetProject(projectRef.ProjectId);
                    if (referencedProject != null)
                    {
                        graph[projectName].Add(referencedProject.Name);
                    }
                }
            }

            return graph;
        }

        /// <summary>
        /// Finds strongly connected components using Tarjan's algorithm
        /// </summary>
        private List<List<string>> FindStronglyConnectedComponents(Dictionary<string, List<string>> graph)
        {
            var index = 0;
            var stack = new Stack<string>();
            var indices = new Dictionary<string, int>();
            var lowLinks = new Dictionary<string, int>();
            var onStack = new HashSet<string>();
            var sccs = new List<List<string>>();

            void StrongConnect(string node)
            {
                indices[node] = index;
                lowLinks[node] = index;
                index++;
                stack.Push(node);
                onStack.Add(node);

                if (graph.ContainsKey(node))
                {
                    foreach (var neighbor in graph[node])
                    {
                        if (!indices.ContainsKey(neighbor))
                        {
                            // Neighbor has not been visited
                            StrongConnect(neighbor);
                            lowLinks[node] = Math.Min(lowLinks[node], lowLinks[neighbor]);
                        }
                        else if (onStack.Contains(neighbor))
                        {
                            // Neighbor is on stack, part of current SCC
                            lowLinks[node] = Math.Min(lowLinks[node], indices[neighbor]);
                        }
                    }
                }

                // If node is a root node, pop the stack and generate an SCC
                if (lowLinks[node] == indices[node])
                {
                    var scc = new List<string>();
                    string w;
                    do
                    {
                        w = stack.Pop();
                        onStack.Remove(w);
                        scc.Add(w);
                    } while (w != node);

                    sccs.Add(scc);
                }
            }

            // Visit all nodes
            foreach (var node in graph.Keys)
            {
                if (!indices.ContainsKey(node))
                {
                    StrongConnect(node);
                }
            }

            return sccs;
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
