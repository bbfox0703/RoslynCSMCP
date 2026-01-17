using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using NuGet.Common;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using RoslynMcpServer.Core.Models;
using System.Collections.Concurrent;
using System.Xml.Linq;

namespace RoslynMcpServer.Core.Services
{
    /// <summary>
    /// Service for analyzing NuGet packages in a solution
    /// </summary>
    public class PackageAnalysisService
    {
        private readonly ILogger<PackageAnalysisService> _logger;
        private readonly UnusedDependencyAnalyzer _unusedDependencyAnalyzer;

        // Common package to namespace mappings
        private static readonly Dictionary<string, string[]> PackageNamespaceMap = new()
        {
            ["Newtonsoft.Json"] = new[] { "Newtonsoft.Json" },
            ["Serilog"] = new[] { "Serilog" },
            ["AutoMapper"] = new[] { "AutoMapper" },
            ["Dapper"] = new[] { "Dapper" },
            ["FluentValidation"] = new[] { "FluentValidation" },
            ["MediatR"] = new[] { "MediatR" },
            ["Polly"] = new[] { "Polly" },
            ["NUnit"] = new[] { "NUnit.Framework" },
            ["xunit"] = new[] { "Xunit" },
            ["Moq"] = new[] { "Moq" }
        };

        public PackageAnalysisService(
            ILogger<PackageAnalysisService> logger,
            UnusedDependencyAnalyzer unusedDependencyAnalyzer)
        {
            _logger = logger;
            _unusedDependencyAnalyzer = unusedDependencyAnalyzer;
        }

