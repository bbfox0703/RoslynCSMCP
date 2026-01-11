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

        [McpServerTool, Description("Find all references to a symbol across multiple solutions")]
        public static async Task<string> FindReferencesAcrossSolutions(
            [Description("Exact symbol name to find references for")] string symbolName,
            [Description("Comma-separated list of solution file paths (.sln)")] string solutionPaths,
            [Description("Detail level: summary (file stats only), locations (with code lines), full (with 5-line context). Default: locations")]
            string detailLevel = "locations",
            [Description("Include symbol definition in results")] bool includeDefinition = true,
            IServiceProvider? serviceProvider = null)
        {
            try
            {
                // Parse solution paths
                var solutionPathArray = solutionPaths
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToArray();

                if (solutionPathArray.Length == 0)
                {
                    return "Error: No solution paths provided.";
                }

                // Validate all paths
                var validator = serviceProvider?.GetService<SecurityValidator>();
                var invalidPaths = solutionPathArray
                    .Where(path => !validator?.ValidateSolutionPath(path) ?? false)
                    .ToList();

                if (invalidPaths.Any())
                {
                    return $"Error: Invalid solution paths: {string.Join(", ", invalidPaths)}";
                }

                var searchService = serviceProvider?.GetService<SymbolSearchService>();
                if (searchService == null)
                {
                    return "Error: Symbol search service not available.";
                }

                var logger = serviceProvider?.GetService<ILogger<CodeNavigationTools>>();
                logger?.LogInformation("Searching for '{SymbolName}' across {Count} solutions",
                    symbolName, solutionPathArray.Length);

                // Search across all solutions
                var results = await searchService.FindReferencesAcrossSolutionsAsync(
                    symbolName,
                    solutionPathArray,
                    includeDefinition);

                // Format based on detail level
                var formattedResult = detailLevel.ToLower() switch
                {
                    "summary" => FormatReferencesSummary(results),
                    "locations" => FormatReferencesLocations(results),
                    "full" => FormatReferencesFull(results),
                    _ => FormatReferencesLocations(results)
                };

                // Add solution summary
                var solutionSummary = $"Searched across {solutionPathArray.Length} solutions:\n" +
                    string.Join("\n", solutionPathArray.Select((path, i) => $"  {i + 1}. {Path.GetFileName(path)}")) +
                    "\n\n";

                return solutionSummary + formattedResult;
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger<CodeNavigationTools>>();
                logger?.LogError(ex, "Error finding references across solutions for symbol: {SymbolName}", symbolName);
                return $"Error: An unexpected error occurred while finding references across solutions: {ex.Message}";
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

                // Normalize detail level to lowercase
                var normalizedLevel = detailLevel.ToLowerInvariant();

                return normalizedLevel switch
                {
                    "summary" => FormatSymbolInfoSummary(info),
                    "basic" => FormatSymbolInfoBasic(info),
                    "full" => FormatSymbolInfoFull(info),
                    _ => FormatSymbolInfoBasic(info)
                };
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

        [McpServerTool, Description("Get compilation errors and warnings from solution to quickly identify build issues without running full build")]
        public static async Task<string> GetCompilationErrors(
            [Description("Path to solution file (.sln)")] string solutionPath,
            [Description("Output mode: compact (error counts and key issues), normal (balanced), detailed (comprehensive). Default: normal")]
            string mode = "normal",
            [Description("Severity filter: Error, Warning, Info, or All (default: All)")] string severity = "All",
            [Description("Filter by project name pattern (supports wildcards: * and ?)")] string? projectFilter = null,
            [Description("Filter by specific error codes (e.g., CS0103, CS0246)")] string[]? errorCodes = null,
            IServiceProvider? serviceProvider = null)
        {
            try
            {
                var validator = serviceProvider?.GetService<SecurityValidator>();
                if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
                {
                    return "Error: Invalid solution path provided.";
                }

                var diagnosticsService = serviceProvider?.GetService<DiagnosticsService>();
                if (diagnosticsService == null)
                {
                    return "Error: Diagnostics service not available.";
                }

                var results = await diagnosticsService.GetCompilationErrorsAsync(
                    solutionPath,
                    severity,
                    projectFilter,
                    errorCodes);

                // Normalize mode to lowercase
                var normalizedMode = mode.ToLowerInvariant();

                return normalizedMode switch
                {
                    "compact" => FormatCompilationErrorsCompact(results, severity),
                    "detailed" => FormatCompilationErrorsDetailed(results, severity),
                    "normal" => FormatCompilationErrors(results, severity),
                    _ => FormatCompilationErrors(results, severity)
                };
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger<CodeNavigationTools>>();
                logger?.LogError(ex, "Error getting compilation errors");
                return $"Error: An unexpected error occurred while getting compilation errors: {ex.Message}";
            }
        }

        [McpServerTool, Description("Get structural outline of a C# file showing types and members without full implementation details")]
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

                // Normalize mode to lowercase
                var normalizedMode = mode.ToLowerInvariant();

                return normalizedMode switch
                {
                    "compact" => FormatFileOutlineCompact(outline, maxMembers),
                    "detailed" => FormatFileOutlineDetailed(outline, maxMembers, includeDocumentation),
                    "normal" => FormatFileOutlineNormal(outline, maxMembers, includeMembers, includeDocumentation),
                    _ => FormatFileOutlineNormal(outline, maxMembers, includeMembers, includeDocumentation)
                };
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger<CodeNavigationTools>>();
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

                // Normalize format to lowercase
                var normalizedFormat = format.ToLowerInvariant();

                return normalizedFormat switch
                {
                    "summary" => FormatImplementationResultsSummary(results, typeName),
                    "detailed" => FormatImplementationResultsDetailed(results, typeName),
                    "normal" => FormatImplementationResults(results, typeName),
                    _ => FormatImplementationResults(results, typeName)
                };
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger<CodeNavigationTools>>();
                logger?.LogError(ex, "Error finding implementations for: {TypeName}", typeName);
                return $"Error: An unexpected error occurred while finding implementations: {ex.Message}";
            }
        }

        [McpServerTool, Description("Find test classes and methods for a given type")]
        public static async Task<string> FindTestsForType(
            [Description("Type name to find tests for")] string typeName,
            [Description("Path to solution file (.sln)")] string solutionPath,
            [Description("Include partial name matches (default: true)")] bool includePartialMatches = true,
            IServiceProvider? serviceProvider = null)
        {
            try
            {
                var validator = serviceProvider?.GetService<SecurityValidator>();
                if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
                {
                    return "Error: Invalid solution path provided.";
                }

                var testDiscoveryService = serviceProvider?.GetService<TestDiscoveryService>();
                if (testDiscoveryService == null)
                {
                    return "Error: Test discovery service not available.";
                }

                var results = await testDiscoveryService.FindTestsForTypeAsync(
                    typeName,
                    solutionPath,
                    includePartialMatches);

                return FormatTestResults(results, typeName);
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger<CodeNavigationTools>>();
                logger?.LogError(ex, "Error finding tests for type: {TypeName}", typeName);
                return $"Error: An unexpected error occurred while finding tests: {ex.Message}";
            }
        }

        [McpServerTool, Description("Get complete class hierarchy showing ancestors (base classes/interfaces) and descendants (derived classes)")]
        public static async Task<string> GetClassHierarchy(
            [Description("Type name to analyze hierarchy for")] string typeName,
            [Description("Path to solution file (.sln)")] string solutionPath,
            [Description("Output format: compact (tree structure only), normal (balanced), detailed (comprehensive). Default: normal")]
            string format = "normal",
            [Description("Direction: ancestors, descendants, or both (default: both)")] string direction = "both",
            [Description("Maximum depth to traverse (default: 10)")] int maxDepth = 10,
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

                var result = await searchService.GetClassHierarchyAsync(
                    typeName,
                    solutionPath,
                    direction,
                    maxDepth);

                if (result == null)
                {
                    return $"Type '{typeName}' not found in solution.";
                }

                // Normalize format to lowercase
                var normalizedFormat = format.ToLowerInvariant();

                return normalizedFormat switch
                {
                    "compact" => FormatClassHierarchyCompact(result, direction),
                    "detailed" => FormatClassHierarchyDetailed(result, direction),
                    "normal" => FormatClassHierarchy(result, direction),
                    _ => FormatClassHierarchy(result, direction)
                };
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger<CodeNavigationTools>>();
                logger?.LogError(ex, "Error getting class hierarchy for: {TypeName}", typeName);
                return $"Error: An unexpected error occurred while getting class hierarchy: {ex.Message}";
            }
        }

        [McpServerTool, Description("Find all usages of a specific attribute across the solution")]
        public static async Task<string> FindAttributeUsages(
            [Description("Attribute name to search for (with or without 'Attribute' suffix)")] string attributeName,
            [Description("Path to solution file (.sln)")] string solutionPath,
            [Description("Output format: inline (compact single-line), normal (balanced), detailed (comprehensive). Default: normal")]
            string format = "normal",
            [Description("Target type filter: class, interface, method, property, field, parameter, or all (default: all)")] string targetType = "all",
            IServiceProvider? serviceProvider = null)
        {
            try
            {
                var validator = serviceProvider?.GetService<SecurityValidator>();
                if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
                {
                    return "Error: Invalid solution path provided.";
                }

                var attributeSearchService = serviceProvider?.GetService<AttributeSearchService>();
                if (attributeSearchService == null)
                {
                    return "Error: Attribute search service not available.";
                }

                var results = await attributeSearchService.FindAttributeUsagesAsync(
                    attributeName,
                    solutionPath,
                    targetType);

                // Normalize format to lowercase
                var normalizedFormat = format.ToLowerInvariant();

                return normalizedFormat switch
                {
                    "inline" => FormatAttributeUsagesInline(results, attributeName, targetType),
                    "detailed" => FormatAttributeUsagesDetailed(results, attributeName, targetType),
                    "normal" => FormatAttributeUsages(results, attributeName, targetType),
                    _ => FormatAttributeUsages(results, attributeName, targetType)
                };
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger<CodeNavigationTools>>();
                logger?.LogError(ex, "Error finding attribute usages for: {AttributeName}", attributeName);
                return $"Error: An unexpected error occurred while finding attribute usages: {ex.Message}";
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

        // Summary mode: Minimal information (30-50 tokens)
        private static string FormatSymbolInfoSummary(RoslynMcpServer.Models.SymbolInfo? info)
        {
            if (info == null)
                return "Symbol not found.";

            var output = new StringBuilder();
            output.AppendLine($"{info.Name} ({info.Kind}, {info.Accessibility})");

            // For methods, show signature
            if (info.Kind == "Method" && !string.IsNullOrEmpty(info.ReturnType))
            {
                var parameters = info.Parameters.Any()
                    ? string.Join(", ", info.Parameters.Select(p => p.Split(' ').First())) // Just types
                    : "";
                output.AppendLine($"→ {info.ReturnType} ({parameters})");
            }
            // For fields/properties, show type
            else if (!string.IsNullOrEmpty(info.ReturnType))
            {
                output.AppendLine($"Type: {info.ReturnType}");
            }

            // Simplified location (just filename and line)
            if (!string.IsNullOrEmpty(info.SourceLocation))
            {
                var parts = info.SourceLocation.Split(':');
                if (parts.Length >= 2)
                {
                    var fileName = Path.GetFileName(parts[0]);
                    output.AppendLine($"@ {fileName}:{parts[1]}");
                }
                else
                {
                    output.AppendLine($"@ {Path.GetFileName(info.SourceLocation)}");
                }
            }

            return output.ToString();
        }

        // Basic mode: Balanced information (80-120 tokens)
        private static string FormatSymbolInfoBasic(RoslynMcpServer.Models.SymbolInfo? info)
        {
            if (info == null)
                return "Symbol not found.";

            var output = new StringBuilder();
            output.AppendLine($"**{info.Name}** ({info.Kind})");

            // Type information
            if (!string.IsNullOrEmpty(info.ReturnType))
            {
                if (info.Kind == "Method")
                {
                    var paramStr = info.Parameters.Any() ? string.Join(", ", info.Parameters) : "";
                    output.AppendLine($"Signature: {info.ReturnType} {info.Name}({paramStr})");
                }
                else
                {
                    output.AppendLine($"Type: {info.ReturnType}");
                }
            }

            output.AppendLine($"Accessibility: {info.Accessibility}");

            // Location (declaring type + namespace, or just namespace)
            if (!string.IsNullOrEmpty(info.DeclaringType))
            {
                output.AppendLine($"In: {info.Namespace}.{info.DeclaringType}");
            }
            else if (!string.IsNullOrEmpty(info.Namespace))
            {
                output.AppendLine($"Namespace: {info.Namespace}");
            }

            // Simplified file location
            if (!string.IsNullOrEmpty(info.SourceLocation))
            {
                var parts = info.SourceLocation.Split(':');
                if (parts.Length >= 2)
                {
                    var fileName = Path.GetFileName(parts[0]);
                    output.AppendLine($"File: {fileName}:{parts[1]}");
                }
                else
                {
                    output.AppendLine($"File: {Path.GetFileName(info.SourceLocation)}");
                }
            }

            // Attributes (if any, show count only)
            if (info.Attributes.Any())
            {
                output.AppendLine($"Attributes: {info.Attributes.Count} (use detailLevel=full to see details)");
            }

            return output.ToString();
        }

        // Full mode: Comprehensive information (150-250 tokens, original behavior)
        private static string FormatSymbolInfoFull(RoslynMcpServer.Models.SymbolInfo? info)
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
            output.AppendLine($"  • Projects Analyzed: {analysis.AnalyzedProjects}");
            output.AppendLine($"  • Projects Failed: {analysis.FailedProjects}");
            output.AppendLine();

            // Show warnings if any
            if (analysis.Warnings.Any())
            {
                output.AppendLine("⚠️ **Warnings:**");
                foreach (var warning in analysis.Warnings.Take(5))
                {
                    output.AppendLine($"   - {warning.Context}: {warning.Message}");
                }
                if (analysis.Warnings.Count > 5)
                {
                    output.AppendLine($"   ... and {analysis.Warnings.Count - 5} more warnings");
                }
                output.AppendLine();
            }

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

        private static string FormatCompilationErrors(CompilationErrorResults errorResults, string severityFilter)
        {
            var errors = errorResults.Errors;

            if (!errors.Any())
                return $"No compilation {severityFilter.ToLower()} issues found. Solution builds successfully!";

            var output = new StringBuilder();
            output.AppendLine($"**Compilation Diagnostics** (Severity: {severityFilter})\n");
            output.AppendLine($"Found {errors.Count} issue{(errors.Count > 1 ? "s" : "")} ({errorResults.AnalyzedProjects} projects analyzed, {errorResults.FailedProjects} projects failed, {errorResults.FailedDiagnostics} diagnostics failed):\n");

            // Show warnings if any
            if (errorResults.Warnings.Any())
            {
                output.AppendLine("⚠️ **Warnings:**");
                foreach (var warning in errorResults.Warnings.Take(5))
                {
                    output.AppendLine($"   - {warning.Context}: {warning.Message}");
                }
                if (errorResults.Warnings.Count > 5)
                {
                    output.AppendLine($"   ... and {errorResults.Warnings.Count - 5} more warnings");
                }
                output.AppendLine();
            }

            // Group by severity
            var groupedBySeverity = errors.GroupBy(e => e.Severity).OrderBy(g => g.Key);
            foreach (var severityGroup in groupedBySeverity)
            {
                output.AppendLine($"## {severityGroup.Key} ({severityGroup.Count()})");
                output.AppendLine();

                // Group by project within severity
                var groupedByProject = severityGroup.GroupBy(e => e.ProjectName).OrderBy(g => g.Key);
                foreach (var projectGroup in groupedByProject)
                {
                    output.AppendLine($"### {projectGroup.Key} ({projectGroup.Count()} issue{(projectGroup.Count() > 1 ? "s" : "")})");
                    output.AppendLine();

                    // Show first 10 errors per project, then summarize
                    var displayedErrors = projectGroup.Take(10);
                    foreach (var error in displayedErrors)
                    {
                        output.AppendLine($"**{error.Id}**: {error.Message}");
                        output.AppendLine($"  📄 {error.FileName}:{error.LineNumber}:{error.ColumnNumber}");
                        if (!string.IsNullOrWhiteSpace(error.LineText))
                        {
                            output.AppendLine($"  ```csharp");
                            output.AppendLine($"  {error.LineText}");
                            output.AppendLine($"  ```");
                        }
                        output.AppendLine();
                    }

                    if (projectGroup.Count() > 10)
                    {
                        output.AppendLine($"... and {projectGroup.Count() - 10} more {severityGroup.Key.ToLower()} issue{(projectGroup.Count() - 10 > 1 ? "s" : "")} in this project");
                        output.AppendLine();
                    }
                }
            }

            // Summary
            var errorCount = errors.Count(e => e.Severity == "Error");
            var warningCount = errors.Count(e => e.Severity == "Warning");
            var infoCount = errors.Count(e => e.Severity == "Info" || e.Severity == "Hidden");

            output.AppendLine("---");
            output.AppendLine($"**Summary**: {errorCount} Error{(errorCount != 1 ? "s" : "")}, {warningCount} Warning{(warningCount != 1 ? "s" : "")}, {infoCount} Info");

            return output.ToString();
        }

        // Compact mode: Show only counts and key error codes
        private static string FormatCompilationErrorsCompact(CompilationErrorResults errorResults, string severityFilter)
        {
            var errors = errorResults.Errors;

            if (!errors.Any())
                return $"✅ No {severityFilter.ToLower()} issues ({errorResults.AnalyzedProjects} projects OK)";

            var output = new StringBuilder();
            output.AppendLine($"Issues: {errors.Count} ({errorResults.AnalyzedProjects} projects, {errorResults.FailedProjects} failed)\n");

            // Group by severity and show counts
            var groupedBySeverity = errors.GroupBy(e => e.Severity).OrderBy(g => g.Key);
            foreach (var severityGroup in groupedBySeverity)
            {
                output.AppendLine($"{severityGroup.Key}: {severityGroup.Count()}");

                // Group by project
                var groupedByProject = severityGroup.GroupBy(e => e.ProjectName).OrderBy(g => g.Key);
                foreach (var projectGroup in groupedByProject)
                {
                    output.AppendLine($"  {projectGroup.Key}: {projectGroup.Count()} issue{(projectGroup.Count() > 1 ? "s" : "")}");

                    // Show top error codes with counts (first 5)
                    var errorCodeGroups = projectGroup.GroupBy(e => e.Id).OrderByDescending(g => g.Count()).Take(5);
                    foreach (var codeGroup in errorCodeGroups)
                    {
                        var firstError = codeGroup.First();
                        output.AppendLine($"    {codeGroup.Key} ({codeGroup.Count()}x): {firstError.FileName}:{firstError.LineNumber}");
                    }

                    if (projectGroup.GroupBy(e => e.Id).Count() > 5)
                    {
                        output.AppendLine($"    ... and {projectGroup.GroupBy(e => e.Id).Count() - 5} more error types");
                    }
                }
            }

            // Compact summary
            var errorCount = errors.Count(e => e.Severity == "Error");
            var warningCount = errors.Count(e => e.Severity == "Warning");
            output.AppendLine($"\nTotal: {errorCount} errors, {warningCount} warnings");

            return output.ToString();
        }

        // Detailed mode: Comprehensive information with code snippets
        private static string FormatCompilationErrorsDetailed(CompilationErrorResults errorResults, string severityFilter)
        {
            var errors = errorResults.Errors;

            if (!errors.Any())
                return $"✅ No compilation {severityFilter.ToLower()} issues found. Solution builds successfully!\n\n**Analysis Summary:**\n  • Projects analyzed: {errorResults.AnalyzedProjects}\n  • Projects failed: {errorResults.FailedProjects}\n  • Diagnostics failed: {errorResults.FailedDiagnostics}";

            var output = new StringBuilder();
            output.AppendLine($"**Compilation Diagnostics** (Severity: {severityFilter})\n");
            output.AppendLine($"📊 **Analysis Summary:**");
            output.AppendLine($"  • Total issues: {errors.Count}");
            output.AppendLine($"  • Projects analyzed: {errorResults.AnalyzedProjects}");
            output.AppendLine($"  • Projects with failures: {errorResults.FailedProjects}");
            output.AppendLine($"  • Diagnostics failed: {errorResults.FailedDiagnostics}");
            output.AppendLine();

            // Show all warnings if any
            if (errorResults.Warnings.Any())
            {
                output.AppendLine("⚠️ **Analysis Warnings:**");
                foreach (var warning in errorResults.Warnings)
                {
                    output.AppendLine($"   - {warning.Context}: {warning.Message}");
                }
                output.AppendLine();
            }

            // Group by severity
            var groupedBySeverity = errors.GroupBy(e => e.Severity).OrderBy(g => g.Key);
            foreach (var severityGroup in groupedBySeverity)
            {
                output.AppendLine($"## {severityGroup.Key} ({severityGroup.Count()})");
                output.AppendLine();

                // Group by project within severity
                var groupedByProject = severityGroup.GroupBy(e => e.ProjectName).OrderBy(g => g.Key);
                foreach (var projectGroup in groupedByProject)
                {
                    output.AppendLine($"### 📦 {projectGroup.Key} ({projectGroup.Count()} issue{(projectGroup.Count() > 1 ? "s" : "")})");
                    output.AppendLine();

                    // Show all errors in detailed mode
                    foreach (var error in projectGroup)
                    {
                        output.AppendLine($"**{error.Id}**: {error.Message}");
                        output.AppendLine($"  📄 Location: {error.FileName}:{error.LineNumber}:{error.ColumnNumber}");
                        if (!string.IsNullOrWhiteSpace(error.LineText))
                        {
                            output.AppendLine($"  Code:");
                            output.AppendLine($"  ```csharp");
                            output.AppendLine($"  {error.LineText.Trim()}");
                            output.AppendLine($"  ```");
                        }
                        output.AppendLine();
                    }
                }
            }

            // Detailed summary with breakdown
            var errorCount = errors.Count(e => e.Severity == "Error");
            var warningCount = errors.Count(e => e.Severity == "Warning");
            var infoCount = errors.Count(e => e.Severity == "Info" || e.Severity == "Hidden");

            output.AppendLine("---");
            output.AppendLine("**Detailed Summary:**");
            output.AppendLine($"  • Errors: {errorCount}");
            output.AppendLine($"  • Warnings: {warningCount}");
            output.AppendLine($"  • Info/Hidden: {infoCount}");

            // Show top error codes across all projects
            var topErrorCodes = errors.GroupBy(e => e.Id)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => $"{g.Key} ({g.Count()}x)")
                .ToList();

            if (topErrorCodes.Any())
            {
                output.AppendLine($"\n**Most Common Issues:**");
                foreach (var code in topErrorCodes)
                {
                    output.AppendLine($"  • {code}");
                }
            }

            return output.ToString();
        }

        // Compact mode: Minimal information, maximum token efficiency
        private static string FormatFileOutlineCompact(FileOutlineResult outline, int maxMembers)
        {
            var output = new StringBuilder();
            output.AppendLine($"File: {outline.FileName} ({outline.CodeLines} LOC, {outline.UsingStatements.Count} usings)\n");

            if (!outline.Types.Any())
            {
                output.AppendLine("No types found.");
                return output.ToString();
            }

            foreach (var type in outline.Types)
            {
                output.AppendLine($"{type.Name} ({type.Kind}, {type.Accessibility}) @ {type.Namespace}");

                // Group members by kind and show counts
                var memberGroups = type.Members.GroupBy(m => m.Kind).OrderBy(g => g.Key).ToList();

                if (memberGroups.Any())
                {
                    foreach (var group in memberGroups)
                    {
                        var members = group.ToList();
                        var publicCount = members.Count(m => m.Accessibility == "Public");
                        var privateCount = members.Count(m => m.Accessibility == "Private");
                        var countDetail = publicCount > 0 && privateCount > 0
                            ? $" ({publicCount} public, {privateCount} private)"
                            : "";

                        output.AppendLine($"  {group.Key}s: {members.Count}{countDetail}");

                        // Show method signatures for methods (compact format)
                        if (group.Key == "Method")
                        {
                            var displayMembers = maxMembers > 0 ? members.Take(maxMembers) : members;
                            foreach (var method in displayMembers)
                            {
                                var modifiers = GetModifiersCompact(method);
                                // Use Signature for display, Type is the return type
                                output.AppendLine($"    {method.Signature} {modifiers}");
                            }

                            if (maxMembers > 0 && members.Count > maxMembers)
                            {
                                output.AppendLine($"    ... and {members.Count - maxMembers} more (use maxMembers=0 for all)");
                            }
                        }
                    }
                }
                else
                {
                    output.AppendLine("  No members");
                }

                output.AppendLine();
            }

            return output.ToString();
        }

        // Normal mode: Balanced information
        private static string FormatFileOutlineNormal(FileOutlineResult outline, int maxMembers, bool includeMembers, bool includeDocumentation)
        {
            var output = new StringBuilder();
            output.AppendLine($"**File Outline**: {outline.FileName}\n");

            // Simplified statistics (no percentages)
            output.AppendLine($"📊 **Statistics**:");
            output.AppendLine($"  • Lines: {outline.CodeLines} code, {outline.CommentLines} comments, {outline.BlankLines} blank");
            output.AppendLine($"  • Types: {outline.Types.Count} ({outline.FailedTypes} failed)");
            if (includeMembers)
            {
                output.AppendLine($"  • Members: {outline.Types.Sum(t => t.Members.Count)} ({outline.FailedMembers} failed)");
            }
            output.AppendLine();

            // Top 5 using statements only
            if (outline.UsingStatements.Any())
            {
                output.AppendLine($"📦 **Using** ({outline.UsingStatements.Count}): {string.Join(", ", outline.UsingStatements.Take(5))}");
                if (outline.UsingStatements.Count > 5)
                {
                    output.AppendLine($"     ... and {outline.UsingStatements.Count - 5} more");
                }
                output.AppendLine();
            }

            // Types
            if (outline.Types.Any())
            {
                output.AppendLine($"📋 **Types** ({outline.Types.Count}):\n");

                foreach (var type in outline.Types)
                {
                    var icon = GetTypeIcon(type.Kind);
                    output.AppendLine($"{icon} **{type.Name}** ({type.Kind}, {type.Accessibility})");
                    output.AppendLine($"   Line {type.LineNumber}, Namespace: {type.Namespace}");

                    if (includeDocumentation && !string.IsNullOrWhiteSpace(type.Documentation))
                    {
                        output.AppendLine($"   💬 {type.Documentation}");
                    }

                    if (includeMembers && type.Members.Any())
                    {
                        var memberGroups = type.Members.GroupBy(m => m.Kind).OrderBy(g => GetMemberKindOrder(g.Key));

                        foreach (var group in memberGroups)
                        {
                            var members = group.ToList();
                            if (!members.Any()) continue;

                            output.AppendLine($"\n   📋 **{group.Key}s** ({members.Count}):");

                            var displayMembers = maxMembers > 0 ? members.Take(maxMembers) : members;
                            foreach (var member in displayMembers)
                            {
                                output.AppendLine($"     • {member.Signature}");
                                if (!string.IsNullOrEmpty(member.Type))
                                {
                                    output.AppendLine($"       → {member.Type}");
                                }
                                output.AppendLine($"       Line {member.LineNumber}, {member.Accessibility}");

                                if (includeDocumentation && !string.IsNullOrWhiteSpace(member.Documentation))
                                {
                                    output.AppendLine($"       💬 {member.Documentation}");
                                }
                            }

                            if (maxMembers > 0 && members.Count > maxMembers)
                            {
                                output.AppendLine($"     ... and {members.Count - maxMembers} more (use maxMembers=0 for all)");
                            }
                        }
                    }

                    output.AppendLine();
                }
            }
            else
            {
                output.AppendLine("No types found in this file.");
            }

            return output.ToString();
        }

        // Detailed mode: Comprehensive information (original behavior)
        private static string FormatFileOutlineDetailed(FileOutlineResult outline, int maxMembers, bool includeDocumentation)
        {
            var output = new StringBuilder();
            output.AppendLine($"**File Outline**: {outline.FileName}\n");
            output.AppendLine($"📊 **Statistics**:");
            output.AppendLine($"  • Total Lines: {outline.TotalLines}");
            output.AppendLine($"  • Code Lines: {outline.CodeLines} ({outline.CodeLines * 100.0 / outline.TotalLines:F1}%)");
            output.AppendLine($"  • Comment Lines: {outline.CommentLines} ({outline.CommentLines * 100.0 / outline.TotalLines:F1}%)");
            output.AppendLine($"  • Blank Lines: {outline.BlankLines} ({outline.BlankLines * 100.0 / outline.TotalLines:F1}%)");
            output.AppendLine($"  • Types Found: {outline.Types.Count} ({outline.FailedTypes} failed)");
            output.AppendLine($"  • Members Found: {outline.Types.Sum(t => t.Members.Count)} ({outline.FailedMembers} failed)");
            output.AppendLine();

            // Show warnings if any
            if (outline.Warnings.Any())
            {
                output.AppendLine("⚠️ **Warnings:**");
                foreach (var warning in outline.Warnings.Take(5))
                {
                    output.AppendLine($"   - {warning.Context}: {warning.Message}");
                }
                if (outline.Warnings.Count > 5)
                {
                    output.AppendLine($"   ... and {outline.Warnings.Count - 5} more warnings");
                }
                output.AppendLine();
            }

            // Using statements
            if (outline.UsingStatements.Any())
            {
                output.AppendLine($"📦 **Using Statements** ({outline.UsingStatements.Count}):");
                foreach (var usingStmt in outline.UsingStatements.Take(10))
                {
                    output.AppendLine($"  • {usingStmt}");
                }
                if (outline.UsingStatements.Count > 10)
                {
                    output.AppendLine($"  ... and {outline.UsingStatements.Count - 10} more");
                }
                output.AppendLine();
            }

            // Namespaces
            if (outline.Namespaces.Any())
            {
                output.AppendLine($"🏷️ **Namespaces**: {string.Join(", ", outline.Namespaces)}");
                output.AppendLine();
            }

            // Types
            if (outline.Types.Any())
            {
                output.AppendLine($"📋 **Types** ({outline.Types.Count}):\n");

                foreach (var type in outline.Types)
                {
                    var icon = type.Kind switch
                    {
                        "Class" => "🔷",
                        "Interface" => "🔹",
                        "Struct" => "🔸",
                        "Enum" => "🔢",
                        "Record" => "📝",
                        "Record Struct" => "📝",
                        _ => "•"
                    };

                    output.AppendLine($"{icon} **{type.Name}** ({type.Kind}, {type.Accessibility})");
                    output.AppendLine($"   Line {type.LineNumber}");

                    if (includeDocumentation && !string.IsNullOrWhiteSpace(type.Documentation))
                    {
                        output.AppendLine($"   💬 {type.Documentation}");
                    }

                    if (type.BaseTypes.Any())
                    {
                        output.AppendLine($"   ↗️ Inherits/Implements: {string.Join(", ", type.BaseTypes)}");
                    }

                    if (type.Attributes.Any())
                    {
                        output.AppendLine($"   🏷️ Attributes: [{string.Join(", ", type.Attributes)}]");
                    }

                    // Members
                    if (type.Members.Any())
                    {
                        output.AppendLine($"   📌 **Members** ({type.Members.Count}):");

                        // Group members by kind
                        var memberGroups = type.Members.GroupBy(m => m.Kind);
                        foreach (var group in memberGroups.OrderBy(g => GetMemberKindOrder(g.Key)))
                        {
                            var memberIcon = GetMemberIcon(group.Key);
                            var members = group.ToList();
                            output.AppendLine($"      {memberIcon} {group.Key}s ({members.Count}):");

                            var displayMembers = maxMembers > 0 ? members.Take(maxMembers) : members;
                            foreach (var member in displayMembers)
                            {
                                var modifiers = new List<string>();
                                if (member.IsStatic) modifiers.Add("static");
                                if (member.IsAsync) modifiers.Add("async");
                                if (member.IsAbstract) modifiers.Add("abstract");
                                if (member.IsVirtual) modifiers.Add("virtual");
                                if (member.IsOverride) modifiers.Add("override");

                                var modifierText = modifiers.Any() ? $" [{string.Join(", ", modifiers)}]" : "";

                                output.AppendLine($"         • {member.Accessibility} {member.Signature}{modifierText}");
                                output.AppendLine($"           Line {member.LineNumber}");

                                if (includeDocumentation && !string.IsNullOrWhiteSpace(member.Documentation))
                                {
                                    output.AppendLine($"           💬 {member.Documentation}");
                                }
                            }

                            if (maxMembers > 0 && members.Count > maxMembers)
                            {
                                output.AppendLine($"         ... and {members.Count - maxMembers} more (use maxMembers=0 for all)");
                            }
                        }
                    }
                    else
                    {
                        output.AppendLine($"   📌 No members");
                    }

                    output.AppendLine();
                }
            }
            else
            {
                output.AppendLine("No types found in this file.");
            }

            return output.ToString();
        }

        private static string GetMemberIcon(string memberKind)
        {
            return memberKind switch
            {
                "Constructor" => "🏗️",
                "Method" => "⚙️",
                "Property" => "🔧",
                "Field" => "📦",
                "Event" => "⚡",
                _ => "•"
            };
        }

        private static int GetMemberKindOrder(string memberKind)
        {
            return memberKind switch
            {
                "Constructor" => 1,
                "Field" => 2,
                "Property" => 3,
                "Method" => 4,
                "Event" => 5,
                _ => 99
            };
        }

        private static string GetTypeIcon(string typeKind)
        {
            return typeKind switch
            {
                "Class" => "🔷",
                "Interface" => "🔹",
                "Struct" => "🔸",
                "Enum" => "🔢",
                "Record" => "📝",
                "Record Struct" => "📝",
                _ => "•"
            };
        }

        private static string GetModifiersCompact(dynamic member)
        {
            var modifiers = new List<string>();
            if (member.IsStatic) modifiers.Add("static");
            if (member.IsAsync) modifiers.Add("async");
            if (member.IsAbstract) modifiers.Add("abstract");
            if (member.IsVirtual) modifiers.Add("virtual");
            if (member.IsOverride) modifiers.Add("override");

            return modifiers.Any() ? $"[{string.Join(",", modifiers)}]" : "";
        }

        // Summary mode: Names and locations only
        private static string FormatImplementationResultsSummary(List<ImplementationResult> results, string typeName)
        {
            if (!results.Any())
                return $"No implementations of '{typeName}' found";

            var output = new StringBuilder();
            output.AppendLine($"Implementations of '{typeName}': {results.Count}\n");

            // Group by project
            var groupedByProject = results.GroupBy(r => r.ProjectName).OrderBy(g => g.Key);

            foreach (var projectGroup in groupedByProject)
            {
                output.AppendLine($"{projectGroup.Key} ({projectGroup.Count()}):");

                foreach (var impl in projectGroup)
                {
                    var modifiers = "";
                    if (impl.IsAbstract) modifiers = " (abstract)";
                    else if (impl.IsSealed) modifiers = " (sealed)";

                    output.AppendLine($"  {impl.ImplementingTypeName}{modifiers} @ {Path.GetFileName(impl.FileName)}:{impl.LineNumber}");
                }
                output.AppendLine();
            }

            return output.ToString();
        }

        // Detailed mode: Comprehensive information with all metadata
        private static string FormatImplementationResultsDetailed(List<ImplementationResult> results, string typeName)
        {
            if (!results.Any())
                return $"No implementations found for '{typeName}'.\nNote: {typeName} must be an interface or abstract class.";

            var output = new StringBuilder();
            output.AppendLine($"**Implementations of '{typeName}'**\n");

            var concreteCount = results.Count(r => !r.IsAbstract);
            var abstractCount = results.Count(r => r.IsAbstract);

            output.AppendLine($"📊 **Analysis Summary:**");
            output.AppendLine($"  • Total implementations: {results.Count}");
            output.AppendLine($"  • Concrete implementations: {concreteCount}");
            output.AppendLine($"  • Abstract implementations: {abstractCount}");
            output.AppendLine();

            // Group by project
            var groupedByProject = results.GroupBy(r => r.ProjectName).OrderBy(g => g.Key);

            foreach (var projectGroup in groupedByProject)
            {
                output.AppendLine($"### 📦 {projectGroup.Key} ({projectGroup.Count()} implementation{(projectGroup.Count() > 1 ? "s" : "")})");
                output.AppendLine();

                foreach (var impl in projectGroup)
                {
                    var icon = impl.IsAbstract ? "🔷" : impl.IsSealed ? "🔒" : "✅";
                    var modifiers = new List<string>();
                    if (impl.IsAbstract) modifiers.Add("abstract");
                    if (impl.IsSealed) modifiers.Add("sealed");

                    var modifierText = modifiers.Any() ? $" [{string.Join(", ", modifiers)}]" : "";

                    output.AppendLine($"{icon} **{impl.ImplementingTypeName}** ({impl.Accessibility}{modifierText})");
                    output.AppendLine($"   📄 Location: {impl.FileName}:{impl.LineNumber}");
                    output.AppendLine($"   Project: {impl.ProjectName}");

                    if (!string.IsNullOrWhiteSpace(impl.Namespace))
                    {
                        output.AppendLine($"   Namespace: {impl.Namespace}");
                    }

                    if (!string.IsNullOrWhiteSpace(impl.Documentation))
                    {
                        output.AppendLine($"   💬 Documentation: {impl.Documentation}");
                    }

                    if (!string.IsNullOrWhiteSpace(impl.BaseClass))
                    {
                        output.AppendLine($"   ↗️ Base Class: {impl.BaseClass}");
                    }

                    // Show all implemented interfaces
                    if (impl.ImplementedInterfaces.Any())
                    {
                        output.AppendLine($"   🔹 **Implemented Interfaces:**");
                        foreach (var iface in impl.ImplementedInterfaces)
                        {
                            var isTarget = iface.Contains(typeName) ? " (target)" : "";
                            output.AppendLine($"      • {iface}{isTarget}");
                        }
                    }

                    output.AppendLine();
                }
            }

            // Detailed summary
            output.AppendLine("---");
            output.AppendLine($"**Detailed Summary:**");
            output.AppendLine($"  • Total: {results.Count} implementation{(results.Count > 1 ? "s" : "")}");
            output.AppendLine($"  • Concrete: {concreteCount}");
            output.AppendLine($"  • Abstract: {abstractCount}");
            output.AppendLine($"  • Projects: {groupedByProject.Count()}");

            // Group by accessibility
            var accessibilityGroups = results.GroupBy(r => r.Accessibility).OrderBy(g => g.Key);
            output.AppendLine($"\n**By Accessibility:**");
            foreach (var group in accessibilityGroups)
            {
                output.AppendLine($"  • {group.Key}: {group.Count()}");
            }

            return output.ToString();
        }

        private static string FormatImplementationResults(List<ImplementationResult> results, string typeName)
        {
            if (!results.Any())
                return $"No implementations found for '{typeName}'.\nNote: {typeName} must be an interface or abstract class.";

            var output = new StringBuilder();
            output.AppendLine($"**Implementations of '{typeName}'**\n");
            output.AppendLine($"Found {results.Count} implementation{(results.Count > 1 ? "s" : "")}:\n");

            // Group by project
            var groupedByProject = results.GroupBy(r => r.ProjectName).OrderBy(g => g.Key);

            foreach (var projectGroup in groupedByProject)
            {
                output.AppendLine($"### {projectGroup.Key} ({projectGroup.Count()} implementation{(projectGroup.Count() > 1 ? "s" : "")})");
                output.AppendLine();

                foreach (var impl in projectGroup)
                {
                    var icon = impl.IsAbstract ? "🔷" : impl.IsSealed ? "🔒" : "✅";
                    var modifiers = new List<string>();
                    if (impl.IsAbstract) modifiers.Add("abstract");
                    if (impl.IsSealed) modifiers.Add("sealed");

                    var modifierText = modifiers.Any() ? $" [{string.Join(", ", modifiers)}]" : "";

                    output.AppendLine($"{icon} **{impl.ImplementingTypeName}** ({impl.Accessibility}{modifierText})");
                    output.AppendLine($"   📄 {impl.FileName}:{impl.LineNumber}");

                    if (!string.IsNullOrWhiteSpace(impl.Namespace))
                    {
                        output.AppendLine($"   📦 Namespace: {impl.Namespace}");
                    }

                    if (!string.IsNullOrWhiteSpace(impl.Documentation))
                    {
                        output.AppendLine($"   💬 {impl.Documentation}");
                    }

                    if (!string.IsNullOrWhiteSpace(impl.BaseClass))
                    {
                        output.AppendLine($"   ↗️ Base Class: {impl.BaseClass}");
                    }

                    if (impl.ImplementedInterfaces.Count > 1) // More than just the target interface
                    {
                        var otherInterfaces = impl.ImplementedInterfaces
                            .Where(i => !i.Contains(typeName))
                            .ToList();

                        if (otherInterfaces.Any())
                        {
                            output.AppendLine($"   🔹 Also Implements: {string.Join(", ", otherInterfaces.Take(3))}");
                            if (otherInterfaces.Count > 3)
                            {
                                output.AppendLine($"      ... and {otherInterfaces.Count - 3} more");
                            }
                        }
                    }

                    output.AppendLine();
                }
            }

            // Summary
            var concreteCount = results.Count(r => !r.IsAbstract);
            var abstractCount = results.Count(r => r.IsAbstract);

            output.AppendLine("---");
            output.AppendLine($"**Summary**: {concreteCount} concrete, {abstractCount} abstract implementation{(results.Count > 1 ? "s" : "")} across {groupedByProject.Count()} project{(groupedByProject.Count() > 1 ? "s" : "")}");

            return output.ToString();
        }

        private static string FormatTestResults(List<TestClassResult> results, string typeName)
        {
            if (!results.Any())
                return $"No test classes found for '{typeName}'.\nNote: Searched test projects for classes matching naming conventions (e.g., {typeName}Tests, {typeName}Test).";

            var output = new StringBuilder();
            var totalTests = results.Sum(r => r.TestCount);

            output.AppendLine($"**Test Classes for '{typeName}'**\n");
            output.AppendLine($"Found {results.Count} test class{(results.Count > 1 ? "es" : "")} with {totalTests} test{(totalTests > 1 ? "s" : "")} total:\n");

            // Group by project
            var groupedByProject = results.GroupBy(r => r.ProjectName).OrderBy(g => g.Key);

            foreach (var projectGroup in groupedByProject)
            {
                var projectTestCount = projectGroup.Sum(r => r.TestCount);
                output.AppendLine($"### {projectGroup.Key} ({projectTestCount} test{(projectTestCount > 1 ? "s" : "")})");
                output.AppendLine();

                foreach (var testClass in projectGroup)
                {
                    output.AppendLine($"🧪 **{testClass.TestClassName}** - {testClass.TestCount} test{(testClass.TestCount > 1 ? "s" : "")}");
                    output.AppendLine($"   📄 {testClass.FileName}:{testClass.LineNumber}");
                    output.AppendLine($"   🔬 Framework: {testClass.TestFramework}");

                    if (!string.IsNullOrWhiteSpace(testClass.Documentation))
                    {
                        output.AppendLine($"   💬 {testClass.Documentation}");
                    }

                    // Show test methods
                    if (testClass.TestMethods.Any())
                    {
                        output.AppendLine($"   📋 **Test Methods**:");

                        // Show first 10 tests
                        var displayedTests = testClass.TestMethods.Take(10);
                        foreach (var testMethod in displayedTests)
                        {
                            var attributeText = string.Join(", ", testMethod.TestAttributes.Select(a => $"[{a}]"));
                            var displayNameText = !string.IsNullOrWhiteSpace(testMethod.DisplayName)
                                ? $" - \"{testMethod.DisplayName}\""
                                : "";

                            output.AppendLine($"      ✓ {testMethod.MethodName} {attributeText}{displayNameText}");
                            output.AppendLine($"        Line {testMethod.LineNumber}");
                        }

                        if (testClass.TestMethods.Count > 10)
                        {
                            output.AppendLine($"      ... and {testClass.TestMethods.Count - 10} more test{(testClass.TestMethods.Count - 10 > 1 ? "s" : "")}");
                        }
                    }

                    output.AppendLine();
                }
            }

            // Summary by framework
            var frameworkGroups = results.GroupBy(r => r.TestFramework);
            output.AppendLine("---");
            output.AppendLine($"**Summary by Framework**:");
            foreach (var framework in frameworkGroups.OrderBy(g => g.Key))
            {
                var frameworkTests = framework.Sum(r => r.TestCount);
                output.AppendLine($"  • {framework.Key}: {framework.Count()} class{(framework.Count() > 1 ? "es" : "")}, {frameworkTests} test{(frameworkTests > 1 ? "s" : "")}");
            }

            return output.ToString();
        }

        // Compact mode: Tree structure only, minimal details
        private static string FormatClassHierarchyCompact(ClassHierarchyResult result, string direction)
        {
            var output = new StringBuilder();
            output.AppendLine($"{result.TypeName} ({result.TypeKind})\n");

            // Ancestors
            if ((direction == "ancestors" || direction == "both") && result.Ancestors.Any())
            {
                output.AppendLine($"Ancestors ({CountTotalNodes(result.Ancestors)}):");
                FormatHierarchyNodesCompact(result.Ancestors, output, "  ", isAncestor: true);
                output.AppendLine();
            }

            // Descendants
            if ((direction == "descendants" || direction == "both") && result.Descendants.Any())
            {
                output.AppendLine($"Descendants ({CountTotalNodes(result.Descendants)}):");
                FormatHierarchyNodesCompact(result.Descendants, output, "  ", isAncestor: false);
                output.AppendLine();
            }

            return output.ToString();
        }

        private static void FormatHierarchyNodesCompact(List<HierarchyNode> nodes, StringBuilder output, string indent, bool isAncestor)
        {
            foreach (var node in nodes)
            {
                var arrow = isAncestor ? "↑" : "↓";
                var typeInfo = node.IsInterface ? "I" : node.IsAbstract ? "A" : "C";
                output.AppendLine($"{indent}{arrow} {node.Name} ({typeInfo})");

                // Recursively format children
                if (node.Children.Any())
                {
                    FormatHierarchyNodesCompact(node.Children, output, indent + "  ", isAncestor);
                }
            }
        }

        // Detailed mode: Comprehensive information with full metadata
        private static string FormatClassHierarchyDetailed(ClassHierarchyResult result, string direction)
        {
            var output = new StringBuilder();
            output.AppendLine($"**Class Hierarchy for '{result.TypeName}'**\n");

            // Type information with full details
            var modifiers = new List<string>();
            if (result.IsAbstract) modifiers.Add("abstract");
            if (result.IsSealed) modifiers.Add("sealed");
            var modifierText = modifiers.Any() ? $" [{string.Join(", ", modifiers)}]" : "";

            output.AppendLine($"📦 **{result.TypeName}** ({result.TypeKind}, {result.Accessibility}{modifierText})");
            output.AppendLine($"   Namespace: {result.Namespace}");
            output.AppendLine($"   Full Path: {result.FilePath}");
            output.AppendLine($"   Line: {result.LineNumber}");

            if (!string.IsNullOrWhiteSpace(result.Documentation))
            {
                output.AppendLine($"   💬 Documentation: {result.Documentation}");
            }

            output.AppendLine();

            // Ancestors with full details
            if ((direction == "ancestors" || direction == "both") && result.Ancestors.Any())
            {
                var ancestorCount = CountTotalNodes(result.Ancestors);
                output.AppendLine($"## ⬆️ ANCESTORS (Inheritance Chain)\n");
                output.AppendLine($"Total: {ancestorCount} type{(ancestorCount > 1 ? "s" : "")} that '{result.TypeName}' inherits from or implements:\n");
                FormatHierarchyNodesDetailed(result.Ancestors, output, "   ", isAncestor: true);
                output.AppendLine();
            }

            // Descendants with full details
            if ((direction == "descendants" || direction == "both") && result.Descendants.Any())
            {
                var descendantCount = CountTotalNodes(result.Descendants);
                output.AppendLine($"## ⬇️ DESCENDANTS (Derived Types)\n");
                output.AppendLine($"Total: {descendantCount} type{(descendantCount > 1 ? "s" : "")} that inherit from or implement '{result.TypeName}':\n");
                FormatHierarchyNodesDetailed(result.Descendants, output, "   ", isAncestor: false);
                output.AppendLine();
            }

            // Detailed summary with statistics
            var ancestorCount2 = CountTotalNodes(result.Ancestors);
            var descendantCount2 = CountTotalNodes(result.Descendants);

            output.AppendLine("---");
            output.AppendLine($"**Detailed Summary:**");
            output.AppendLine($"  • Target Type: {result.TypeName} ({result.TypeKind})");
            output.AppendLine($"  • Ancestors: {ancestorCount2}");
            output.AppendLine($"  • Descendants: {descendantCount2}");
            output.AppendLine($"  • Total Hierarchy Size: {ancestorCount2 + descendantCount2 + 1} types");

            return output.ToString();
        }

        private static void FormatHierarchyNodesDetailed(List<HierarchyNode> nodes, StringBuilder output, string indent, bool isAncestor)
        {
            foreach (var node in nodes)
            {
                var icon = node.IsInterface ? "🔹" : node.IsAbstract ? "🔷" : "▪️";
                var arrow = isAncestor ? "↑" : "↓";
                var typeInfo = node.IsInterface ? "Interface" : node.TypeKind;
                var modifiers = new List<string>();
                if (node.IsAbstract) modifiers.Add("abstract");
                var modifierText = modifiers.Any() ? $" [{string.Join(", ", modifiers)}]" : "";

                output.AppendLine($"{indent}{arrow} {icon} **{node.Name}** ({typeInfo}{modifierText})");

                if (!string.IsNullOrWhiteSpace(node.Namespace))
                {
                    output.AppendLine($"{indent}   Namespace: {node.Namespace}");
                }

                if (!string.IsNullOrWhiteSpace(node.ProjectName))
                {
                    output.AppendLine($"{indent}   Project: {node.ProjectName}");
                }

                if (node.LineNumber > 0)
                {
                    output.AppendLine($"{indent}   Location: {node.FilePath}:{node.LineNumber}");
                }

                // Recursively format children
                if (node.Children.Any())
                {
                    FormatHierarchyNodesDetailed(node.Children, output, indent + "   ", isAncestor);
                }
            }
        }

        private static string FormatClassHierarchy(ClassHierarchyResult result, string direction)
        {
            var output = new StringBuilder();
            output.AppendLine($"**Class Hierarchy for '{result.TypeName}'**\n");

            // Type information
            var modifiers = new List<string>();
            if (result.IsAbstract) modifiers.Add("abstract");
            if (result.IsSealed) modifiers.Add("sealed");
            var modifierText = modifiers.Any() ? $" [{string.Join(", ", modifiers)}]" : "";

            output.AppendLine($"📦 **{result.TypeName}** ({result.TypeKind}, {result.Accessibility}{modifierText})");
            output.AppendLine($"   Namespace: {result.Namespace}");
            output.AppendLine($"   📄 {Path.GetFileName(result.FilePath)}:{result.LineNumber}");

            if (!string.IsNullOrWhiteSpace(result.Documentation))
            {
                output.AppendLine($"   💬 {result.Documentation}");
            }

            output.AppendLine();

            // Ancestors
            if ((direction == "ancestors" || direction == "both") && result.Ancestors.Any())
            {
                output.AppendLine($"## ⬆️ ANCESTORS (Inheritance Chain)\n");
                output.AppendLine($"Types that '{result.TypeName}' inherits from or implements:\n");
                FormatHierarchyNodes(result.Ancestors, output, "   ", isAncestor: true);
                output.AppendLine();
            }

            // Descendants
            if ((direction == "descendants" || direction == "both") && result.Descendants.Any())
            {
                output.AppendLine($"## ⬇️ DESCENDANTS (Derived Types)\n");
                output.AppendLine($"Types that inherit from or implement '{result.TypeName}':\n");
                FormatHierarchyNodes(result.Descendants, output, "   ", isAncestor: false);
                output.AppendLine();
            }

            // Summary
            var ancestorCount = CountTotalNodes(result.Ancestors);
            var descendantCount = CountTotalNodes(result.Descendants);

            output.AppendLine("---");
            output.AppendLine($"**Summary**: {ancestorCount} ancestor{(ancestorCount != 1 ? "s" : "")}, {descendantCount} descendant{(descendantCount != 1 ? "s" : "")}");

            return output.ToString();
        }

        private static void FormatHierarchyNodes(List<HierarchyNode> nodes, StringBuilder output, string indent, bool isAncestor)
        {
            foreach (var node in nodes)
            {
                var icon = node.IsInterface ? "🔹" : node.IsAbstract ? "🔷" : "▪️";
                var arrow = isAncestor ? "↑" : "↓";
                var typeInfo = node.IsInterface ? "Interface" : node.TypeKind;

                output.AppendLine($"{indent}{arrow} {icon} **{node.Name}** ({typeInfo})");

                if (!string.IsNullOrWhiteSpace(node.Namespace))
                {
                    output.AppendLine($"{indent}   Namespace: {node.Namespace}");
                }

                if (!string.IsNullOrWhiteSpace(node.ProjectName))
                {
                    output.AppendLine($"{indent}   Project: {node.ProjectName}");
                }

                if (node.LineNumber > 0)
                {
                    output.AppendLine($"{indent}   📄 {Path.GetFileName(node.FilePath)}:{node.LineNumber}");
                }

                // Recursively format children
                if (node.Children.Any())
                {
                    FormatHierarchyNodes(node.Children, output, indent + "   ", isAncestor);
                }
            }
        }

        private static int CountTotalNodes(List<HierarchyNode> nodes)
        {
            int count = nodes.Count;
            foreach (var node in nodes)
            {
                count += CountTotalNodes(node.Children);
            }
            return count;
        }

        // Inline mode: Compact single-line format
        private static string FormatAttributeUsagesInline(AttributeSearchResults searchResults, string attributeName, string targetType)
        {
            var results = searchResults.Usages;

            if (!results.Any())
                return $"No [{attributeName}] usages" + (targetType != "all" ? $" on {targetType}" : "");

            var output = new StringBuilder();
            output.AppendLine($"[{attributeName}]: {results.Count} usage{(results.Count > 1 ? "s" : "")} found\n");

            // Group by target type
            var groupedByType = results.GroupBy(r => r.TargetType).OrderBy(g => g.Key);

            foreach (var typeGroup in groupedByType)
            {
                output.AppendLine($"{typeGroup.Key}s ({typeGroup.Count()}):");

                foreach (var usage in typeGroup.Take(30))  // Show up to 30 per type
                {
                    var location = $"{Path.GetFileName(usage.FileName)}:{usage.LineNumber}";
                    var args = "";

                    if (usage.AttributeArguments.Any() || usage.NamedArguments.Any())
                    {
                        var allArgs = new List<string>();
                        allArgs.AddRange(usage.AttributeArguments.Take(2));  // First 2 args only
                        if (usage.AttributeArguments.Count > 2)
                            allArgs.Add("...");

                        args = $"({string.Join(", ", allArgs)})";
                    }

                    output.AppendLine($"  {usage.TargetName}{args} @ {location}");
                }

                if (typeGroup.Count() > 30)
                {
                    output.AppendLine($"  ... and {typeGroup.Count() - 30} more");
                }
                output.AppendLine();
            }

            return output.ToString();
        }

        // Detailed mode: Comprehensive information with all details
        private static string FormatAttributeUsagesDetailed(AttributeSearchResults searchResults, string attributeName, string targetType)
        {
            var results = searchResults.Usages;

            if (!results.Any())
                return $"No usages of [{attributeName}] attribute found" +
                       (targetType != "all" ? $" on {targetType} targets." : ".") +
                       $"\n\n**Analysis:** {searchResults.SuccessCount} projects analyzed successfully, {searchResults.FailureCount} failed.";

            var output = new StringBuilder();
            output.AppendLine($"**Attribute Usages: [{attributeName}]**\n");

            if (targetType != "all")
            {
                output.AppendLine($"🔍 Filter: {targetType} targets only\n");
            }

            output.AppendLine($"📊 **Analysis Summary:**");
            output.AppendLine($"  • Total usages: {results.Count}");
            output.AppendLine($"  • Projects analyzed: {searchResults.SuccessCount}");
            output.AppendLine($"  • Projects failed: {searchResults.FailureCount}");
            output.AppendLine();

            // Show all warnings if any
            if (searchResults.Warnings.Any())
            {
                output.AppendLine("⚠️ **Analysis Warnings:**");
                foreach (var warning in searchResults.Warnings)
                {
                    output.AppendLine($"   - {warning.Context}: {warning.Message}");
                }
                output.AppendLine();
            }

            // Group by target type
            var groupedByType = results.GroupBy(r => r.TargetType).OrderBy(g => g.Key);

            foreach (var typeGroup in groupedByType)
            {
                output.AppendLine($"## {typeGroup.Key}s ({typeGroup.Count()})");
                output.AppendLine();

                // Group by project within each type
                var groupedByProject = typeGroup.GroupBy(r => r.ProjectName).OrderBy(g => g.Key);

                foreach (var projectGroup in groupedByProject)
                {
                    output.AppendLine($"### 📦 {projectGroup.Key} ({projectGroup.Count()} usage{(projectGroup.Count() > 1 ? "s" : "")})");
                    output.AppendLine();

                    // Show all usages in detailed mode
                    foreach (var usage in projectGroup)
                    {
                        var icon = usage.TargetType switch
                        {
                            "Class" => "🔷",
                            "Interface" => "🔹",
                            "Method" => "⚙️",
                            "Property" => "🔧",
                            "Field" => "📦",
                            "Parameter" => "📝",
                            "Event" => "⚡",
                            _ => "•"
                        };

                        output.AppendLine($"{icon} **{usage.TargetName}**");
                        output.AppendLine($"   📄 Location: {usage.FileName}:{usage.LineNumber}");
                        output.AppendLine($"   Project: {usage.ProjectName}");

                        if (!string.IsNullOrWhiteSpace(usage.DeclaringType))
                        {
                            output.AppendLine($"   Declaring Type: {usage.DeclaringType}");
                        }

                        if (!string.IsNullOrWhiteSpace(usage.Signature))
                        {
                            output.AppendLine($"   Signature: `{usage.Signature}`");
                        }

                        // Show all attribute arguments
                        if (usage.AttributeArguments.Any() || usage.NamedArguments.Any())
                        {
                            output.AppendLine($"   **Attribute Arguments:**");

                            if (usage.AttributeArguments.Any())
                            {
                                output.AppendLine($"     Positional:");
                                foreach (var arg in usage.AttributeArguments)
                                {
                                    output.AppendLine($"       • {arg}");
                                }
                            }

                            if (usage.NamedArguments.Any())
                            {
                                output.AppendLine($"     Named:");
                                foreach (var kvp in usage.NamedArguments)
                                {
                                    output.AppendLine($"       • {kvp.Key} = {kvp.Value}");
                                }
                            }
                        }

                        output.AppendLine();
                    }
                }
            }

            // Detailed summary by target type
            output.AppendLine("---");
            output.AppendLine($"**Detailed Summary:**");
            output.AppendLine($"\n**By Target Type:**");
            foreach (var typeGroup in groupedByType)
            {
                output.AppendLine($"  • {typeGroup.Key}: {typeGroup.Count()} usage{(typeGroup.Count() > 1 ? "s" : "")}");
            }

            // By project
            output.AppendLine($"\n**By Project:**");
            var projectSummary = results.GroupBy(r => r.ProjectName).OrderByDescending(g => g.Count()).Take(10);
            foreach (var projectGroup in projectSummary)
            {
                output.AppendLine($"  • {projectGroup.Key}: {projectGroup.Count()} usage{(projectGroup.Count() > 1 ? "s" : "")}");
            }

            return output.ToString();
        }

        private static string FormatAttributeUsages(AttributeSearchResults searchResults, string attributeName, string targetType)
        {
            var results = searchResults.Usages;

            if (!results.Any())
                return $"No usages of [{attributeName}] attribute found" +
                       (targetType != "all" ? $" on {targetType} targets." : ".");

            var output = new StringBuilder();
            output.AppendLine($"**Attribute Usages: [{attributeName}]**\n");

            if (targetType != "all")
            {
                output.AppendLine($"Filter: {targetType} targets only\n");
            }

            output.AppendLine($"Found {results.Count} usage{(results.Count > 1 ? "s" : "")} ({searchResults.SuccessCount} successful, {searchResults.FailureCount} failed):\n");

            // Show warnings if any
            if (searchResults.Warnings.Any())
            {
                output.AppendLine("⚠️ **Warnings:**");
                foreach (var warning in searchResults.Warnings.Take(5))
                {
                    output.AppendLine($"   - {warning.Context}: {warning.Message}");
                }
                if (searchResults.Warnings.Count > 5)
                {
                    output.AppendLine($"   ... and {searchResults.Warnings.Count - 5} more warnings");
                }
                output.AppendLine();
            }

            // Group by target type
            var groupedByType = results.GroupBy(r => r.TargetType).OrderBy(g => g.Key);

            foreach (var typeGroup in groupedByType)
            {
                output.AppendLine($"### {typeGroup.Key}s ({typeGroup.Count()})");
                output.AppendLine();

                // Group by project within each type
                var groupedByProject = typeGroup.GroupBy(r => r.ProjectName).OrderBy(g => g.Key);

                foreach (var projectGroup in groupedByProject)
                {
                    output.AppendLine($"**{projectGroup.Key}** ({projectGroup.Count()} usage{(projectGroup.Count() > 1 ? "s" : "")}):");
                    output.AppendLine();

                    foreach (var usage in projectGroup.Take(20))  // Limit to 20 per project
                    {
                        var icon = usage.TargetType switch
                        {
                            "Class" => "🔷",
                            "Interface" => "🔹",
                            "Method" => "⚙️",
                            "Property" => "🔧",
                            "Field" => "📦",
                            "Parameter" => "📝",
                            "Event" => "⚡",
                            _ => "•"
                        };

                        output.AppendLine($"{icon} **{usage.TargetName}**");
                        output.AppendLine($"   📄 {usage.FileName}:{usage.LineNumber}");

                        if (!string.IsNullOrWhiteSpace(usage.DeclaringType))
                        {
                            output.AppendLine($"   In: {usage.DeclaringType}");
                        }

                        if (!string.IsNullOrWhiteSpace(usage.Signature))
                        {
                            output.AppendLine($"   Signature: `{usage.Signature}`");
                        }

                        // Show attribute arguments if present
                        if (usage.AttributeArguments.Any() || usage.NamedArguments.Any())
                        {
                            var args = new List<string>();
                            args.AddRange(usage.AttributeArguments);
                            args.AddRange(usage.NamedArguments.Select(kvp => $"{kvp.Key} = {kvp.Value}"));

                            output.AppendLine($"   Arguments: {string.Join(", ", args)}");
                        }

                        output.AppendLine();
                    }

                    if (projectGroup.Count() > 20)
                    {
                        output.AppendLine($"... and {projectGroup.Count() - 20} more usage{(projectGroup.Count() - 20 > 1 ? "s" : "")} in this project");
                        output.AppendLine();
                    }
                }
            }

            // Summary by target type
            output.AppendLine("---");
            output.AppendLine($"**Summary by Target Type**:");
            foreach (var typeGroup in groupedByType)
            {
                output.AppendLine($"  • {typeGroup.Key}: {typeGroup.Count()} usage{(typeGroup.Count() > 1 ? "s" : "")}");
            }

            return output.ToString();
        }
    }
}