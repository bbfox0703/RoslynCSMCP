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
    [McpServerTool, Description("""
        Scan C# source in every project for five categories with pattern checks, not data-flow analysis, so any
        non-literal argument to Path.Combine or File.Read*/Write* and any runtime value in a SQL-looking string is
        flagged regardless of origin. Findings are only ever Critical or High. Returns findings grouped by category,
        up to 10 per category, with severity, title, file, and line. Does not scan configuration files or detect
        command injection, XSS, or insecure randomness.
        """)]
    public static async Task<string> FindSecurityIssues(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary (counts only), normal (grouped list), detailed (full information). Default: normal")]
        string format = "normal",
        [Description("Comma-separated, lowercase: sql-injection (runtime values in SQL-looking strings), secrets (literals assigned to password, secret, apikey, token, or connectionstring names, and connection-string literals), crypto (MD5, SHA1, DES, TripleDES, RC2), path-traversal (non-literal arguments to Path, File, and Directory APIs), deserialization (BinaryFormatter, JavaScriptSerializer, NetDataContractSerializer), or all. Default: all")]
        string categories = "all",
        [Description("Exact severity to return, not a minimum (case-insensitive): Critical, High, or all. No check emits Medium or Low. Default: all")] string minSeverity = "all",
        SecurityIssueAnalyzer analyzer = null!,
        SecurityValidator validator = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            var pathError = validator.ValidateSolutionPath(solutionPath, errorHandler);
            if (pathError != null) return pathError;

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
            return errorHandler.HandleException(ex, "FindSecurityIssues");
        }
    }

    [McpServerTool, Description("Detect shared-state thread-safety risks and potential race conditions: mutable static fields, shared state accessed without synchronization, and non-thread-safe collections. Does not check async/await usage, await inside lock, or CancellationToken propagation.")]
    public static async Task<string> FindThreadSafetyIssues(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary (counts only), normal (grouped list), detailed (full information). Default: normal")]
        string format = "normal",
        [Description("Issue types to check (comma-separated): MutableStatic, UnsynchronizedAccess, UnsafeCollection, DoubleCheckLocking, all. Default: all")]
        string issueTypes = "all",
        Phase2AnalysisService analyzer = null!,
        SecurityValidator validator = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            var pathError = validator.ValidateSolutionPath(solutionPath, errorHandler);
            if (pathError != null) return pathError;

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
            return errorHandler.HandleException(ex, "FindThreadSafetyIssues");
        }
    }

    [McpServerTool, Description("""
        Check every catch clause in the solution: EmptyCatch (no statements, High), SwallowedException (no throw and
        no logging-like call, Medium), and GenericException (catch (System.Exception) or a bare catch, without an
        exception filter, Low). MissingUsing (Medium) flags IDisposable locals the method creates (new, a static
        factory, or a Create/Open/Begin call) and never disposes, returns, stores, or passes on; using declarations
        are not flagged. Each file is analyzed once, and line numbers point to the catch clause or declaration.
        Returns findings grouped by issue type, up to 10 per type, with method and file:line; detailed adds full
        paths and the caught exception type.
        """)]
    public static async Task<string> AnalyzeExceptionHandling(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary (counts only), normal (grouped list), detailed (full information). Default: normal")]
        string format = "normal",
        [Description("Comma-separated, case-insensitive: EmptyCatch, SwallowedException, GenericException (alias GenericCatch), MissingUsing, or all. Default: all")]
        string issueTypes = "all",
        Phase2AnalysisService analyzer = null!,
        SecurityValidator validator = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            var pathError = validator.ValidateSolutionPath(solutionPath, errorHandler);
            if (pathError != null) return pathError;

            var requested = issueTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var all = requested.Count == 0 || requested.Contains("all");

            var results = await analyzer.AnalyzeExceptionHandlingAsync(
                solutionPath,
                checkEmptyCatch: all || requested.Contains("EmptyCatch"),
                checkSwallowedExceptions: all || requested.Contains("SwallowedException"),
                checkMissingUsing: all || requested.Contains("MissingUsing"),
                checkGenericCatch: all || requested.Contains("GenericException") || requested.Contains("GenericCatch"));

            return format.ToLowerInvariant() switch
            {
                "summary" => FormatExceptionHandlingSummary(results),
                "detailed" => FormatExceptionHandlingDetailed(results),
                _ => FormatExceptionHandlingNormal(results)
            };
        }
        catch (Exception ex)
        {
            return errorHandler.HandleException(ex, "AnalyzeExceptionHandling");
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
        return output.AppendWarnings(results.Warnings).ToString();
    }

    private static string FormatSecurityIssuesNormal(SecurityIssueResults results)
    {
        if (!results.Issues.Any())
            return "No security issues found.".WithWarnings(results.Warnings);

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

        return output.AppendWarnings(results.Warnings).ToString();
    }

    private static string FormatSecurityIssuesDetailed(SecurityIssueResults results)
    {
        if (!results.Issues.Any())
            return "No security issues found.".WithWarnings(results.Warnings);

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

        return output.AppendWarnings(results.Warnings).ToString();
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
        return output.AppendWarnings(results.Warnings).ToString();
    }

    private static string FormatThreadSafetyIssuesNormal(ThreadSafetyResults results)
    {
        if (!results.Issues.Any())
            return "No thread safety issues found.".WithWarnings(results.Warnings);

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

        return output.AppendWarnings(results.Warnings).ToString();
    }

    private static string FormatThreadSafetyIssuesDetailed(ThreadSafetyResults results)
    {
        if (!results.Issues.Any())
            return "No thread safety issues found.".WithWarnings(results.Warnings);

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

        return output.AppendWarnings(results.Warnings).ToString();
    }

    private static string FormatExceptionHandlingSummary(ExceptionHandlingResults results)
    {
        var output = new StringBuilder();
        output.AppendLine("Exception Handling Analysis Summary:");
        output.AppendLine($"  Total issues: {results.TotalIssues}");
        output.AppendLine($"  Empty catch: {results.EmptyCatchCount}");
        output.AppendLine($"  Swallowed: {results.SwallowedExceptionCount}");
        output.AppendLine($"  Generic catch: {results.GenericCatchCount}");
        output.AppendLine($"  Missing using: {results.MissingUsingCount}");
        return output.AppendWarnings(results.Warnings).ToString();
    }

    private static string FormatExceptionHandlingNormal(ExceptionHandlingResults results)
    {
        if (!results.Issues.Any())
            return "No exception handling issues found.".WithWarnings(results.Warnings);

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

        return output.AppendWarnings(results.Warnings).ToString();
    }

    private static string FormatExceptionHandlingDetailed(ExceptionHandlingResults results)
    {
        if (!results.Issues.Any())
            return "No exception handling issues found.".WithWarnings(results.Warnings);

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

        return output.AppendWarnings(results.Warnings).ToString();
    }

    #endregion
}
