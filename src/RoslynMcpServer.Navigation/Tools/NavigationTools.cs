using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcpServer.Core.Models;
using RoslynMcpServer.Core.Services;
using System.ComponentModel;
using System.Text;

namespace RoslynMcpServer.Navigation.Tools;

/// <summary>
/// MCP Tools for C# code navigation.
/// This module provides navigation tools with cursor-based pagination support.
/// </summary>
[McpServerToolType]
public class NavigationTools
{
    #region Tool Methods

    [McpServerTool, Description("""
        Search every project in a solution for types and members whose simple or fully qualified name matches a
        wildcard pattern (* and ?, matched against the whole name). Symbols from referenced assemblies such as the
        .NET framework are included, and a symbol visible to several projects is listed once per project. Results
        are ranked with exact and prefix matches first and returned one page at a time, with a nextCursor when more
        remain. Members of nested types are not searched.
        """)]
    public static async Task<string> SearchSymbols(
        [Description("Wildcard pattern (* and ?) matched against the whole simple name or fully qualified name, e.g. 'User*', '*Service', 'MyApp.Services.*'.")] string pattern,
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Comma-separated kinds: class, interface, struct, enum, method, property, field, event, namespace. class, interface, struct, and enum all select every type declaration, so they cannot be told apart.")] string symbolTypes = "class,interface,method,property",
        [Description("Whether to ignore case in search")] bool ignoreCase = true,
        [Description("Number of results per page (default: 20, max: 100)")] int pageSize = 20,
        [Description("Cursor for pagination: pass nextCursor from the previous response with the same query arguments; if they differ, the first page is returned.")] string? cursor = null,
        SymbolSearchService searchService = null!,
        SecurityValidator validator = null!,
        McpErrorHandler errorHandler = null!,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(pattern))
                return McpError.InvalidParams("pattern", "Search pattern cannot be empty").ToToolResponse();

            var pathError = validator.ValidateSolutionPath(solutionPath, errorHandler);
            if (pathError != null) return pathError;

            var sanitizedPattern = validator.SanitizeSearchPattern(pattern);

            var paginationRequest = new PaginationRequest { PageSize = pageSize, Cursor = cursor };
            paginationRequest.Validate();

            cancellationToken.ThrowIfCancellationRequested();

            var results = await searchService.SearchSymbolsAsync(
                sanitizedPattern, solutionPath, symbolTypes, ignoreCase);

            cancellationToken.ThrowIfCancellationRequested();

            var queryHash = paginationRequest.ComputeQueryHash(pattern, solutionPath, symbolTypes, ignoreCase.ToString());
            var paginatedResults = PaginatedResult<SymbolSearchResult>.FromCursor(
                results, cursor, paginationRequest.PageSize, queryHash);

