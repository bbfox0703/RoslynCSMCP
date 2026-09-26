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
        Flag package and project references that look unused, per project, using heuristics. A PackageReference
        counts as used when a using directive starts with the package ID (or, for IDs with three or more segments,
        the ID minus its last segment); a project reference counts as used when any identifier binds to a symbol
        from that project. Analyzers, build-time packages, test SDKs, packages whose namespaces differ from their
        IDs, and packages imported only through csproj <Using> items are reported as unused. Normal groups results
        by project; detailed adds version and reason. Load and analysis failures are listed as warnings.
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
        List the PackageReference items written in each .csproj and report packages referenced at different versions
        across projects. Versions from Directory.Packages.props or imported props files are not read. Update checks
        and unused-package results are not included in this module's output, and vulnerability checking is not
        implemented. Summary gives package and conflict counts; normal lists up to 10 conflicts; detailed lists
        every package reference with version and project.
        """)]
    public static async Task<string> AnalyzePackages(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary (counts only), normal (grouped list), detailed (full information). Default: normal")]
        string format = "normal",
        [Description("Queries nuget.org for newer versions (network access), but the results are not shown in this module's output, so false only saves time. Default: true")] bool checkUpdates = true,
        [Description("Currently has no effect; version conflicts are always reported. Default: true")] bool checkConflicts = true,
        PackageAnalysisService analyzer = null!,
        SecurityValidator validator = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            var pathError = validator.ValidateSolutionPath(solutionPath, errorHandler);
            if (pathError != null) return pathError;

            var results = await analyzer.AnalyzePackagesAsync(solutionPath, checkUpdates, checkConflicts);

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
        and captive-lifetime and circular dependencies, the last two only for classes registered under their own
        concrete type. Expect false positives for classes the container does not create.
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
            return output.AppendWarnings(results.Warnings).ToString();
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

        return output.AppendWarnings(results.Warnings).ToString();
    }

    private static string FormatUnusedDependenciesSummary(UnusedDependencyResults results)
    {
        var output = new StringBuilder();
        output.AppendLine("Unused Dependencies Summary:");
        output.AppendLine($"  Total: {results.TotalUnusedDependencies}");
        output.AppendLine($"  NuGet packages: {results.UnusedNuGetPackages}");
        output.AppendLine($"  Project references: {results.UnusedProjectReferences}");
        return output.AppendWarnings(results.Warnings).ToString();
    }

    private static string FormatUnusedDependenciesNormal(UnusedDependencyResults results)
    {
        if (!results.UnusedDependencies.Any())
            return "No unused dependencies found.".WithWarnings(results.Warnings);

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

        return output.AppendWarnings(results.Warnings).ToString();
    }

    private static string FormatUnusedDependenciesDetailed(UnusedDependencyResults results)
    {
        if (!results.UnusedDependencies.Any())
            return "No unused dependencies found.".WithWarnings(results.Warnings);

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

        return output.AppendWarnings(results.Warnings).ToString();
    }

    private static string FormatPackageAnalysisSummary(PackageAnalysisResults results)
    {
        var output = new StringBuilder();
        output.AppendLine("Package Analysis Summary:");
        output.AppendLine($"  Total packages: {results.TotalPackages}");
        output.AppendLine($"  Unique packages: {results.UniquePackages}");
        output.AppendLine($"  Vulnerabilities: {results.VulnerablePackages}");
        output.AppendLine($"  Version conflicts: {results.ConflictingPackages}");
        return output.AppendWarnings(results.Warnings).ToString();
    }

    private static string FormatPackageAnalysisNormal(PackageAnalysisResults results)
    {
        var output = new StringBuilder();
        output.AppendLine($"# Package Analysis");
        output.AppendLine($"Total: {results.TotalPackages} packages, {results.UniquePackages} unique\n");

        if (results.Vulnerabilities.Any())
        {
            output.AppendLine("## Vulnerabilities:");
            foreach (var vuln in results.Vulnerabilities.Take(10))
            {
                output.AppendLine($"  - [{vuln.Severity}] {vuln.PackageName} {vuln.AffectedVersion}");
            }
            output.AppendLine();
        }

        if (results.VersionConflicts.Any())
        {
            output.AppendLine("## Version Conflicts:");
            foreach (var conflict in results.VersionConflicts.Take(10))
            {
                output.AppendLine($"  - {conflict.PackageName}: {string.Join(", ", conflict.VersionUsages.Select(v => v.Version))}");
            }
        }

        return output.AppendWarnings(results.Warnings).ToString();
    }

    private static string FormatPackageAnalysisDetailed(PackageAnalysisResults results)
    {
        var output = new StringBuilder();
        output.AppendLine($"# Package Analysis");
        output.AppendLine($"Total: {results.TotalPackages} packages\n");

        output.AppendLine("## All Packages:");
        foreach (var pkg in results.AllPackages.OrderBy(p => p.Name))
        {
            output.AppendLine($"  - {pkg.Name} v{pkg.Version} ({pkg.ProjectName})");
        }

        return output.AppendWarnings(results.Warnings).ToString();
    }

    private static string FormatDIContainerSummary(DIContainerResults results)
    {
        var output = new StringBuilder();
        output.AppendLine("DI Container Analysis Summary:");
        output.AppendLine($"  Total issues: {results.TotalIssues}");
        output.AppendLine($"  Unregistered: {results.UnregisteredCount}");
        output.AppendLine($"  Lifetime mismatches: {results.LifetimeMismatchCount}");
        output.AppendLine($"  Circular: {results.CircularDependencyCount}");
        return output.AppendWarnings(results.Warnings).ToString();
    }

    private static string FormatDIContainerNormal(DIContainerResults results)
    {
        if (!results.Issues.Any())
            return "No DI container issues found.".WithWarnings(results.Warnings);

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

        return output.AppendWarnings(results.Warnings).ToString();
    }

    private static string FormatDIContainerDetailed(DIContainerResults results)
    {
        if (!results.Issues.Any())
            return "No DI container issues found.".WithWarnings(results.Warnings);

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

        return output.AppendWarnings(results.Warnings).ToString();
    }

    #endregion
}
