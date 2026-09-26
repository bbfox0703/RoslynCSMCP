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

    [McpServerTool, Description("Search for symbols in C# code using wildcard patterns (* and ?)")]
    public static async Task<string> SearchSymbols(
        [Description("Wildcard pattern to search for (e.g., 'User*', '*Service', 'Get*User')")] string pattern,
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Symbol types to include: class,interface,method,property,field (comma-separated)")] string symbolTypes = "class,interface,method,property",
        [Description("Whether to ignore case in search")] bool ignoreCase = true,
        [Description("Number of results per page (default: 20, max: 100)")] int pageSize = 20,
        [Description("Cursor for pagination - use nextCursor from previous response to get next page")] string? cursor = null,
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
        Find source references to the symbols declared in the solution that match symbolName, plus their
        declaration sites when includeDefinition is true. A simple name combines every match (overloads and
        same-named members of different types); qualify it with the containing type or namespace to narrow it.
        Members of referenced assemblies such as the .NET framework are never matched. References are grouped by
        file, one entry per line, and returned one page at a time with a nextCursor. A name that matches no
        declared symbol returns a symbol-not-found error; a declared symbol with no references returns a
        'No references found' message.
        """)]
    public static async Task<string> FindReferences(
        [Description("Symbol name: simple ('Save') or qualified by containing type and/or namespace ('UserService.Save', 'MyApp.Services.UserService.Save'); generic arguments and parameter lists are ignored. Only symbols declared in the solution's source match, never framework or package members; exact-case matches are preferred, otherwise case is ignored.")] string symbolName,
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Detail level: summary (file stats only), locations (with code lines), full (with 5-line context). Default: locations")]
        string detailLevel = "locations",
        [Description("Also return each matching symbol's declaration sites (every part of a partial declaration), marked as definitions (default: true).")] bool includeDefinition = true,
        [Description("Number of results per page (default: 20, max: 100)")] int pageSize = 20,
        [Description("Cursor for pagination - use nextCursor from previous response to get next page")] string? cursor = null,
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

            var search = await searchService.SearchReferencesAsync(symbolName, solutionPath, includeDefinition);

            if (search.MatchedSymbolCount == 0)
                return McpError.SymbolNotFound(symbolName, solutionPath).ToToolResponse();

            if (search.References.Count == 0)
                return $"No references found for '{symbolName}'; it is declared in the solution but never referenced.";

            var results = search.References;

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
        Find references the way FindReferences does, then narrow them by project name pattern, test-project
        exclusion, cross-project usage, write access, or public API context. Filters are applied to each
        reference before lines are merged, so a line that both reads and writes the symbol counts as a write.
        Declaration sites (includeDefinition) are subject to projectFilter, excludeTests, and publicOnly, and
        are dropped by crossProjectOnly and writesOnly. Results are grouped by file and returned one page at a
        time with a nextCursor; an empty result returns 'No references found.'
        """)]
    public static async Task<string> FindReferencesFiltered(
        [Description("Symbol name: simple ('Save') or qualified by containing type and/or namespace ('UserService.Save', 'MyApp.Services.UserService.Save'); generic arguments and parameter lists are ignored. Only symbols declared in the solution's source match, never framework or package members; exact-case matches are preferred, otherwise case is ignored.")] string symbolName,
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Detail level: summary (file stats only), locations (with code lines), full (with 5-line context). Default: locations")]
        string detailLevel = "locations",
        [Description("Also return each matching symbol's declaration sites (every part of a partial declaration), marked as definitions (default: true).")] bool includeDefinition = true,
        [Description("Project name wildcard pattern (* and ?), matched case-insensitively against the whole name.")] string? projectFilter = null,
        [Description("Drop references in projects whose name contains 'test' or 'spec' (case-insensitive substring).")] bool excludeTests = false,
        [Description("Keep only references located in a project other than the one declaring the referenced symbol (checked per symbol when several match); declaration sites are dropped.")] bool crossProjectOnly = false,
        [Description("Keep only references that write the symbol: assignment or compound-assignment target (including object initializers and deconstruction), ++/-- operand, or out/ref argument. Reads, including right-hand-side reads inside an assignment, and declaration sites are dropped.")] bool writesOnly = false,
        [Description("Keep only locations inside a type or member visible outside its assembly: it and every containing type are public, protected, or protected internal, and an accessor's own modifier counts (e.g. a private setter). Code in private or internal members, top-level statements, and using directives is dropped.")] bool publicOnly = false,
        [Description("Number of results per page (default: 20, max: 100)")] int pageSize = 20,
        [Description("Cursor for pagination - use nextCursor from previous response to get next page")] string? cursor = null,
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

            var filteredResults = await searchService.FindReferencesFilteredAsync(
                symbolName,
                solutionPath,
                includeDefinition,
                publicOnly: publicOnly,
                excludeTests: excludeTests,
                crossProjectOnly: crossProjectOnly,
                writesOnly: writesOnly,
                projectFilter: projectFilter);

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

    [McpServerTool, Description("Get detailed information about a specific symbol")]
    public static async Task<string> GetSymbolInfo(
        [Description("Exact symbol name or full qualified name")] string symbolName,
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

    [McpServerTool, Description("Get hierarchical structure of projects, namespaces, and types")]
    public static async Task<string> GetProjectStructure(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Include member signatures (default: false)")] bool includeMembers = false,
        [Description("Filter by namespace pattern (optional, e.g., 'MyProject.Services')")] string? namespaceFilter = null,
        [Description("Include only public types (default: true)")] bool publicOnly = true,
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

    [McpServerTool, Description("Get structural outline of a C# file showing types and members")]
    public static async Task<string> GetFileOutline(
        [Description("Path to C# source file (.cs)")] string filePath,
        [Description("Output mode: compact (minimal info), normal (balanced), detailed (comprehensive). Default: normal")]
        string mode = "normal",
        [Description("Maximum members to show per type (default: 10, 0=show all)")] int maxMembers = 10,
        [Description("Include member details (default: true)")] bool includeMembers = true,
        [Description("Include documentation comments (default: true)")] bool includeDocumentation = true,
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

    [McpServerTool, Description("Find all implementations of an interface or abstract class")]
    public static async Task<string> FindImplementations(
        [Description("Interface or abstract class name to find implementations for")] string typeName,
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary (names only), normal (balanced), detailed (comprehensive). Default: normal")]
        string format = "normal",
        [Description("Include abstract implementations (default: false)")] bool includeAbstractImplementations = false,
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
                var kind = reference.IsDefinition ? "[DEFINITION]" : reference.ReferenceKind;
                output.AppendLine($"  Line {reference.LineNumber}: {kind}");
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
                var kind = reference.IsDefinition ? "[DEFINITION]" : reference.ReferenceKind;
                output.AppendLine($"  Line {reference.LineNumber}: {kind}");
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

        return output.ToString();
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

        return output.ToString();
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

        return output.ToString();
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
