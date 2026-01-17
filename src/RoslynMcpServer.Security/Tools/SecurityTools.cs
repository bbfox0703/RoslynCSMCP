using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcpServer.Core.Models;
using RoslynMcpServer.Core.Services;
using System.ComponentModel;
using System.Text;

namespace RoslynMcpServer.Security.Tools;

/// <summary>
/// MCP Tools for C# security analysis.
/// This module provides 3 tools (~525 tokens) for security analysis.
/// </summary>
[McpServerToolType]
public class SecurityTools
{
    [McpServerTool, Description("Find security issues and anti-patterns in the solution (SQL injection, hardcoded secrets, weak crypto, etc.)")]
    public static async Task<string> FindSecurityIssues(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary (counts only), normal (grouped list), detailed (full information). Default: normal")]
        string format = "normal",
        [Description("Categories to check (comma-separated): sql-injection, secrets, crypto, path-traversal, deserialization, all. Default: all")]
        string categories = "all",
        [Description("Minimum severity: Critical, High, Medium, Low. Default: Low")] string minSeverity = "Low",
        IServiceProvider? serviceProvider = null)
    {
        try
        {
            var validator = serviceProvider?.GetService<SecurityValidator>();
            if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
            {
                return "Error: Invalid solution path provided.";
            }

            var analyzer = serviceProvider?.GetService<SecurityIssueAnalyzer>();
            if (analyzer == null)
            {
                return "Error: Security issue analyzer service not available.";
            }

            var categoryArray = categories.Equals("all", StringComparison.OrdinalIgnoreCase)
                ? new[] { "sql-injection", "secrets", "crypto", "path-traversal", "deserialization" }
                : categories.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var results = await analyzer.AnalyzeSecurityIssuesAsync(solutionPath, categoryArray, minSeverity);

            return format.ToLowerInvariant() switch
            {
                "summary" => FormatSecurityIssuesSummary(results),
                "detailed" => FormatSecurityIssuesDetailed(results),
                _ => FormatSecurityIssuesNormal(results)
            };
        }
        catch (Exception ex)
        {
            var logger = serviceProvider?.GetService<ILogger<SecurityTools>>();
            logger?.LogError(ex, "Error finding security issues");
            return $"Error: An unexpected error occurred while finding security issues: {ex.Message}";
        }
    }

