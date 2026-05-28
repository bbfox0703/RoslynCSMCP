using ModelContextProtocol.Server;
using RoslynMcpServer.Core.Models;
using RoslynMcpServer.Core.Services;
using System.ComponentModel;
using System.Text;

namespace RoslynMcpServer.Tools
{
    /// <summary>
    /// MCP Tools for Native AOT and interop compatibility analysis (full server version).
    /// Provides 2 tools: AOT compatibility analysis and P/Invoke analysis.
    /// </summary>
    [McpServerToolType]
    public class InteropTools
    {
        [McpServerTool, Description("""
            Analyze Native AOT and trimming compatibility issues across C#, XAML (.axaml), and .csproj.
            Detects: reflection patterns incompatible with AOT (Type.GetType, Activator.CreateInstance, etc.),
            JSON serializer reflection overloads (per-call) and JsonValue.Create, runtime Regex that should
            use [GeneratedRegex], methods missing [RequiresUnreferencedCode] / [DynamicallyAccessedMembers],
            Avalonia ResourceInclude/StyleInclude created in code-behind, [DllImport] that should use
            [LibraryImport], Assembly.GetExecutingAssembly() (trimmed in single-file AOT), Avalonia AppBuilder
            chains missing .UseHarfBuzz()/RedirectionSurface or using UsePlatformDetect(), AOT-hostile XAML
            (<Run> bindings, missing x:DataType, DataGridTemplateColumn sort), and AOT-hostile .csproj build
            settings (BuiltInComInteropSupport, Avalonia.Desktop on win-x64, missing TrimmerRootAssembly).
            """)]
        public static async Task<string> AnalyzeAotCompatibility(
            [Description("Path to solution file (.sln)")] string solutionPath,
            [Description("Output format: summary, normal, detailed. Default: normal")] string format = "normal",
            [Description("Categories (comma-separated): Reflection, JsonSerialization, GeneratedRegex, TrimAnnotation, AvaloniaRuntime, DllImport, AssemblyApi, AvaloniaAppBuilder, Xaml, BuildConfig, all. Default: all")] string categories = "all",
            [Description("Minimum severity to report: Critical, High, Medium, Low, all. Default: all")] string severity = "all",
            AotCompatibilityAnalyzer analyzer = null!,
            SecurityValidator validator = null!,
            McpErrorHandler errorHandler = null!)
        {
            try
            {
                var pathError = validator.ValidateSolutionPath(solutionPath, errorHandler);
                if (pathError != null) return pathError;

                var categoryArray = categories.Equals("all", StringComparison.OrdinalIgnoreCase)
                    ? new[] { "Reflection", "JsonSerialization", "GeneratedRegex", "TrimAnnotation", "AvaloniaRuntime", "DllImport", "AssemblyApi", "AvaloniaAppBuilder", "Xaml", "BuildConfig" }
                    : categories.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                var results = await analyzer.AnalyzeAsync(solutionPath, categoryArray);

                // Apply severity filter
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
                    "summary" => FormatSummary(results),
                    "detailed" => FormatDetailed(results),
                    _ => FormatNormal(results)
                };
            }
            catch (Exception ex)
            {
                return errorHandler.HandleException(ex, "AnalyzeAotCompatibility");
            }
        }

