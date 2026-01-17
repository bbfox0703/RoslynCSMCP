using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Core.Models;
using System.Collections.Concurrent;
using System.Xml.Linq;

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

                // Process projects in parallel
                var unusedDependencies = new ConcurrentBag<UnusedDependency>();

                var projectTasks = solution.Projects
                    .Where(p => p.SupportsCompilation)
                    .Select(async project =>
                    {
                        try
                        {
                            var projectUnused = new List<UnusedDependency>();

                            // Analyze NuGet packages
                            if (includeNuGetPackages)
                            {
                                var packageDeps = await AnalyzeNuGetPackagesAsync(project);
                                projectUnused.AddRange(packageDeps);
                            }

                            // Analyze project references
                            if (includeProjectReferences)
                            {
                                var projectDeps = await AnalyzeProjectReferencesAsync(project, solution);
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
                            results.FailedProjects++;
                        }
                    });

                await Task.WhenAll(projectTasks);

                results.UnusedDependencies = unusedDependencies.ToList();
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
        /// Analyzes NuGet package references for a project
        /// </summary>
        private async Task<List<UnusedDependency>> AnalyzeNuGetPackagesAsync(Project project)
        {
            var unusedPackages = new List<UnusedDependency>();

            try
            {
                // Read project file to get PackageReference items
                if (project.FilePath == null || !File.Exists(project.FilePath))
                    return unusedPackages;

                var projectXml = await File.ReadAllTextAsync(project.FilePath);
                var doc = XDocument.Parse(projectXml);

                var packageReferences = doc.Descendants("PackageReference")
                    .Select(pr => new
                    {
                        Name = pr.Attribute("Include")?.Value ?? string.Empty,
                        Version = pr.Attribute("Version")?.Value ?? pr.Element("Version")?.Value ?? string.Empty
                    })
                    .Where(pr => !string.IsNullOrWhiteSpace(pr.Name))
                    .ToList();

                if (!packageReferences.Any())
                    return unusedPackages;

                // Get all using directives in the project
                var compilation = await project.GetCompilationAsync();
                if (compilation == null)
                    return unusedPackages;

                var allUsings = new HashSet<string>();
                foreach (var syntaxTree in compilation.SyntaxTrees)
                {
                    var root = await syntaxTree.GetRootAsync();
                    var usingDirectives = root.DescendantNodes()
                        .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax>()
                        .Select(u => u.Name?.ToString() ?? string.Empty)
                        .Where(n => !string.IsNullOrWhiteSpace(n));

                    foreach (var usingDir in usingDirectives)
                    {
                        allUsings.Add(usingDir);
                    }
                }

                // Check each package
                foreach (var package in packageReferences)
                {
                    // Map common package names to their namespaces
                    var expectedNamespaces = GetExpectedNamespaces(package.Name);

                    // Check if any expected namespace is used
                    bool isUsed = expectedNamespaces.Any(ns =>
                        allUsings.Any(u => u.StartsWith(ns, StringComparison.OrdinalIgnoreCase)));

                    if (!isUsed)
                    {
                        unusedPackages.Add(new UnusedDependency
                        {
                            Name = package.Name,
                            Version = package.Version,
                            Type = "NuGetPackage",
                            ProjectName = project.Name,
                            ProjectPath = project.FilePath,
                            Reason = "No using directives found for expected namespaces",
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
        /// Analyzes project references for a project
        /// </summary>
        private async Task<List<UnusedDependency>> AnalyzeProjectReferencesAsync(Project project, Solution solution)
        {
            var unusedReferences = new List<UnusedDependency>();

            try
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation == null)
                    return unusedReferences;

                // Get all referenced projects
                var referencedProjects = project.ProjectReferences
                    .Select(pr => solution.GetProject(pr.ProjectId))
                    .Where(p => p != null)
                    .ToList();

                foreach (var referencedProject in referencedProjects)
                {
                    if (referencedProject == null)
                        continue;

                    // Check if any types from the referenced project are used
                    var referencedCompilation = await referencedProject.GetCompilationAsync();
                    if (referencedCompilation == null)
                        continue;

                    var referencedTypes = referencedCompilation.Assembly.GlobalNamespace
                        .GetNamespaceMembers()
                        .SelectMany(ns => GetAllTypes(ns))
                        .Select(t => t.ToDisplayString())
                        .ToHashSet();

                    // Check if any referenced type is used in the project
                    bool isUsed = false;
                    foreach (var syntaxTree in compilation.SyntaxTrees)
                    {
                        var semanticModel = compilation.GetSemanticModel(syntaxTree);
                        var root = await syntaxTree.GetRootAsync();

                        var identifiers = root.DescendantNodes()
                            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax>();

                        foreach (var identifier in identifiers)
                        {
                            var symbolInfo = semanticModel.GetSymbolInfo(identifier);
                            var symbol = symbolInfo.Symbol;

                            if (symbol != null)
                            {
                                var containingAssembly = symbol.ContainingAssembly;
                                if (containingAssembly != null &&
                                    containingAssembly.Name == referencedCompilation.AssemblyName)
                                {
                                    isUsed = true;
                                    break;
                                }
                            }
                        }

                        if (isUsed)
                            break;
                    }

                    if (!isUsed)
                    {
                        unusedReferences.Add(new UnusedDependency
                        {
                            Name = referencedProject.Name,
                            Version = string.Empty,
                            Type = "ProjectReference",
                            ProjectName = project.Name,
                            ProjectPath = project.FilePath ?? string.Empty,
                            Reason = "No types from this project are used",
                            ExpectedNamespaces = new List<string>()
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to analyze project references for: {ProjectName}", project.Name);
            }

            return unusedReferences;
        }

        /// <summary>
        /// Gets all types from a namespace recursively
        /// </summary>
        private IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceSymbol ns)
        {
            foreach (var type in ns.GetTypeMembers())
            {
                yield return type;
            }

            foreach (var childNs in ns.GetNamespaceMembers())
            {
                foreach (var type in GetAllTypes(childNs))
                {
                    yield return type;
                }
            }
        }

        /// <summary>
        /// Maps package names to their expected namespaces
        /// </summary>
        private List<string> GetExpectedNamespaces(string packageName)
        {
            // Common package name to namespace mappings
            var mappings = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                { "Newtonsoft.Json", new List<string> { "Newtonsoft.Json" } },
                { "Microsoft.Extensions.Logging", new List<string> { "Microsoft.Extensions.Logging" } },
                { "Microsoft.Extensions.DependencyInjection", new List<string> { "Microsoft.Extensions.DependencyInjection" } },
                { "Microsoft.EntityFrameworkCore", new List<string> { "Microsoft.EntityFrameworkCore" } },
                { "AutoMapper", new List<string> { "AutoMapper" } },
                { "Serilog", new List<string> { "Serilog" } },
                { "FluentValidation", new List<string> { "FluentValidation" } },
                { "MediatR", new List<string> { "MediatR" } },
                { "Dapper", new List<string> { "Dapper" } },
                { "NUnit", new List<string> { "NUnit.Framework" } },
                { "xUnit", new List<string> { "Xunit" } },
                { "Moq", new List<string> { "Moq" } },
                { "System.Text.Json", new List<string> { "System.Text.Json" } }
            };

            if (mappings.TryGetValue(packageName, out var namespaces))
            {
                return namespaces;
            }

            // Default: assume package name is the namespace
            // Split by dots and try various combinations
            var parts = packageName.Split('.');
            var expectedNamespaces = new List<string> { packageName };

            // For packages like "Microsoft.Extensions.Logging.Abstractions",
            // also check "Microsoft.Extensions.Logging"
            if (parts.Length > 2)
            {
                expectedNamespaces.Add(string.Join(".", parts.Take(parts.Length - 1)));
            }

            return expectedNamespaces;
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
