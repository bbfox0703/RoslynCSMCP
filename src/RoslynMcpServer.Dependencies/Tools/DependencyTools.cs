using ModelContextProtocol.Server;
using RoslynMcpServer.Core.Models;
using RoslynMcpServer.Core.Services;
using System.ComponentModel;
using System.Text;

namespace RoslynMcpServer.Dependencies.Tools;

/// <summary>
/// MCP Tools for C# dependency analysis.
/// This module provides 5 tools (~875 tokens) for dependency analysis.
/// </summary>
[McpServerToolType]
public class DependencyTools
{
    [McpServerTool, Description("""
        Summarize the solution's dependencies as one aggregated list: every project reference and every referenced
        assembly except framework ones (System*, Microsoft*, mscorlib, netstandard), with assembly versions, plus
        circular project-reference cycles. Assembly names are listed, not NuGet package IDs. summary returns only
        the counts; normal and detailed return the same full list. For per-project edges use GetDependencyGraph; for
        unused references use FindUnusedDependencies.
        """)]
    public static async Task<string> AnalyzeDependencies(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary (counts only), normal (grouped list), detailed (full information). Default: normal")]
        string format = "normal",
        CodeAnalysisService analysisService = null!,
        SecurityValidator validator = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            var pathError = validator.ValidateSolutionPath(solutionPath, errorHandler);
            if (pathError != null) return pathError;

            var results = await analysisService.AnalyzeDependenciesAsync(solutionPath);
            return FormatDependencyAnalysis(results, format);
        }
        catch (Exception ex)
        {
            return errorHandler.HandleException(ex, "AnalyzeDependencies");
        }
    }

    [McpServerTool, Description("""
        Return the project-reference graph of the solution: text lists each project with the projects it references,
        then summary counts; mermaid and dot return diagram source with project-to-project edges only. With
        includePackages, text output also lists each project's referenced non-framework assemblies, including
        transitive ones, which requires compiling every project. Does not show type-level dependencies or detect
        cycles; AnalyzeDependencies reports circular references.
        """)]
    public static async Task<string> GetDependencyGraph(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: text (per-project list), mermaid, or dot (Graphviz); other values fall back to text. Default: text")]
        string format = "text",
        [Description("Also list each project's referenced assemblies (NuGet and other, including transitive; framework assemblies excluded). Affects text format only. Default: false")] bool includePackages = false,
        DependencyGraphService graphService = null!,
        SecurityValidator validator = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            var pathError = validator.ValidateSolutionPath(solutionPath, errorHandler);
            if (pathError != null) return pathError;

            return await graphService.GetDependencyGraphAsync(solutionPath, format, includePackages);
        }
        catch (Exception ex)
        {
            return errorHandler.HandleException(ex, "GetDependencyGraph");
        }
    }

    [McpServerTool, Description("""
        Flag package and project references that look unused, per project file, using heuristics. A PackageReference
        counts as used when a using directive, global usings included (such as those the SDK generates from csproj
        <Using> items), imports the package ID, the ID minus its last segment for IDs with three or more segments, or
        a sub-namespace of either; packages without compile assets and known build, test, and analyzer packages are
        never flagged. AnalyzePackages applies the same rule. A project reference counts as used when any identifier
        binds to a symbol from that project in any target framework. Packages whose namespaces differ from their IDs
        are still reported as unused. Normal groups results by project; detailed adds version and reason. A load
        failure reads as a clean result.
        """)]
    public static async Task<string> FindUnusedDependencies(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary (counts only), normal (grouped list), detailed (full information). Default: normal")]
        string format = "normal",
        [Description("Include NuGet package analysis (default: true)")] bool includeNuGetPackages = true,
        [Description("Include project reference analysis (default: true)")] bool includeProjectReferences = true,
        UnusedDependencyAnalyzer analyzer = null!,
        SecurityValidator validator = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            var pathError = validator.ValidateSolutionPath(solutionPath, errorHandler);
            if (pathError != null) return pathError;

            var results = await analyzer.AnalyzeUnusedDependenciesAsync(solutionPath, includeNuGetPackages, includeProjectReferences);

            return format.ToLowerInvariant() switch
            {
                "summary" => FormatUnusedDependenciesSummary(results),
                "detailed" => FormatUnusedDependenciesDetailed(results),
                _ => FormatUnusedDependenciesNormal(results)
            };
        }
        catch (Exception ex)
        {
            return errorHandler.HandleException(ex, "FindUnusedDependencies");
        }
    }

    [McpServerTool, Description("""
        List the PackageReference items written in each .csproj (versions from Directory.Packages.props or imported
        props files are not read) and report newer stable versions on nuget.org, packages referenced at different
        versions across projects, known vulnerabilities, and packages that look unused. The vulnerability check runs
        'dotnet list package --vulnerable --include-transitive', which needs network access and a restored
        solution; its problems appear as warnings. The unused check is the FindUnusedDependencies heuristic.
        Summary gives counts; normal lists up to 10 each of vulnerabilities, updates, conflicts, and unused
        packages, plus warnings; detailed lists all of them and every package reference with version and project.
        """)]
    public static async Task<string> AnalyzePackages(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary (counts only), normal (grouped list), detailed (full information). Default: normal")]
        string format = "normal",
        [Description("Query nuget.org for the latest stable version of each package (network access; other feeds are ignored). Default: true")] bool checkUpdates = true,
        [Description("Report packages referenced at different versions across projects. Default: true")] bool checkConflicts = true,
        [Description("Report packages with known advisories, direct and transitive, using 'dotnet list package --vulnerable' (network access; the solution must already be restored, no restore is run; takes up to a few minutes). Default: true")] bool checkVulnerabilities = true,
        [Description("Flag packages that no using directive (global usings included) imports; packages without compile assets and known build, test, and analyzer packages are exempt. Heuristic: packages whose namespaces differ from their IDs are false positives. Default: true")] bool analyzeUsage = true,
        PackageAnalysisService analyzer = null!,
        SecurityValidator validator = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            var pathError = validator.ValidateSolutionPath(solutionPath, errorHandler);
            if (pathError != null) return pathError;

            var results = await analyzer.AnalyzePackagesAsync(
                solutionPath,
                checkUpdates: checkUpdates,
                checkVulnerabilities: checkVulnerabilities,
                analyzeUsage: analyzeUsage,
                checkConflicts: checkConflicts);

            return format.ToLowerInvariant() switch
            {
                "summary" => FormatPackageAnalysisSummary(results),
                "detailed" => FormatPackageAnalysisDetailed(results),
                _ => FormatPackageAnalysisNormal(results)
            };
        }
        catch (Exception ex)
        {
            return errorHandler.HandleException(ex, "AnalyzePackages");
        }
    }

    [McpServerTool, Description("""
        Statically compare dependency-injection registrations with constructor parameters across all projects. Only
        generic AddSingleton, AddScoped, AddTransient, and TryAdd* calls count as registrations; non-generic, keyed,
        AddHostedService, AddDbContext, and similar forms are ignored. Reports unregistered constructor-parameter
        types for every class (ILogger, IOptions, IConfiguration, and System.* excepted), duplicate registrations,
        lifetime mismatches, and captive-lifetime and circular dependencies; the last two follow each service to the
        implementation it is registered with (AddScoped<IService, Impl>). Expect false positives for classes the
        container does not create.
        """)]
    public static async Task<string> AnalyzeDIContainer(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary (counts only), normal (grouped list), detailed (full information). Default: normal")]
        string format = "normal",
        Phase2AnalysisService analyzer = null!,
        SecurityValidator validator = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            var pathError = validator.ValidateSolutionPath(solutionPath, errorHandler);
            if (pathError != null) return pathError;

            var results = await analyzer.AnalyzeDIContainerAsync(solutionPath);

            return format.ToLowerInvariant() switch
            {
                "summary" => FormatDIContainerSummary(results),
                "detailed" => FormatDIContainerDetailed(results),
                _ => FormatDIContainerNormal(results)
            };
        }
        catch (Exception ex)
        {
            return errorHandler.HandleException(ex, "AnalyzeDIContainer");
        }
    }

    #region Formatting Methods

    private static string FormatDependencyAnalysis(DependencyAnalysis results, string format)
    {
        var output = new StringBuilder();

        if (format == "summary")
        {
            output.AppendLine($"Dependency Analysis for {results.ProjectName}:");
            output.AppendLine($"  Dependencies: {results.Dependencies.Count}");
            output.AppendLine($"  Circular: {results.CircularDependencyCount}");
            return output.ToString();
        }

        output.AppendLine($"# Dependency Analysis: {results.ProjectName}");
        output.AppendLine($"Total dependencies: {results.Dependencies.Count}\n");

        if (results.Dependencies.Any())
        {
            output.AppendLine("## Dependencies:");
            foreach (var dep in results.Dependencies)
            {
                output.AppendLine($"  - {dep.Name} ({dep.Type}) v{dep.Version}");
            }
        }

        if (results.HasCircularDependencies)
        {
            output.AppendLine("\n## Circular Dependencies:");
            foreach (var circular in results.CircularDependencies)
            {
                output.AppendLine($"  - {string.Join(" -> ", circular.ProjectChain)}");
            }
        }

        return output.ToString();
    }

    private static string FormatUnusedDependenciesSummary(UnusedDependencyResults results)
    {
        var output = new StringBuilder();
        output.AppendLine("Unused Dependencies Summary:");
        output.AppendLine($"  Total: {results.TotalUnusedDependencies}");
        output.AppendLine($"  NuGet packages: {results.UnusedNuGetPackages}");
        output.AppendLine($"  Project references: {results.UnusedProjectReferences}");
        return output.ToString();
    }

    private static string FormatUnusedDependenciesNormal(UnusedDependencyResults results)
    {
        if (!results.UnusedDependencies.Any())
            return "No unused dependencies found.";

        var output = new StringBuilder();
        output.AppendLine($"Found {results.TotalUnusedDependencies} unused dependencies:\n");

        var grouped = results.UnusedDependencies.GroupBy(d => d.ProjectName);
        foreach (var group in grouped)
        {
            output.AppendLine($"**{group.Key}**:");
            foreach (var dep in group)
            {
                output.AppendLine($"  - {dep.Name} ({dep.Type})");
            }
            output.AppendLine();
        }

        return output.ToString();
    }

    private static string FormatUnusedDependenciesDetailed(UnusedDependencyResults results)
    {
        if (!results.UnusedDependencies.Any())
            return "No unused dependencies found.";

        var output = new StringBuilder();
        output.AppendLine($"# Unused Dependencies Analysis");
        output.AppendLine($"Total: {results.TotalUnusedDependencies}\n");

        foreach (var dep in results.UnusedDependencies)
        {
            output.AppendLine($"## {dep.Name}");
            output.AppendLine($"  Project: {dep.ProjectName}");
            output.AppendLine($"  Type: {dep.Type}");
            if (!string.IsNullOrEmpty(dep.Version))
                output.AppendLine($"  Version: {dep.Version}");
            output.AppendLine($"  Reason: {dep.Reason}");
            output.AppendLine();
        }

        return output.ToString();
    }

    private static string FormatPackageAnalysisSummary(PackageAnalysisResults results)
    {
        var output = new StringBuilder();
        output.AppendLine("Package Analysis Summary:");
        output.AppendLine($"  Total packages: {results.TotalPackages}");
        output.AppendLine($"  Unique packages: {results.UniquePackages}");
        output.AppendLine($"  Vulnerabilities: {results.VulnerablePackages}");
        output.AppendLine($"  Updates available: {results.AvailableUpdates.Count}");
        output.AppendLine($"  Version conflicts: {results.ConflictingPackages}");
        output.AppendLine($"  Unused packages: {results.UnusedPackagesCount}");
        if (results.Warnings.Any())
            output.AppendLine($"  Warnings: {results.Warnings.Count}");
        return output.ToString();
    }

    private static string FormatPackageAnalysisNormal(PackageAnalysisResults results)
    {
        var output = new StringBuilder();
        output.AppendLine($"# Package Analysis");
        output.AppendLine($"Total: {results.TotalPackages} packages, {results.UniquePackages} unique\n");

        AppendPackageFindings(output, results, limit: 10);
        return output.ToString();
    }

    private static string FormatPackageAnalysisDetailed(PackageAnalysisResults results)
    {
        var output = new StringBuilder();
        output.AppendLine($"# Package Analysis");
        output.AppendLine($"Total: {results.TotalPackages} packages, {results.UniquePackages} unique\n");

        AppendPackageFindings(output, results, limit: int.MaxValue);

        output.AppendLine("## All Packages:");
        foreach (var pkg in results.AllPackages.OrderBy(p => p.Name))
        {
            output.AppendLine($"  - {pkg.Name} v{pkg.Version} ({pkg.ProjectName})");
        }

        return output.ToString();
    }

    private static void AppendPackageFindings(StringBuilder output, PackageAnalysisResults results, int limit)
    {
        static string More(int total, int limit) => total > limit ? $"  ... and {total - limit} more" : string.Empty;

        if (results.Vulnerabilities.Any())
        {
            output.AppendLine($"## Vulnerabilities ({results.VulnerablePackages}):");
            foreach (var vuln in results.Vulnerabilities.Take(limit))
            {
                var dependency = vuln.IsTransitive ? ", transitive" : string.Empty;
                output.AppendLine($"  - [{vuln.Severity}] {vuln.PackageName} {vuln.AffectedVersion}{dependency}: {vuln.AdvisoryUrl}");
                output.AppendLine($"    Projects: {string.Join(", ", vuln.AffectedProjects)}");
            }
            var more = More(results.Vulnerabilities.Count, limit);
            if (more.Length > 0) output.AppendLine(more);
            output.AppendLine();
        }

        if (results.AvailableUpdates.Any())
        {
            output.AppendLine($"## Updates Available ({results.AvailableUpdates.Count}):");
            foreach (var update in results.AvailableUpdates
                         .OrderByDescending(u => u.MajorVersionsAhead)
                         .ThenByDescending(u => u.MinorVersionsAhead)
                         .Take(limit))
            {
                var breaking = update.IsBreakingChange ? " (major)" : string.Empty;
                output.AppendLine($"  - {update.PackageName}: {update.CurrentVersion} -> {update.LatestVersion}{breaking} ({string.Join(", ", update.AffectedProjects)})");
            }
            var more = More(results.AvailableUpdates.Count, limit);
            if (more.Length > 0) output.AppendLine(more);
            output.AppendLine();
        }

        if (results.VersionConflicts.Any())
        {
            output.AppendLine($"## Version Conflicts ({results.ConflictingPackages}):");
            foreach (var conflict in results.VersionConflicts.Take(limit))
            {
                var usages = conflict.VersionUsages.Select(v => $"{v.Version} ({v.ProjectName})");
                output.AppendLine($"  - {conflict.PackageName}: {string.Join(", ", usages)} -> standardize on {conflict.RecommendedVersion}");
            }
            var more = More(results.VersionConflicts.Count, limit);
            if (more.Length > 0) output.AppendLine(more);
            output.AppendLine();
        }

        if (results.UnusedPackages.Any())
        {
            output.AppendLine($"## Possibly Unused Packages ({results.UnusedPackagesCount}):");
            foreach (var pkg in results.UnusedPackages.Take(limit))
            {
                output.AppendLine($"  - {pkg.Name} v{pkg.Version} ({pkg.ProjectName}); expected namespaces: {string.Join(", ", pkg.ExpectedNamespaces)}");
            }
            var more = More(results.UnusedPackages.Count, limit);
            if (more.Length > 0) output.AppendLine(more);
            output.AppendLine();
        }

        if (results.Warnings.Any())
        {
            output.AppendLine("## Warnings:");
            foreach (var warning in results.Warnings)
            {
                output.AppendLine($"  - {warning.Context}: {warning.Message}");
            }
            output.AppendLine();
        }
    }

    private static string FormatDIContainerSummary(DIContainerResults results)
    {
        var output = new StringBuilder();
        output.AppendLine("DI Container Analysis Summary:");
        output.AppendLine($"  Total issues: {results.TotalIssues}");
        output.AppendLine($"  Unregistered: {results.UnregisteredCount}");
        output.AppendLine($"  Lifetime mismatches: {results.LifetimeMismatchCount}");
        output.AppendLine($"  Circular: {results.CircularDependencyCount}");
        return output.ToString();
    }

    private static string FormatDIContainerNormal(DIContainerResults results)
    {
        if (!results.Issues.Any())
            return "No DI container issues found.";

        var output = new StringBuilder();
        output.AppendLine($"Found {results.TotalIssues} DI container issues:\n");

        var grouped = results.Issues.GroupBy(i => i.IssueType);
        foreach (var group in grouped)
        {
            output.AppendLine($"**{group.Key}** ({group.Count()}):");
            foreach (var issue in group.Take(10))
            {
                output.AppendLine($"  - [{issue.Severity}] {issue.ServiceType}");
            }
            output.AppendLine();
        }

        return output.ToString();
    }

    private static string FormatDIContainerDetailed(DIContainerResults results)
    {
        if (!results.Issues.Any())
            return "No DI container issues found.";

        var output = new StringBuilder();
        output.AppendLine($"# DI Container Analysis");
        output.AppendLine($"Total: {results.TotalIssues} issues\n");

        foreach (var issue in results.Issues)
        {
            output.AppendLine($"## [{issue.Severity}] {issue.IssueType}");
            output.AppendLine($"  Service: {issue.ServiceType}");
            output.AppendLine($"  Implementation: {issue.ImplementationType}");
            output.AppendLine($"  Lifetime: {issue.ServiceLifetime}");
            output.AppendLine($"  Description: {issue.Description}");
            output.AppendLine($"  Recommendation: {issue.Recommendation}");
            output.AppendLine();
        }

        return output.ToString();
    }

    #endregion
}