            return FormatSearchResultsPaginated(paginatedResults);
        }
        catch (OperationCanceledException)
        {
            return McpError.Create(
                McpErrorCodes.OperationCancelled,
                "Search operation was cancelled",
                new McpErrorData { Operation = "SearchSymbols", Reason = "Cancelled by client request" }
            ).ToToolResponse();
        }
        catch (FileNotFoundException ex)
        {
            return McpError.SolutionNotFound(ex.FileName ?? solutionPath).ToToolResponse();
        }
        catch (UnauthorizedAccessException)
        {
            return McpError.AccessDenied(solutionPath, "Please check file permissions").ToToolResponse();
        }
        catch (Exception ex)
        {
            return errorHandler.HandleException(ex, "SearchSymbols");
        }
    }

    [McpServerTool, Description("""
        Find source references to every symbol whose simple name equals symbolName, ignoring case; overloads,
        same-named members of other types, and same-named framework members are combined, and qualified names are
        not supported. References are grouped by file, one entry per line, and returned one page at a time with a
        nextCursor. Declaration sites are not included, and a symbol with no references returns a symbol-not-found
        error.
        """)]
    public static async Task<string> FindReferences(
        [Description("Simple (unqualified) symbol name, matched case-insensitively against every declared symbol, including framework members; forms like 'Ns.Type.Member' do not match.")] string symbolName,
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Detail level: summary (file stats only), locations (with code lines), full (with 5-line context). Default: locations")]
        string detailLevel = "locations",
        [Description("Currently has no effect; declaration sites are never returned.")] bool includeDefinition = true,
        [Description("Number of results per page (default: 20, max: 100)")] int pageSize = 20,
        [Description("Cursor for pagination: pass nextCursor from the previous response with the same query arguments; if they differ, the first page is returned.")] string? cursor = null,
        SymbolSearchService searchService = null!,
        SecurityValidator validator = null!,
        McpErrorHandler errorHandler = null!,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(symbolName))
                return McpError.InvalidParams("symbolName", "Symbol name cannot be empty").ToToolResponse();

            var pathError = validator.ValidateSolutionPath(solutionPath, errorHandler);
            if (pathError != null) return pathError;

            var paginationRequest = new PaginationRequest { PageSize = pageSize, Cursor = cursor };
            paginationRequest.Validate();

            cancellationToken.ThrowIfCancellationRequested();

            var results = await searchService.FindReferencesAsync(symbolName, solutionPath, includeDefinition);

            if (!results.Any())
                return McpError.SymbolNotFound(symbolName, solutionPath).ToToolResponse();

            var queryHash = paginationRequest.ComputeQueryHash(symbolName, solutionPath, detailLevel, includeDefinition.ToString());
            var paginatedResults = PaginatedResult<ReferenceResult>.FromCursor(
                results, cursor, paginationRequest.PageSize, queryHash);

            return detailLevel.ToLower() switch
            {
                "summary" => FormatReferencesSummaryPaginated(paginatedResults),
                "locations" => FormatReferencesLocationsPaginated(paginatedResults),
                "full" => FormatReferencesFullPaginated(paginatedResults),
                _ => FormatReferencesLocationsPaginated(paginatedResults)
            };
        }
        catch (OperationCanceledException)
        {
            return McpError.Create(
                McpErrorCodes.OperationCancelled,
                "FindReferences operation was cancelled",
                new McpErrorData { Operation = "FindReferences", Reason = "Cancelled by client request" }
            ).ToToolResponse();
        }
        catch (FileNotFoundException ex)
        {
            return McpError.SolutionNotFound(ex.FileName ?? solutionPath).ToToolResponse();
        }
        catch (Exception ex)
        {
            return errorHandler.HandleException(ex, "FindReferences");
        }
    }

    [McpServerTool, Description("""
        Find references the way FindReferences does (simple name, ignoring case, every same-named symbol combined,
        declaration sites not included) and narrow them by project name pattern or by excluding test projects.
        Results are grouped by file and returned one page at a time with a nextCursor; an empty result returns 'No
        references found.'
        """)]
    public static async Task<string> FindReferencesFiltered(
        [Description("Simple (unqualified) symbol name, matched case-insensitively against every declared symbol, including framework members; forms like 'Ns.Type.Member' do not match.")] string symbolName,
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Detail level: summary (file stats only), locations (with code lines), full (with 5-line context). Default: locations")]
        string detailLevel = "locations",
        [Description("Currently has no effect; declaration sites are never returned.")] bool includeDefinition = true,
        [Description("Project name wildcard pattern (* and ?), matched case-insensitively against the whole name.")] string? projectFilter = null,
        [Description("Drop references in projects whose name contains 'test' or 'spec' (case-insensitive substring).")] bool excludeTests = false,
        [Description("Currently has no effect.")] bool crossProjectOnly = false,
        [Description("Currently unreliable: keeps type references and drops field, property, and method references instead of detecting writes.")] bool writesOnly = false,
        [Description("Currently ignored.")] bool publicOnly = false,
        [Description("Number of results per page (default: 20, max: 100)")] int pageSize = 20,
        [Description("Cursor for pagination: pass nextCursor from the previous response with the same query arguments; if they differ, the first page is returned.")] string? cursor = null,
        SymbolSearchService searchService = null!,
        SecurityValidator validator = null!,
        McpErrorHandler errorHandler = null!,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(symbolName))
                return McpError.InvalidParams("symbolName", "Symbol name cannot be empty").ToToolResponse();

            var pathError = validator.ValidateSolutionPath(solutionPath, errorHandler);
            if (pathError != null) return pathError;

            var paginationRequest = new PaginationRequest { PageSize = pageSize, Cursor = cursor };
            paginationRequest.Validate();

            cancellationToken.ThrowIfCancellationRequested();

            var results = await searchService.FindReferencesAsync(symbolName, solutionPath, includeDefinition);

            // Apply filters
            var filteredResults = results.AsEnumerable();

            if (!string.IsNullOrEmpty(projectFilter))
            {
                var filterPattern = "^" + System.Text.RegularExpressions.Regex.Escape(projectFilter)
                    .Replace("\\*", ".*").Replace("\\?", ".") + "$";
                var filterRegex = new System.Text.RegularExpressions.Regex(filterPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                filteredResults = filteredResults.Where(r => filterRegex.IsMatch(r.ProjectName));
            }

            if (excludeTests)
            {
                var testPatterns = new[] { "test", "tests", "testing", "spec", "specs" };
                filteredResults = filteredResults.Where(r =>
                    !testPatterns.Any(p => r.ProjectName.Contains(p, StringComparison.OrdinalIgnoreCase)));
            }

            if (crossProjectOnly)
            {
                var definitionProject = results.FirstOrDefault(r => r.IsDefinition)?.ProjectName;
                if (!string.IsNullOrEmpty(definitionProject))
                {
                    filteredResults = filteredResults.Where(r =>
                        !r.ProjectName.Equals(definitionProject, StringComparison.OrdinalIgnoreCase) || r.IsDefinition);
                }
            }

            if (writesOnly)
            {
                var writeKinds = new[] { "assignment", "increment", "decrement", "compound", "out", "ref" };
                filteredResults = filteredResults.Where(r =>
                    writeKinds.Any(k => r.ReferenceKind.Contains(k, StringComparison.OrdinalIgnoreCase)) || r.IsDefinition);
            }

            var queryHash = paginationRequest.ComputeQueryHash(
                symbolName, solutionPath, detailLevel, includeDefinition.ToString(),
                projectFilter ?? "", excludeTests.ToString(), crossProjectOnly.ToString(),
                writesOnly.ToString(), publicOnly.ToString());
            var paginatedResults = PaginatedResult<ReferenceResult>.FromCursor(
                filteredResults, cursor, paginationRequest.PageSize, queryHash);

            return detailLevel.ToLower() switch
            {
                "summary" => FormatReferencesSummaryPaginated(paginatedResults),
                "locations" => FormatReferencesLocationsPaginated(paginatedResults),
                "full" => FormatReferencesFullPaginated(paginatedResults),
                _ => FormatReferencesLocationsPaginated(paginatedResults)
            };
        }
        catch (OperationCanceledException)
        {
            return McpError.Create(
                McpErrorCodes.OperationCancelled,
                "FindReferencesFiltered operation was cancelled",
                new McpErrorData { Operation = "FindReferencesFiltered", Reason = "Cancelled by client request" }
            ).ToToolResponse();
        }
        catch (FileNotFoundException ex)
        {
            return McpError.SolutionNotFound(ex.FileName ?? solutionPath).ToToolResponse();
        }
        catch (Exception ex)
        {
            return errorHandler.HandleException(ex, "FindReferencesFiltered");
        }
    }

    [McpServerTool, Description("""
        Describe one declared symbol found by simple name, ignoring case: kind, accessibility, namespace, return or
        property type, and source file and line; the full level adds declaring type, parameters, and the XML
        documentation comment. When several symbols share the name, framework members included, only the first one
        found is described, with no indication that others exist. Qualified names return 'Symbol not found.' Field
        types and attributes are not reported.
        """)]
    public static async Task<string> GetSymbolInfo(
        [Description("Simple (unqualified) symbol name, matched case-insensitively; qualified names are not supported. When several symbols share the name, the first one found is used.")] string symbolName,
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Detail level: summary (minimal), basic (balanced), full (comprehensive). Default: basic")]
        string detailLevel = "basic",
        SymbolSearchService searchService = null!,
        SecurityValidator validator = null!,
        McpErrorHandler errorHandler = null!,
        ILogger<NavigationTools> logger = null!,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var pathError = validator.ValidateSolutionPath(solutionPath, errorHandler);
            if (pathError != null) return pathError;

            var info = await searchService.GetSymbolInfoAsync(symbolName, solutionPath);

            return detailLevel.ToLowerInvariant() switch
            {
                "summary" => FormatSymbolInfoSummary(info),
                "basic" => FormatSymbolInfoBasic(info),
                "full" => FormatSymbolInfoFull(info),
                _ => FormatSymbolInfoBasic(info)
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting symbol info for: {SymbolName}", symbolName);
            return errorHandler.HandleException(ex, "GetSymbolInfo");
        }
    }

    [McpServerTool, Description("""
        List the source-declared types of every project in a solution, grouped by project and namespace, with each
        type's kind and accessibility and optionally its member signatures. Nested types appear under their
        namespace without their containing type, and projects with no matching types are omitted. Output is not
        truncated, so large solutions produce long output unless filtered by namespace. Does not show project
        references, file paths, or line numbers.
        """)]
    public static async Task<string> GetProjectStructure(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Include member signatures (default: false)")] bool includeMembers = false,
        [Description("Keep only types whose namespace contains this text (case-insensitive substring, no wildcards). Optional.")] string? namespaceFilter = null,
        [Description("Include only public types, and only public members when includeMembers is true (default: true).")] bool publicOnly = true,
        ProjectStructureService structureService = null!,
        SecurityValidator validator = null!,
        McpErrorHandler errorHandler = null!,
        ILogger<NavigationTools> logger = null!,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var pathError = validator.ValidateSolutionPath(solutionPath, errorHandler);
            if (pathError != null) return pathError;

            return await structureService.GetStructureAsync(
                solutionPath,
                includeMembers,
                namespaceFilter,
                publicOnly);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting project structure");
            return errorHandler.HandleException(ex, "GetProjectStructure");
        }
    }

    [McpServerTool, Description("""
        Parse a single .cs file on its own, without loading a solution, and outline its classes, interfaces,
        structs, enums, and records with their constructors, fields, properties, methods, and events, shown by kind
        and name; detailed mode adds accessibility and type. Signatures and documentation comments are not shown.
        Only syntax is read, so accessibility comes from written modifiers (none is reported as Private). Nested
        types are listed separately; enum members, indexers, operators, delegates, and field-like events are
        omitted.
        """)]
    public static async Task<string> GetFileOutline(
        [Description("Path to C# source file (.cs)")] string filePath,
        [Description("Output mode: compact (minimal info), normal (balanced), detailed (comprehensive). Default: normal")]
        string mode = "normal",
        [Description("Maximum members listed per type in normal and detailed modes; 0 shows all (default: 10).")] int maxMembers = 10,
        [Description("List members in normal mode; compact and detailed modes ignore it (default: true).")] bool includeMembers = true,
        [Description("Currently ignored; no documentation is output.")] bool includeDocumentation = true,
        FileAnalysisService fileAnalysisService = null!,
        McpErrorHandler errorHandler = null!,
        ILogger<NavigationTools> logger = null!,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(filePath))
                return McpError.InvalidParams("filePath", "File not found").ToToolResponse();

            if (!filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                return McpError.InvalidParams("filePath", "File must be a C# source file (.cs)").ToToolResponse();

            var outline = await fileAnalysisService.GetFileOutlineAsync(filePath);

            return mode.ToLowerInvariant() switch
            {
                "compact" => FormatFileOutlineCompact(outline, maxMembers),
                "detailed" => FormatFileOutlineDetailed(outline, maxMembers, includeDocumentation),
                "normal" => FormatFileOutlineNormal(outline, maxMembers, includeMembers, includeDocumentation),
                _ => FormatFileOutlineNormal(outline, maxMembers, includeMembers, includeDocumentation)
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting file outline for: {FilePath}", filePath);
            return errorHandler.HandleException(ex, "GetFileOutline");
        }
    }

    [McpServerTool, Description("""
        Find source types that implement an interface (directly, through a base class, or through an inherited
        interface) or derive from an abstract class at any depth; depending on format it returns names only, names
        with project, file, and line, or full details including implemented interfaces. The target is the first type
        whose simple name matches, ignoring case, and can be a framework type such as IDisposable. A concrete class,
        an unknown name, and no matches all return the same no-implementations message, and the same type can be
        listed more than once.
        """)]
    public static async Task<string> FindImplementations(
        [Description("Simple (unqualified) name of an interface or abstract class, matched case-insensitively; the first matching type is used.")] string typeName,
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary (names only), normal (balanced), detailed (comprehensive). Default: normal")]
        string format = "normal",
        [Description("Also list abstract classes and, for an interface target, derived interfaces (default: false).")] bool includeAbstractImplementations = false,
        SymbolSearchService searchService = null!,
        SecurityValidator validator = null!,
        McpErrorHandler errorHandler = null!,
        ILogger<NavigationTools> logger = null!,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var pathError = validator.ValidateSolutionPath(solutionPath, errorHandler);
            if (pathError != null) return pathError;

            var results = await searchService.FindImplementationsAsync(
                typeName,
                solutionPath,
                includeAbstractImplementations);

            return format.ToLowerInvariant() switch
            {
                "summary" => FormatImplementationResultsSummary(results, typeName),
                "detailed" => FormatImplementationResultsDetailed(results, typeName),
                "normal" => FormatImplementationResults(results, typeName),
                _ => FormatImplementationResults(results, typeName)
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error finding implementations for: {TypeName}", typeName);
            return errorHandler.HandleException(ex, "FindImplementations");
        }
    }

    #endregion

    #region Formatting Methods

    #region Paginated Formatting Methods

    private static string FormatSearchResultsPaginated(PaginatedResult<SymbolSearchResult> paginatedResults)
    {
        var output = new StringBuilder();

        // Header with pagination info
        output.AppendLine("# Symbol Search Results");
        output.AppendLine();
        output.Append(paginatedResults.FormatPaginationHeader());
        output.AppendLine();

        if (!paginatedResults.Items.Any())
        {
            output.AppendLine("No symbols found matching the pattern.");
            return output.ToString();
        }

        // Group by category for display
        var grouped = paginatedResults.Items.GroupBy(r => r.Category);

        foreach (var group in grouped.OrderBy(g => g.Key))
        {
            output.AppendLine($"**{group.Key}** ({group.Count()}):");
            foreach (var result in group)
            {
                output.AppendLine($"  - `{result.Name}` in {result.Location}");
                if (!string.IsNullOrEmpty(result.Summary))
                    output.AppendLine($"    {result.Summary}");
            }
            output.AppendLine();
        }

        // Footer with pagination cursor
        output.Append(paginatedResults.FormatPaginationFooter());

        return output.ToString();
    }

    private static string FormatReferencesSummaryPaginated(PaginatedResult<ReferenceResult> paginatedResults)
    {
        if (!paginatedResults.Items.Any())
            return "No references found.";

        var output = new StringBuilder();
        var symbolName = paginatedResults.Items.First().SymbolName;

        // Header with pagination info
        output.AppendLine($"# References to '{symbolName}'");
        output.AppendLine();
        output.Append(paginatedResults.FormatPaginationHeader());
        output.AppendLine();

        var groupedByFile = paginatedResults.Items.GroupBy(r => r.DocumentPath).OrderBy(g => g.Key);

        foreach (var fileGroup in groupedByFile)
        {
            var fileName = Path.GetFileName(fileGroup.Key);
            var count = fileGroup.Count();
            output.AppendLine($"  {fileName}: {count} references");
        }

        output.Append(paginatedResults.FormatPaginationFooter());

        return output.ToString();
    }

    private static string FormatReferencesLocationsPaginated(PaginatedResult<ReferenceResult> paginatedResults)
    {
        if (!paginatedResults.Items.Any())
            return "No references found.";

        var output = new StringBuilder();
        var symbolName = paginatedResults.Items.First().SymbolName;

        // Header with pagination info
        output.AppendLine($"# References to '{symbolName}'");
        output.AppendLine();
        output.Append(paginatedResults.FormatPaginationHeader());
        output.AppendLine();

        var groupedByFile = paginatedResults.Items.GroupBy(r => r.DocumentPath).OrderBy(g => g.Key);

        foreach (var fileGroup in groupedByFile)
        {
            output.AppendLine($"**{Path.GetFileName(fileGroup.Key)}**");
            foreach (var reference in fileGroup.OrderBy(r => r.LineNumber))
            {
                var icon = reference.IsDefinition ? "[DEF]" : "";
                output.AppendLine($"  Line {reference.LineNumber}: {reference.LineText.Trim()} {icon}");
            }
            output.AppendLine();
        }

        output.Append(paginatedResults.FormatPaginationFooter());

        return output.ToString();
    }

    private static string FormatReferencesFullPaginated(PaginatedResult<ReferenceResult> paginatedResults)
    {
        if (!paginatedResults.Items.Any())
            return "No references found.";

        var output = new StringBuilder();
        var symbolName = paginatedResults.Items.First().SymbolName;

        // Header with pagination info
        output.AppendLine($"# References to '{symbolName}'");
        output.AppendLine();
        output.Append(paginatedResults.FormatPaginationHeader());
        output.AppendLine();

        var groupedByFile = paginatedResults.Items.GroupBy(r => r.DocumentPath).OrderBy(g => g.Key);

        foreach (var fileGroup in groupedByFile)
        {
            output.AppendLine($"**{Path.GetFileName(fileGroup.Key)}** ({fileGroup.Count()} references):");
            foreach (var reference in fileGroup.OrderBy(r => r.LineNumber))
            {
                var icon = reference.IsDefinition ? "[DEFINITION]" : "";
                output.AppendLine($"  Line {reference.LineNumber}: {reference.ReferenceKind} {icon}");
                if (reference.Context != null && reference.Context.Any())
                {
                    foreach (var line in reference.Context)
                        output.AppendLine($"    {line}");
                }
                else
                {
                    output.AppendLine($"    {reference.LineText.Trim()}");
                }
                output.AppendLine();
            }
        }

        output.Append(paginatedResults.FormatPaginationFooter());

        return output.ToString();
    }

    #endregion

    #region Legacy Formatting Methods (for backward compatibility)

    private static string FormatSearchResults(IEnumerable<SymbolSearchResult> results)
    {
        var grouped = results.GroupBy(r => r.Category);
        var output = new StringBuilder();

        output.AppendLine($"Found {results.Count()} symbols:\n");

        foreach (var group in grouped.OrderBy(g => g.Key))
        {
            output.AppendLine($"**{group.Key}** ({group.Count()}):");
            foreach (var result in group.Take(20))
            {
                output.AppendLine($"  - `{result.Name}` in {result.Location}");
                if (!string.IsNullOrEmpty(result.Summary))
                    output.AppendLine($"    {result.Summary}");
            }
            if (group.Count() > 20)
                output.AppendLine($"    ... and {group.Count() - 20} more");
            output.AppendLine();
        }

        return output.ToString();
    }

    private static string FormatReferencesSummary(IEnumerable<ReferenceResult> results)
    {
        if (!results.Any())
            return "No references found.";

        var output = new StringBuilder();
        var groupedByFile = results.GroupBy(r => r.DocumentPath).OrderBy(g => g.Key);
        var totalCount = results.Count();
        var fileCount = groupedByFile.Count();
        var symbolName = results.First().SymbolName;

        output.AppendLine($"Found {totalCount} references to '{symbolName}' in {fileCount} files:\n");

        foreach (var fileGroup in groupedByFile)
        {
            var fileName = Path.GetFileName(fileGroup.Key);
            var count = fileGroup.Count();
            output.AppendLine($"  {fileName}: {count} references");
        }

        return output.ToString();
    }

    private static string FormatReferencesLocations(IEnumerable<ReferenceResult> results)
    {
        if (!results.Any())
            return "No references found.";

        var output = new StringBuilder();
        var symbolName = results.First().SymbolName;
        output.AppendLine($"Found {results.Count()} references to '{symbolName}':\n");

        var groupedByFile = results.GroupBy(r => r.DocumentPath).OrderBy(g => g.Key);

        foreach (var fileGroup in groupedByFile)
        {
            output.AppendLine($"**{Path.GetFileName(fileGroup.Key)}**");
            foreach (var reference in fileGroup.OrderBy(r => r.LineNumber).Take(10))
            {
                var icon = reference.IsDefinition ? "[DEF]" : "";
                output.AppendLine($"  Line {reference.LineNumber}: {reference.LineText.Trim()} {icon}");
            }
            if (fileGroup.Count() > 10)
                output.AppendLine($"  ... and {fileGroup.Count() - 10} more");
            output.AppendLine();
        }

        return output.ToString();
    }

    private static string FormatReferencesFull(IEnumerable<ReferenceResult> results)
    {
        if (!results.Any())
            return "No references found.";

        var output = new StringBuilder();
        var symbolName = results.First().SymbolName;
        output.AppendLine($"Found {results.Count()} references to '{symbolName}':\n");

        var groupedByFile = results.GroupBy(r => r.DocumentPath).OrderBy(g => g.Key);

        foreach (var fileGroup in groupedByFile)
        {
            output.AppendLine($"**{Path.GetFileName(fileGroup.Key)}** ({fileGroup.Count()} references):");
            foreach (var reference in fileGroup.OrderBy(r => r.LineNumber))
            {
                var icon = reference.IsDefinition ? "[DEFINITION]" : "";
                output.AppendLine($"  Line {reference.LineNumber}: {reference.ReferenceKind} {icon}");
                if (reference.Context != null && reference.Context.Any())
                {
                    foreach (var line in reference.Context)
                        output.AppendLine($"    {line}");
                }
                else
                {
                    output.AppendLine($"    {reference.LineText.Trim()}");
                }
                output.AppendLine();
            }
        }

        return output.ToString();
    }

    private static string FormatSymbolInfoSummary(SymbolInfo? info)
    {
        if (info == null)
            return "Symbol not found.";

        return $"{info.Name} ({info.Kind}, {info.Accessibility}) @ {info.SourceLocation}";
    }

    private static string FormatSymbolInfoBasic(SymbolInfo? info)
    {
        if (info == null)
            return "Symbol not found.";

        var output = new StringBuilder();
        output.AppendLine($"**{info.Name}** ({info.Kind})");
        output.AppendLine($"  Accessibility: {info.Accessibility}");
        output.AppendLine($"  Namespace: {info.Namespace}");
        if (!string.IsNullOrEmpty(info.ReturnType))
            output.AppendLine($"  Type: {info.ReturnType}");
        output.AppendLine($"  Location: {info.SourceLocation}");

        return output.ToString();
    }

    private static string FormatSymbolInfoFull(SymbolInfo? info)
    {
        if (info == null)
            return "Symbol not found.";

        var output = new StringBuilder();
        output.AppendLine($"# {info.Name}");
        output.AppendLine($"**Kind:** {info.Kind}");
        output.AppendLine($"**Accessibility:** {info.Accessibility}");
        output.AppendLine($"**Namespace:** {info.Namespace}");
        output.AppendLine($"**Declaring Type:** {info.DeclaringType}");

        if (!string.IsNullOrEmpty(info.ReturnType))
            output.AppendLine($"**Return Type:** {info.ReturnType}");

        if (info.Parameters.Any())
        {
            output.AppendLine("**Parameters:**");
            foreach (var param in info.Parameters)
                output.AppendLine($"  - {param}");
        }

        output.AppendLine($"**Location:** {info.SourceLocation}");

        if (!string.IsNullOrEmpty(info.Documentation))
            output.AppendLine($"**Documentation:**\n{info.Documentation}");

        return output.ToString();
    }

    private static string FormatFileOutlineCompact(FileOutlineResult outline, int maxMembers)
    {
        var output = new StringBuilder();
        output.AppendLine($"File: {Path.GetFileName(outline.FilePath)} ({outline.TotalLines} lines)");

        foreach (var type in outline.Types)
        {
            output.AppendLine($"  {type.Kind}: {type.Name} ({type.Members.Count} members)");
        }

        return output.AppendWarnings(outline.Warnings).ToString();
    }

    private static string FormatFileOutlineNormal(FileOutlineResult outline, int maxMembers, bool includeMembers, bool includeDocumentation)
    {
        var output = new StringBuilder();
        output.AppendLine($"# {Path.GetFileName(outline.FilePath)}");
        output.AppendLine($"Lines: {outline.TotalLines} | Usings: {outline.UsingStatements.Count}");
        output.AppendLine();

        foreach (var type in outline.Types)
        {
            output.AppendLine($"## {type.Kind}: {type.Name}");
            output.AppendLine($"   Namespace: {type.Namespace}");
            output.AppendLine($"   Accessibility: {type.Accessibility}");

            if (includeMembers && type.Members.Any())
            {
                output.AppendLine("   Members:");
                var membersToShow = maxMembers > 0 ? type.Members.Take(maxMembers) : type.Members;
                foreach (var member in membersToShow)
                {
                    output.AppendLine($"     - {member.Kind}: {member.Name}");
                }
                if (maxMembers > 0 && type.Members.Count > maxMembers)
                    output.AppendLine($"     ... and {type.Members.Count - maxMembers} more");
            }
            output.AppendLine();
        }

        return output.AppendWarnings(outline.Warnings).ToString();
    }

    private static string FormatFileOutlineDetailed(FileOutlineResult outline, int maxMembers, bool includeDocumentation)
    {
        var output = new StringBuilder();
        output.AppendLine($"# File: {outline.FilePath}");
        output.AppendLine($"**Total Lines:** {outline.TotalLines} (Code: {outline.CodeLines}, Comments: {outline.CommentLines}, Blank: {outline.BlankLines})");
        output.AppendLine();

        if (outline.UsingStatements.Any())
        {
            output.AppendLine("## Using Directives");
            foreach (var u in outline.UsingStatements)
                output.AppendLine($"  - {u}");
            output.AppendLine();
        }

        foreach (var type in outline.Types)
        {
            output.AppendLine($"## {type.Accessibility} {type.Kind}: {type.Name}");
            output.AppendLine($"**Namespace:** {type.Namespace}");
            output.AppendLine($"**Line:** {type.LineNumber}");

            if (type.BaseTypes.Any())
                output.AppendLine($"**Base Types:** {string.Join(", ", type.BaseTypes)}");

            if (type.Members.Any())
            {
                output.AppendLine("**Members:**");
                var membersToShow = maxMembers > 0 ? type.Members.Take(maxMembers) : type.Members;
                foreach (var member in membersToShow)
                {
                    output.AppendLine($"  - {member.Accessibility} {member.Kind}: {member.Name}");
                    if (!string.IsNullOrEmpty(member.Type))
                        output.AppendLine($"    Type: {member.Type}");
                }
                if (maxMembers > 0 && type.Members.Count > maxMembers)
                    output.AppendLine($"  ... and {type.Members.Count - maxMembers} more members");
            }
            output.AppendLine();
        }

        return output.AppendWarnings(outline.Warnings).ToString();
    }

    private static string FormatImplementationResults(List<ImplementationResult> results, string typeName)
    {
        if (!results.Any())
            return $"No implementations found for '{typeName}'.";

        var output = new StringBuilder();
        output.AppendLine($"Found {results.Count} implementations of '{typeName}':\n");

        var groupedByProject = results.GroupBy(r => r.ProjectName).OrderBy(g => g.Key);

        foreach (var projectGroup in groupedByProject)
        {
            output.AppendLine($"**{projectGroup.Key}** ({projectGroup.Count()}):");
            foreach (var impl in projectGroup)
            {
                output.AppendLine($"  - {impl.ImplementingTypeName} @ {Path.GetFileName(impl.FilePath)}:{impl.LineNumber}");
            }
            output.AppendLine();
        }

        return output.ToString();
    }

    private static string FormatImplementationResultsSummary(List<ImplementationResult> results, string typeName)
    {
        if (!results.Any())
            return $"No implementations found for '{typeName}'.";

        var output = new StringBuilder();
        output.AppendLine($"Implementations of '{typeName}': {results.Count}\n");

        foreach (var impl in results.OrderBy(r => r.ImplementingTypeName))
        {
            output.AppendLine($"  - {impl.ImplementingTypeName}");
        }

        return output.ToString();
    }

    private static string FormatImplementationResultsDetailed(List<ImplementationResult> results, string typeName)
    {
        if (!results.Any())
            return $"No implementations found for '{typeName}'.";

        var output = new StringBuilder();
        output.AppendLine($"# Implementations of '{typeName}'");
        output.AppendLine($"Total: {results.Count}\n");

        foreach (var impl in results.OrderBy(r => r.ProjectName).ThenBy(r => r.ImplementingTypeName))
        {
            output.AppendLine($"## {impl.ImplementingTypeName}");
            output.AppendLine($"  Project: {impl.ProjectName}");
            output.AppendLine($"  File: {impl.FilePath}");
            output.AppendLine($"  Line: {impl.LineNumber}");
            output.AppendLine($"  Accessibility: {impl.Accessibility}");
            output.AppendLine($"  Abstract: {impl.IsAbstract} | Sealed: {impl.IsSealed}");

            if (impl.ImplementedInterfaces.Any())
                output.AppendLine($"  Interfaces: {string.Join(", ", impl.ImplementedInterfaces)}");

            output.AppendLine();
        }

        return output.ToString();
    }

    #endregion // Legacy Formatting Methods

    #endregion // Formatting Methods
}
