using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcpServer.Models;
using RoslynMcpServer.Services;
using System.ComponentModel;
using System.Text;

namespace RoslynMcpServer.Tools
{
    [McpServerToolType]
    public class CodeNavigationTools
    {
        [McpServerTool, Description("Search for symbols in C# code using wildcard patterns (* and ?)")]
        public static async Task<string> SearchSymbols(
            [Description("Wildcard pattern to search for (e.g., 'User*', '*Service', 'Get*User')")] string pattern,
            [Description("Path to solution file (.sln)")] string solutionPath,
            [Description("Symbol types to include: class,interface,method,property,field (comma-separated)")] string symbolTypes = "class,interface,method,property",
            [Description("Whether to ignore case in search")] bool ignoreCase = true,
            IServiceProvider? serviceProvider = null)
        {
            try
            {
                var validator = serviceProvider?.GetService<SecurityValidator>();
                var logger = serviceProvider?.GetService<ILogger<CodeNavigationTools>>();
                
                // Validate inputs
                if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
                {
                    return "Error: Invalid solution path provided.";
                }
                
                var sanitizedPattern = validator?.SanitizeSearchPattern(pattern) ?? pattern;
                
                // Perform search with timeout
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                var searchService = serviceProvider?.GetService<SymbolSearchService>();
                if (searchService == null)
                {
                    return "Error: Symbol search service not available.";
                }
                
                var results = await searchService.SearchSymbolsAsync(
                    sanitizedPattern, solutionPath, symbolTypes, ignoreCase);
                
                return FormatSearchResults(results);
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
                var logger = serviceProvider?.GetService<ILogger<CodeNavigationTools>>();
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

                var results = await searchService.FindReferencesAsync(symbolName, solutionPath, includeDefinition);

                // Format based on detail level
                return detailLevel.ToLower() switch
                {
                    "summary" => FormatReferencesSummary(results),
                    "locations" => FormatReferencesLocations(results),
                    "full" => FormatReferencesFull(results),
                    _ => FormatReferencesLocations(results) // Default to locations
                };
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger<CodeNavigationTools>>();
                logger?.LogError(ex, "Error finding references for symbol: {SymbolName}", symbolName);
                return "Error: An unexpected error occurred while finding references.";
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
                var logger = serviceProvider?.GetService<ILogger<CodeNavigationTools>>();
                logger?.LogError(ex, "Error getting project structure");
                return $"Error: An unexpected error occurred while getting project structure: {ex.Message}";
            }
        }

        [McpServerTool, Description("Get type signature with members but without implementation")]
        public static async Task<string> GetTypeSignature(
            [Description("Fully qualified or simple type name (e.g., 'UserService' or 'MyProject.Services.UserService')")]
            string typeName,
            [Description("Path to solution file (.sln)")] string solutionPath,
            [Description("Include private members (default: false)")] bool includePrivate = false,
            [Description("Include XML documentation comments (default: true)")] bool includeDocumentation = true,
            IServiceProvider? serviceProvider = null)
        {
            try
            {
                var validator = serviceProvider?.GetService<SecurityValidator>();
                if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
                {
                    return "Error: Invalid solution path provided.";
                }

                var typeSignatureService = serviceProvider?.GetService<TypeSignatureService>();
                if (typeSignatureService == null)
                {
                    return "Error: Type signature service not available.";
                }

                return await typeSignatureService.GetTypeSignatureAsync(
                    typeName,
                    solutionPath,
                    includePrivate,
                    includeDocumentation);
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger<CodeNavigationTools>>();
                logger?.LogError(ex, "Error getting type signature for: {TypeName}", typeName);
                return $"Error: An unexpected error occurred while getting type signature: {ex.Message}";
            }
        }

        [McpServerTool, Description("Get detailed information about a specific symbol")]
        public static async Task<string> GetSymbolInfo(
            [Description("Exact symbol name or full qualified name")] string symbolName,
            [Description("Path to solution file (.sln)")] string solutionPath,
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
                return FormatSymbolInfo(info);
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger<CodeNavigationTools>>();
                logger?.LogError(ex, "Error getting symbol info for: {SymbolName}", symbolName);
                return "Error: An unexpected error occurred while getting symbol information.";
            }
        }

