using ModelContextProtocol.Server;
using RoslynMcpServer.Core.Models;
using RoslynMcpServer.Core.Services;
using System.ComponentModel;
using System.Text;

namespace RoslynMcpServer.Tools
{
    /// <summary>
    /// MCP Tools for concurrency pattern analysis (full server version).
    /// </summary>
    [McpServerToolType]
    public class QualityTools
    {
        [McpServerTool, Description("""
            Analyze concurrency anti-patterns in C# code for correctness and performance.
            Detects: await missing .ConfigureAwait(false) (library deadlock risk),
            System.Threading.Timer not stopped in Dispose (callback-after-dispose),
            Task.WhenAll with unbounded .Select() without SemaphoreSlim throttling,
            public async methods missing CancellationToken parameter,
            CancellationToken received but not propagated to inner async calls,
            static List<T>/Dictionary<K,V> used without thread-safe alternatives,
            mutable static primitive fields without volatile/Interlocked/ThreadStatic,
            and await expressions inside lock() blocks (use SemaphoreSlim instead).
            """)]
        public static async Task<string> AnalyzeConcurrencyPatterns(
            [Description("Path to solution file (.sln)")] string solutionPath,
            [Description("Output format: summary, normal, detailed. Default: normal")] string format = "normal",
            [Description("Issue types (comma-separated): ConfigureAwait, TimerDispose, UnboundedConcurrency, MissingCancellationToken, CancellationNotPropagated, NonThreadSafeCollection, StaticFieldLock, AwaitInLock, all. Default: all")] string issueTypes = "all",
            [Description("Minimum severity to report: Critical, High, Medium, Low, all. Default: all")] string severity = "all",
            ConcurrencyPatternAnalyzer analyzer = null!,
            SecurityValidator validator = null!,
            McpErrorHandler errorHandler = null!)
        {
            try
            {
                var pathError = validator.ValidateSolutionPath(solutionPath, errorHandler);
                if (pathError != null) return pathError;

                var issueTypeArray = issueTypes.Equals("all", StringComparison.OrdinalIgnoreCase)
                    ? new[] { "ConfigureAwait", "TimerDispose", "UnboundedConcurrency", "MissingCancellationToken",
                              "CancellationNotPropagated", "NonThreadSafeCollection", "StaticFieldLock", "AwaitInLock" }
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
                    "summary" => FormatConcurrencySummary(results),
                    "detailed" => FormatConcurrencyDetailed(results),
                    _ => FormatConcurrencyNormal(results)
                };
            }
            catch (Exception ex)
            {
                return errorHandler.HandleException(ex, "AnalyzeConcurrencyPatterns");
            }
        }

        #region Formatting Methods

        private static int SeverityOrder(string severity) => severity switch
        {
            "Critical" => 0,
            "High" => 1,
            "Medium" => 2,
            "Low" => 3,
            _ => 99
        };

        private static string FormatConcurrencySummary(ConcurrencyAnalysisResults results)
        {
            var output = new StringBuilder();
            output.AppendLine("Concurrency Pattern Analysis — Summary");
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
            output.AppendLine($"Concurrency Health Score: {score}/100");
            if (score == 100) output.AppendLine("  Excellent — no concurrency issues detected.");
            else if (score >= 75) output.AppendLine("  Good — minor concurrency improvements possible.");
            else if (score >= 50) output.AppendLine("  Fair — several concurrency issues to address.");
            else output.AppendLine("  Poor — significant concurrency risks requiring attention.");

            if (results.Warnings.Count > 0)
            {
                output.AppendLine();
                foreach (var w in results.Warnings)
                    output.AppendLine($"Warning: {w.Message}");
            }

            return output.ToString();
        }

        private static string FormatConcurrencyNormal(ConcurrencyAnalysisResults results)
        {
            if (results.TotalIssues == 0)
            {
                var ok = new StringBuilder();
                ok.AppendLine("No concurrency issues found.");
                ok.AppendLine($"Analyzed {results.AnalyzedProjects} project(s), {results.AnalyzedFiles} file(s).");
                return ok.ToString();
            }

            var output = new StringBuilder();
            output.AppendLine($"Found {results.TotalIssues} concurrency issue(s) " +
                              $"(Critical:{results.CriticalIssues} High:{results.HighIssues} " +
                              $"Medium:{results.MediumIssues} Low:{results.LowIssues})");
            output.AppendLine();

            var grouped = results.Issues.GroupBy(i => i.IssueType);
            foreach (var group in grouped.OrderBy(g => g.Key))
            {
                output.AppendLine($"**{group.Key}** ({group.Count()}):");
                foreach (var issue in group.OrderBy(i => SeverityOrder(i.Severity)).Take(10))
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

        private static string FormatConcurrencyDetailed(ConcurrencyAnalysisResults results)
        {
            if (results.TotalIssues == 0)
                return $"No concurrency issues found. Analyzed {results.AnalyzedProjects} project(s).";

            var output = new StringBuilder();
            output.AppendLine("# Concurrency Pattern Analysis — Detailed Report");
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

                foreach (var issue in group.OrderBy(i => SeverityOrder(i.Severity)))
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

        #endregion
    }
}
