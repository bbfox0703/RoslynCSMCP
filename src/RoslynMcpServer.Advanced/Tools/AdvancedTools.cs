using ModelContextProtocol.Server;
using RoslynMcpServer.Core.Models;
using RoslynMcpServer.Core.Services;
using System.ComponentModel;
using System.Text;

namespace RoslynMcpServer.Advanced.Tools;

/// <summary>
/// MCP Tools for advanced C# analysis.
/// This module provides 13 tools (~2,275 tokens) for advanced analysis.
/// </summary>
[McpServerToolType]
public class AdvancedTools
{
    [McpServerTool, Description("Execute multiple queries in a single batch request")]
    public static async Task<string> BatchQuery(
        [Description("JSON array of queries to execute")] string queriesJson,
        [Description("Path to solution file (.sln)")] string solutionPath,
        BatchQueryService batchService = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            return await batchService.ExecuteBatchAsync(queriesJson);
        }
        catch (Exception ex)
        {
            return errorHandler.HandleException(ex, "BatchQuery");
        }
    }

    [McpServerTool, Description("Find references with advanced filtering options to reduce noise and focus on specific usage patterns")]
    public static async Task<string> FindReferencesFiltered(
        [Description("Symbol name to find references for")] string symbolName,
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Include definitions (default: true)")] bool includeDefinition = true,
        [Description("Public only (default: false)")] bool publicOnly = false,
        [Description("Exclude tests (default: false)")] bool excludeTests = false,
        [Description("Cross-project references only (default: false)")] bool crossProjectOnly = false,
        [Description("Writes only (default: false)")] bool writesOnly = false,
        [Description("Filter by project name pattern (optional)")] string? projectFilter = null,
        SymbolSearchService searchService = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            var results = await searchService.FindReferencesFilteredAsync(
                symbolName, solutionPath, includeDefinition, publicOnly, excludeTests, crossProjectOnly, writesOnly, projectFilter);

            return FormatFilteredReferences(results, symbolName);
        }
        catch (Exception ex)
        {
            return errorHandler.HandleException(ex, "FindReferencesFiltered");
        }
    }

    [McpServerTool, Description("Find all references to a symbol across multiple solutions")]
    public static async Task<string> FindReferencesAcrossSolutions(
        [Description("Symbol name to find references for")] string symbolName,
        [Description("Comma-separated paths to solution files")] string solutionPaths,
        SymbolSearchService searchService = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            var paths = solutionPaths.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var results = await searchService.FindReferencesAcrossSolutionsAsync(symbolName, paths, includeDefinition: true);

            var groupedResults = results
                .GroupBy(r => r.ProjectName)
                .ToDictionary(g => g.Key, g => g.ToList());

            return FormatCrossReferences(groupedResults, symbolName);
        }
        catch (Exception ex)
        {
            return errorHandler.HandleException(ex, "FindReferencesAcrossSolutions");
        }
    }

    [McpServerTool, Description("Get compilation errors and warnings from solution")]
    public static async Task<string> GetCompilationErrors(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Minimum severity: Error, Warning, Info. Default: Warning")] string minSeverity = "Warning",
        [Description("Output format: summary, normal, detailed. Default: normal")] string format = "normal",
        DiagnosticsService diagnosticsService = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            var results = await diagnosticsService.GetCompilationErrorsAsync(solutionPath, minSeverity);
            return FormatCompilationErrors(results, format);
        }
        catch (Exception ex)
        {
            return errorHandler.HandleException(ex, "GetCompilationErrors");
        }
    }

    [McpServerTool, Description("Get call hierarchy showing callers and callees for a method")]
    public static async Task<string> GetCallHierarchy(
        [Description("Method name to analyze")] string methodName,
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Direction: callers, callees, both. Default: both")] string direction = "both",
        [Description("Maximum depth (default: 3)")] int maxDepth = 3,
        CallHierarchyService callService = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            return await callService.GetCallHierarchyAsync(solutionPath, methodName, direction, maxDepth);
        }
        catch (Exception ex)
        {
            return errorHandler.HandleException(ex, "GetCallHierarchy");
        }
    }

    [McpServerTool, Description("Get complete class hierarchy showing ancestors and descendants")]
    public static async Task<string> GetClassHierarchy(
        [Description("Type name to analyze")] string typeName,
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: text, mermaid, json. Default: text")] string format = "text",
        SymbolSearchService searchService = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            var results = await searchService.GetClassHierarchyAsync(typeName, solutionPath);
            return FormatClassHierarchy(results, format);
        }
        catch (Exception ex)
        {
            return errorHandler.HandleException(ex, "GetClassHierarchy");
        }
    }

    [McpServerTool, Description("Get type signature with members but without implementation")]
    public static async Task<string> GetTypeSignature(
        [Description("Type name to get signature for")] string typeName,
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Include private members (default: false)")] bool includePrivate = false,
        TypeSignatureService signatureService = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            return await signatureService.GetTypeSignatureAsync(solutionPath, typeName, includePrivate);
        }
        catch (Exception ex)
        {
            return errorHandler.HandleException(ex, "GetTypeSignature");
        }
    }

    [McpServerTool, Description("Find all usages of a specific attribute across the solution")]
    public static async Task<string> FindAttributeUsages(
        [Description("Attribute name (e.g., 'Obsolete', 'Serializable')")] string attributeName,
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary, normal, detailed. Default: normal")] string format = "normal",
        AttributeSearchService searchService = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            var results = await searchService.FindAttributeUsagesAsync(solutionPath, attributeName);
            return FormatAttributeUsages(results, attributeName, format);
        }
        catch (Exception ex)
        {
            return errorHandler.HandleException(ex, "FindAttributeUsages");
        }
    }

    [McpServerTool, Description("Find usages of deprecated/obsolete APIs in the solution")]
    public static async Task<string> FindDeprecatedAPIs(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary, normal, detailed. Default: normal")] string format = "normal",
        DeprecatedAPIAnalyzer analyzer = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            var results = await analyzer.AnalyzeDeprecatedAPIsAsync(solutionPath);
            return FormatDeprecatedAPIs(results, format);
        }
        catch (Exception ex)
        {
            return errorHandler.HandleException(ex, "FindDeprecatedAPIs");
        }
    }

    [McpServerTool, Description("Find TODO, FIXME, HACK, and other special comments in code")]
    public static async Task<string> FindTODOComments(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary, normal, detailed. Default: normal")] string format = "normal",
        [Description("Comment types to find: TODO, FIXME, HACK, NOTE, BUG, all. Default: all")] string types = "all",
        TODOCommentAnalyzer analyzer = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            var typeArray = types.Equals("all", StringComparison.OrdinalIgnoreCase)
                ? null
                : types.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var results = await analyzer.AnalyzeTODOCommentsAsync(solutionPath, typeArray!);
            return FormatTODOComments(results, format);
        }
        catch (Exception ex)
        {
            return errorHandler.HandleException(ex, "FindTODOComments");
        }
    }

    [McpServerTool, Description("Find large source files that may need refactoring")]
    public static async Task<string> FindLargeFiles(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Minimum lines to consider large (default: 500)")] int minLines = 500,
        [Description("Output format: summary, normal, detailed. Default: normal")] string format = "normal",
        LargeFileAnalyzer analyzer = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            var results = await analyzer.AnalyzeLargeFilesAsync(solutionPath, minLines);
            return FormatLargeFiles(results, format);
        }
        catch (Exception ex)
        {
            return errorHandler.HandleException(ex, "FindLargeFiles");
        }
    }

    [McpServerTool, Description("Analyze API changes between two versions of a solution")]
    public static async Task<string> AnalyzeAPIChanges(
        [Description("Path to old version solution file")] string oldSolutionPath,
        [Description("Path to new version solution file")] string newSolutionPath,
        [Description("Output format: summary, normal, detailed. Default: normal")] string format = "normal",
        APIChangeAnalyzer analyzer = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            var results = await analyzer.AnalyzeAPIChangesAsync(
                oldSolutionPath, newSolutionPath, "Old", "New", false);
            return FormatAPIChanges(results, format);
        }
        catch (Exception ex)
        {
            return errorHandler.HandleException(ex, "AnalyzeAPIChanges");
        }
    }

    [McpServerTool, Description("Find common performance anti-patterns and issues in C# code")]
    public static async Task<string> FindPerformanceIssues(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary, normal, detailed. Default: normal")] string format = "normal",
        [Description("Issue types to check (comma-separated): BoxingInLoop, StringConcatInLoop, LinqInLoop, all. Default: all")] string issueTypes = "all",
        PerformanceIssueAnalyzer analyzer = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            var issueTypeArray = issueTypes.Equals("all", StringComparison.OrdinalIgnoreCase)
                ? null
                : issueTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var results = await analyzer.AnalyzePerformanceIssuesAsync(solutionPath, issueTypeArray);
            return FormatPerformanceIssues(results, format);
        }
        catch (Exception ex)
        {
            return errorHandler.HandleException(ex, "FindPerformanceIssues");
        }
    }

    [McpServerTool, Description("""
        Analyze IPC (Inter-Process Communication) patterns for named pipes and JSON-RPC.
        Detects: NamedPipeClientStream/ServerStream usage and configuration (NamedPipeUsage),
        StreamJsonRpc / JSON-RPC InvokeAsync call sites (JsonRpcPattern),
        pipe Read/Write/Connect without IOException handling (IpcErrorHandling),
        synchronous pipe I/O inside async methods (SynchronousPipeIo),
        Connect/WaitForConnection without timeout or CancellationToken (MissingPipeTimeout),
        hardcoded string literals used as pipe names (HardcodedPipeName),
        pipes used without StreamReader/Writer buffering (UnbufferedPipe),
        and JSON-RPC InvokeAsync without RemoteInvocationException handling (JsonRpcMissingErrorHandling).
        """)]
    public static async Task<string> AnalyzeIpcPatterns(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary, normal, detailed. Default: normal")] string format = "normal",
        [Description("Issue types (comma-separated): NamedPipeUsage, JsonRpcPattern, IpcErrorHandling, SynchronousPipeIo, MissingPipeTimeout, HardcodedPipeName, UnbufferedPipe, JsonRpcMissingErrorHandling, all. Default: all")] string issueTypes = "all",
        [Description("Minimum severity to report: Critical, High, Medium, Low, all. Default: all")] string severity = "all",
        IpcPatternAnalyzer analyzer = null!,
        SecurityValidator validator = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            var pathError = validator.ValidateSolutionPath(solutionPath, errorHandler);
            if (pathError != null) return pathError;

            var issueTypeArray = issueTypes.Equals("all", StringComparison.OrdinalIgnoreCase)
                ? new[] { "NamedPipeUsage", "JsonRpcPattern", "IpcErrorHandling", "SynchronousPipeIo",
                          "MissingPipeTimeout", "HardcodedPipeName", "UnbufferedPipe", "JsonRpcMissingErrorHandling" }
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
                "summary" => FormatIpcSummary(results),
                "detailed" => FormatIpcDetailed(results),
                _ => FormatIpcNormal(results)
            };
        }
        catch (Exception ex)
        {
            return errorHandler.HandleException(ex, "AnalyzeIpcPatterns");
        }
    }

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

    private static int IpcSeverityOrder(string severity) => severity switch
    {
        "Critical" => 0,
        "High" => 1,
        "Medium" => 2,
        "Low" => 3,
        _ => 99
    };

    private static string FormatIpcSummary(IpcAnalysisResults results)
    {
        var output = new StringBuilder();
        output.AppendLine("IPC Pattern Analysis — Summary");
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
                output.AppendLine($"  {type,-30}: {count}");
            output.AppendLine();
        }

        var score = Math.Max(0, 100 - results.CriticalIssues * 10 - results.HighIssues * 5
                                     - results.MediumIssues * 2 - results.LowIssues);
        output.AppendLine($"IPC Reliability Score: {score}/100");
        if (score == 100) output.AppendLine("  Excellent — no IPC issues detected.");
        else if (score >= 75) output.AppendLine("  Good — minor IPC improvements possible.");
        else if (score >= 50) output.AppendLine("  Fair — several IPC reliability issues to address.");
        else output.AppendLine("  Poor — significant IPC risks requiring attention.");

        if (results.Warnings.Count > 0)
        {
            output.AppendLine();
            foreach (var w in results.Warnings)
                output.AppendLine($"Warning: {w.Message}");
        }

        return output.ToString();
    }

    private static string FormatIpcNormal(IpcAnalysisResults results)
    {
        if (results.TotalIssues == 0)
        {
            var ok = new StringBuilder();
            ok.AppendLine("No IPC issues found.");
            ok.AppendLine($"Analyzed {results.AnalyzedProjects} project(s), {results.AnalyzedFiles} file(s).");
            return ok.ToString();
        }

        var output = new StringBuilder();
        output.AppendLine($"Found {results.TotalIssues} IPC issue(s) " +
                          $"(Critical:{results.CriticalIssues} High:{results.HighIssues} " +
                          $"Medium:{results.MediumIssues} Low:{results.LowIssues})");
        output.AppendLine();

        var grouped = results.Issues.GroupBy(i => i.IssueType);
        foreach (var group in grouped.OrderBy(g => g.Key))
        {
            output.AppendLine($"**{group.Key}** ({group.Count()}):");
            foreach (var issue in group.OrderBy(i => IpcSeverityOrder(i.Severity)).Take(10))
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

    private static string FormatIpcDetailed(IpcAnalysisResults results)
    {
        if (results.TotalIssues == 0)
            return $"No IPC issues found. Analyzed {results.AnalyzedProjects} project(s).";

        var output = new StringBuilder();
        output.AppendLine("# IPC Pattern Analysis — Detailed Report");
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

            foreach (var issue in group.OrderBy(i => IpcSeverityOrder(i.Severity)))
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

    private static int IntegritySeverityOrder(string severity) => severity switch
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
            foreach (var issue in group.OrderBy(i => IntegritySeverityOrder(i.Severity)).Take(10))
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
        output.AppendLine($"Projects analyzed : {results.AnalyzedProjects}");
        output.AppendLine($"Files with patterns: {results.AnalyzedFiles}");
        output.AppendLine($"Total patterns    : {results.TotalIssues} " +
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

            foreach (var issue in group.OrderBy(i => IntegritySeverityOrder(i.Severity)))
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

    private static string FormatFilteredReferences(IEnumerable<ReferenceResult> results, string symbolName)
    {
        if (!results.Any())
            return $"No references found for '{symbolName}'.";

        var output = new StringBuilder();
        output.AppendLine($"Found {results.Count()} references to '{symbolName}':\n");

        var grouped = results.GroupBy(r => r.DocumentPath);
        foreach (var group in grouped)
        {
            output.AppendLine($"**{Path.GetFileName(group.Key)}**");
            foreach (var r in group.Take(10))
                output.AppendLine($"  Line {r.LineNumber}: {r.LineText.Trim()}");
            if (group.Count() > 10)
                output.AppendLine($"  ... and {group.Count() - 10} more");
            output.AppendLine();
        }

        return output.ToString();
    }

    private static string FormatCrossReferences(Dictionary<string, List<ReferenceResult>> results, string symbolName)
    {
        if (!results.Any() || results.Values.All(v => !v.Any()))
            return $"No references found for '{symbolName}' across solutions.";

        var output = new StringBuilder();
        var total = results.Values.Sum(v => v.Count);
        output.AppendLine($"Found {total} references to '{symbolName}' across {results.Count} solutions:\n");

        foreach (var (solution, refs) in results.Where(kv => kv.Value.Any()))
        {
            output.AppendLine($"## {Path.GetFileName(solution)} ({refs.Count} refs)");
            foreach (var r in refs.Take(5))
                output.AppendLine($"  - {Path.GetFileName(r.DocumentPath)}:{r.LineNumber}");
            if (refs.Count > 5)
                output.AppendLine($"  ... and {refs.Count - 5} more");
            output.AppendLine();
        }

        return output.ToString();
    }

    private static string FormatCompilationErrors(CompilationErrorResults results, string format)
    {
        if (!results.Errors.Any())
            return "No compilation errors or warnings found.";

        var output = new StringBuilder();

        if (format == "summary")
        {
            var errors = results.Errors.Count(e => e.Severity == "Error");
            var warnings = results.Errors.Count(e => e.Severity == "Warning");
            output.AppendLine($"Compilation: {errors} errors, {warnings} warnings");
            return output.ToString();
        }

        output.AppendLine($"# Compilation Diagnostics ({results.Errors.Count}):\n");

        var grouped = results.Errors.GroupBy(e => e.Severity);
        foreach (var group in grouped.OrderBy(g => g.Key))
        {
            output.AppendLine($"## {group.Key} ({group.Count()}):");
            foreach (var error in group.Take(format == "detailed" ? 50 : 10))
            {
                output.AppendLine($"  - {error.Id}: {error.Message}");
                output.AppendLine($"    @ {error.FileName}:{error.LineNumber}");
            }
            output.AppendLine();
        }

        return output.ToString();
    }

    private static string FormatClassHierarchy(ClassHierarchyResult? result, string format)
    {
        if (result == null)
            return "Type not found.";

        var output = new StringBuilder();
        output.AppendLine($"# Class Hierarchy: {result.TypeName}");
        output.AppendLine($"Kind: {result.TypeKind}");
        output.AppendLine($"Namespace: {result.Namespace}\n");

        if (result.Ancestors.Any())
        {
            output.AppendLine("## Ancestors:");
            foreach (var ancestor in result.Ancestors)
                output.AppendLine($"  - {ancestor.FullName} ({ancestor.TypeKind})");
            output.AppendLine();
        }

        if (result.Descendants.Any())
        {
            output.AppendLine("## Descendants:");
            foreach (var desc in result.Descendants)
                output.AppendLine($"  - {desc.FullName} ({desc.TypeKind})");
        }

        return output.ToString();
    }

    private static string FormatAttributeUsages(AttributeSearchResults results, string attributeName, string format)
    {
        if (!results.Usages.Any())
            return $"No usages found for attribute '{attributeName}'.";

        var output = new StringBuilder();
        output.AppendLine($"# Attribute Usages: [{attributeName}]");
        output.AppendLine($"Found {results.Usages.Count} usages:\n");

        var grouped = results.Usages.GroupBy(u => u.TargetType);
        foreach (var group in grouped)
        {
            output.AppendLine($"## {group.Key} ({group.Count()}):");
            foreach (var usage in group.Take(10))
                output.AppendLine($"  - {usage.TargetName} @ {usage.FileName}:{usage.LineNumber}");
            if (group.Count() > 10)
                output.AppendLine($"  ... and {group.Count() - 10} more");
            output.AppendLine();
        }

        return output.ToString();
    }

    private static string FormatDeprecatedAPIs(DeprecatedAPIResults results, string format)
    {
        if (!results.DeprecatedAPIs.Any())
            return "No deprecated API usages found.";

        var output = new StringBuilder();
        output.AppendLine($"# Deprecated API Usages");
        output.AppendLine($"Total: {results.TotalUsages} usages of {results.TotalDeprecatedAPIs} deprecated APIs\n");

        foreach (var api in results.DeprecatedAPIs.Take(format == "detailed" ? 50 : 20))
        {
            output.AppendLine($"## {api.APIName}");
            output.AppendLine($"  Message: {api.ObsoleteMessage}");
            output.AppendLine($"  Usages: {api.Usages.Count}");
            foreach (var usage in api.Usages.Take(5))
                output.AppendLine($"    - {usage.FileName}:{usage.LineNumber}");
            output.AppendLine();
        }

        return output.ToString();
    }

    private static string FormatTODOComments(TODOCommentResults results, string format)
    {
        if (!results.Comments.Any())
            return "No TODO comments found.";

        var output = new StringBuilder();
        output.AppendLine($"# TODO Comments ({results.TotalComments})");
        output.AppendLine($"TODO: {results.TODOCount} | FIXME: {results.FIXMECount} | HACK: {results.HACKCount}\n");

        var grouped = results.Comments.GroupBy(c => c.Type);
        foreach (var group in grouped)
        {
            output.AppendLine($"## {group.Key} ({group.Count()}):");
            foreach (var comment in group.Take(10))
                output.AppendLine($"  - {comment.FileName}:{comment.LineNumber}: {comment.Message.Substring(0, Math.Min(50, comment.Message.Length))}...");
            if (group.Count() > 10)
                output.AppendLine($"  ... and {group.Count() - 10} more");
            output.AppendLine();
        }

        return output.ToString();
    }

    private static string FormatLargeFiles(LargeFileResults results, string format)
    {
        if (!results.LargeFiles.Any())
            return "No large files found.";

        var output = new StringBuilder();
        output.AppendLine($"# Large Files ({results.TotalLargeFiles})");
        output.AppendLine($"Average: {results.AverageLineCount} lines | Max: {results.MaxLineCount} lines\n");

        foreach (var file in results.LargeFiles.OrderByDescending(f => f.LineCount).Take(20))
        {
            output.AppendLine($"  - {file.FileName}: {file.LineCount} lines ({file.TypeCount} types, {file.MethodCount} methods)");
        }

        return output.ToString();
    }

    private static string FormatAPIChanges(APIChangeResults results, string format)
    {
        var output = new StringBuilder();
        output.AppendLine($"# API Changes Analysis");
        output.AppendLine($"Breaking changes: {results.BreakingChanges}");
        output.AppendLine($"Added: {results.AddedSymbols} | Removed: {results.RemovedSymbols} | Modified: {results.ModifiedSymbols}");
        output.AppendLine($"Recommended version bump: {results.RecommendedVersionBump}\n");

        if (results.Changes.Any(c => c.ImpactLevel == "Breaking"))
        {
            output.AppendLine("## Breaking Changes:");
            foreach (var change in results.Changes.Where(c => c.ImpactLevel == "Breaking").Take(20))
            {
                output.AppendLine($"  - {change.SymbolName}: {change.ChangeType}");
                output.AppendLine($"    {change.Description}");
            }
        }

        return output.ToString();
    }

    private static string FormatPerformanceIssues(PerformanceIssueResults results, string format)
    {
        if (!results.Issues.Any())
            return "No performance issues found.";

        var output = new StringBuilder();
        output.AppendLine($"# Performance Issues ({results.TotalIssues})");
        output.AppendLine($"Critical: {results.CriticalIssues} | High: {results.HighIssues} | Medium: {results.MediumIssues}\n");

        var grouped = results.Issues.GroupBy(i => i.IssueType);
        foreach (var group in grouped)
        {
            output.AppendLine($"## {group.Key} ({group.Count()}):");
            foreach (var issue in group.Take(format == "detailed" ? 20 : 5))
            {
                output.AppendLine($"  - [{issue.Severity}] {issue.Title}");
                output.AppendLine($"    @ {issue.FileName}:{issue.LineNumber}");
                if (format == "detailed")
                    output.AppendLine($"    Recommendation: {issue.Recommendation}");
            }
            output.AppendLine();
        }

        return output.ToString();
    }

    #endregion
}