        [McpServerTool, Description("Get code metrics and statistics for entire solution")]
        public static async Task<string> GetCodeMetrics(
            [Description("Path to solution file (.sln)")] string solutionPath,
            [Description("Group by: project | namespace | type (default: project)")] string groupBy = "project",
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

                return await metricsService.GetMetricsAsync(solutionPath, groupBy);
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger<CodeNavigationTools>>();
                logger?.LogError(ex, "Error getting code metrics");
                return $"Error: An unexpected error occurred while getting code metrics: {ex.Message}";
            }
        }

        [McpServerTool, Description("Get project dependency graph in various formats")]
        public static async Task<string> GetDependencyGraph(
            [Description("Path to solution file (.sln)")] string solutionPath,
            [Description("Output format: text | dot | mermaid (default: text)")] string format = "text",
            [Description("Include package dependencies (default: false)")] bool includePackages = false,
            IServiceProvider? serviceProvider = null)
        {
            try
            {
                var validator = serviceProvider?.GetService<SecurityValidator>();
                if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
                {
                    return "Error: Invalid solution path provided.";
                }

                var graphService = serviceProvider?.GetService<DependencyGraphService>();
                if (graphService == null)
                {
                    return "Error: Dependency graph service not available.";
                }

                return await graphService.GetDependencyGraphAsync(solutionPath, format, includePackages);
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger<CodeNavigationTools>>();
                logger?.LogError(ex, "Error getting dependency graph");
                return $"Error: An unexpected error occurred while getting dependency graph: {ex.Message}";
            }
        }

        [McpServerTool, Description("Get call hierarchy showing callers and callees for a method")]
        public static async Task<string> GetCallHierarchy(
            [Description("Path to solution file (.sln)")] string solutionPath,
            [Description("Method name to analyze")] string methodName,
            [Description("Direction: both | callers | callees (default: both)")] string direction = "both",
            [Description("Maximum depth for hierarchy traversal (default: 3)")] int maxDepth = 3,
            IServiceProvider? serviceProvider = null)
        {
            try
            {
                var validator = serviceProvider?.GetService<SecurityValidator>();
                if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
                {
                    return "Error: Invalid solution path provided.";
                }

                if (string.IsNullOrWhiteSpace(methodName))
                {
                    return "Error: Method name is required.";
                }

                var callHierarchyService = serviceProvider?.GetService<CallHierarchyService>();
                if (callHierarchyService == null)
                {
                    return "Error: Call hierarchy service not available.";
                }

                return await callHierarchyService.GetCallHierarchyAsync(
                    solutionPath,
                    methodName,
                    direction,
                    maxDepth);
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger<CodeNavigationTools>>();
                logger?.LogError(ex, "Error getting call hierarchy");
                return $"Error: An unexpected error occurred while getting call hierarchy: {ex.Message}";
            }
        }

        [McpServerTool, Description("Analyze project dependencies and symbol usage patterns")]
        public static async Task<string> AnalyzeDependencies(
            [Description("Path to solution file (.sln)")] string solutionPath,
            [Description("Maximum depth for dependency analysis")] int maxDepth = 3,
            IServiceProvider? serviceProvider = null)
        {
            try
            {
                var validator = serviceProvider?.GetService<SecurityValidator>();
                if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
                {
                    return "Error: Invalid solution path provided.";
                }

                var analysisService = serviceProvider?.GetService<CodeAnalysisService>();
                if (analysisService == null)
                {
                    return "Error: Code analysis service not available.";
                }

                var dependencies = await analysisService.AnalyzeDependenciesAsync(solutionPath, maxDepth);
                return FormatDependencyAnalysis(dependencies);
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger<CodeNavigationTools>>();
                logger?.LogError(ex, "Error analyzing dependencies");
                return "Error: An unexpected error occurred during dependency analysis.";
            }
        }

        [McpServerTool, Description("Analyze code complexity and identify high-complexity methods")]
        public static async Task<string> AnalyzeCodeComplexity(
            [Description("Path to solution file")] string solutionPath,
            [Description("Complexity threshold (1-10)")] int threshold = 5,
            IServiceProvider? serviceProvider = null)
        {
            try
            {
                var validator = serviceProvider?.GetService<SecurityValidator>();
                if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
                {
                    return "Error: Invalid solution path provided.";
                }
                
                var analysisService = serviceProvider?.GetService<CodeAnalysisService>();
                if (analysisService == null)
                {
                    return "Error: Code analysis service not available.";
                }
                
                var solution = await analysisService.GetSolutionAsync(solutionPath);
                var complexityResults = new List<ComplexityResult>();
                
                foreach (var project in solution.Projects.Where(p => p.SupportsCompilation))
                {
                    var compilation = await project.GetCompilationAsync();
                    if (compilation == null) continue;
                    
                    foreach (var tree in compilation.SyntaxTrees)
                    {
                        var root = await tree.GetRootAsync();
                        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
                        
                        foreach (var method in methods)
                        {
                            var complexity = CalculateCyclomaticComplexity(method);
                            if (complexity >= threshold)
                            {
                                var lineSpan = method.GetLocation().GetLineSpan();
                                complexityResults.Add(new ComplexityResult
                                {
                                    MethodName = method.Identifier.ValueText,
                                    FileName = Path.GetFileName(tree.FilePath),
                                    LineNumber = lineSpan.StartLinePosition.Line + 1,
                                    Complexity = complexity,
                                    ClassName = GetContainingClassName(method),
                                    Namespace = GetContainingNamespace(method)
                                });
                            }
                        }
                    }
                }
                
                return FormatComplexityResults(complexityResults);
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger<CodeNavigationTools>>();
                logger?.LogError(ex, "Error analyzing code complexity");
                return "Error: An unexpected error occurred during complexity analysis.";
            }
        }

        [McpServerTool, Description("Execute multiple queries in a single batch request")]
        public static async Task<string> BatchQuery(
            [Description("JSON array of query specifications. Each query should have 'tool' (tool name) and 'parameters' (dict of parameters)")] string queriesJson,
            [Description("Execute queries in parallel (default: true)")] bool parallel = true,
            IServiceProvider? serviceProvider = null)
        {
            try
            {
                var batchQueryService = serviceProvider?.GetService<BatchQueryService>();
                if (batchQueryService == null)
                {
                    return "Error: Batch query service not available.";
                }

                return await batchQueryService.ExecuteBatchAsync(queriesJson, parallel);
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger<CodeNavigationTools>>();
                logger?.LogError(ex, "Error executing batch query");
                return $"Error: An unexpected error occurred while executing batch query: {ex.Message}";
            }
        }

        [McpServerTool, Description("Find references with advanced filtering options to reduce noise and focus on specific usage patterns")]
        public static async Task<string> FindReferencesFiltered(
            [Description("Exact symbol name to find references for")] string symbolName,
            [Description("Path to solution file (.sln)")] string solutionPath,
            [Description("Detail level: summary (file stats only), locations (with code lines), full (with 5-line context). Default: locations")]
            string detailLevel = "locations",
            [Description("Include symbol definition in results")] bool includeDefinition = true,
            [Description("Only show references in public API contexts (excludes private/internal usage)")] bool publicOnly = false,
            [Description("Exclude test projects (projects with 'Test', 'Tests', 'Testing', 'Spec' in name)")] bool excludeTests = false,
            [Description("Only show cross-project references (exclude same-project usage)")] bool crossProjectOnly = false,
            [Description("Only show write operations (assignments, increments, etc.)")] bool writesOnly = false,
            [Description("Filter by project name pattern (supports wildcards: * and ?)")] string? projectFilter = null,
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

                var diagnosticLogger = serviceProvider?.GetService<DiagnosticLogger>();
                var logger = serviceProvider?.GetService<ILogger<CodeNavigationTools>>();

                Func<Task<IEnumerable<ReferenceResult>>> operation = async () =>
                    await searchService.FindReferencesFilteredAsync(
                        symbolName,
                        solutionPath,
                        includeDefinition,
                        publicOnly,
                        excludeTests,
                        crossProjectOnly,
                        writesOnly,
                        projectFilter);

                var results = diagnosticLogger != null
                    ? await diagnosticLogger.LoggedExecutionAsync(
                        "FindReferencesFiltered",
                        operation,
                        new { symbolName, detailLevel, publicOnly, excludeTests, crossProjectOnly, writesOnly, projectFilter })
                    : await operation();

                // Build filter summary
                var filters = new List<string>();
                if (excludeTests) filters.Add("excluding tests");
                if (crossProjectOnly) filters.Add("cross-project only");
                if (publicOnly) filters.Add("public API only");
                if (writesOnly) filters.Add("writes only");
                if (!string.IsNullOrWhiteSpace(projectFilter)) filters.Add($"project: {projectFilter}");

                var filterSummary = filters.Any()
                    ? $"\nFilters applied: {string.Join(", ", filters)}\n"
                    : "";

                // Format based on detail level
                var formattedResults = detailLevel.ToLower() switch
                {
                    "summary" => FormatReferencesSummary(results),
                    "locations" => FormatReferencesLocations(results),
                    "full" => FormatReferencesFull(results),
                    _ => FormatReferencesLocations(results)
                };

                return filterSummary + formattedResults;
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger<CodeNavigationTools>>();
                logger?.LogError(ex, "Error finding filtered references for symbol: {SymbolName}", symbolName);
                return "Error: An unexpected error occurred while finding filtered references.";
            }
        }

        private static string FormatSearchResults(IEnumerable<SymbolSearchResult> results)
        {
            var grouped = results.GroupBy(r => r.Category);
            var output = new StringBuilder();
            
            output.AppendLine($"Found {results.Count()} symbols:\n");
            
            foreach (var group in grouped.OrderBy(g => g.Key))
            {
                output.AppendLine($"**{group.Key}** ({group.Count()}):");
                foreach (var result in group.Take(20)) // Limit results
                {
                    output.AppendLine($"  • `{result.Name}` in {result.Location}");
                    if (!string.IsNullOrEmpty(result.Summary))
                        output.AppendLine($"    {result.Summary}");
                }
                if (group.Count() > 20)
                    output.AppendLine($"    ... and {group.Count() - 20} more");
                output.AppendLine();
            }
            
            return output.ToString();
        }

        // Format: Summary - Only file statistics and line numbers
        private static string FormatReferencesSummary(IEnumerable<ReferenceResult> results)
        {
            if (!results.Any())
                return "No references found.";

            var output = new StringBuilder();
            var groupedByFile = results.GroupBy(r => r.DocumentPath).OrderBy(g => g.Key);
            var totalCount = results.Count();
            var fileCount = groupedByFile.Count();
            var projectCount = results.Select(r => r.ProjectName).Distinct().Count();
            var symbolName = results.First().SymbolName;

            output.AppendLine($"Found {totalCount} reference{(totalCount > 1 ? "s" : "")} to '{symbolName}' across {fileCount} file{(fileCount > 1 ? "s" : "")}:\n");

            foreach (var fileGroup in groupedByFile)
            {
                var fileName = Path.GetFileName(fileGroup.Key);
                var count = fileGroup.Count();
                var hasDefinition = fileGroup.Any(r => r.IsDefinition);
                var defSuffix = hasDefinition ? " (Definition)" : "";

                output.AppendLine($"📄 {fileName}: {count} reference{(count > 1 ? "s" : "")}{defSuffix}");

                var lines = fileGroup.Select(r => r.LineNumber).OrderBy(l => l);
                var linesSummary = lines.Count() > 10
                    ? $"{string.Join(", ", lines.Take(10))}, ..."
                    : string.Join(", ", lines);

                output.AppendLine($"   Lines: {linesSummary}");
                output.AppendLine();
            }

            output.AppendLine($"Total: {totalCount} reference{(totalCount > 1 ? "s" : "")} in {fileCount} file{(fileCount > 1 ? "s" : "")} across {projectCount} project{(projectCount > 1 ? "s" : "")}");

            return output.ToString();
        }

        // Format: Locations - Show code lines without full context
        private static string FormatReferencesLocations(IEnumerable<ReferenceResult> results)
        {
            if (!results.Any())
                return "No references found.";

            var output = new StringBuilder();
            var symbolName = results.First().SymbolName;
            output.AppendLine($"Found {results.Count()} reference{(results.Count() > 1 ? "s" : "")} to '{symbolName}':\n");

            var groupedByFile = results.GroupBy(r => r.DocumentPath).OrderBy(g => g.Key);

            foreach (var fileGroup in groupedByFile)
            {
                var fileName = Path.GetFileName(fileGroup.Key);
                var count = fileGroup.Count();

                output.AppendLine($"📄 {fileName} ({count} reference{(count > 1 ? "s" : "")})");

                // Show details for files with <= 5 references, summary for larger files
                if (count <= 5)
                {
                    foreach (var reference in fileGroup.OrderBy(r => r.LineNumber))
                    {
                        var icon = reference.IsDefinition ? "📍" : "✓";
                        var refType = reference.IsDefinition ? "Definition" : reference.ReferenceKind;
                        output.AppendLine($"  {icon} Line {reference.LineNumber}: {refType}");
                        output.AppendLine($"    {reference.LineText.Trim()}");
                        output.AppendLine();
                    }
                }
                else
                {
                    // Summary for files with many references
                    var lines = fileGroup.Select(r => r.LineNumber).OrderBy(l => l);
                    output.AppendLine($"  Lines: {string.Join(", ", lines)}");

                    var definitionRef = fileGroup.FirstOrDefault(r => r.IsDefinition);
                    if (definitionRef != null)
                    {
                        output.AppendLine($"  📍 Definition at line {definitionRef.LineNumber}");
                    }
                    output.AppendLine();
                }
            }

            return output.ToString();
        }

        // Format: Full - Show complete 5-line context (original behavior)
        private static string FormatReferencesFull(IEnumerable<ReferenceResult> results)
        {
            if (!results.Any())
                return "No references found.";

            var output = new StringBuilder();
            var symbolName = results.First().SymbolName;
            output.AppendLine($"Found {results.Count()} reference{(results.Count() > 1 ? "s" : "")} to '{symbolName}':\n");

            var groupedByFile = results.GroupBy(r => r.DocumentPath).OrderBy(g => g.Key);

            foreach (var fileGroup in groupedByFile)
            {
                output.AppendLine($"📄 **{Path.GetFileName(fileGroup.Key)}** ({fileGroup.Count()} references):");

                foreach (var reference in fileGroup.OrderBy(r => r.LineNumber))
                {
                    var icon = reference.IsDefinition ? "📍" : "✓";
                    var refType = reference.IsDefinition ? "Definition" : reference.ReferenceKind;
                    output.AppendLine($"  {icon} Line {reference.LineNumber}: {refType}");

                    // Show 5-line context if available
                    if (reference.Context != null && reference.Context.Any())
                    {
                        foreach (var contextLine in reference.Context)
                        {
                            output.AppendLine($"    {contextLine}");
                        }
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

        private static string FormatSymbolInfo(RoslynMcpServer.Models.SymbolInfo? info)
        {
            if (info == null)
                return "Symbol not found.";
            
            var output = new StringBuilder();
            output.AppendLine($"**{info.Name}** ({info.Kind})");
            output.AppendLine($"Full Name: `{info.FullName}`");
            output.AppendLine($"Accessibility: {info.Accessibility}");
            
            if (!string.IsNullOrEmpty(info.Namespace))
                output.AppendLine($"Namespace: {info.Namespace}");
            
            if (!string.IsNullOrEmpty(info.DeclaringType))
                output.AppendLine($"Declaring Type: {info.DeclaringType}");
            
            if (!string.IsNullOrEmpty(info.ReturnType))
                output.AppendLine($"Return Type: {info.ReturnType}");
            
            if (info.Parameters.Any())
            {
                output.AppendLine("Parameters:");
                foreach (var param in info.Parameters)
                    output.AppendLine($"  • {param}");
            }
            
            if (info.Attributes.Any())
            {
                output.AppendLine("Attributes:");
                foreach (var attr in info.Attributes)
                    output.AppendLine($"  • {attr}");
            }
            
            if (!string.IsNullOrEmpty(info.SourceLocation))
                output.AppendLine($"Location: {info.SourceLocation}");
            
            return output.ToString();
        }

        private static string FormatDependencyAnalysis(DependencyAnalysis analysis)
        {
            var output = new StringBuilder();
            output.AppendLine($"**Dependency Analysis for {analysis.ProjectName}**\n");
            
            output.AppendLine($"Symbol Summary:");
            output.AppendLine($"  • Total Symbols: {analysis.TotalSymbols}");
            output.AppendLine($"  • Public Symbols: {analysis.PublicSymbols}");
            output.AppendLine($"  • Internal Symbols: {analysis.InternalSymbols}");
            output.AppendLine();
            
            if (analysis.Dependencies.Any())
            {
                output.AppendLine("Dependencies:");
                var groupedDeps = analysis.Dependencies.GroupBy(d => d.Type);
                foreach (var group in groupedDeps)
                {
                    output.AppendLine($"  **{group.Key}** ({group.Count()}):");
                    foreach (var dep in group.Take(10))
                        output.AppendLine($"    • {dep.Name}");
                    if (group.Count() > 10)
                        output.AppendLine($"    ... and {group.Count() - 10} more");
                }
                output.AppendLine();
            }
            
            if (analysis.NamespaceUsages.Any())
            {
                output.AppendLine("Top Namespace Usages:");
                foreach (var usage in analysis.NamespaceUsages.OrderByDescending(n => n.UsageCount).Take(10))
                    output.AppendLine($"  • {usage.Namespace}: {usage.UsageCount} usages");
            }
            
            return output.ToString();
        }

        private static string FormatComplexityResults(List<ComplexityResult> results)
        {
            var output = new StringBuilder();
            output.AppendLine($"**Code Complexity Analysis**\n");
            output.AppendLine($"Found {results.Count} methods with high complexity:\n");
            
            foreach (var result in results.OrderByDescending(r => r.Complexity).Take(20))
            {
                output.AppendLine($"**{result.ClassName}.{result.MethodName}** (Complexity: {result.Complexity})");
                output.AppendLine($"  Location: {result.FileName}:{result.LineNumber}");
                if (!string.IsNullOrEmpty(result.Namespace))
                    output.AppendLine($"  Namespace: {result.Namespace}");
                output.AppendLine();
            }
            
            if (results.Count > 20)
                output.AppendLine($"... and {results.Count - 20} more methods with high complexity");
            
            return output.ToString();
        }

        private static int CalculateCyclomaticComplexity(MethodDeclarationSyntax method)
        {
            int complexity = 1; // Base complexity
            
            var decisionPoints = method.DescendantNodes().Where(node => 
                node.IsKind(SyntaxKind.IfStatement) ||
                node.IsKind(SyntaxKind.WhileStatement) ||
                node.IsKind(SyntaxKind.ForStatement) ||
                node.IsKind(SyntaxKind.ForEachStatement) ||
                node.IsKind(SyntaxKind.SwitchStatement) ||
                node.IsKind(SyntaxKind.CatchClause));
            
            complexity += decisionPoints.Count();
            
            var logicalOperators = method.DescendantTokens().Where(token =>
                token.IsKind(SyntaxKind.AmpersandAmpersandToken) ||
                token.IsKind(SyntaxKind.BarBarToken));
            
            complexity += logicalOperators.Count();
            
            return complexity;
        }

        private static string GetContainingClassName(MethodDeclarationSyntax method)
        {
            var classDeclaration = method.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
            return classDeclaration?.Identifier.ValueText ?? "";
        }

        private static string GetContainingNamespace(MethodDeclarationSyntax method)
        {
            var namespaceDeclaration = method.Ancestors().OfType<NamespaceDeclarationSyntax>().FirstOrDefault();
            return namespaceDeclaration?.Name.ToString() ?? "";
        }
    }
}