        /// <summary>
        /// Analyzes all packages in a solution
        /// </summary>
        public async Task<PackageAnalysisResults> AnalyzePackagesAsync(
            string solutionPath,
            bool checkUpdates = true,
            bool checkVulnerabilities = true,
            bool analyzeUsage = true)
        {
            var results = new PackageAnalysisResults();

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

                // Collect all packages from all projects
                var allPackages = new ConcurrentBag<PackageInfo>();

                var projectTasks = solution.Projects
                    .Where(p => p.SupportsCompilation && p.FilePath != null)
                    .Select(async project =>
                    {
                        try
                        {
                            var packages = await ExtractPackagesFromProjectAsync(project, analyzeUsage);
                            foreach (var package in packages)
                            {
                                allPackages.Add(package);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to analyze packages for project: {ProjectName}", project.Name);
                            results.FailedProjects++;
                        }
                    });

                await Task.WhenAll(projectTasks);

                results.AllPackages = allPackages.ToList();

                // Analyze package updates
                if (checkUpdates)
                {
                    _logger.LogInformation("Checking for package updates...");
                    results.AvailableUpdates = await CheckForUpdatesAsync(results.AllPackages);
                }

                // Detect version conflicts
                results.VersionConflicts = DetectVersionConflicts(results.AllPackages);

                // Identify unused packages
                if (analyzeUsage)
                {
                    results.UnusedPackages = results.AllPackages.Where(p => !p.IsUsed).ToList();
                }

                // Check for vulnerabilities (placeholder - requires external API)
                if (checkVulnerabilities)
                {
                    // Note: Vulnerability checking requires NuGet Audit API or similar
                    // This is a placeholder for future implementation
                    results.Warnings.Add(new OperationWarning
                    {
                        Context = "Vulnerability Check",
                        Message = "Vulnerability checking is not yet implemented. Consider using 'dotnet list package --vulnerable' manually."
                    });
                }

                _logger.LogInformation(
                    "Package analysis complete: {PackageCount} total packages, {UpdateCount} updates available, {ConflictCount} conflicts",
                    results.TotalPackages,
                    results.AvailableUpdates.Count,
                    results.VersionConflicts.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing packages");
                results.Warnings.Add(new OperationWarning
                {
                    Context = "Analysis",
                    Message = $"Error: {ex.Message}"
                });
            }

            return results;
        }

        /// <summary>
        /// Extracts package references from a project file
        /// </summary>
        private async Task<List<PackageInfo>> ExtractPackagesFromProjectAsync(Project project, bool analyzeUsage)
        {
            var packages = new List<PackageInfo>();

            try
            {
                if (project.FilePath == null || !File.Exists(project.FilePath))
                    return packages;

                // Parse project file
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

                // Get using directives if analyzing usage
                HashSet<string> usedNamespaces = new();
                if (analyzeUsage)
                {
                    var compilation = await project.GetCompilationAsync();
                    if (compilation != null)
                    {
                        foreach (var syntaxTree in compilation.SyntaxTrees)
                        {
                            var root = await syntaxTree.GetRootAsync();
                            var usings = root.DescendantNodes()
                                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax>()
                                .Select(u => u.Name?.ToString() ?? string.Empty)
                                .Where(n => !string.IsNullOrWhiteSpace(n));

                            foreach (var u in usings)
                            {
                                usedNamespaces.Add(u);
                            }
                        }
                    }
                }

                // Create PackageInfo objects
                foreach (var pkgRef in packageReferences)
                {
                    var expectedNamespaces = GetExpectedNamespaces(pkgRef.Name);
                    var usedNs = expectedNamespaces.Where(ns => usedNamespaces.Contains(ns)).ToList();
                    var isUsed = !analyzeUsage || usedNs.Any() || IsAlwaysUsedPackage(pkgRef.Name);

                    packages.Add(new PackageInfo
                    {
                        Name = pkgRef.Name,
                        Version = pkgRef.Version,
                        ProjectName = project.Name,
                        ProjectPath = project.FilePath,
                        IsUsed = isUsed,
                        UsedNamespaces = usedNs,
                        ExpectedNamespaces = expectedNamespaces.ToList()
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract packages from project: {ProjectName}", project.Name);
            }

            return packages;
        }

        /// <summary>
        /// Checks for available package updates
        /// </summary>
        private async Task<List<PackageUpdate>> CheckForUpdatesAsync(List<PackageInfo> packages)
        {
            var updates = new List<PackageUpdate>();

            try
            {
                // Group packages by name
                var packageGroups = packages.GroupBy(p => p.Name);

                // Setup NuGet API
                var cache = new SourceCacheContext();
                var repository = Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json");
                var resource = await repository.GetResourceAsync<FindPackageByIdResource>();

                foreach (var group in packageGroups)
                {
                    try
                    {
                        var packageName = group.Key;
                        var currentVersions = group.Select(p => p.Version).Distinct().ToList();

                        // Get latest version from NuGet
                        var versions = await resource.GetAllVersionsAsync(
                            packageName,
                            cache,
                            NullLogger.Instance,
                            CancellationToken.None);

                        if (versions == null || !versions.Any())
                            continue;

                        var latestVersion = versions
                            .Where(v => !v.IsPrerelease)
                            .OrderByDescending(v => v)
                            .FirstOrDefault();

                        if (latestVersion == null)
                            continue;

                        // Check each current version against latest
                        foreach (var currentVersionStr in currentVersions)
                        {
                            if (NuGetVersion.TryParse(currentVersionStr, out var currentVersion))
                            {
                                if (latestVersion > currentVersion)
                                {
                                    var affectedProjects = group
                                        .Where(p => p.Version == currentVersionStr)
                                        .Select(p => p.ProjectName)
                                        .Distinct()
                                        .ToList();

                                    updates.Add(new PackageUpdate
                                    {
                                        PackageName = packageName,
                                        CurrentVersion = currentVersion.ToString(),
                                        LatestVersion = latestVersion.ToString(),
                                        MajorVersionsAhead = latestVersion.Major - currentVersion.Major,
                                        MinorVersionsAhead = latestVersion.Minor - currentVersion.Minor,
                                        PatchVersionsAhead = latestVersion.Patch - currentVersion.Patch,
                                        AffectedProjects = affectedProjects,
                                        IsBreakingChange = latestVersion.Major > currentVersion.Major
                                    });
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to check updates for package: {PackageName}", group.Key);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking for package updates");
            }

            return updates;
        }

        /// <summary>
        /// Detects version conflicts across projects
        /// </summary>
        private List<PackageConflict> DetectVersionConflicts(List<PackageInfo> packages)
        {
            var conflicts = new List<PackageConflict>();

            var packageGroups = packages.GroupBy(p => p.Name);

            foreach (var group in packageGroups)
            {
                var distinctVersions = group.Select(p => p.Version).Distinct().ToList();

                if (distinctVersions.Count > 1)
                {
                    var versionUsages = group
                        .GroupBy(p => p.Version)
                        .Select(g => new PackageVersionUsage
                        {
                            Version = g.Key,
                            ProjectName = string.Join(", ", g.Select(p => p.ProjectName).Distinct())
                        })
                        .ToList();

                    // Recommend the highest version
                    var recommendedVersion = distinctVersions
                        .Select(v => NuGetVersion.TryParse(v, out var nv) ? nv : null)
                        .Where(v => v != null)
                        .OrderByDescending(v => v)
                        .FirstOrDefault()?.ToString() ?? distinctVersions.First();

                    conflicts.Add(new PackageConflict
                    {
                        PackageName = group.Key,
                        VersionUsages = versionUsages,
                        RecommendedVersion = recommendedVersion
                    });
                }
            }

            return conflicts;
        }

        /// <summary>
        /// Gets expected namespaces for a package
        /// </summary>
        private string[] GetExpectedNamespaces(string packageName)
        {
            if (PackageNamespaceMap.TryGetValue(packageName, out var namespaces))
            {
                return namespaces;
            }

            // Default: assume namespace matches package name
            return new[] { packageName.Replace(".", ".") };
        }

        /// <summary>
        /// Checks if a package is always considered used (e.g., build tools, analyzers)
        /// </summary>
        private bool IsAlwaysUsedPackage(string packageName)
        {
            // Packages that don't have direct namespace usage but are still used
            var alwaysUsedPackages = new[]
            {
                "Microsoft.NET.Test.Sdk",
                "coverlet.collector",
                "Microsoft.CodeAnalysis.NetAnalyzers",
                "StyleCop.Analyzers",
                "SonarAnalyzer.CSharp"
            };

            return alwaysUsedPackages.Any(p => packageName.Contains(p, StringComparison.OrdinalIgnoreCase));
        }
    }
}
