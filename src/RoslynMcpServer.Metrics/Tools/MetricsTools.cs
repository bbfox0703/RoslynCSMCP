using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        IServiceProvider? serviceProvider = null)
    {
        try
        {
            var validator = serviceProvider?.GetService<SecurityValidator>();
            if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
            {
                return "Error: Invalid solution path provided.";
            }

            var metricsService = serviceProvider?.GetService<CodeMetricsService>();
            if (metricsService == null)
            {
                return "Error: Code metrics service not available.";
            }

            // GetMetricsAsync returns formatted string directly
            var groupBy = format.ToLowerInvariant() == "detailed" ? "namespace" : "project";
            return await metricsService.GetMetricsAsync(solutionPath, groupBy);
        }
        catch (Exception ex)
        {
            var logger = serviceProvider?.GetService<ILogger<MetricsTools>>();
            logger?.LogError(ex, "Error getting code metrics");
            return $"Error: An unexpected error occurred while getting code metrics: {ex.Message}";
        }
    }

    [McpServerTool, Description("Get comprehensive statistics for a C# file (LOC, complexity, dependencies, documentation coverage)")]
    public static async Task<string> GetFileStatistics(
        [Description("Path to C# source file (.cs)")] string filePath,
        [Description("Output format: summary (key metrics), normal (balanced), detailed (comprehensive). Default: normal")]
        string format = "normal",
        IServiceProvider? serviceProvider = null)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return "Error: File not found.";
            }

            if (!filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                return "Error: File must be a C# source file (.cs)";
            }

            var analyzer = serviceProvider?.GetService<FileStatisticsAnalyzer>();
            if (analyzer == null)
            {
                return "Error: File statistics analyzer service not available.";
            }

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
            var logger = serviceProvider?.GetService<ILogger<MetricsTools>>();
            logger?.LogError(ex, "Error getting file statistics");
            return $"Error: An unexpected error occurred while getting file statistics: {ex.Message}";
        }
    }

    [McpServerTool, Description("Analyze XML documentation coverage for types and members")]
    public static async Task<string> AnalyzeDocumentationCoverage(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary (counts only), normal (grouped list), detailed (with suggestions). Default: normal")]
        string format = "normal",
        [Description("Scope filter: public (public only), all (all symbols). Default: public")]
        string scope = "public",
        IServiceProvider? serviceProvider = null)
    {
        try
        {
            var validator = serviceProvider?.GetService<SecurityValidator>();
            if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
            {
                return "Error: Invalid solution path provided.";
            }

            var analyzer = serviceProvider?.GetService<DocumentationAnalyzer>();
            if (analyzer == null)
            {
                return "Error: Documentation analyzer service not available.";
            }

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
            var logger = serviceProvider?.GetService<ILogger<MetricsTools>>();
            logger?.LogError(ex, "Error analyzing documentation coverage");
            return $"Error: An unexpected error occurred while analyzing documentation coverage: {ex.Message}";
        }
    }

    #region Formatting Methods

    private static string FormatCodeMetrics(object metrics, string format)
    {
        var output = new StringBuilder();
        output.AppendLine("# Solution Code Metrics");
        output.AppendLine("(Detailed metrics from CodeMetricsService)");
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
