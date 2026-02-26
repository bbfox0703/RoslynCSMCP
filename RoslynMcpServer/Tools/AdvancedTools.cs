using ModelContextProtocol.Server;
using RoslynMcpServer.Core.Models;
using RoslynMcpServer.Core.Services;
using System.ComponentModel;
using System.Text;

namespace RoslynMcpServer.Tools
{
    /// <summary>
    /// MCP Tools for advanced integrity and protection pattern analysis (full server version).
    /// </summary>
    [McpServerToolType]
    public class AdvancedTools
    {
        [McpServerTool, Description("""
            Analyze integrity verification and software protection patterns in C# code.
            Detects: SHA256/MD5/SHA1 runtime hash computations used as integrity sentinels,
            XOR cipher patterns in loops used for string/byte obfuscation (XOR key ^ data),
            hardcoded byte arrays of 16/20/32/64 bytes matching MD5/SHA1/SHA256/SHA512 digest sizes,
            anti-debug patterns (Debugger.IsAttached, Debugger.Launch, Environment.FailFast),
            and magic hex sentinel constants (0xDEADBEEF, 0xCAFEBABE, etc.) in comparisons.
            Useful for auditing security-sensitive code, anti-tamper mechanisms, and protection layers.
            """)]
        public static async Task<string> AnalyzeIntegrityPatterns(
            [Description("Path to solution file (.sln)")] string solutionPath,
            [Description("Output format: summary, normal, detailed. Default: normal")] string format = "normal",
            [Description("Issue types (comma-separated): Sha256Sentinel, XorStringProtection, HardcodedChecksum, AntiDebugPattern, SentinelMagicBytes, all. Default: all")] string issueTypes = "all",
            [Description("Minimum severity to report: Critical, High, Medium, Low, all. Default: all")] string severity = "all",
            IntegrityPatternAnalyzer analyzer = null!,
            SecurityValidator validator = null!,
            McpErrorHandler errorHandler = null!)
        {
            try
            {
                var pathError = validator.ValidateSolutionPath(solutionPath, errorHandler);
                if (pathError != null) return pathError;

                var issueTypeArray = issueTypes.Equals("all", StringComparison.OrdinalIgnoreCase)
                    ? new[] { "Sha256Sentinel", "XorStringProtection", "HardcodedChecksum", "AntiDebugPattern", "SentinelMagicBytes" }
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
                    "summary" => FormatIntegritySummary(results),
                    "detailed" => FormatIntegrityDetailed(results),
                    _ => FormatIntegrityNormal(results)
                };
            }
            catch (Exception ex)
            {
                return errorHandler.HandleException(ex, "AnalyzeIntegrityPatterns");
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

        private static string FormatIntegritySummary(IntegrityAnalysisResults results)
        {
            var output = new StringBuilder();
            output.AppendLine("Integrity Pattern Analysis — Summary");
            output.AppendLine($"  Analyzed projects : {results.AnalyzedProjects}");
            output.AppendLine($"  Analyzed files    : {results.AnalyzedFiles}");
            if (results.FailedProjects > 0)
                output.AppendLine($"  Failed projects   : {results.FailedProjects}");
            output.AppendLine();
            output.AppendLine("Patterns by severity:");
            output.AppendLine($"  Critical : {results.CriticalIssues}");
            output.AppendLine($"  High     : {results.HighIssues}");
            output.AppendLine($"  Medium   : {results.MediumIssues}");
            output.AppendLine($"  Low      : {results.LowIssues}");
            output.AppendLine($"  Total    : {results.TotalIssues}");
            output.AppendLine();

            if (results.IssuesByType.Count > 0)
            {
                output.AppendLine("Patterns by type:");
                foreach (var (type, count) in results.IssuesByType.OrderByDescending(kv => kv.Value))
                    output.AppendLine($"  {type,-26}: {count}");
                output.AppendLine();
            }

            if (results.TotalIssues == 0)
                output.AppendLine("No integrity/protection patterns detected.");
            else if (results.HighIssues > 0)
                output.AppendLine($"Found {results.HighIssues} high-severity pattern(s) requiring review.");
            else
                output.AppendLine("No high-severity patterns found. Review medium/low findings for completeness.");

            if (results.Warnings.Count > 0)
            {
                output.AppendLine();
                foreach (var w in results.Warnings)
                    output.AppendLine($"Warning: {w.Message}");
            }

            return output.ToString();
        }

        private static string FormatIntegrityNormal(IntegrityAnalysisResults results)
        {
            if (results.TotalIssues == 0)
            {
                var ok = new StringBuilder();
                ok.AppendLine("No integrity/protection patterns detected.");
                ok.AppendLine($"Analyzed {results.AnalyzedProjects} project(s), {results.AnalyzedFiles} file(s).");
                return ok.ToString();
            }

            var output = new StringBuilder();
            output.AppendLine($"Found {results.TotalIssues} integrity pattern(s) " +
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
                    if (!string.IsNullOrEmpty(issue.Notes))
                        output.AppendLine($"    → {issue.Notes}");
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

        private static string FormatIntegrityDetailed(IntegrityAnalysisResults results)
        {
            if (results.TotalIssues == 0)
                return $"No integrity/protection patterns detected. Analyzed {results.AnalyzedProjects} project(s).";

            var output = new StringBuilder();
            output.AppendLine("# Integrity Pattern Analysis — Detailed Report");
            output.AppendLine();
            output.AppendLine($"Projects analyzed  : {results.AnalyzedProjects}");
            output.AppendLine($"Files with patterns: {results.AnalyzedFiles}");
            output.AppendLine($"Total patterns     : {results.TotalIssues} " +
                              $"(C:{results.CriticalIssues} H:{results.HighIssues} " +
                              $"M:{results.MediumIssues} L:{results.LowIssues})");
            output.AppendLine();

            if (results.IssuesByProject.Count > 1)
            {
                output.AppendLine("## Patterns by Project");
                foreach (var (proj, count) in results.IssuesByProject.OrderByDescending(kv => kv.Value))
                    output.AppendLine($"  {proj}: {count}");
                output.AppendLine();
            }

            var grouped = results.Issues.GroupBy(i => i.IssueType).OrderBy(g => g.Key);
            foreach (var group in grouped)
            {
                output.AppendLine($"## {group.Key} ({group.Count()} finding(s))");
                output.AppendLine();

                foreach (var issue in group.OrderBy(i => SeverityOrder(i.Severity)))
                {
                    output.AppendLine($"### [{issue.Severity}] {issue.Title}");
                    output.AppendLine($"- **File**: `{issue.FilePath}:{issue.LineNumber}`");
                    output.AppendLine($"- **Project**: {issue.ProjectName}");
                    output.AppendLine($"- **Description**: {issue.Description}");
                    if (!string.IsNullOrEmpty(issue.CodeSnippet))
                        output.AppendLine($"- **Code**: `{issue.CodeSnippet}`");
                    if (!string.IsNullOrEmpty(issue.Notes))
                        output.AppendLine($"- **Notes**: {issue.Notes}");
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
