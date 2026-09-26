using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using NuGet.Common;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using RoslynMcpServer.Core.Models;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace RoslynMcpServer.Core.Services
{
    /// <summary>
    /// Service for analyzing NuGet packages in a solution
    /// </summary>
    public class PackageAnalysisService
    {
        private static readonly TimeSpan VulnerabilityCheckTimeout = TimeSpan.FromMinutes(5);

        private readonly ILogger<PackageAnalysisService> _logger;
        private readonly UnusedDependencyAnalyzer _unusedDependencyAnalyzer;

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
            bool analyzeUsage = true,
            bool checkConflicts = true)
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

                // Collect all packages from all projects. A multi-targeted project appears once per
                // target framework but has one project file, so each file is read once.
                var allPackages = new ConcurrentBag<PackageInfo>();
                int failedProjects = 0;

                var projectTasks = solution.Projects
                    .Where(p => p.SupportsCompilation && p.FilePath != null)
                    .GroupBy(p => p.FilePath!, StringComparer.OrdinalIgnoreCase)
                    .Select(async projectGroup =>
                    {
                        var project = projectGroup.First();
                        try
                        {
                            var packages = await ExtractPackagesFromProjectAsync(project, projectGroup.ToList(), analyzeUsage);
                            foreach (var package in packages)
                            {
                                allPackages.Add(package);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to analyze packages for project: {ProjectName}", project.Name);
                            Interlocked.Increment(ref failedProjects);
                        }
                    });

                await Task.WhenAll(projectTasks);

                results.FailedProjects = failedProjects;
                results.AllPackages = allPackages
                    .OrderBy(p => p.ProjectName, StringComparer.Ordinal)
                    .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // Analyze package updates
                if (checkUpdates)
                {
                    _logger.LogInformation("Checking for package updates...");
                    results.AvailableUpdates = await CheckForUpdatesAsync(results.AllPackages);
                }

                // Detect version conflicts
                if (checkConflicts)
                {
                    results.VersionConflicts = DetectVersionConflicts(results.AllPackages);
                }

                // Identify unused packages
                if (analyzeUsage)
                {
                    results.UnusedPackages = results.AllPackages.Where(p => !p.IsUsed).ToList();
                }

                // Check for known vulnerabilities with the .NET SDK
                if (checkVulnerabilities)
                {
                    results.Vulnerabilities = await CheckVulnerabilitiesAsync(solutionPath, results.Warnings);
                }

                _logger.LogInformation(
                    "Package analysis complete: {PackageCount} total packages, {UpdateCount} updates available, {ConflictCount} conflicts, {VulnerabilityCount} vulnerabilities",
                    results.TotalPackages,
                    results.AvailableUpdates.Count,
                    results.VersionConflicts.Count,
                    results.Vulnerabilities.Count);
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
        /// Extracts package references from a project file. Usage is judged against the using
        /// directives of every target framework's compilation of that project.
        /// </summary>
        private async Task<List<PackageInfo>> ExtractPackagesFromProjectAsync(
            Project project,
            IReadOnlyList<Project> targetFrameworkProjects,
            bool analyzeUsage)
        {
            var packages = new List<PackageInfo>();

            try
            {
                if (project.FilePath == null || !File.Exists(project.FilePath))
                    return packages;

                var packageReferences = PackageUsageHeuristics.ReadPackageReferences(project.FilePath);

                // Get using directives if analyzing usage
                var importedNamespaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (analyzeUsage && packageReferences.Count > 0)
                {
                    foreach (var targetProject in targetFrameworkProjects)
                    {
                        var compilation = await targetProject.GetCompilationAsync();
                        if (compilation != null)
                            importedNamespaces.UnionWith(PackageUsageHeuristics.CollectImportedNamespaces(compilation.SyntaxTrees));
                    }
                }

                var projectName = GetProjectDisplayName(project);

                // Create PackageInfo objects
                foreach (var pkgRef in packageReferences)
                {
                    var (isUsed, expectedNamespaces, usedNamespaces) = PackageUsageHeuristics.Evaluate(pkgRef, importedNamespaces);

                    packages.Add(new PackageInfo
                    {
                        Name = pkgRef.Name,
                        Version = pkgRef.Version,
                        ProjectName = projectName,
                        ProjectPath = project.FilePath,
                        IsUsed = !analyzeUsage || isUsed,
                        UsedNamespaces = usedNamespaces,
                        ExpectedNamespaces = expectedNamespaces
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
        /// Project name without the "(net8.0)" suffix MSBuildWorkspace adds for multi-targeting.
        /// </summary>
        private static string GetProjectDisplayName(Project project)
        {
            return project.FilePath != null
                ? Path.GetFileNameWithoutExtension(project.FilePath)
                : project.Name;
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
                var packageGroups = packages.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase);

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
        internal static List<PackageConflict> DetectVersionConflicts(List<PackageInfo> packages)
        {
            var conflicts = new List<PackageConflict>();

            var packageGroups = packages.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase);

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
        /// Runs "dotnet list &lt;solution&gt; package --vulnerable --include-transitive --format json --no-restore".
        /// The SDK queries the vulnerability data of the configured NuGet sources (network access), and
        /// the solution must already be restored. Failures become warnings, never exceptions.
        /// </summary>
        private async Task<List<PackageVulnerability>> CheckVulnerabilitiesAsync(string solutionPath, List<OperationWarning> warnings)
        {
            const string context = "Vulnerability Check";

            var startInfo = new ProcessStartInfo(GetDotnetExecutable())
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(solutionPath)) ?? Environment.CurrentDirectory
            };
            foreach (var argument in new[] { "list", solutionPath, "package", "--vulnerable", "--include-transitive", "--format", "json", "--no-restore" })
                startInfo.ArgumentList.Add(argument);

            startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
            startInfo.Environment["DOTNET_NOLOGO"] = "1";
            startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";

            // MSBuildLocator points this process at one SDK's MSBuild; let the child pick its own.
            foreach (var variable in new[] { "MSBUILD_EXE_PATH", "MSBuildExtensionsPath", "MSBuildSDKsPath" })
                startInfo.Environment.Remove(variable);

            try
            {
                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    warnings.Add(new OperationWarning { Context = context, Message = "Could not start 'dotnet list package --vulnerable'." });
                    return new List<PackageVulnerability>();
                }

                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();

                using var timeout = new CancellationTokenSource(VulnerabilityCheckTimeout);
                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    try { process.Kill(entireProcessTree: true); } catch { /* already exited */ }
                    warnings.Add(new OperationWarning
                    {
                        Context = context,
                        Message = $"'dotnet list package --vulnerable' did not finish within {VulnerabilityCheckTimeout.TotalMinutes:0} minutes and was stopped."
                    });
                    return new List<PackageVulnerability>();
                }

                var stdout = await stdoutTask;
                var stderr = await stderrTask;

                try
                {
                    return ParseVulnerabilityReport(stdout, warnings);
                }
                catch (JsonException)
                {
                    var detail = FirstNonEmptyLine(stderr) ?? FirstNonEmptyLine(stdout) ?? $"exit code {process.ExitCode}";
                    warnings.Add(new OperationWarning
                    {
                        Context = context,
                        Message = $"'dotnet list package --vulnerable' did not return a report: {detail}"
                    });
                    return new List<PackageVulnerability>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Vulnerability check failed");
                warnings.Add(new OperationWarning
                {
                    Context = context,
                    Message = $"Could not run 'dotnet list package --vulnerable': {ex.Message}"
                });
                return new List<PackageVulnerability>();
            }
        }

        private static string GetDotnetExecutable()
        {
            // When the server itself runs under the dotnet host, reuse that host.
            var processPath = Environment.ProcessPath;
            if (processPath != null &&
                Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                return processPath;
            }

            return "dotnet";
        }

        private static string? FirstNonEmptyLine(string text)
        {
            return text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        }

        /// <summary>
        /// Parses the JSON report of "dotnet list package --vulnerable --format json" into one entry per
        /// package, resolved version, and advisory, with the projects that reference it. "problems"
        /// entries (for example a project that has not been restored) become warnings.
        /// </summary>
        internal static List<PackageVulnerability> ParseVulnerabilityReport(string json, List<OperationWarning> warnings)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            AddProblems(root, warnings);

            var byKey = new Dictionary<(string Package, string Version, string Advisory), PackageVulnerability>();

            if (root.TryGetProperty("projects", out var projects) && projects.ValueKind == JsonValueKind.Array)
            {
                foreach (var project in projects.EnumerateArray())
                {
                    AddProblems(project, warnings);

                    var projectPath = GetString(project, "path");
                    var projectName = string.IsNullOrEmpty(projectPath) ? "(unknown project)" : Path.GetFileNameWithoutExtension(projectPath);

                    if (!project.TryGetProperty("frameworks", out var frameworks) || frameworks.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var framework in frameworks.EnumerateArray())
                    {
                        foreach (var (listName, isTransitive) in new[] { ("topLevelPackages", false), ("transitivePackages", true) })
                        {
                            if (!framework.TryGetProperty(listName, out var packages) || packages.ValueKind != JsonValueKind.Array)
                                continue;

                            foreach (var package in packages.EnumerateArray())
                            {
                                if (!package.TryGetProperty("vulnerabilities", out var vulnerabilities) || vulnerabilities.ValueKind != JsonValueKind.Array)
                                    continue;

                                var id = GetString(package, "id");
                                var version = GetString(package, "resolvedVersion");

                                foreach (var vulnerability in vulnerabilities.EnumerateArray())
                                {
                                    var advisoryUrl = GetString(vulnerability, "advisoryurl");
                                    var key = (id.ToLowerInvariant(), version, advisoryUrl);

                                    if (!byKey.TryGetValue(key, out var entry))
                                    {
                                        var severity = NormalizeSeverity(GetString(vulnerability, "severity"));
                                        byKey[key] = entry = new PackageVulnerability
                                        {
                                            PackageName = id,
                                            AffectedVersion = version,
                                            Severity = severity,
                                            VulnerabilityId = GetAdvisoryId(advisoryUrl),
                                            AdvisoryUrl = advisoryUrl,
                                            Description = $"{severity} severity vulnerability in {id} {version}",
                                            IsTransitive = isTransitive
                                        };
                                    }

                                    // Direct in any project wins over transitive.
                                    entry.IsTransitive &= isTransitive;
                                    if (!entry.AffectedProjects.Contains(projectName))
                                        entry.AffectedProjects.Add(projectName);
                                }
                            }
                        }
                    }
                }
            }

            return byKey.Values
                .OrderBy(v => SeverityRank(v.Severity))
                .ThenBy(v => v.PackageName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(v => v.VulnerabilityId, StringComparer.Ordinal)
                .ToList();
        }

        private static void AddProblems(JsonElement element, List<OperationWarning> warnings)
        {
            if (!element.TryGetProperty("problems", out var problems) || problems.ValueKind != JsonValueKind.Array)
                return;

            foreach (var problem in problems.EnumerateArray())
            {
                var project = GetString(problem, "project");
                var text = GetString(problem, "text");
                warnings.Add(new OperationWarning
                {
                    Context = "Vulnerability Check",
                    Message = string.IsNullOrEmpty(project) ? text : $"{Path.GetFileName(project)}: {text}"
                });
            }
        }

        private static string GetString(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }

        /// <summary>
        /// NuGet reports Low, Moderate, High, Critical; the model uses Medium for Moderate.
        /// </summary>
        internal static string NormalizeSeverity(string severity)
        {
            return severity.Trim().ToLowerInvariant() switch
            {
                "critical" => "Critical",
                "high" => "High",
                "moderate" or "medium" => "Medium",
                "low" => "Low",
                "" => "Unknown",
                _ => severity.Trim()
            };
        }

        internal static int SeverityRank(string severity) => severity switch
        {
            "Critical" => 0,
            "High" => 1,
            "Medium" => 2,
            "Low" => 3,
            _ => 4
        };

        private static string GetAdvisoryId(string advisoryUrl)
        {
            if (string.IsNullOrEmpty(advisoryUrl))
                return "(no advisory)";

            var trimmed = advisoryUrl.TrimEnd('/');
            var lastSlash = trimmed.LastIndexOf('/');
            return lastSlash >= 0 ? trimmed.Substring(lastSlash + 1) : trimmed;
        }
    }
}
