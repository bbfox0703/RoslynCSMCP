using ModelContextProtocol.Server;
using RoslynMcpServer.Core.Models;
using RoslynMcpServer.Core.Services;
using System.ComponentModel;
using System.Text;

namespace RoslynMcpServer.Tools
{
    /// <summary>
    /// MCP Tools for source generator opportunity analysis (full server version).
    /// </summary>
    [McpServerToolType]
    public class RefactoringTools
    {
        [McpServerTool, Description("""
            Find source generator migration opportunities to reduce boilerplate and improve performance/AOT compatibility.
            Detects: runtime Regex that can use [GeneratedRegex] (compile-time, AOT-safe),
            JsonSerializer calls without a JsonSerializerContext (add [JsonSerializable] for AOT/performance),
            manual INotifyPropertyChanged that can use [ObservableProperty] (CommunityToolkit.Mvvm),
            manual ICommand classes that can use [RelayCommand] (CommunityToolkit.Mvvm),
            and logger calls with string interpolation that should use [LoggerMessage] (zero-allocation logging).
            """)]
        public static async Task<string> FindSourceGeneratorOpportunities(
            [Description("Path to solution file (.sln)")] string solutionPath,
            [Description("Output format: summary, normal, detailed. Default: normal")] string format = "normal",
            [Description("Categories (comma-separated): GeneratedRegex, JsonSerializable, ObservableProperty, RelayCommand, LoggerMessage, all. Default: all")] string categories = "all",
            [Description("Minimum severity to report: High, Medium, Low, all. Default: all")] string severity = "all",
            SourceGeneratorOpportunityAnalyzer analyzer = null!,
            SecurityValidator validator = null!,
            McpErrorHandler errorHandler = null!)
        {
            try
            {
                var pathError = validator.ValidateSolutionPath(solutionPath, errorHandler);
                if (pathError != null) return pathError;

                var categoryArray = categories.Equals("all", StringComparison.OrdinalIgnoreCase)
                    ? new[] { "GeneratedRegex", "JsonSerializable", "ObservableProperty", "RelayCommand", "LoggerMessage" }
                    : categories.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                var results = await analyzer.AnalyzeAsync(solutionPath, categoryArray);

                // Apply severity filter
                if (!severity.Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    var severityOrder = new[] { "High", "Medium", "Low" };
                    var minIdx = Array.FindIndex(severityOrder, s => s.Equals(severity, StringComparison.OrdinalIgnoreCase));
                    if (minIdx >= 0)
                    {
                        results.Opportunities = results.Opportunities
                            .Where(o => Array.FindIndex(severityOrder, s => s == o.Severity) <= minIdx)
                            .ToList();
                    }
                }

                return format.ToLowerInvariant() switch
                {
                    "summary" => FormatSrcGenSummary(results),
                    "detailed" => FormatSrcGenDetailed(results),
                    _ => FormatSrcGenNormal(results)
                };
            }
            catch (Exception ex)
            {
                return errorHandler.HandleException(ex, "FindSourceGeneratorOpportunities");
            }
        }

        #region Formatting Methods

        private static int SrcGenSeverityOrder(string severity) => severity switch
        {
            "High" => 0,
            "Medium" => 1,
            "Low" => 2,
            _ => 99
        };

        private static string FormatSrcGenSummary(SourceGeneratorAnalysisResults results)
        {
            var output = new StringBuilder();
            output.AppendLine("Source Generator Opportunities — Summary");
            output.AppendLine($"  Analyzed projects : {results.AnalyzedProjects}");
            output.AppendLine($"  Analyzed files    : {results.AnalyzedFiles}");
            if (results.FailedProjects > 0)
                output.AppendLine($"  Failed projects   : {results.FailedProjects}");
            output.AppendLine();
            output.AppendLine("Opportunities by severity:");
            output.AppendLine($"  High   : {results.HighOpportunities}");
            output.AppendLine($"  Medium : {results.MediumOpportunities}");
            output.AppendLine($"  Low    : {results.LowOpportunities}");
            output.AppendLine($"  Total  : {results.TotalOpportunities}");
            output.AppendLine();

            if (results.OpportunitiesByCategory.Count > 0)
            {
                output.AppendLine("Opportunities by category:");
                foreach (var (cat, count) in results.OpportunitiesByCategory.OrderByDescending(kv => kv.Value))
                    output.AppendLine($"  {cat,-22}: {count}");
                output.AppendLine();
            }

            // Modernization score: 100 - (opportunities × 2), minimum 0
            var score = Math.Max(0, 100 - results.TotalOpportunities * 2);
            output.AppendLine($"Modernization Score: {score}/100");
            if (score == 100) output.AppendLine("  Excellent — already using source generators where applicable.");
            else if (score >= 75) output.AppendLine("  Good — a few source generator opportunities remain.");
            else if (score >= 50) output.AppendLine("  Fair — meaningful boilerplate reduction possible.");
            else output.AppendLine("  Poor — significant source generator adoption opportunities.");

            if (results.Warnings.Count > 0)
            {
                output.AppendLine();
                foreach (var w in results.Warnings)
                    output.AppendLine($"Warning: {w.Message}");
            }

            return output.ToString();
        }

