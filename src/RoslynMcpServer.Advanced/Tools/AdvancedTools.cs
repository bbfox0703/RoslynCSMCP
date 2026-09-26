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
    [McpServerTool, Description("""
        Run several read-only queries in one call. Returns each result under a "Query N: <tool>" header,
        then a count of succeeded and failed queries; one failing query does not stop the others.
        Supports only these tools, with these parameter names (? = optional):
        SearchSymbols (solutionPath, searchPattern, symbolKind?, ignoreCase?),
        FindReferences (solutionPath, symbolName, includeDefinition?),
        GetSymbolInfo (solutionPath, symbolName),
        GetCodeMetrics (solutionPath, groupBy?),
        GetDependencyGraph (solutionPath, format?, includePackages?),
        GetCallHierarchy (solutionPath, methodName, direction?, maxDepth?),
        AnalyzeDependencies (solutionPath, maxDepth?).
        Batch parameter names differ from the standalone tools (searchPattern/symbolKind, not pattern/symbolTypes),
        and the standalone tools expose more options, so call them directly when you need those options
        or any tool not listed here.
        """)]
    public static async Task<string> BatchQuery(
        [Description("JSON array of objects, each with a \"tool\" name and a \"parameters\" object. Tool names are the PascalCase names listed above, matched case-insensitively; snake_case names such as search_symbols are not recognized.")] string queriesJson,
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

    [McpServerTool, Description("""
        Find source references to every symbol whose simple name equals symbolName, ignoring case (framework members
        included, declaration sites not included), and narrow them by project name pattern, test-project exclusion,
        cross-project usage, or write access. The write filter is a syntax heuristic: references inside an
        assignment or ++/-- expression count, including right-hand-side reads, while out and ref arguments are
        missed. Output is a total count and code lines grouped by file, up to 10 per file.
        """)]
    public static async Task<string> FindReferencesFiltered(
        [Description("Simple (unqualified) symbol name, matched case-insensitively against every declared symbol, including framework members; forms like 'Ns.Type.Member' do not match.")] string symbolName,
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Currently has no effect; declaration sites are never returned.")] bool includeDefinition = true,
        [Description("If the first symbol matching the name is not declared public, nothing is returned; otherwise no references are filtered out.")] bool publicOnly = false,
        [Description("Drop references in projects whose name contains 'test' or 'spec' (case-insensitive substring).")] bool excludeTests = false,
        [Description("Keep only references outside the project that declares the symbol (the first match when several share the name).")] bool crossProjectOnly = false,
        [Description("Keep only references inside an assignment, compound assignment, or ++/-- expression (syntax heuristic: right-hand-side reads are also kept; out and ref arguments are missed).")] bool writesOnly = false,
        [Description("Project name wildcard pattern (* and ?), matched case-insensitively against the whole name.")] string? projectFilter = null,
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

    [McpServerTool, Description("""
        Find source references to a symbol in each of several solutions and merge them, dropping duplicate
        locations. symbolName is a simple name matched ignoring case, and every same-named symbol is combined,
        framework members included; qualified names do not match. Output groups references by project (under
        headings labeled as solutions), up to 5 file:line locations each. Declaration sites are not included, and a
        solution that fails to load contributes nothing without an error.
        """)]
    public static async Task<string> FindReferencesAcrossSolutions(
        [Description("Simple (unqualified) symbol name, matched case-insensitively against every declared symbol, including framework members; forms like 'Ns.Type.Member' do not match.")] string symbolName,
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

    [McpServerTool, Description("""
        Report compiler diagnostics (CS codes) for every project by compiling the solution in memory, without
        running a build. Analyzer rules (CA, IDE, StyleCop), NuGet and MSBuild errors, and diagnostics without a
        source location are not included. Results are grouped by severity with file:line, up to 10 per severity (50
        in detailed) without noting omissions; summary gives only error and warning counts.
        """)]
    public static async Task<string> GetCompilationErrors(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Severity to return, case-insensitive: Error, Warning, or Info returns only that severity (not that level and above); All returns every severity, including hidden ones (default: All).")] string minSeverity = "All",
        [Description("Output format, lowercase: summary (error and warning counts), normal (first 10 per severity), detailed (first 50 per severity). Default: normal")] string format = "normal",
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

    [McpServerTool, Description("""
        List the callers and callees of one method as trees up to maxDepth levels deep (callers of callers, callees
        of callees). The method is found by exact, case-sensitive simple name among method declarations in source,
        and the first declaration found is used, so other overloads and same-named methods in other types are
        ignored. Callers show one entry per calling member with a call count. Callees include only calls to methods
        declared in source, one entry per overload; framework calls, constructors, and property accesses are
        omitted. Each method is expanded once (later repeats and recursive calls are marked, not expanded), and
        each direction stops after 200 entries, listing shallower levels first.
        """)]
    public static async Task<string> GetCallHierarchy(
        [Description("Simple method name, case-sensitive, without type or parameters (e.g., 'SaveAsync'); the first matching declaration in the solution is used.")] string methodName,
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Direction: both, callers, or callees, case-insensitive; other values return an error (default: both).")] string direction = "both",
        [Description("Number of levels to follow: 1 lists only direct callers and callees; values below 1 count as 1 and above 10 as 10 (default: 3).")] int maxDepth = 3,
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

    [McpServerTool, Description("""
        Show the inheritance tree of one type, up to 10 levels each way: ancestors (base-class chain excluding
        System.Object, plus declared interfaces, recursively, including framework types) and descendants (source
        types that derive from or directly implement it, recursively, including through constructed generic bases
        such as Base<int>). The name is a simple type name matched case-insensitively; qualified names are not
        accepted. Source types, nested ones included, are searched first and the first match wins; framework types
        are used only when no source type matches. A descendant appears once under each type it directly derives
        from or implements.
        """)]
    public static async Task<string> GetClassHierarchy(
        [Description("Simple type name without namespace or generic arguments, matched case-insensitively; source types are searched before referenced assemblies and the first match is used.")] string typeName,
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: compact (indented type names), normal (full names, kinds, and file:line), detailed (adds project, namespace, abstract marker, and the target's documentation). Default: normal")] string format = "normal",
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

    [McpServerTool, Description("""
        Return a C#-style outline of one type: its declaration (modifiers, type parameters, base class, interfaces)
        and member signatures without bodies, grouped as fields, constructors, properties, events, and methods, with
        <summary> doc text. Nested types, operators, attributes, generic constraints, parameter modifiers, and
        default values are omitted. The name is matched case-sensitively and the first match in project order wins;
        a namespace-qualified name can also resolve a framework type.
        """)]
    public static async Task<string> GetTypeSignature(
        [Description("Type name, case-sensitive: simple ('UserService'), namespace-qualified ('MyProject.Services.UserService'), generic ('MyProject.Repo<T>'), or nested metadata form ('MyProject.Outer+Inner').")] string typeName,
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Include non-public members (private, internal, private protected). When false, only public, protected, and protected internal members are listed (default: false).")] bool includePrivate = false,
        TypeSignatureService signatureService = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            return await signatureService.GetTypeSignatureAsync(typeName: typeName, solutionPath: solutionPath, includePrivate: includePrivate);
        }
        catch (Exception ex)
        {
            return errorHandler.HandleException(ex, "GetTypeSignature");
        }
    }

    [McpServerTool, Description("""
        Find where an attribute is applied in source across all projects and list each target's name and file:line,
        grouped by target kind (class, method, property, parameter, and so on), up to 10 per kind; attribute
        arguments are not shown. The attribute is matched by simple class name, case-insensitively, with or without
        the Attribute suffix; qualified names do not match, and same-named attributes from different namespaces are
        combined. Attributes on regular fields, field-like events, and the assembly are not found. For call sites of
        [Obsolete] members, use FindDeprecatedAPIs.
        """)]
    public static async Task<string> FindAttributeUsages(
        [Description("Attribute class simple name, with or without the 'Attribute' suffix (e.g., 'Obsolete'), matched case-insensitively; namespace-qualified names do not match.")] string attributeName,
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Currently ignored.")] string format = "normal",
        AttributeSearchService searchService = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            var results = await searchService.FindAttributeUsagesAsync(attributeName: attributeName, solutionPath: solutionPath);
            return FormatAttributeUsages(results, attributeName, format);
        }
        catch (Exception ex)
        {
            return errorHandler.HandleException(ex, "FindAttributeUsages");
        }
    }

    [McpServerTool, Description("""
        Find references to symbols marked [Obsolete], declared in the solution or in referenced assemblies, plus any
        use of six legacy types regardless of attributes: BinaryFormatter, WebRequest, HttpWebRequest,
        ServicePointManager, MD5, and SHA1. Only simple identifiers are checked, so obsolete constructors and
        explicitly generic calls such as M<int>() are missed. Returns up to 20 APIs (50 in detailed), each with its
        obsolete message, usage count, and first 5 locations. A path that fails to load returns the same text as a
        clean result.
        """)]
    public static async Task<string> FindDeprecatedAPIs(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: 'detailed' lists up to 50 APIs instead of 20; 'summary' and 'normal' are identical. Default: normal")] string format = "normal",
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

    [McpServerTool, Description("""
        Scan every comment in the solution's compiled files, including XML doc comments, for TODO, FIXME, HACK,
        NOTE, BUG, XXX, OPTIMIZE, and REFACTOR markers. Markers must be whole words, so 'debug' or 'notes' do not
        count; a marker in any case is accepted at the start of a comment line, elsewhere only in upper case. Each
        comment line yields at most one marker: the first one of a requested type. Returns up to 10 entries per
        marker type with file:line and the first 50 characters of the text. A path that fails to load returns the
        same text as a clean result.
        """)]
    public static async Task<string> FindTODOComments(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Currently ignored; all values produce the same output. Default: normal")] string format = "normal",
        [Description("Comma-separated marker types (case-insensitive): TODO, FIXME, HACK, NOTE, BUG, XXX, OPTIMIZE, REFACTOR, or all. Default: all")] string types = "all",
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

    [McpServerTool, Description("""
        List source files whose physical line count (blank and comment lines included) is at least minLines, largest
        first, skipping generated files (.g.cs, .designer.cs, .Generated.cs) and obj/bin output. Returns the number
        of large files, their average and maximum line counts, and the top 20 with type and method counts. A path
        that fails to load returns the same text as a clean result.
        """)]
    public static async Task<string> FindLargeFiles(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Minimum physical line count, inclusive; values below 100 are replaced with 500 (default: 500)")] int minLines = 500,
        [Description("Currently ignored; all values produce the same output. Default: normal")] string format = "normal",
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

    [McpServerTool, Description("""
        Compare the public API (public and protected types, methods, properties, fields, and events; internal
        members are not compared) of two solutions and recommend a Major, Minor, or Patch version bump. Symbols are
        matched by documentation comment ID, so each overload and each same-named member of another type is compared
        separately; a changed parameter list shows as a removal plus an addition, and a changed return type as a
        signature change. Removals, return-type and property-type changes, base-type changes, abstract/sealed
        changes, and reduced visibility outside the assembly count as breaking. Lists at most 20 breaking changes and
        only counts additions; a load failure is reported as a warning.
        """)]
    public static async Task<string> AnalyzeAPIChanges(
        [Description("Path to old version solution file")] string oldSolutionPath,
        [Description("Path to new version solution file")] string newSolutionPath,
        [Description("Currently ignored; all values produce the same output. Default: normal")] string format = "normal",
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

    [McpServerTool, Description("""
        Run five heuristics over every file in the solution. LinqMisuse (every Enumerable.Count() call, nested
        ToList(), ToList() inside a foreach) and SyncOverAsync (any .Result or .Wait inside an async method) are
        name-based and produce false positives. StringConcatenation reports each string += x or s = s + x inside a
        loop once, skipping strings declared inside that loop. DisposableNotDisposed reports locals created with new,
        a static factory, or a Create/Open/Begin call that are never disposed, returned, stored, or passed on, and
        instance fields a type creates but never disposes. ExceptionHandling reports each empty catch block once.
        Groups issues by type, 5 per type (20 in detailed). A path that fails to load returns the same text as a
        clean result.
        """)]
    public static async Task<string> FindPerformanceIssues(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: 'detailed' shows 20 issues per type with recommendations; 'summary' and 'normal' both show 5. Default: normal")] string format = "normal",
        [Description("Comma-separated, case-sensitive: LinqMisuse, StringConcatenation, SyncOverAsync, DisposableNotDisposed, ExceptionHandling, or all. Unrecognized names run no checks. Default: all")] string issueTypes = "all",
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

        // "text" was the historical default and is treated as normal, as is any unknown value
        var normalizedFormat = (format ?? "normal").Trim().ToLowerInvariant();

        var output = new StringBuilder();
        output.AppendLine($"# Class Hierarchy: {result.TypeName}");
        output.AppendLine($"Kind: {result.TypeKind}");
        output.AppendLine($"Namespace: {result.Namespace}");

        if (normalizedFormat == "detailed")
        {
            var modifiers = (result.IsAbstract ? ", abstract" : "") + (result.IsSealed ? ", sealed" : "");
            output.AppendLine($"Accessibility: {result.Accessibility}{modifiers}");
            if (result.LineNumber > 0)
                output.AppendLine($"Location: {result.FilePath}:{result.LineNumber}");
            if (!string.IsNullOrWhiteSpace(result.Documentation))
                output.AppendLine($"Documentation: {result.Documentation}");
        }

        output.AppendLine();

        if (result.Ancestors.Any())
        {
            output.AppendLine($"## Ancestors ({CountHierarchyNodes(result.Ancestors)}):");
            AppendHierarchyNodes(output, result.Ancestors, "  ", normalizedFormat);
            output.AppendLine();
        }

        if (result.Descendants.Any())
        {
            output.AppendLine($"## Descendants ({CountHierarchyNodes(result.Descendants)}):");
            AppendHierarchyNodes(output, result.Descendants, "  ", normalizedFormat);
        }

        return output.ToString();
    }

    private static void AppendHierarchyNodes(StringBuilder output, List<HierarchyNode> nodes, string indent, string format)
    {
        foreach (var node in nodes)
        {
            switch (format)
            {
                case "compact":
                    output.AppendLine($"{indent}- {node.Name}");
                    break;

                case "detailed":
                    var abstractMarker = node.IsAbstract && !node.IsInterface ? ", abstract" : "";
                    output.AppendLine($"{indent}- {node.FullName} ({node.TypeKind}{abstractMarker})");
                    output.AppendLine($"{indent}  Project: {node.ProjectName} | Namespace: {node.Namespace}");
                    if (node.LineNumber > 0)
                        output.AppendLine($"{indent}  Location: {node.FilePath}:{node.LineNumber}");
                    break;

                default:
                    var location = node.LineNumber > 0 ? $" @ {Path.GetFileName(node.FilePath)}:{node.LineNumber}" : "";
                    output.AppendLine($"{indent}- {node.FullName} ({node.TypeKind}){location}");
                    break;
            }

            if (node.Children.Any())
                AppendHierarchyNodes(output, node.Children, indent + "  ", format);
        }
    }

    private static int CountHierarchyNodes(List<HierarchyNode> nodes) =>
        nodes.Sum(n => 1 + CountHierarchyNodes(n.Children));

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

        if (results.Warnings.Any())
        {
            output.AppendLine("## Warnings:");
            foreach (var warning in results.Warnings)
                output.AppendLine($"  - [{warning.Context}] {warning.Message}");
            output.AppendLine();
        }

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
