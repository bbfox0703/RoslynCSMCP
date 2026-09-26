using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Core.Models;
using System.Collections.Concurrent;

namespace RoslynMcpServer.Core.Services
{
    /// <summary>
    /// Service for analyzing unused dependencies (NuGet packages and project references)
    /// </summary>
    public class UnusedDependencyAnalyzer
    {
        private readonly ILogger<UnusedDependencyAnalyzer> _logger;

        public UnusedDependencyAnalyzer(ILogger<UnusedDependencyAnalyzer> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Analyzes solution for unused dependencies
        /// </summary>
        public async Task<UnusedDependencyResults> AnalyzeUnusedDependenciesAsync(
            string solutionPath,
            bool includeNuGetPackages = true,
            bool includeProjectReferences = true)
        {
            var results = new UnusedDependencyResults();

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

                // Process projects in parallel. A multi-targeted project appears once per target
                // framework; it is judged once, over all of its target frameworks.
                var unusedDependencies = new ConcurrentBag<UnusedDependency>();
                int failedProjects = 0;

                var projectTasks = solution.Projects
                    .Where(p => p.SupportsCompilation)
                    .GroupBy(p => p.FilePath ?? p.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(async projectGroup =>
                    {
                        var targetFrameworkProjects = projectGroup.ToList();
                        var project = targetFrameworkProjects[0];
                        try
                        {
                            var projectUnused = new List<UnusedDependency>();

                            // Analyze NuGet packages
                            if (includeNuGetPackages)
                            {
                                var packageDeps = await AnalyzeNuGetPackagesAsync(project, targetFrameworkProjects);
                                projectUnused.AddRange(packageDeps);
                            }

                            // Analyze project references
                            if (includeProjectReferences)
                            {
                                var projectDeps = await AnalyzeProjectReferencesAsync(targetFrameworkProjects, solution);
                                projectUnused.AddRange(projectDeps);
                            }

                            foreach (var dep in projectUnused)
                            {
                                unusedDependencies.Add(dep);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to analyze project: {ProjectName}", project.Name);
                            Interlocked.Increment(ref failedProjects);
                        }
                    });

                await Task.WhenAll(projectTasks);

                results.FailedProjects = failedProjects;
                results.UnusedDependencies = unusedDependencies
                    .OrderBy(d => d.ProjectName, StringComparer.Ordinal)
                    .ThenBy(d => d.Type, StringComparer.Ordinal)
                    .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                CalculateStatistics(results);

                _logger.LogInformation(
                    "Dependency analysis complete: {UnusedCount} unused dependencies found in {ProjectCount} projects",
                    results.UnusedDependencies.Count,
                    results.AnalyzedProjects);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing dependencies");
                results.Warnings.Add(new OperationWarning
                {
                    Context = "Analysis",
                    Message = $"Error: {ex.Message}"
                });
            }

            return results;
        }

        /// <summary>
        /// Analyzes NuGet package references for a project file, using the same heuristic as
        /// AnalyzePackages (see <see cref="PackageUsageHeuristics"/>).
        /// </summary>
        private async Task<List<UnusedDependency>> AnalyzeNuGetPackagesAsync(Project project, IReadOnlyList<Project> targetFrameworkProjects)
        {
            var unusedPackages = new List<UnusedDependency>();

            try
            {
                // Read project file to get PackageReference items
                if (project.FilePath == null || !File.Exists(project.FilePath))
                    return unusedPackages;

                var packageReferences = PackageUsageHeuristics.ReadPackageReferences(project.FilePath);
                if (!packageReferences.Any())
                    return unusedPackages;

                // Get all using directives (including global usings) in every target framework
                var importedNamespaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var targetProject in targetFrameworkProjects)
                {
                    var compilation = await targetProject.GetCompilationAsync();
                    if (compilation != null)
                        importedNamespaces.UnionWith(PackageUsageHeuristics.CollectImportedNamespaces(compilation.SyntaxTrees));
                }

                // Check each package
                foreach (var package in packageReferences)
                {
                    var (isUsed, expectedNamespaces, _) = PackageUsageHeuristics.Evaluate(package, importedNamespaces);

                    if (!isUsed)
                    {
                        unusedPackages.Add(new UnusedDependency
                        {
                            Name = package.Name,
                            Version = package.Version,
                            Type = "NuGetPackage",
                            ProjectName = GetProjectDisplayName(project),
                            ProjectPath = project.FilePath,
                            Reason = "No using directive (including global usings) imports an expected namespace",
                            ExpectedNamespaces = expectedNamespaces
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to analyze NuGet packages for project: {ProjectName}", project.Name);
            }

            return unusedPackages;
        }

        /// <summary>
        /// Project name without the "(net8.0)" suffix MSBuildWorkspace adds for multi-targeting.
        /// </summary>
        private static string GetProjectDisplayName(Project project)
        {
            return project.FilePath != null
                ? Path.GetFileNameWithoutExtension(project.FilePath)
                : project.Name;
        }

        /// <summary>
        /// Analyzes project references for a project file. A reference is reported only when no
        /// target framework of the project uses it.
        /// </summary>
        private async Task<List<UnusedDependency>> AnalyzeProjectReferencesAsync(IReadOnlyList<Project> targetFrameworkProjects, Solution solution)
        {
            var unusedReferences = new List<UnusedDependency>();
            var project = targetFrameworkProjects[0];

            try
            {
                Dictionary<string, Project>? unusedInAll = null;

                foreach (var targetProject in targetFrameworkProjects)
                {
                    var unused = await FindUnusedProjectReferencesAsync(targetProject, solution);
                    if (unusedInAll == null)
                    {
                        unusedInAll = unused;
                    }
                    else
                    {
                        foreach (var key in unusedInAll.Keys.Except(unused.Keys).ToList())
                            unusedInAll.Remove(key);
                    }
                }

                foreach (var referencedProject in (unusedInAll ?? new Dictionary<string, Project>()).Values)
                {
                    unusedReferences.Add(new UnusedDependency
                    {
                        Name = GetProjectDisplayName(referencedProject),
                        Version = string.Empty,
                        Type = "ProjectReference",
                        ProjectName = GetProjectDisplayName(project),
                        ProjectPath = project.FilePath ?? string.Empty,
                        Reason = "No types from this project are used",
                        ExpectedNamespaces = new List<string>()
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to analyze project references for: {ProjectName}", project.Name);
            }

            return unusedReferences;
        }

        /// <summary>
        /// Referenced projects (keyed by project file) that no identifier in this compilation binds to.
        /// </summary>
        private async Task<Dictionary<string, Project>> FindUnusedProjectReferencesAsync(Project project, Solution solution)
        {
            var unused = new Dictionary<string, Project>(StringComparer.OrdinalIgnoreCase);

            var compilation = await project.GetCompilationAsync();
            if (compilation == null)
                return unused;

            // Get all referenced projects
            var referencedProjects = project.ProjectReferences
                .Select(pr => solution.GetProject(pr.ProjectId))
                .Where(p => p != null)
                .Cast<Project>()
                .ToList();

            foreach (var referencedProject in referencedProjects)
            {
                // Check if any types from the referenced project are used
                var referencedCompilation = await referencedProject.GetCompilationAsync();
                if (referencedCompilation == null)
                    continue;

                // Check if any referenced symbol is used in the project
                bool isUsed = false;
                foreach (var syntaxTree in compilation.SyntaxTrees)
                {
                    var semanticModel = compilation.GetSemanticModel(syntaxTree);
                    var root = await syntaxTree.GetRootAsync();

                    var identifiers = root.DescendantNodes()
                        .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax>();

                    foreach (var identifier in identifiers)
                    {
                        var symbol = semanticModel.GetSymbolInfo(identifier).Symbol;
                        var containingAssembly = symbol?.ContainingAssembly;
                        if (containingAssembly != null &&
                            containingAssembly.Name == referencedCompilation.AssemblyName)
                        {
                            isUsed = true;
                            break;
                        }
                    }

                    if (isUsed)
                        break;
                }

                if (!isUsed)
                    unused[referencedProject.FilePath ?? referencedProject.Name] = referencedProject;
            }

            return unused;
        }

        /// <summary>
        /// Calculates statistics for the results
        /// </summary>
        private void CalculateStatistics(UnusedDependencyResults results)
        {
            results.UnusedNuGetPackages = results.UnusedDependencies
                .Count(d => d.Type == "NuGetPackage");

            results.UnusedProjectReferences = results.UnusedDependencies
                .Count(d => d.Type == "ProjectReference");
        }
    }
}