        [McpServerTool, Description("""
            Analyze P/Invoke and native interop patterns for migration, safety, and modernization opportunities.
            Detects: [DllImport] methods that should migrate to [LibraryImport] (AOT-compatible),
            Marshal.PtrToStringAuto usage, bool parameters missing [MarshalAs(UnmanagedType.Bool)],
            [DllImport] with SetLastError without checking Marshal.GetLastWin32Error(),
            GCHandle/Marshal resource leaks, IntPtr/UIntPtr modernization, magic offset numbers,
            and NativeLibrary.SetDllImportResolver without error handling.
            """)]
        public static async Task<string> AnalyzePInvoke(
            [Description("Path to solution file (.sln)")] string solutionPath,
            [Description("Output format: summary, normal, detailed. Default: normal")] string format = "normal",
            [Description("Issue types (comma-separated): DllImportMigration, MarshalUnsafety, MissingMarshalAs, SetLastError, ResourceLeak, ModernizeTypes, MagicOffset, NativeLibrary, all. Default: all")] string issueTypes = "all",
            [Description("Minimum severity to report: Critical, High, Medium, Low, all. Default: all")] string severity = "all",
            PInvokeCompatibilityAnalyzer analyzer = null!,
            SecurityValidator validator = null!,
            McpErrorHandler errorHandler = null!)
        {
            try
            {
                var pathError = validator.ValidateSolutionPath(solutionPath, errorHandler);
                if (pathError != null) return pathError;

                var issueTypeArray = issueTypes.Equals("all", StringComparison.OrdinalIgnoreCase)
                    ? new[] { "DllImportMigration", "MarshalUnsafety", "MissingMarshalAs", "SetLastError", "ResourceLeak", "ModernizeTypes", "MagicOffset", "NativeLibrary" }
                    : issueTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                var results = await analyzer.AnalyzeAsync(solutionPath, issueTypeArray);

                // Apply severity filter
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
                    "summary" => FormatPInvokeSummary(results),
                    "detailed" => FormatPInvokeDetailed(results),
                    _ => FormatPInvokeNormal(results)
                };
            }
            catch (Exception ex)
            {
                return errorHandler.HandleException(ex, "AnalyzePInvoke");
            }
        }

        [McpServerTool, Description("""
            Analyze unsafe C# code for risks: stack overflow from stackalloc, missing Span<T> wrapping,
            stackalloc inside loops, pointer arithmetic without bounds checks, void* casts with alignment risks,
            unsafe pointers captured in cross-thread lambdas (Task.Run/ThreadPool),
            oversized fixed blocks causing GC pressure, and async methods that are also unsafe.
            """)]
        public static async Task<string> AnalyzeUnsafeCode(
            [Description("Path to solution file (.sln)")] string solutionPath,
            [Description("Output format: summary, normal, detailed. Default: normal")] string format = "normal",
            [Description("Issue types (comma-separated): StackAllocSize, StackAllocNoSpan, StackAllocInLoop, PointerArithmetic, VoidPointerCast, CrossThreadPointer, OversizedFixed, UnsafeAsync, all. Default: all")] string issueTypes = "all",
            [Description("Minimum severity to report: Critical, High, Medium, Low, all. Default: all")] string severity = "all",
            UnsafeCodeAnalyzer analyzer = null!,
            SecurityValidator validator = null!,
            McpErrorHandler errorHandler = null!)
        {
            try
            {
                var pathError = validator.ValidateSolutionPath(solutionPath, errorHandler);
                if (pathError != null) return pathError;

                var issueTypeArray = issueTypes.Equals("all", StringComparison.OrdinalIgnoreCase)
                    ? new[] { "StackAllocSize", "StackAllocNoSpan", "StackAllocInLoop", "PointerArithmetic", "VoidPointerCast", "CrossThreadPointer", "OversizedFixed", "UnsafeAsync" }
                    : issueTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                var results = await analyzer.AnalyzeAsync(solutionPath, issueTypeArray);

                // Apply severity filter
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
                    "summary" => FormatUnsafeSummary(results),
                    "detailed" => FormatUnsafeDetailed(results),
                    _ => FormatUnsafeNormal(results)
                };
            }
            catch (Exception ex)
            {
                return errorHandler.HandleException(ex, "AnalyzeUnsafeCode");
            }
        }

        #region Formatting Methods

        private static string FormatSummary(AotCompatibilityResults results)
        {
            var output = new StringBuilder();
            output.AppendLine("Native AOT Compatibility Analysis — Summary");
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

            if (results.IssuesByCategory.Count > 0)
            {
                output.AppendLine("Issues by category:");
                foreach (var (cat, count) in results.IssuesByCategory.OrderByDescending(kv => kv.Value))
                    output.AppendLine($"  {cat,-20}: {count}");
                output.AppendLine();
            }

            // AOT compatibility score: 100 − (issues × 5), minimum 0
            var score = Math.Max(0, 100 - results.TotalIssues * 5);
            output.AppendLine($"AOT Compatibility Score: {score}/100");
            if (score == 100) output.AppendLine("  Excellent — no issues detected.");
            else if (score >= 75) output.AppendLine("  Good — minor issues to address.");
            else if (score >= 50) output.AppendLine("  Fair — significant work needed before AOT publish.");
            else output.AppendLine("  Poor — many AOT-incompatible patterns detected.");

            if (results.Warnings.Count > 0)
            {
                output.AppendLine();
                foreach (var w in results.Warnings)
                    output.AppendLine($"Warning: {w.Message}");
            }

            return output.ToString();
        }

        private static string FormatNormal(AotCompatibilityResults results)
        {
            if (results.TotalIssues == 0)
            {
                var ok = new StringBuilder();
                ok.AppendLine("No AOT compatibility issues found.");
                ok.AppendLine($"Analyzed {results.AnalyzedProjects} project(s), {results.AnalyzedFiles} file(s).");
                return ok.ToString();
            }

            var output = new StringBuilder();
            output.AppendLine($"Found {results.TotalIssues} AOT compatibility issue(s) " +
                              $"(Critical:{results.CriticalIssues} High:{results.HighIssues} " +
                              $"Medium:{results.MediumIssues} Low:{results.LowIssues})");
            output.AppendLine();

            var grouped = results.Issues.GroupBy(i => i.Category);
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

        private static string FormatDetailed(AotCompatibilityResults results)
        {
            if (results.TotalIssues == 0)
                return $"No AOT compatibility issues found. Analyzed {results.AnalyzedProjects} project(s).";

            var output = new StringBuilder();
            output.AppendLine("# Native AOT Compatibility Analysis — Detailed Report");
            output.AppendLine();
            output.AppendLine($"Projects analyzed : {results.AnalyzedProjects}");
            output.AppendLine($"Files with issues : {results.AnalyzedFiles}");
            output.AppendLine($"Total issues      : {results.TotalIssues} " +
                              $"(C:{results.CriticalIssues} H:{results.HighIssues} " +
                              $"M:{results.MediumIssues} L:{results.LowIssues})");
            output.AppendLine();

            // By project summary
            if (results.IssuesByProject.Count > 1)
            {
                output.AppendLine("## Issues by Project");
                foreach (var (proj, count) in results.IssuesByProject.OrderByDescending(kv => kv.Value))
                    output.AppendLine($"  {proj}: {count}");
                output.AppendLine();
            }

            // Detailed issues grouped by category
            var grouped = results.Issues.GroupBy(i => i.Category).OrderBy(g => g.Key);
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

        private static int SeverityOrder(string severity) => severity switch
        {
            "Critical" => 0,
            "High" => 1,
            "Medium" => 2,
            "Low" => 3,
            _ => 99
        };

        private static string FormatPInvokeSummary(PInvokeAnalysisResults results)
        {
            var output = new StringBuilder();
            output.AppendLine("P/Invoke Compatibility Analysis — Summary");
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
                    output.AppendLine($"  {type,-22}: {count}");
                output.AppendLine();
            }

            // P/Invoke compatibility score: 100 - (issues × 3), minimum 0
            var score = Math.Max(0, 100 - results.TotalIssues * 3);
            output.AppendLine($"P/Invoke Health Score: {score}/100");
            if (score == 100) output.AppendLine("  Excellent — no issues detected.");
            else if (score >= 75) output.AppendLine("  Good — minor modernization opportunities.");
            else if (score >= 50) output.AppendLine("  Fair — several interop issues to address.");
            else output.AppendLine("  Poor — significant P/Invoke safety or migration work needed.");

            if (results.Warnings.Count > 0)
            {
                output.AppendLine();
                foreach (var w in results.Warnings)
                    output.AppendLine($"Warning: {w.Message}");
            }

            return output.ToString();
        }

        private static string FormatPInvokeNormal(PInvokeAnalysisResults results)
        {
            if (results.TotalIssues == 0)
            {
                var ok = new StringBuilder();
                ok.AppendLine("No P/Invoke issues found.");
                ok.AppendLine($"Analyzed {results.AnalyzedProjects} project(s), {results.AnalyzedFiles} file(s).");
                return ok.ToString();
            }

            var output = new StringBuilder();
            output.AppendLine($"Found {results.TotalIssues} P/Invoke issue(s) " +
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

        private static string FormatPInvokeDetailed(PInvokeAnalysisResults results)
        {
            if (results.TotalIssues == 0)
                return $"No P/Invoke issues found. Analyzed {results.AnalyzedProjects} project(s).";

            var output = new StringBuilder();
            output.AppendLine("# P/Invoke Compatibility Analysis — Detailed Report");
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

        private static string FormatUnsafeSummary(UnsafeCodeAnalysisResults results)
        {
            var output = new StringBuilder();
            output.AppendLine("Unsafe Code Analysis — Summary");
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
                    output.AppendLine($"  {type,-22}: {count}");
                output.AppendLine();
            }

            var score = Math.Max(0, 100 - results.CriticalIssues * 10 - results.HighIssues * 5
                                         - results.MediumIssues * 3 - results.LowIssues);
            output.AppendLine($"Unsafe Code Safety Score: {score}/100");
            if (score == 100) output.AppendLine("  Excellent — no unsafe code risks detected.");
            else if (score >= 75) output.AppendLine("  Good — minor unsafe code issues to review.");
            else if (score >= 50) output.AppendLine("  Fair — several unsafe code risks to address.");
            else output.AppendLine("  Poor — significant unsafe code risks requiring attention.");

            if (results.Warnings.Count > 0)
            {
                output.AppendLine();
                foreach (var w in results.Warnings)
                    output.AppendLine($"Warning: {w.Message}");
            }

            return output.ToString();
        }

        private static string FormatUnsafeNormal(UnsafeCodeAnalysisResults results)
        {
            if (results.TotalIssues == 0)
            {
                var ok = new StringBuilder();
                ok.AppendLine("No unsafe code issues found.");
                ok.AppendLine($"Analyzed {results.AnalyzedProjects} project(s), {results.AnalyzedFiles} file(s).");
                return ok.ToString();
            }

            var output = new StringBuilder();
            output.AppendLine($"Found {results.TotalIssues} unsafe code issue(s) " +
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

        private static string FormatUnsafeDetailed(UnsafeCodeAnalysisResults results)
        {
            if (results.TotalIssues == 0)
                return $"No unsafe code issues found. Analyzed {results.AnalyzedProjects} project(s).";

            var output = new StringBuilder();
            output.AppendLine("# Unsafe Code Analysis — Detailed Report");
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
