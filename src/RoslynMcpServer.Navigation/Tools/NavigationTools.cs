using Microsoft.Extensions.DependencyInjection;
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
        IServiceProvider? serviceProvider = null)
    {
        try
        {
            var validator = serviceProvider?.GetService<SecurityValidator>();
            var logger = serviceProvider?.GetService<ILogger<NavigationTools>>();

            if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
            {
                return "Error: Invalid solution path provided.";
            }

            var sanitizedPattern = validator?.SanitizeSearchPattern(pattern) ?? pattern;

            // Validate pagination parameters
            var paginationRequest = new PaginationRequest { PageSize = pageSize, Cursor = cursor };
            paginationRequest.Validate();

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var searchService = serviceProvider?.GetService<SymbolSearchService>();
            if (searchService == null)
            {
                return "Error: Symbol search service not available.";
            }

            var results = await searchService.SearchSymbolsAsync(
                sanitizedPattern, solutionPath, symbolTypes, ignoreCase);

            // Apply pagination
            var queryHash = paginationRequest.ComputeQueryHash(pattern, solutionPath, symbolTypes, ignoreCase.ToString());
            var paginatedResults = PaginatedResult<SymbolSearchResult>.FromCursor(
                results, cursor, paginationRequest.PageSize, queryHash);

            return FormatSearchResultsPaginated(paginatedResults);
        }
        catch (OperationCanceledException)
        {
            return "Error: Search operation timed out. The codebase may be too large or complex.";
        }
        catch (FileNotFoundException)
        {
            return "Error: Solution file not found. Please check the path and try again.";
        }
        catch (UnauthorizedAccessException)
        {
            return "Error: Access denied. Please check file permissions.";
        }
        catch (Exception ex)
        {
            var logger = serviceProvider?.GetService<ILogger<NavigationTools>>();
            logger?.LogError(ex, "Unexpected error during symbol search");
            return "Error: An unexpected error occurred during the search operation.";
        }
    }

    [McpServerTool, Description("Find all references to a specific symbol with configurable detail level")]
    public static async Task<string> FindReferences(
        [Description("Exact symbol name to find references for")] string symbolName,
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Detail level: summary (file stats only), locations (with code lines), full (with 5-line context). Default: locations")]
        string detailLevel = "locations",
        [Description("Include symbol definition in results")] bool includeDefinition = true,
        [Description("Number of results per page (default: 20, max: 100)")] int pageSize = 20,
        [Description("Cursor for pagination - use nextCursor from previous response to get next page")] string? cursor = null,
        IServiceProvider? serviceProvider = null)
    {
        try
        {
            var validator = serviceProvider?.GetService<SecurityValidator>();
            if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
            {
                return "Error: Invalid solution path provided.";
            }

            // Validate pagination parameters
            var paginationRequest = new PaginationRequest { PageSize = pageSize, Cursor = cursor };
            paginationRequest.Validate();

            var searchService = serviceProvider?.GetService<SymbolSearchService>();
            if (searchService == null)
            {
                return "Error: Symbol search service not available.";
            }

            var results = await searchService.FindReferencesAsync(symbolName, solutionPath, includeDefinition);

            // Apply pagination
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
        catch (Exception ex)
        {
            var logger = serviceProvider?.GetService<ILogger<NavigationTools>>();
            logger?.LogError(ex, "Error finding references for symbol: {SymbolName}", symbolName);
            return "Error: An unexpected error occurred while finding references.";
        }
    }

    [McpServerTool, Description("Find references with advanced filtering options to reduce noise and focus on specific usage patterns")]
    public static async Task<string> FindReferencesFiltered(
        [Description("Exact symbol name to find references for")] string symbolName,
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Detail level: summary (file stats only), locations (with code lines), full (with 5-line context). Default: locations")]
        string detailLevel = "locations",
        [Description("Include symbol definition in results")] bool includeDefinition = true,
        [Description("Filter by project name pattern (supports wildcards: * and ?)")] string? projectFilter = null,
        [Description("Exclude test projects (projects with 'Test', 'Tests', 'Testing', 'Spec' in name)")] bool excludeTests = false,
        [Description("Only show cross-project references (exclude same-project usage)")] bool crossProjectOnly = false,
        [Description("Only show write operations (assignments, increments, etc.)")] bool writesOnly = false,
        [Description("Only show references in public API contexts (excludes private/internal usage)")] bool publicOnly = false,
        [Description("Number of results per page (default: 20, max: 100)")] int pageSize = 20,
        [Description("Cursor for pagination - use nextCursor from previous response to get next page")] string? cursor = null,
        IServiceProvider? serviceProvider = null)
    {
        try
        {
            var validator = serviceProvider?.GetService<SecurityValidator>();
            if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
            {
                return "Error: Invalid solution path provided.";
            }

            // Validate pagination parameters
            var paginationRequest = new PaginationRequest { PageSize = pageSize, Cursor = cursor };
            paginationRequest.Validate();

            var searchService = serviceProvider?.GetService<SymbolSearchService>();
            if (searchService == null)
            {
                return "Error: Symbol search service not available.";
            }

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

            // Apply pagination
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
        catch (Exception ex)
        {
            var logger = serviceProvider?.GetService<ILogger<NavigationTools>>();
            logger?.LogError(ex, "Error finding filtered references for symbol: {SymbolName}", symbolName);
            return "Error: An unexpected error occurred while finding references.";
        }
    }

    [McpServerTool, Description("Get detailed information about a specific symbol")]
    public static async Task<string> GetSymbolInfo(
        [Description("Exact symbol name or full qualified name")] string symbolName,
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Detail level: summary (minimal), basic (balanced), full (comprehensive). Default: basic")]
        string detailLevel = "basic",
        IServiceProvider? serviceProvider = null)
    {
        try
        {
            var validator = serviceProvider?.GetService<SecurityValidator>();
            if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
            {
                return "Error: Invalid solution path provided.";
            }

            var searchService = serviceProvider?.GetService<SymbolSearchService>();
            if (searchService == null)
            {
                return "Error: Symbol search service not available.";
            }

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
            var logger = serviceProvider?.GetService<ILogger<NavigationTools>>();
            logger?.LogError(ex, "Error getting symbol info for: {SymbolName}", symbolName);
            return "Error: An unexpected error occurred while getting symbol information.";
        }
    }

    [McpServerTool, Description("Get hierarchical structure of projects, namespaces, and types")]
    public static async Task<string> GetProjectStructure(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Include member signatures (default: false)")] bool includeMembers = false,
        [Description("Filter by namespace pattern (optional, e.g., 'MyProject.Services')")] string? namespaceFilter = null,
        [Description("Include only public types (default: true)")] bool publicOnly = true,
        IServiceProvider? serviceProvider = null)
    {
        try
        {
            var validator = serviceProvider?.GetService<SecurityValidator>();
            if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
            {
                return "Error: Invalid solution path provided.";
            }

            var structureService = serviceProvider?.GetService<ProjectStructureService>();
            if (structureService == null)
            {
                return "Error: Project structure service not available.";
            }

            return await structureService.GetStructureAsync(
                solutionPath,
                includeMembers,
                namespaceFilter,
                publicOnly);
        }
        catch (Exception ex)
        {
            var logger = serviceProvider?.GetService<ILogger<NavigationTools>>();
            logger?.LogError(ex, "Error getting project structure");
            return $"Error: An unexpected error occurred while getting project structure: {ex.Message}";
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

            var fileAnalysisService = serviceProvider?.GetService<FileAnalysisService>();
            if (fileAnalysisService == null)
            {
                return "Error: File analysis service not available.";
            }

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
            var logger = serviceProvider?.GetService<ILogger<NavigationTools>>();
            logger?.LogError(ex, "Error getting file outline for: {FilePath}", filePath);
            return $"Error: An unexpected error occurred while getting file outline: {ex.Message}";
        }
    }

    [McpServerTool, Description("Find all implementations of an interface or abstract class")]
    public static async Task<string> FindImplementations(
        [Description("Interface or abstract class name to find implementations for")] string typeName,
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary (names only), normal (balanced), detailed (comprehensive). Default: normal")]
        string format = "normal",
        [Description("Include abstract implementations (default: false)")] bool includeAbstractImplementations = false,
        IServiceProvider? serviceProvider = null)
    {
        try
        {
            var validator = serviceProvider?.GetService<SecurityValidator>();
            if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
            {
                return "Error: Invalid solution path provided.";
            }

            var searchService = serviceProvider?.GetService<SymbolSearchService>();
            if (searchService == null)
            {
                return "Error: Symbol search service not available.";
            }

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
            var logger = serviceProvider?.GetService<ILogger<NavigationTools>>();
            logger?.LogError(ex, "Error finding implementations for: {TypeName}", typeName);
            return $"Error: An unexpected error occurred while finding implementations: {ex.Message}";
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