        private static string FormatSrcGenNormal(SourceGeneratorAnalysisResults results)
        {
            if (results.TotalOpportunities == 0)
            {
                var ok = new StringBuilder();
                ok.AppendLine("No source generator opportunities found.");
                ok.AppendLine($"Analyzed {results.AnalyzedProjects} project(s), {results.AnalyzedFiles} file(s).");
                return ok.ToString();
            }

            var output = new StringBuilder();
            output.AppendLine($"Found {results.TotalOpportunities} source generator opportunity/ies " +
                              $"(High:{results.HighOpportunities} Medium:{results.MediumOpportunities} Low:{results.LowOpportunities})");
            output.AppendLine();

            var grouped = results.Opportunities.GroupBy(o => o.Category);
            foreach (var group in grouped.OrderBy(g => g.Key))
            {
                output.AppendLine($"**{group.Key}** ({group.Count()}):");
                foreach (var opp in group.OrderBy(o => SrcGenSeverityOrder(o.Severity)).Take(10))
                {
                    output.AppendLine($"  - [{opp.Severity}] {opp.Title}");
                    output.AppendLine($"    @ {opp.FileName}:{opp.LineNumber}  ({opp.ProjectName})");
                    if (!string.IsNullOrEmpty(opp.Recommendation))
                        output.AppendLine($"    → {opp.Recommendation}");
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

        private static string FormatSrcGenDetailed(SourceGeneratorAnalysisResults results)
        {
            if (results.TotalOpportunities == 0)
                return $"No source generator opportunities found. Analyzed {results.AnalyzedProjects} project(s).";

            var output = new StringBuilder();
            output.AppendLine("# Source Generator Opportunities — Detailed Report");
            output.AppendLine();
            output.AppendLine($"Projects analyzed    : {results.AnalyzedProjects}");
            output.AppendLine($"Files with findings  : {results.AnalyzedFiles}");
            output.AppendLine($"Total opportunities  : {results.TotalOpportunities} " +
                              $"(H:{results.HighOpportunities} M:{results.MediumOpportunities} L:{results.LowOpportunities})");
            output.AppendLine();

            if (results.OpportunitiesByProject.Count > 1)
            {
                output.AppendLine("## Opportunities by Project");
                foreach (var (proj, count) in results.OpportunitiesByProject.OrderByDescending(kv => kv.Value))
                    output.AppendLine($"  {proj}: {count}");
                output.AppendLine();
            }

            var grouped = results.Opportunities.GroupBy(o => o.Category).OrderBy(g => g.Key);
            foreach (var group in grouped)
            {
                output.AppendLine($"## {group.Key} ({group.Count()} opportunity/ies)");
                output.AppendLine();

                foreach (var opp in group.OrderBy(o => SrcGenSeverityOrder(o.Severity)))
                {
                    output.AppendLine($"### [{opp.Severity}] {opp.Title}");
                    output.AppendLine($"- **File**: `{opp.FilePath}:{opp.LineNumber}`");
                    output.AppendLine($"- **Project**: {opp.ProjectName}");
                    output.AppendLine($"- **Description**: {opp.Description}");
                    if (!string.IsNullOrEmpty(opp.CodeSnippet))
                        output.AppendLine($"- **Code**: `{opp.CodeSnippet}`");
                    output.AppendLine($"- **Recommendation**: {opp.Recommendation}");
                    if (!string.IsNullOrEmpty(opp.FixExample))
                    {
                        output.AppendLine("- **Fix Example**:");
                        output.AppendLine("  ```csharp");
                        foreach (var line in opp.FixExample.Split('\n'))
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

        #endregion
    }
}