    [McpServerTool, Description("Detect common thread safety issues and potential race conditions (mutable static fields, unsynchronized access)")]
    public static async Task<string> FindThreadSafetyIssues(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary (counts only), normal (grouped list), detailed (full information). Default: normal")]
        string format = "normal",
        [Description("Issue types to check (comma-separated): MutableStatic, UnsynchronizedAccess, UnsafeCollection, DoubleCheckLocking, all. Default: all")]
        string issueTypes = "all",
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

            // Parse issue types to determine which checks to run
            var checkStaticFields = issueTypes.Contains("MutableStatic", StringComparison.OrdinalIgnoreCase) ||
                                   issueTypes.Equals("all", StringComparison.OrdinalIgnoreCase);
            var checkSharedState = issueTypes.Contains("UnsynchronizedAccess", StringComparison.OrdinalIgnoreCase) ||
                                  issueTypes.Equals("all", StringComparison.OrdinalIgnoreCase);
            var checkCollections = issueTypes.Contains("UnsafeCollection", StringComparison.OrdinalIgnoreCase) ||
                                  issueTypes.Contains("DoubleCheckLocking", StringComparison.OrdinalIgnoreCase) ||
                                  issueTypes.Equals("all", StringComparison.OrdinalIgnoreCase);

            var results = await analyzer.FindThreadSafetyIssuesAsync(solutionPath, checkStaticFields, checkSharedState, checkCollections);

            return format.ToLowerInvariant() switch
            {
                "summary" => FormatThreadSafetyIssuesSummary(results),
                "detailed" => FormatThreadSafetyIssuesDetailed(results),
                _ => FormatThreadSafetyIssuesNormal(results)
            };
        }
        catch (Exception ex)
        {
            var logger = serviceProvider?.GetService<ILogger<SecurityTools>>();
            logger?.LogError(ex, "Error finding thread safety issues");
            return $"Error: An unexpected error occurred while finding thread safety issues: {ex.Message}";
        }
    }

    [McpServerTool, Description("Analyze exception handling patterns and detect anti-patterns (empty catch, swallowed exceptions)")]
    public static async Task<string> AnalyzeExceptionHandling(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary (counts only), normal (grouped list), detailed (full information). Default: normal")]
        string format = "normal",
        [Description("Issue types to check (comma-separated): EmptyCatch, SwallowedException, GenericCatch, MissingUsing, all. Default: all")]
        string issueTypes = "all",
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

            // Parse issue types to determine which checks to run
            var checkEmptyCatch = issueTypes.Contains("EmptyCatch", StringComparison.OrdinalIgnoreCase) ||
                                 issueTypes.Equals("all", StringComparison.OrdinalIgnoreCase);
            var checkSwallowedExceptions = issueTypes.Contains("SwallowedException", StringComparison.OrdinalIgnoreCase) ||
                                          issueTypes.Contains("GenericCatch", StringComparison.OrdinalIgnoreCase) ||
                                          issueTypes.Equals("all", StringComparison.OrdinalIgnoreCase);
            var checkMissingUsing = issueTypes.Contains("MissingUsing", StringComparison.OrdinalIgnoreCase) ||
                                   issueTypes.Equals("all", StringComparison.OrdinalIgnoreCase);

            var results = await analyzer.AnalyzeExceptionHandlingAsync(solutionPath, checkEmptyCatch, checkSwallowedExceptions, checkMissingUsing);

            return format.ToLowerInvariant() switch
            {
                "summary" => FormatExceptionHandlingSummary(results),
                "detailed" => FormatExceptionHandlingDetailed(results),
                _ => FormatExceptionHandlingNormal(results)
            };
        }
        catch (Exception ex)
        {
            var logger = serviceProvider?.GetService<ILogger<SecurityTools>>();
            logger?.LogError(ex, "Error analyzing exception handling");
            return $"Error: An unexpected error occurred while analyzing exception handling: {ex.Message}";
        }
    }

    #region Formatting Methods

    private static string FormatSecurityIssuesSummary(SecurityIssueResults results)
    {
        var output = new StringBuilder();
        output.AppendLine("Security Issue Analysis Summary:");
        output.AppendLine($"  Total issues: {results.TotalIssues}");
        output.AppendLine($"  Critical: {results.CriticalCount}");
        output.AppendLine($"  High: {results.HighCount}");
        output.AppendLine($"  Medium: {results.MediumCount}");
        output.AppendLine($"  Low: {results.LowCount}");
        return output.ToString();
    }

    private static string FormatSecurityIssuesNormal(SecurityIssueResults results)
    {
        if (!results.Issues.Any())
            return "No security issues found.";

        var output = new StringBuilder();
        output.AppendLine($"Found {results.TotalIssues} security issues:\n");

        var grouped = results.Issues.GroupBy(i => i.Category);
        foreach (var group in grouped)
        {
            output.AppendLine($"**{group.Key}** ({group.Count()}):");
            foreach (var issue in group.Take(10))
            {
                output.AppendLine($"  - [{issue.Severity}] {issue.Title}");
                output.AppendLine($"    @ {issue.FileName}:{issue.LineNumber}");
            }
            if (group.Count() > 10)
                output.AppendLine($"    ... and {group.Count() - 10} more");
            output.AppendLine();
        }

        return output.ToString();
    }

    private static string FormatSecurityIssuesDetailed(SecurityIssueResults results)
    {
        if (!results.Issues.Any())
            return "No security issues found.";

        var output = new StringBuilder();
        output.AppendLine($"# Security Issue Analysis");
        output.AppendLine($"Total: {results.TotalIssues} issues\n");

        foreach (var issue in results.Issues.OrderBy(i => i.Severity))
        {
            output.AppendLine($"## [{issue.Severity}] {issue.Title}");
            output.AppendLine($"  Category: {issue.Category}");
            output.AppendLine($"  File: {issue.FilePath}:{issue.LineNumber}");
            output.AppendLine($"  Method: {issue.MethodName}");
            output.AppendLine($"  Description: {issue.Description}");
            output.AppendLine($"  Recommendation: {issue.Recommendation}");
            output.AppendLine();
        }

        return output.ToString();
    }

    private static string FormatThreadSafetyIssuesSummary(ThreadSafetyResults results)
    {
        var output = new StringBuilder();
        output.AppendLine("Thread Safety Analysis Summary:");
        output.AppendLine($"  Total issues: {results.TotalIssues}");
        output.AppendLine($"  Critical: {results.CriticalCount}");
        output.AppendLine($"  High: {results.HighCount}");
        output.AppendLine($"  Medium: {results.MediumCount}");
        output.AppendLine($"  Low: {results.LowCount}");
        return output.ToString();
    }

    private static string FormatThreadSafetyIssuesNormal(ThreadSafetyResults results)
    {
        if (!results.Issues.Any())
            return "No thread safety issues found.";

        var output = new StringBuilder();
        output.AppendLine($"Found {results.TotalIssues} thread safety issues:\n");

        var grouped = results.Issues.GroupBy(i => i.IssueType);
        foreach (var group in grouped)
        {
            output.AppendLine($"**{group.Key}** ({group.Count()}):");
            foreach (var issue in group.Take(10))
            {
                output.AppendLine($"  - [{issue.Severity}] {issue.MemberName}");
                output.AppendLine($"    @ {issue.FileName}:{issue.LineNumber}");
            }
            if (group.Count() > 10)
                output.AppendLine($"    ... and {group.Count() - 10} more");
            output.AppendLine();
        }

        return output.ToString();
    }

    private static string FormatThreadSafetyIssuesDetailed(ThreadSafetyResults results)
    {
        if (!results.Issues.Any())
            return "No thread safety issues found.";

        var output = new StringBuilder();
        output.AppendLine($"# Thread Safety Analysis");
        output.AppendLine($"Total: {results.TotalIssues} issues\n");

        foreach (var issue in results.Issues.OrderBy(i => i.Severity))
        {
            output.AppendLine($"## [{issue.Severity}] {issue.IssueType}");
            output.AppendLine($"  Member: {issue.MemberName} ({issue.MemberType})");
            output.AppendLine($"  File: {issue.FilePath}:{issue.LineNumber}");
            output.AppendLine($"  Static: {issue.IsStaticMember}");
            output.AppendLine($"  Description: {issue.Description}");
            output.AppendLine($"  Recommendation: {issue.Recommendation}");
            output.AppendLine();
        }

        return output.ToString();
    }

    private static string FormatExceptionHandlingSummary(ExceptionHandlingResults results)
    {
        var output = new StringBuilder();
        output.AppendLine("Exception Handling Analysis Summary:");
        output.AppendLine($"  Total issues: {results.TotalIssues}");
        output.AppendLine($"  Empty catch: {results.EmptyCatchCount}");
        output.AppendLine($"  Swallowed: {results.SwallowedExceptionCount}");
        output.AppendLine($"  Generic catch: {results.GenericCatchCount}");
        return output.ToString();
    }

    private static string FormatExceptionHandlingNormal(ExceptionHandlingResults results)
    {
        if (!results.Issues.Any())
            return "No exception handling issues found.";

        var output = new StringBuilder();
        output.AppendLine($"Found {results.TotalIssues} exception handling issues:\n");

        var grouped = results.Issues.GroupBy(i => i.IssueType);
        foreach (var group in grouped)
        {
            output.AppendLine($"**{group.Key}** ({group.Count()}):");
            foreach (var issue in group.Take(10))
            {
                output.AppendLine($"  - [{issue.Severity}] {issue.MethodName}");
                output.AppendLine($"    @ {issue.FileName}:{issue.LineNumber}");
            }
            if (group.Count() > 10)
                output.AppendLine($"    ... and {group.Count() - 10} more");
            output.AppendLine();
        }

        return output.ToString();
    }

    private static string FormatExceptionHandlingDetailed(ExceptionHandlingResults results)
    {
        if (!results.Issues.Any())
            return "No exception handling issues found.";

        var output = new StringBuilder();
        output.AppendLine($"# Exception Handling Analysis");
        output.AppendLine($"Total: {results.TotalIssues} issues\n");

        foreach (var issue in results.Issues.OrderBy(i => i.Severity))
        {
            output.AppendLine($"## [{issue.Severity}] {issue.IssueType}");
            output.AppendLine($"  Method: {issue.MethodName}");
            output.AppendLine($"  File: {issue.FilePath}:{issue.LineNumber}");
            output.AppendLine($"  Exception Type: {issue.ExceptionType}");
            output.AppendLine($"  Description: {issue.Description}");
            output.AppendLine($"  Recommendation: {issue.Recommendation}");
            output.AppendLine();
        }

        return output.ToString();
    }

    #endregion
}
