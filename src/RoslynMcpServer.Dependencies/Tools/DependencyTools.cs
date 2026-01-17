using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    [McpServerTool, Description("Analyze project dependencies and symbol usage patterns")]
    public static async Task<string> AnalyzeDependencies(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary (counts only), normal (grouped list), detailed (full information). Default: normal")]
        string format = "normal",
        IServiceProvider? serviceProvider = null)
    {
        try
        {
            var validator = serviceProvider?.GetService<SecurityValidator>();
            if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
            {
                return "Error: Invalid solution path provided.";
            }

            var analysisService = serviceProvider?.GetService<CodeAnalysisService>();
            if (analysisService == null)
            {
                return "Error: Code analysis service not available.";
            }

            var results = await analysisService.AnalyzeDependenciesAsync(solutionPath);
            return FormatDependencyAnalysis(results, format);
        }
        catch (Exception ex)
        {
            var logger = serviceProvider?.GetService<ILogger<DependencyTools>>();
            logger?.LogError(ex, "Error analyzing dependencies");
            return $"Error: An unexpected error occurred while analyzing dependencies: {ex.Message}";
        }
    }

    [McpServerTool, Description("Get project dependency graph in various formats")]
    public static async Task<string> GetDependencyGraph(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: text (hierarchical text), mermaid (diagram), json (structured). Default: text")]
        string format = "text",
        [Description("Include package dependencies (default: false)")] bool includePackages = false,
        IServiceProvider? serviceProvider = null)
    {
        try
        {
            var validator = serviceProvider?.GetService<SecurityValidator>();
            if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
            {
                return "Error: Invalid solution path provided.";
            }

            var graphService = serviceProvider?.GetService<DependencyGraphService>();
            if (graphService == null)
            {
                return "Error: Dependency graph service not available.";
            }

            return await graphService.GetDependencyGraphAsync(solutionPath, format, includePackages);
        }
        catch (Exception ex)
        {
            var logger = serviceProvider?.GetService<ILogger<DependencyTools>>();
            logger?.LogError(ex, "Error getting dependency graph");
            return $"Error: An unexpected error occurred while getting dependency graph: {ex.Message}";
        }
    }

    [McpServerTool, Description("Find unused dependencies (NuGet packages and project references) in the solution")]
    public static async Task<string> FindUnusedDependencies(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary (counts only), normal (grouped list), detailed (full information). Default: normal")]
        string format = "normal",
        [Description("Include NuGet package analysis (default: true)")] bool includeNuGetPackages = true,
        [Description("Include project reference analysis (default: true)")] bool includeProjectReferences = true,
        IServiceProvider? serviceProvider = null)
    {
        try
        {
            var validator = serviceProvider?.GetService<SecurityValidator>();
            if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
            {
                return "Error: Invalid solution path provided.";
            }

            var analyzer = serviceProvider?.GetService<UnusedDependencyAnalyzer>();
            if (analyzer == null)
            {
                return "Error: Unused dependency analyzer service not available.";
            }

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
            var logger = serviceProvider?.GetService<ILogger<DependencyTools>>();
            logger?.LogError(ex, "Error finding unused dependencies");
            return $"Error: An unexpected error occurred while finding unused dependencies: {ex.Message}";
        }
    }

    [McpServerTool, Description("Analyze NuGet packages in solution: check for updates, version conflicts, unused packages, and security vulnerabilities")]
    public static async Task<string> AnalyzePackages(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary (counts only), normal (grouped list), detailed (full information). Default: normal")]
        string format = "normal",
        [Description("Check for available updates (default: true)")] bool checkUpdates = true,
        [Description("Check for version conflicts (default: true)")] bool checkConflicts = true,
        IServiceProvider? serviceProvider = null)
    {
        try
        {
            var validator = serviceProvider?.GetService<SecurityValidator>();
            if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
            {
                return "Error: Invalid solution path provided.";
            }

            var analyzer = serviceProvider?.GetService<PackageAnalysisService>();
            if (analyzer == null)
            {
                return "Error: Package analysis service not available.";
            }

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
            var logger = serviceProvider?.GetService<ILogger<DependencyTools>>();
            logger?.LogError(ex, "Error analyzing packages");
            return $"Error: An unexpected error occurred while analyzing packages: {ex.Message}";
        }
    }

    [McpServerTool, Description("Analyze dependency injection container configuration for common issues (unregistered dependencies, lifetime mismatches)")]
    public static async Task<string> AnalyzeDIContainer(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary (counts only), normal (grouped list), detailed (full information). Default: normal")]
        string format = "normal",
        IServiceProvider? serviceProvider = null)
    {
        try
        {
            var validator = serviceProvider?.GetService<SecurityValidator>();
            if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
            {
                return "Error: Invalid solution path provided.";
            }

            var analyzer = serviceProvider?.GetService<Phase2AnalysisService>();
            if (analyzer == null)
            {
                return "Error: Phase2 analysis service not available.";
            }

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
            var logger = serviceProvider?.GetService<ILogger<DependencyTools>>();
            logger?.LogError(ex, "Error analyzing DI container");
            return $"Error: An unexpected error occurred while analyzing DI container: {ex.Message}";
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
        output.AppendLine($"  Version conflicts: {results.ConflictingPackages}");
        return output.ToString();
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

        return output.ToString();
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

        return output.ToString();
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
