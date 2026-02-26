using ModelContextProtocol.Server;
using RoslynMcpServer.Core.Models;
using RoslynMcpServer.Core.Services;
using System.ComponentModel;
using System.Text;

namespace RoslynMcpServer.Metrics.Tools;

/// <summary>
/// MCP Tools for C# metrics analysis.
/// This module provides 3 tools (~525 tokens) for metrics analysis.
/// </summary>
[McpServerToolType]
public class MetricsTools
{
    [McpServerTool, Description("Get code metrics and statistics for entire solution")]
    public static async Task<string> GetCodeMetrics(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary (key metrics), normal (balanced), detailed (comprehensive). Default: normal")]
        string format = "normal",
        CodeMetricsService metricsService = null!,
        SecurityValidator validator = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            var pathError = validator.ValidateSolutionPath(solutionPath, errorHandler);
            if (pathError != null) return pathError;

            // GetMetricsAsync returns formatted string directly
            var groupBy = format.ToLowerInvariant() == "detailed" ? "namespace" : "project";
            return await metricsService.GetMetricsAsync(solutionPath, groupBy);
        }
        catch (Exception ex)
        {
            return errorHandler.HandleException(ex, "GetCodeMetrics");
        }
    }

    [McpServerTool, Description("Get comprehensive statistics for a C# file (LOC, complexity, dependencies, documentation coverage)")]
    public static async Task<string> GetFileStatistics(
        [Description("Path to C# source file (.cs)")] string filePath,
        [Description("Output format: summary (key metrics), normal (balanced), detailed (comprehensive). Default: normal")]
        string format = "normal",
        FileStatisticsAnalyzer analyzer = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            if (!File.Exists(filePath))
                return errorHandler.ValidationError("filePath", "File not found");

            if (!filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                return errorHandler.ValidationError("filePath", "File must be a C# source file (.cs)");

            var results = await analyzer.AnalyzeFileStatisticsAsync(filePath);

            return format.ToLowerInvariant() switch
            {
                "summary" => FormatFileStatisticsSummary(results),
                "detailed" => FormatFileStatisticsDetailed(results),
                _ => FormatFileStatisticsNormal(results)
            };
        }
        catch (Exception ex)
        {
            return errorHandler.HandleException(ex, "GetFileStatistics");
        }
    }

    [McpServerTool, Description("Analyze XML documentation coverage for types and members")]
    public static async Task<string> AnalyzeDocumentationCoverage(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary (counts only), normal (grouped list), detailed (with suggestions). Default: normal")]
        string format = "normal",
        [Description("Scope filter: public (public only), all (all symbols). Default: public")]
        string scope = "public",
        DocumentationAnalyzer analyzer = null!,
        SecurityValidator validator = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            var pathError = validator.ValidateSolutionPath(solutionPath, errorHandler);
            if (pathError != null) return pathError;

            var results = await analyzer.AnalyzeDocumentationCoverageAsync(solutionPath, scope);

            return format.ToLowerInvariant() switch
            {
                "summary" => FormatDocCoverageSummary(results),
                "detailed" => FormatDocCoverageDetailed(results),
                _ => FormatDocCoverageNormal(results)
            };
        }
        catch (Exception ex)
        {
            return errorHandler.HandleException(ex, "AnalyzeDocumentationCoverage");
        }
    }

    [McpServerTool, Description("""
        Analyze memory allocation hotspots and optimization opportunities in C# code.
        Detects: new byte[]/char[]/int[] inside loops (use ArrayPool<T> to avoid GC pressure),
        non-generic collections (ArrayList, Hashtable) that box value types,
        value types assigned to object variables (boxing),
        string.Substring() calls that allocate new strings (use AsSpan().Slice()),
        byte[]/char[] parameters that could be ReadOnlySpan<T> for zero-copy API design,
        and string += concatenation in loops (use StringBuilder to avoid O(n²) allocations).
        """)]
    public static async Task<string> AnalyzeMemoryAllocation(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary, normal, detailed. Default: normal")] string format = "normal",
        [Description("Issue types (comma-separated): ArrayPoolOpportunity, Boxing, SubstringSpan, ReadOnlySpan, StringBuilderOpportunity, all. Default: all")] string issueTypes = "all",
        [Description("Minimum severity to report: Critical, High, Medium, Low, all. Default: all")] string severity = "all",
        MemoryAllocationAnalyzer analyzer = null!,
        SecurityValidator validator = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            var pathError = validator.ValidateSolutionPath(solutionPath, errorHandler);
            if (pathError != null) return pathError;

            var issueTypeArray = issueTypes.Equals("all", StringComparison.OrdinalIgnoreCase)
                ? new[] { "ArrayPoolOpportunity", "Boxing", "SubstringSpan", "ReadOnlySpan", "StringBuilderOpportunity" }
                : issueTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var results = await analyzer.AnalyzeAsync(solutionPath, issueTypeArray);

            if (!severity.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                var severityOrder = new[] { "Critical", "High", "Medium", "Low" };
                var minIdx = Array.FindIndex(severityOrder, s => s.Equals(severity, StringComparison.OrdinalIgnoreCase));
                if (minIdx >= 0)
                {
                    results.Issues = results.Issues
                        .Where(i => Array.FindIndex(severityOrder, s => s == i.Severity) <= minIdx)
                        .ToList();
                }
            }

            return format.ToLowerInvariant() switch
            {
                "summary" => FormatMemorySummary(results),
                "detailed" => FormatMemoryDetailed(results),
                _ => FormatMemoryNormal(results)
            };
        }
        catch (Exception ex)
        {
            return errorHandler.HandleException(ex, "AnalyzeMemoryAllocation");
        }
    }

    #region Formatting Methods

    private static int MemorySeverityOrder(string severity) => severity switch
    {
        "Critical" => 0,
        "High" => 1,
        "Medium" => 2,
        "Low" => 3,
        _ => 99
    };

    private static string FormatMemorySummary(MemoryAllocationResults results)
    {
        var output = new StringBuilder();
        output.AppendLine("Memory Allocation Analysis — Summary");
        output.AppendLine($"  Analyzed projects : {results.AnalyzedProjects}");
        output.AppendLine($"  Analyzed files    : {results.AnalyzedFiles}");
        if (results.FailedProjects > 0)
            output.AppendLine($"  Failed projects   : {results.FailedProjects}");
        output.AppendLine();
        output.AppendLine("Issues by severity:");
        output.AppendLine($"  Critical : {results.CriticalIssues}");
        output.AppendLine($"  High     : {results.HighIssues}");
        output.AppendLine($"  Medium   : {results.MediumIssues}");
        output.AppendLine($"  Low      : {results.LowIssues}");
        output.AppendLine($"  Total    : {results.TotalIssues}");
        output.AppendLine();

        if (results.IssuesByType.Count > 0)
        {
            output.AppendLine("Issues by type:");
            foreach (var (type, count) in results.IssuesByType.OrderByDescending(kv => kv.Value))
                output.AppendLine($"  {type,-26}: {count}");
            output.AppendLine();
        }

        var score = Math.Max(0, 100 - results.CriticalIssues * 10 - results.HighIssues * 5
                                     - results.MediumIssues * 2 - results.LowIssues);
        output.AppendLine($"Memory Efficiency Score: {score}/100");
        if (score == 100) output.AppendLine("  Excellent — no memory allocation issues detected.");
        else if (score >= 75) output.AppendLine("  Good — minor allocation improvements possible.");
        else if (score >= 50) output.AppendLine("  Fair — several allocation hotspots to address.");
        else output.AppendLine("  Poor — significant allocation overhead requiring attention.");

        if (results.Warnings.Count > 0)
        {
            output.AppendLine();
            foreach (var w in results.Warnings)
                output.AppendLine($"Warning: {w.Message}");
        }

        return output.ToString();
    }

    private static string FormatMemoryNormal(MemoryAllocationResults results)
    {
        if (results.TotalIssues == 0)
        {
            var ok = new StringBuilder();
            ok.AppendLine("No memory allocation issues found.");
            ok.AppendLine($"Analyzed {results.AnalyzedProjects} project(s), {results.AnalyzedFiles} file(s).");
            return ok.ToString();
        }

        var output = new StringBuilder();
        output.AppendLine($"Found {results.TotalIssues} memory allocation issue(s) " +
                          $"(Critical:{results.CriticalIssues} High:{results.HighIssues} " +
                          $"Medium:{results.MediumIssues} Low:{results.LowIssues})");
        output.AppendLine();

        var grouped = results.Issues.GroupBy(i => i.IssueType);
        foreach (var group in grouped.OrderBy(g => g.Key))
        {
            output.AppendLine($"**{group.Key}** ({group.Count()}):");
            foreach (var issue in group.OrderBy(i => MemorySeverityOrder(i.Severity)).Take(10))
            {
                output.AppendLine($"  - [{issue.Severity}] {issue.Title}");
                output.AppendLine($"    @ {issue.FileName}:{issue.LineNumber}  ({issue.ProjectName})");
                if (!string.IsNullOrEmpty(issue.Recommendation))
                    output.AppendLine($"    → {issue.Recommendation}");
            }
            if (group.Count() > 10)
                output.AppendLine($"  ... and {group.Count() - 10} more (use format=detailed to see all)");
            output.AppendLine();
        }

        if (results.Warnings.Count > 0)
            foreach (var w in results.Warnings)
                output.AppendLine($"Warning: {w.Message}");

        return output.ToString();
    }

    private static string FormatMemoryDetailed(MemoryAllocationResults results)
    {
        if (results.TotalIssues == 0)
            return $"No memory allocation issues found. Analyzed {results.AnalyzedProjects} project(s).";

        var output = new StringBuilder();
        output.AppendLine("# Memory Allocation Analysis — Detailed Report");
        output.AppendLine();
        output.AppendLine($"Projects analyzed : {results.AnalyzedProjects}");
        output.AppendLine($"Files with issues : {results.AnalyzedFiles}");
        output.AppendLine($"Total issues      : {results.TotalIssues} " +
                          $"(C:{results.CriticalIssues} H:{results.HighIssues} " +
                          $"M:{results.MediumIssues} L:{results.LowIssues})");
        output.AppendLine();

        if (results.IssuesByProject.Count > 1)
        {
            output.AppendLine("## Issues by Project");
            foreach (var (proj, count) in results.IssuesByProject.OrderByDescending(kv => kv.Value))
                output.AppendLine($"  {proj}: {count}");
            output.AppendLine();
        }

        var grouped = results.Issues.GroupBy(i => i.IssueType).OrderBy(g => g.Key);
        foreach (var group in grouped)
        {
            output.AppendLine($"## {group.Key} ({group.Count()} issue(s))");
            output.AppendLine();

            foreach (var issue in group.OrderBy(i => MemorySeverityOrder(i.Severity)))
            {
                output.AppendLine($"### [{issue.Severity}] {issue.Title}");
                output.AppendLine($"- **File**: `{issue.FilePath}:{issue.LineNumber}`");
                output.AppendLine($"- **Project**: {issue.ProjectName}");
                output.AppendLine($"- **Description**: {issue.Description}");
                if (!string.IsNullOrEmpty(issue.CodeSnippet))
                    output.AppendLine($"- **Code**: `{issue.CodeSnippet}`");
                output.AppendLine($"- **Recommendation**: {issue.Recommendation}");
                if (!string.IsNullOrEmpty(issue.FixExample))
                {
                    output.AppendLine("- **Fix Example**:");
                    output.AppendLine("  ```csharp");
                    foreach (var line in issue.FixExample.Split('\n'))
                        output.AppendLine($"  {line}");
                    output.AppendLine("  ```");
                }
                output.AppendLine();
            }
        }

        if (results.Warnings.Count > 0)
        {
            output.AppendLine("## Warnings");
            foreach (var w in results.Warnings)
                output.AppendLine($"- {w.Message}");
        }

        return output.ToString();
    }

    private static string FormatFileStatisticsSummary(FileStatisticsResults results)
    {
        if (results.Statistics == null)
            return "Unable to analyze file statistics.";

        var stats = results.Statistics;
        var output = new StringBuilder();
        output.AppendLine($"File: {stats.FileName}");
        output.AppendLine($"  Lines: {stats.TotalLines} (Code: {stats.CodeLines}, Comments: {stats.CommentLines})");
        output.AppendLine($"  Complexity: {stats.CyclomaticComplexity}");
        output.AppendLine($"  Doc coverage: {stats.DocumentationCoverage:F1}%");
        return output.ToString();
    }

    private static string FormatFileStatisticsNormal(FileStatisticsResults results)
    {
        if (results.Statistics == null)
            return "Unable to analyze file statistics.";

        var stats = results.Statistics;
        var output = new StringBuilder();
        output.AppendLine($"# File Statistics: {stats.FileName}");
        output.AppendLine();
        output.AppendLine("## Lines:");
        output.AppendLine($"  Total: {stats.TotalLines}");
        output.AppendLine($"  Code: {stats.CodeLines}");
        output.AppendLine($"  Comments: {stats.CommentLines}");
        output.AppendLine($"  Blank: {stats.BlankLines}");
        output.AppendLine();
        output.AppendLine("## Code Elements:");
        output.AppendLine($"  Classes: {stats.ClassCount}");
        output.AppendLine($"  Interfaces: {stats.InterfaceCount}");
        output.AppendLine($"  Methods: {stats.MethodCount}");
        output.AppendLine($"  Properties: {stats.PropertyCount}");
        output.AppendLine();
        output.AppendLine("## Complexity:");
        output.AppendLine($"  Total: {stats.CyclomaticComplexity}");
        output.AppendLine($"  Max method: {stats.MaxMethodComplexity}");
        if (!string.IsNullOrEmpty(stats.MostComplexMethod))
            output.AppendLine($"  Most complex: {stats.MostComplexMethod}");

        return output.ToString();
    }

    private static string FormatFileStatisticsDetailed(FileStatisticsResults results)
    {
        if (results.Statistics == null)
            return "Unable to analyze file statistics.";

        var stats = results.Statistics;
        var output = new StringBuilder();
        output.AppendLine($"# Detailed File Statistics: {stats.FilePath}");
        output.AppendLine();
        output.AppendLine("## File Info:");
        output.AppendLine($"  Size: {stats.SizeInBytes / 1024.0:F1} KB");
        output.AppendLine($"  Project: {stats.ProjectName}");
        output.AppendLine();
        output.AppendLine("## Lines:");
        output.AppendLine($"  Total: {stats.TotalLines}");
        output.AppendLine($"  Code: {stats.CodeLines}");
        output.AppendLine($"  Comments: {stats.CommentLines}");
        output.AppendLine($"  Blank: {stats.BlankLines}");
        output.AppendLine();
        output.AppendLine("## Code Elements:");
        output.AppendLine($"  Classes: {stats.ClassCount}");
        output.AppendLine($"  Interfaces: {stats.InterfaceCount}");
        output.AppendLine($"  Structs: {stats.StructCount}");
        output.AppendLine($"  Enums: {stats.EnumCount}");
        output.AppendLine($"  Methods: {stats.MethodCount}");
        output.AppendLine($"  Properties: {stats.PropertyCount}");
        output.AppendLine($"  Fields: {stats.FieldCount}");
        output.AppendLine();
        output.AppendLine("## Complexity:");
        output.AppendLine($"  Total: {stats.CyclomaticComplexity}");
        output.AppendLine($"  Max method: {stats.MaxMethodComplexity}");
        output.AppendLine($"  Most complex: {stats.MostComplexMethod}");
        output.AppendLine();
        output.AppendLine("## Documentation:");
        output.AppendLine($"  Coverage: {stats.DocumentationCoverage:F1}%");
        output.AppendLine($"  Documented: {stats.DocumentedMembers}");
        output.AppendLine($"  Undocumented: {stats.UndocumentedMembers}");
        output.AppendLine();
        output.AppendLine("## Dependencies:");
        output.AppendLine($"  Using directives: {stats.UsingDirectivesCount}");
        if (stats.Namespaces.Any())
        {
            output.AppendLine("  Namespaces:");
            foreach (var ns in stats.Namespaces)
                output.AppendLine($"    - {ns}");
        }

        return output.ToString();
    }

    private static string FormatDocCoverageSummary(DocumentationCoverageResults results)
    {
        var output = new StringBuilder();
        output.AppendLine("Documentation Coverage Summary:");
        output.AppendLine($"  Coverage: {results.CoveragePercentage:F1}%");
        output.AppendLine($"  Documented: {results.DocumentedSymbols}/{results.TotalSymbols}");
        output.AppendLine($"  Undocumented: {results.UndocumentedCount}");
        return output.ToString();
    }

    private static string FormatDocCoverageNormal(DocumentationCoverageResults results)
    {
        var output = new StringBuilder();
        output.AppendLine($"# Documentation Coverage: {results.CoveragePercentage:F1}%");
        output.AppendLine($"Documented: {results.DocumentedSymbols}/{results.TotalSymbols}\n");

        if (results.UndocumentedSymbols.Any())
        {
            output.AppendLine("## Undocumented Symbols:");
            var grouped = results.UndocumentedSymbols.GroupBy(u => u.Kind);
            foreach (var group in grouped)
            {
                output.AppendLine($"**{group.Key}** ({group.Count()}):");
                foreach (var symbol in group.Take(10))
                {
                    output.AppendLine($"  - {symbol.Name} @ {symbol.FileName}:{symbol.LineNumber}");
                }
                if (group.Count() > 10)
                    output.AppendLine($"    ... and {group.Count() - 10} more");
                output.AppendLine();
            }
        }

        return output.ToString();
    }

    private static string FormatDocCoverageDetailed(DocumentationCoverageResults results)
    {
        var output = new StringBuilder();
        output.AppendLine($"# Documentation Coverage Analysis");
        output.AppendLine($"Coverage: {results.CoveragePercentage:F1}%");
        output.AppendLine($"Total symbols: {results.TotalSymbols}");
        output.AppendLine($"Documented: {results.DocumentedSymbols}");
        output.AppendLine($"Undocumented: {results.UndocumentedCount}\n");

        output.AppendLine("## Statistics by Kind:");
        output.AppendLine($"  Classes: {results.UndocumentedClasses} undocumented");
        output.AppendLine($"  Methods: {results.UndocumentedMethods} undocumented");
        output.AppendLine($"  Properties: {results.UndocumentedProperties} undocumented");
        output.AppendLine();

        if (results.UndocumentedSymbols.Any())
        {
            output.AppendLine("## Undocumented Symbols:");
            foreach (var symbol in results.UndocumentedSymbols.Take(50))
            {
                output.AppendLine($"### {symbol.Kind}: {symbol.FullName}");
                output.AppendLine($"  File: {symbol.FilePath}:{symbol.LineNumber}");
                output.AppendLine($"  Accessibility: {symbol.Accessibility}");
                if (!string.IsNullOrEmpty(symbol.SuggestedDocumentation))
                    output.AppendLine($"  Suggestion: {symbol.SuggestedDocumentation}");
                output.AppendLine();
            }
        }

        return output.ToString();
    }

    #endregion
}
