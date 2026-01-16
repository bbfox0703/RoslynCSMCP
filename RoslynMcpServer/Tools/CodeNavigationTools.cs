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

        [McpServerTool, Description("Find unused code (dead code) in the solution - types, methods, properties, and fields with no references")]
        public static async Task<string> FindUnusedCode(
            [Description("Path to solution file (.sln)")] string solutionPath,
            [Description("Output format: summary (counts only), normal (grouped list), detailed (full information). Default: normal")]
            string format = "normal",
            [Description("Scope: private (private members only), internal (internal members only), public (public members only), all (all members). Default: all")]
            string scope = "all",
            [Description("Include test projects in analysis (default: false)")] bool includeTests = false,
            IServiceProvider? serviceProvider = null)
        {
            try
            {
                var validator = serviceProvider?.GetService<SecurityValidator>();
                if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
                {
                    return "Error: Invalid solution path provided.";
                }

                var analyzer = serviceProvider?.GetService<UnusedCodeAnalyzer>();
                if (analyzer == null)
                {
                    return "Error: Unused code analyzer service not available.";
                }

                var results = await analyzer.AnalyzeUnusedCodeAsync(solutionPath, scope, includeTests);

                // Normalize format to lowercase
                var normalizedFormat = format.ToLowerInvariant();

                return normalizedFormat switch
                {
                    "summary" => FormatUnusedCodeSummary(results),
                    "detailed" => FormatUnusedCodeDetailed(results),
                    "normal" => FormatUnusedCodeNormal(results),
                    _ => FormatUnusedCodeNormal(results)
                };
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger<CodeNavigationTools>>();
                logger?.LogError(ex, "Error finding unused code");
                return $"Error: An unexpected error occurred while finding unused code: {ex.Message}";
            }
        }

        [McpServerTool, Description("Find unused dependencies (NuGet packages and project references) in the solution")]
        public static async Task<string> FindUnusedDependencies(
            [Description("Path to solution file (.sln)")] string solutionPath,
            [Description("Output format: summary (counts only), normal (grouped list), detailed (full information). Default: normal")]
            string format = "normal",
            [Description("Include NuGet package analysis (default: true)")] bool includeNuGetPackages = true,
            [Description("Include project reference analysis (default: true)")] bool includeProjectReferences = true,
            IServiceProvider? serviceProvider = null)
        {
            try
            {
                var validator = serviceProvider?.GetService<SecurityValidator>();
                if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
                {
                    return "Error: Invalid solution path provided.";
                }

                var analyzer = serviceProvider?.GetService<UnusedDependencyAnalyzer>();
                if (analyzer == null)
                {
                    return "Error: Unused dependency analyzer service not available.";
                }

                var results = await analyzer.AnalyzeUnusedDependenciesAsync(
                    solutionPath,
                    includeNuGetPackages,
                    includeProjectReferences);

                // Normalize format to lowercase
                var normalizedFormat = format.ToLowerInvariant();

                return normalizedFormat switch
                {
                    "summary" => FormatUnusedDependenciesSummary(results),
                    "detailed" => FormatUnusedDependenciesDetailed(results),
                    "normal" => FormatUnusedDependenciesNormal(results),
                    _ => FormatUnusedDependenciesNormal(results)
                };
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger<CodeNavigationTools>>();
                logger?.LogError(ex, "Error finding unused dependencies");
                return $"Error: An unexpected error occurred while finding unused dependencies: {ex.Message}";
            }
        }

        [McpServerTool, Description("Find security issues and anti-patterns in the solution (SQL injection, hardcoded secrets, weak crypto, etc.)")]
        public static async Task<string> FindSecurityIssues(
            [Description("Path to solution file (.sln)")] string solutionPath,
            [Description("Output format: summary (counts only), normal (grouped list), detailed (full information). Default: normal")]
            string format = "normal",
            [Description("Categories to check (comma-separated): sql-injection, secrets, crypto, path-traversal, deserialization, all. Default: all")]
            string categories = "all",
            [Description("Severity filter: critical, high, medium, low, all. Default: all")]
            string severity = "all",
            IServiceProvider? serviceProvider = null)
        {
            try
            {
                var validator = serviceProvider?.GetService<SecurityValidator>();
                if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
                {
                    return "Error: Invalid solution path provided.";
                }

                var analyzer = serviceProvider?.GetService<SecurityIssueAnalyzer>();
                if (analyzer == null)
                {
                    return "Error: Security issue analyzer service not available.";
                }

                // Parse categories
                var categoryArray = categories.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                var results = await analyzer.AnalyzeSecurityIssuesAsync(
                    solutionPath,
                    categoryArray,
                    severity);

                // Normalize format to lowercase
                var normalizedFormat = format.ToLowerInvariant();

                return normalizedFormat switch
                {
                    "summary" => FormatSecurityIssuesSummary(results),
                    "detailed" => FormatSecurityIssuesDetailed(results),
                    "normal" => FormatSecurityIssuesNormal(results),
                    _ => FormatSecurityIssuesNormal(results)
                };
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger<CodeNavigationTools>>();
                logger?.LogError(ex, "Error finding security issues");
                return $"Error: An unexpected error occurred while finding security issues: {ex.Message}";
            }
        }

        [McpServerTool, Description("Find duplicate code blocks across the solution")]
        public static async Task<string> FindDuplicateCode(
            [Description("Path to solution file (.sln)")] string solutionPath,
            [Description("Output format: summary (counts only), normal (grouped list), detailed (full information). Default: normal")]
            string format = "normal",
            [Description("Minimum lines to consider duplicate (default: 5)")] int minLines = 5,
            [Description("Similarity threshold percentage 70-100 (default: 90)")] int similarity = 90,
            IServiceProvider? serviceProvider = null)
        {
            try
            {
                var validator = serviceProvider?.GetService<SecurityValidator>();
                if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
                {
                    return "Error: Invalid solution path provided.";
                }

                var analyzer = serviceProvider?.GetService<DuplicateCodeAnalyzer>();
                if (analyzer == null)
                {
                    return "Error: Duplicate code analyzer service not available.";
                }

                var results = await analyzer.AnalyzeDuplicateCodeAsync(
                    solutionPath,
                    minLines,
                    similarity);

                // Normalize format to lowercase
                var normalizedFormat = format.ToLowerInvariant();

                return normalizedFormat switch
                {
                    "summary" => FormatDuplicateCodeSummary(results),
                    "detailed" => FormatDuplicateCodeDetailed(results),
                    "normal" => FormatDuplicateCodeNormal(results),
                    _ => FormatDuplicateCodeNormal(results)
                };
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger<CodeNavigationTools>>();
                logger?.LogError(ex, "Error finding duplicate code");
                return $"Error: An unexpected error occurred while finding duplicate code: {ex.Message}";
            }
        }

        [McpServerTool, Description("Analyze XML documentation coverage for types and members")]
        public static async Task<string> AnalyzeDocumentationCoverage(
            [Description("Path to solution file (.sln)")] string solutionPath,
            [Description("Output format: summary (counts only), normal (grouped list), detailed (with suggestions). Default: normal")]
            string format = "normal",
            [Description("Scope filter: public (public only), all (all symbols). Default: public")]
            string scope = "public",
            IServiceProvider? serviceProvider = null)
        {
            try
            {
                var validator = serviceProvider?.GetService<SecurityValidator>();
                if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
                {
                    return "Error: Invalid solution path provided.";
                }

                var analyzer = serviceProvider?.GetService<DocumentationAnalyzer>();
                if (analyzer == null)
                {
                    return "Error: Documentation analyzer service not available.";
                }

                var results = await analyzer.AnalyzeDocumentationCoverageAsync(
                    solutionPath,
                    scope);

                // Normalize format to lowercase
                var normalizedFormat = format.ToLowerInvariant();

                return normalizedFormat switch
                {
                    "summary" => FormatDocumentationCoverageSummary(results),
                    "detailed" => FormatDocumentationCoverageDetailed(results),
                    "normal" => FormatDocumentationCoverageNormal(results),
                    _ => FormatDocumentationCoverageNormal(results)
                };
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger<CodeNavigationTools>>();
                logger?.LogError(ex, "Error analyzing documentation coverage");
                return $"Error: An unexpected error occurred while analyzing documentation coverage: {ex.Message}";
            }
        }

        [McpServerTool, Description("Find TODO, FIXME, HACK, and other special comments in code")]
        public static async Task<string> FindTODOComments(
            [Description("Path to solution file (.sln)")] string solutionPath,
            [Description("Output format: summary (counts only), normal (grouped list), detailed (with code context). Default: normal")]
            string format = "normal",
            [Description("Comment types to find (comma-separated): TODO, FIXME, HACK, NOTE, BUG, XXX, OPTIMIZE, REFACTOR. Default: all")]
            string types = "all",
            IServiceProvider? serviceProvider = null)
        {
            var logger = serviceProvider?.GetService<ILogger<TODOCommentAnalyzer>>();
            var securityValidator = serviceProvider?.GetService<SecurityValidator>();

            try
            {
                // Validate solution path
                if (securityValidator != null && !securityValidator.ValidateSolutionPath(solutionPath))
                {
                    logger?.LogWarning("Invalid solution path: {SolutionPath}", solutionPath);
                    return $"Error: Invalid solution path: {solutionPath}";
                }

                // Parse comment types
                string[]? commentTypes = null;
                if (!string.IsNullOrWhiteSpace(types) && types.ToLowerInvariant() != "all")
                {
                    commentTypes = types.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                }

                // Get analyzer service
                var analyzer = serviceProvider?.GetService<TODOCommentAnalyzer>();
                if (analyzer == null)
                {
                    return "Error: TODO comment analyzer service not available.";
                }

                // Perform analysis
                var results = await analyzer.AnalyzeTODOCommentsAsync(solutionPath, commentTypes!);

                // Format output based on format parameter
                return format.ToLowerInvariant() switch
                {
                    "summary" => FormatTODOCommentsSummary(results),
                    "detailed" => FormatTODOCommentsDetailed(results),
                    _ => FormatTODOCommentsNormal(results)
                };
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error finding TODO comments");
                return $"Error: An unexpected error occurred while finding TODO comments: {ex.Message}";
            }
        }

        [McpServerTool, Description("Find large source files that may need refactoring")]
        public static async Task<string> FindLargeFiles(
            [Description("Path to solution file (.sln)")] string solutionPath,
            [Description("Output format: summary (top files only), normal (balanced), detailed (with metrics). Default: normal")]
            string format = "normal",
            [Description("Minimum line count threshold (default: 500)")] int threshold = 500,
            IServiceProvider? serviceProvider = null)
        {
            var logger = serviceProvider?.GetService<ILogger<LargeFileAnalyzer>>();
            var securityValidator = serviceProvider?.GetService<SecurityValidator>();

            try
            {
                // Validate solution path
                if (securityValidator != null && !securityValidator.ValidateSolutionPath(solutionPath))
                {
                    logger?.LogWarning("Invalid solution path: {SolutionPath}", solutionPath);
                    return $"Error: Invalid solution path: {solutionPath}";
                }

                // Get analyzer service
                var analyzer = serviceProvider?.GetService<LargeFileAnalyzer>();
                if (analyzer == null)
                {
                    return "Error: Large file analyzer service not available.";
                }

                // Perform analysis
                var results = await analyzer.AnalyzeLargeFilesAsync(solutionPath, threshold);

                // Format output based on format parameter
                return format.ToLowerInvariant() switch
                {
                    "summary" => FormatLargeFilesSummary(results, threshold),
                    "detailed" => FormatLargeFilesDetailed(results, threshold),
                    _ => FormatLargeFilesNormal(results, threshold)
                };
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error finding large files");
                return $"Error: An unexpected error occurred while finding large files: {ex.Message}";
            }
        }

        [McpServerTool, Description("Find usages of deprecated/obsolete APIs in the solution")]
        public static async Task<string> FindDeprecatedAPIs(
            [Description("Path to solution file (.sln)")] string solutionPath,
            [Description("Output format: summary (counts and top APIs), normal (grouped by API), detailed (with code context). Default: normal")]
            string format = "normal",
            [Description("Include .NET Framework obsolete APIs (default: true)")] bool includeFrameworkAPIs = true,
            IServiceProvider? serviceProvider = null)
        {
            var logger = serviceProvider?.GetService<ILogger<DeprecatedAPIAnalyzer>>();
            var securityValidator = serviceProvider?.GetService<SecurityValidator>();

            try
            {
                // Validate solution path
                if (securityValidator != null && !securityValidator.ValidateSolutionPath(solutionPath))
                {
                    logger?.LogWarning("Invalid solution path: {SolutionPath}", solutionPath);
                    return $"Error: Invalid solution path: {solutionPath}";
                }

                // Get analyzer service
                var analyzer = serviceProvider?.GetService<DeprecatedAPIAnalyzer>();
                if (analyzer == null)
                {
                    return "Error: Deprecated API analyzer service not available.";
                }

                // Perform analysis
                var results = await analyzer.AnalyzeDeprecatedAPIsAsync(solutionPath, includeFrameworkAPIs);

                // Format output based on format parameter
                return format.ToLowerInvariant() switch
                {
                    "summary" => FormatDeprecatedAPIsSummary(results),
                    "detailed" => FormatDeprecatedAPIsDetailed(results),
                    _ => FormatDeprecatedAPIsNormal(results)
                };
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error finding deprecated APIs");
                return $"Error: An unexpected error occurred while finding deprecated APIs: {ex.Message}";
            }
        }

        [McpServerTool, Description("Get comprehensive statistics for a C# file (LOC, complexity, dependencies, documentation coverage)")]
        public static async Task<string> GetFileStatistics(
            [Description("Path to C# source file (.cs)")] string filePath,
            [Description("Output format: summary (key metrics), normal (balanced), detailed (comprehensive). Default: normal")]
            string format = "normal",
            IServiceProvider? serviceProvider = null)
        {
            var logger = serviceProvider?.GetService<ILogger<FileStatisticsAnalyzer>>();

            try
            {
                // Get analyzer service
                var analyzer = serviceProvider?.GetService<FileStatisticsAnalyzer>();
                if (analyzer == null)
                {
                    return "Error: File statistics analyzer service not available.";
                }

                // Perform analysis
                var results = await analyzer.AnalyzeFileStatisticsAsync(filePath);

                // Format output based on format parameter
                return format.ToLowerInvariant() switch
                {
                    "summary" => FormatFileStatisticsSummary(results),
                    "detailed" => FormatFileStatisticsDetailed(results),
                    _ => FormatFileStatisticsNormal(results)
                };
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error getting file statistics");
                return $"Error: An unexpected error occurred while getting file statistics: {ex.Message}";
            }
        }

        [McpServerTool, Description("Analyze NuGet packages in solution: check for updates, version conflicts, unused packages, and security vulnerabilities")]
        public static async Task<string> AnalyzePackages(
            [Description("Path to solution file (.sln)")] string solutionPath,
            [Description("Output format: summary (key metrics), normal (balanced), detailed (comprehensive). Default: normal")]
            string format = "normal",
            [Description("Check for available package updates (default: true)")] bool checkUpdates = true,
            [Description("Check for security vulnerabilities (default: true)")] bool checkVulnerabilities = true,
            [Description("Analyze package usage to detect unused packages (default: true)")] bool analyzeUsage = true,
            IServiceProvider? serviceProvider = null)
        {
            var logger = serviceProvider?.GetService<ILogger<PackageAnalysisService>>();

            try
            {
                // Validate solution path
                var validator = serviceProvider?.GetService<SecurityValidator>();
                if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
                {
                    return "Error: Invalid solution path provided.";
                }

                // Get analyzer service
                var analyzer = serviceProvider?.GetService<PackageAnalysisService>();
                if (analyzer == null)
                {
                    return "Error: Package analysis service not available.";
                }

                // Perform analysis
                var diagnosticLogger = serviceProvider?.GetService<DiagnosticLogger>();
                Func<Task<PackageAnalysisResults>> operation = async () =>
                    await analyzer.AnalyzePackagesAsync(solutionPath, checkUpdates, checkVulnerabilities, analyzeUsage);

                var results = diagnosticLogger != null
                    ? await diagnosticLogger.LoggedExecutionAsync(
                        "AnalyzePackages",
                        operation,
                        new { solutionPath, format, checkUpdates, checkVulnerabilities, analyzeUsage })
                    : await operation();

                // Format output based on format parameter
                return format.ToLowerInvariant() switch
                {
                    "summary" => FormatPackageAnalysisSummary(results),
                    "detailed" => FormatPackageAnalysisDetailed(results),
                    _ => FormatPackageAnalysisNormal(results)
                };
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error analyzing packages");
                return $"Error: An unexpected error occurred while analyzing packages: {ex.Message}";
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

        [McpServerTool, Description("Analyze test coverage for all types in solution - identify untested code, coverage percentages, and high-risk areas")]
        public static async Task<string> GetTestCoverage(
            [Description("Path to solution file (.sln)")] string solutionPath,
            [Description("Output format: summary (key metrics), normal (balanced), detailed (comprehensive). Default: normal")]
            string format = "normal",
            [Description("Scope: public (only public types), all (all types). Default: public")]
            string scope = "public",
            [Description("Group by: project, namespace. Default: project")]
            string groupBy = "project",
            IServiceProvider? serviceProvider = null)
        {
            var logger = serviceProvider?.GetService<ILogger<TestCoverageAnalyzer>>();

            try
            {
                // Validate solution path
                var validator = serviceProvider?.GetService<SecurityValidator>();
                if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
                {
                    return "Error: Invalid solution path provided.";
                }

                // Get analyzer service
                var analyzer = serviceProvider?.GetService<TestCoverageAnalyzer>();
                if (analyzer == null)
                {
                    return "Error: Test coverage analyzer service not available.";
                }

                // Perform analysis
                var diagnosticLogger = serviceProvider?.GetService<DiagnosticLogger>();
                Func<Task<TestCoverageResults>> operation = async () =>
                    await analyzer.AnalyzeTestCoverageAsync(solutionPath, scope, groupBy);

                var results = diagnosticLogger != null
                    ? await diagnosticLogger.LoggedExecutionAsync(
                        "GetTestCoverage",
                        operation,
                        new { solutionPath, format, scope, groupBy })
                    : await operation();

                // Format output based on format parameter
                return format.ToLowerInvariant() switch
                {
                    "summary" => FormatTestCoverageSummary(results),
                    "detailed" => FormatTestCoverageDetailed(results, groupBy),
                    _ => FormatTestCoverageNormal(results, groupBy)
                };
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error analyzing test coverage");
                return $"Error: An unexpected error occurred while analyzing test coverage: {ex.Message}";
            }
        }

        [McpServerTool, Description("Analyze impact of changing a symbol - identify all dependent code, assess risk, and get recommendations before refactoring")]
        public static async Task<string> GetChangeImpact(
            [Description("Symbol name to analyze (class, method, property, etc.)")] string symbolName,
            [Description("Path to solution file (.sln)")] string solutionPath,
            [Description("Output format: summary (key metrics), normal (balanced), detailed (comprehensive). Default: normal")]
            string format = "normal",
            [Description("Maximum depth for indirect dependency analysis (default: 3)")] int maxDepth = 3,
            [Description("Include indirect references (default: true)")] bool includeIndirectReferences = true,
            IServiceProvider? serviceProvider = null)
        {
            var logger = serviceProvider?.GetService<ILogger<ChangeImpactAnalyzer>>();

            try
            {
                // Validate solution path
                var validator = serviceProvider?.GetService<SecurityValidator>();
                if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
                {
                    return "Error: Invalid solution path provided.";
                }

                // Get analyzer service
                var analyzer = serviceProvider?.GetService<ChangeImpactAnalyzer>();
                if (analyzer == null)
                {
                    return "Error: Change impact analyzer service not available.";
                }

                // Perform analysis
                var diagnosticLogger = serviceProvider?.GetService<DiagnosticLogger>();
                Func<Task<ChangeImpactResults>> operation = async () =>
                    await analyzer.AnalyzeChangeImpactAsync(symbolName, solutionPath, maxDepth, includeIndirectReferences);

                var results = diagnosticLogger != null
                    ? await diagnosticLogger.LoggedExecutionAsync(
                        "GetChangeImpact",
                        operation,
                        new { symbolName, solutionPath, format, maxDepth, includeIndirectReferences })
                    : await operation();

                // Format output based on format parameter
                return format.ToLowerInvariant() switch
                {
                    "summary" => FormatChangeImpactSummary(results),
                    "detailed" => FormatChangeImpactDetailed(results),
                    _ => FormatChangeImpactNormal(results)
                };
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error analyzing change impact");
                return $"Error: An unexpected error occurred while analyzing change impact: {ex.Message}";
            }
        }

        [McpServerTool, Description("Find common performance anti-patterns and issues in C# code")]
        public static async Task<string> FindPerformanceIssues(
            [Description("Path to solution file (.sln)")] string solutionPath,
            [Description("Output format: summary (key metrics), normal (balanced), detailed (comprehensive). Default: normal")]
            string format = "normal",
            [Description("Comma-separated issue types to check: LinqMisuse, StringConcatenation, SyncOverAsync, DisposableNotDisposed, ExceptionHandling. Default: all")]
            string? issueTypes = null,
            IServiceProvider? serviceProvider = null)
        {
            var logger = serviceProvider?.GetService<ILogger<PerformanceIssueAnalyzer>>();

            try
            {
                // Validate solution path
                var validator = serviceProvider?.GetService<SecurityValidator>();
                if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
                {
                    return "Error: Invalid solution path provided.";
                }

                // Get analyzer service
                var analyzer = serviceProvider?.GetService<PerformanceIssueAnalyzer>();
                if (analyzer == null)
                {
                    return "Error: Performance issue analyzer service not available.";
                }

                // Parse issue types filter
                string[]? issueTypesArray = null;
                if (!string.IsNullOrWhiteSpace(issueTypes))
                {
                    issueTypesArray = issueTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                }

                // Perform analysis
                var diagnosticLogger = serviceProvider?.GetService<DiagnosticLogger>();
                Func<Task<PerformanceIssueResults>> operation = async () =>
                    await analyzer.AnalyzePerformanceIssuesAsync(solutionPath, issueTypesArray);

                var results = diagnosticLogger != null
                    ? await diagnosticLogger.LoggedExecutionAsync(
                        "FindPerformanceIssues",
                        operation,
                        new { solutionPath, format, issueTypes })
                    : await operation();

                // Format output based on format parameter
                return format.ToLowerInvariant() switch
                {
                    "summary" => FormatPerformanceIssuesSummary(results),
                    "detailed" => FormatPerformanceIssuesDetailed(results),
                    _ => FormatPerformanceIssuesNormal(results)
                };
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error finding performance issues");
                return $"Error: An unexpected error occurred while analyzing performance issues: {ex.Message}";
            }
        }

        [McpServerTool, Description("Analyze C# naming convention compliance and detect violations")]
        public static async Task<string> AnalyzeNamingConventions(
            [Description("Path to solution file (.sln)")] string solutionPath,
            [Description("Output format: summary (key metrics), normal (balanced), detailed (comprehensive). Default: normal")]
            string format = "normal",
            [Description("Comma-separated violation types to check: InterfaceNaming, TypeNaming, MethodNaming, PropertyNaming, FieldNaming, ParameterNaming, TypeParameterNaming. Default: all")]
            string? violationTypes = null,
            [Description("Analysis scope: all, public, internal. Default: all")]
            string scope = "all",
            IServiceProvider? serviceProvider = null)
        {
            var logger = serviceProvider?.GetService<ILogger<NamingConventionAnalyzer>>();

            try
            {
                // Validate solution path
                var validator = serviceProvider?.GetService<SecurityValidator>();
                if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
                {
                    return "Error: Invalid solution path provided.";
                }

                // Get analyzer service
                var analyzer = serviceProvider?.GetService<NamingConventionAnalyzer>();
                if (analyzer == null)
                {
                    return "Error: Naming convention analyzer service not available.";
                }

                // Parse violation types filter
                string[]? violationTypesArray = null;
                if (!string.IsNullOrWhiteSpace(violationTypes))
                {
                    violationTypesArray = violationTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                }

                // Perform analysis
                var diagnosticLogger = serviceProvider?.GetService<DiagnosticLogger>();
                Func<Task<NamingConventionResults>> operation = async () =>
                    await analyzer.AnalyzeNamingConventionsAsync(solutionPath, violationTypesArray, scope);

                var results = diagnosticLogger != null
                    ? await diagnosticLogger.LoggedExecutionAsync(
                        "AnalyzeNamingConventions",
                        operation,
                        new { solutionPath, format, violationTypes, scope })
                    : await operation();

                // Format output based on format parameter
                return format.ToLowerInvariant() switch
                {
                    "summary" => FormatNamingConventionsSummary(results),
                    "detailed" => FormatNamingConventionsDetailed(results),
                    _ => FormatNamingConventionsNormal(results)
                };
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error analyzing naming conventions");
                return $"Error: An unexpected error occurred while analyzing naming conventions: {ex.Message}";
            }
        }

        [McpServerTool, Description("Analyze API changes between two versions of a solution - detect breaking changes, additions, removals, and get semantic versioning recommendations")]
        public static async Task<string> AnalyzeAPIChanges(
            [Description("Path to old version solution file (.sln)")] string oldSolutionPath,
            [Description("Path to new version solution file (.sln)")] string newSolutionPath,
            [Description("Output format: summary (key metrics), normal (balanced), detailed (comprehensive). Default: normal")]
            string format = "normal",
            [Description("Label for old version (e.g., 'v1.0.0', 'main'). Default: 'Old'")] string oldVersionLabel = "Old",
            [Description("Label for new version (e.g., 'v2.0.0', 'develop'). Default: 'New'")] string newVersionLabel = "New",
            [Description("Include internal API changes (default: false)")] bool includeInternal = false,
            IServiceProvider? serviceProvider = null)
        {
            var logger = serviceProvider?.GetService<ILogger<APIChangeAnalyzer>>();

            try
            {
                // Validate solution paths
                var validator = serviceProvider?.GetService<SecurityValidator>();
                if (!validator?.ValidateSolutionPath(oldSolutionPath) ?? false)
                {
                    return "Error: Invalid old solution path provided.";
                }
                if (!validator?.ValidateSolutionPath(newSolutionPath) ?? false)
                {
                    return "Error: Invalid new solution path provided.";
                }

                // Get analyzer service
                var analyzer = serviceProvider?.GetService<APIChangeAnalyzer>();
                if (analyzer == null)
                {
                    return "Error: API change analyzer service not available.";
                }

                // Perform analysis
                var diagnosticLogger = serviceProvider?.GetService<DiagnosticLogger>();
                Func<Task<APIChangeResults>> operation = async () =>
                    await analyzer.AnalyzeAPIChangesAsync(
                        oldSolutionPath,
                        newSolutionPath,
                        oldVersionLabel,
                        newVersionLabel,
                        includeInternal);

                var results = diagnosticLogger != null
                    ? await diagnosticLogger.LoggedExecutionAsync(
                        "AnalyzeAPIChanges",
                        operation,
                        new { oldSolutionPath, newSolutionPath, format, oldVersionLabel, newVersionLabel, includeInternal })
                    : await operation();

                // Format output based on format parameter
                return format.ToLowerInvariant() switch
                {
                    "summary" => FormatAPIChangesSummary(results),
                    "detailed" => FormatAPIChangesDetailed(results),
                    _ => FormatAPIChangesNormal(results)
                };
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error analyzing API changes");
                return $"Error: An unexpected error occurred while analyzing API changes: {ex.Message}";
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

        // Summary mode: Show only statistics and counts (50-70% token savings)
        private static string FormatUnusedCodeSummary(UnusedCodeResults results)
        {
            if (!results.UnusedItems.Any())
                return $"✅ No unused code found ({results.AnalyzedProjects} projects analyzed)";

            var output = new StringBuilder();
            output.AppendLine($"Unused code: {results.UnusedItems.Count} items ({results.AnalyzedProjects} projects, {results.FailedProjects} failed)\n");

            // Statistics by accessibility
            output.AppendLine("By Accessibility:");
            if (results.PrivateCount > 0)
                output.AppendLine($"  Private: {results.PrivateCount}");
            if (results.InternalCount > 0)
                output.AppendLine($"  Internal: {results.InternalCount}");
            if (results.PublicCount > 0)
                output.AppendLine($"  Public: {results.PublicCount} ⚠️");
            output.AppendLine();

            // Statistics by kind
            output.AppendLine("By Kind:");
            if (results.ClassCount > 0)
                output.AppendLine($"  Classes: {results.ClassCount}");
            if (results.MethodCount > 0)
                output.AppendLine($"  Methods: {results.MethodCount}");
            if (results.PropertyCount > 0)
                output.AppendLine($"  Properties: {results.PropertyCount}");
            if (results.FieldCount > 0)
                output.AppendLine($"  Fields: {results.FieldCount}");
            if (results.EventCount > 0)
                output.AppendLine($"  Events: {results.EventCount}");

            // Show top 5 unused items
            if (results.UnusedItems.Any())
            {
                output.AppendLine("\nTop unused items:");
                var topItems = results.UnusedItems.Take(5);
                foreach (var item in topItems)
                {
                    var icon = item.Kind switch
                    {
                        "NamedType" => "🔷",
                        "Method" => "⚙️",
                        "Property" => "🔧",
                        "Field" => "📦",
                        "Event" => "⚡",
                        _ => "•"
                    };
                    output.AppendLine($"  {icon} {item.Accessibility} {item.Kind}: {item.Name} ({item.FileName}:{item.LineNumber})");
                }

                if (results.UnusedItems.Count > 5)
                {
                    output.AppendLine($"  ... and {results.UnusedItems.Count - 5} more (use format=normal for full list)");
                }
            }

            return output.ToString();
        }

        // Normal mode: Balanced format with grouped listings
        private static string FormatUnusedCodeNormal(UnusedCodeResults results)
        {
            if (!results.UnusedItems.Any())
                return $"✅ No unused code found. All symbols have references!\n\n**Analysis Summary:**\n  • Projects analyzed: {results.AnalyzedProjects}\n  • Projects failed: {results.FailedProjects}";

            var output = new StringBuilder();
            output.AppendLine($"**Unused Code Analysis**\n");
            output.AppendLine($"Found {results.UnusedItems.Count} unused item{(results.UnusedItems.Count > 1 ? "s" : "")} ({results.AnalyzedProjects} projects analyzed, {results.FailedProjects} failed):\n");

            // Show warnings if any
            if (results.Warnings.Any())
            {
                output.AppendLine("⚠️ **Warnings:**");
                foreach (var warning in results.Warnings.Take(5))
                {
                    output.AppendLine($"   - {warning.Context}: {warning.Message}");
                }
                if (results.Warnings.Count > 5)
                {
                    output.AppendLine($"   ... and {results.Warnings.Count - 5} more warnings");
                }
                output.AppendLine();
            }

            // Group by accessibility
            var groupedByAccessibility = results.UnusedItems.GroupBy(i => i.Accessibility).OrderBy(g => g.Key);
            foreach (var accessGroup in groupedByAccessibility)
            {
                var warningIcon = accessGroup.Key == "Public" ? " ⚠️" : "";
                output.AppendLine($"## {accessGroup.Key} ({accessGroup.Count()}){warningIcon}");
                output.AppendLine();

                // Group by kind within accessibility
                var groupedByKind = accessGroup.GroupBy(i => i.Kind).OrderBy(g => g.Key);
                foreach (var kindGroup in groupedByKind)
                {
                    output.AppendLine($"### {kindGroup.Key} ({kindGroup.Count()})");
                    output.AppendLine();

                    // Show first 10 items per kind
                    var displayedItems = kindGroup.Take(10);
                    foreach (var item in displayedItems)
                    {
                        var icon = item.Kind switch
                        {
                            "NamedType" => "🔷",
                            "Method" => "⚙️",
                            "Property" => "🔧",
                            "Field" => "📦",
                            "Event" => "⚡",
                            _ => "•"
                        };

                        output.AppendLine($"{icon} **{item.Name}**");
                        if (!string.IsNullOrWhiteSpace(item.DeclaringType))
                        {
                            output.AppendLine($"   In: {item.DeclaringType}");
                        }
                        output.AppendLine($"   📄 {item.FileName}:{item.LineNumber}");
                        if (!string.IsNullOrWhiteSpace(item.ProjectName))
                        {
                            output.AppendLine($"   Project: {item.ProjectName}");
                        }
                        output.AppendLine();
                    }

                    if (kindGroup.Count() > 10)
                    {
                        output.AppendLine($"... and {kindGroup.Count() - 10} more unused {kindGroup.Key.ToLower()}{(kindGroup.Count() - 10 > 1 ? "s" : "")}");
                        output.AppendLine();
                    }
                }
            }

            // Summary
            output.AppendLine("---");
            output.AppendLine("**Summary by Accessibility:**");
            output.AppendLine($"  • Private: {results.PrivateCount}");
            output.AppendLine($"  • Internal: {results.InternalCount}");
            if (results.PublicCount > 0)
            {
                output.AppendLine($"  • Public: {results.PublicCount} ⚠️ (Consider removing or marking as obsolete)");
            }

            output.AppendLine("\n**Summary by Kind:**");
            if (results.ClassCount > 0)
                output.AppendLine($"  • Classes: {results.ClassCount}");
            if (results.MethodCount > 0)
                output.AppendLine($"  • Methods: {results.MethodCount}");
            if (results.PropertyCount > 0)
                output.AppendLine($"  • Properties: {results.PropertyCount}");
            if (results.FieldCount > 0)
                output.AppendLine($"  • Fields: {results.FieldCount}");
            if (results.EventCount > 0)
                output.AppendLine($"  • Events: {results.EventCount}");

            return output.ToString();
        }

        // Detailed mode: Comprehensive format with all metadata and signatures
        private static string FormatUnusedCodeDetailed(UnusedCodeResults results)
        {
            if (!results.UnusedItems.Any())
                return $"✅ No unused code found. All symbols have references!\n\n**Analysis Summary:**\n  • Projects analyzed: {results.AnalyzedProjects}\n  • Symbols analyzed: {results.AnalyzedSymbols}\n  • Projects failed: {results.FailedProjects}";

            var output = new StringBuilder();
            output.AppendLine($"**Unused Code Analysis (Detailed)**\n");
            output.AppendLine($"📊 **Analysis Summary:**");
            output.AppendLine($"  • Total unused items: {results.UnusedItems.Count}");
            output.AppendLine($"  • Projects analyzed: {results.AnalyzedProjects}");
            output.AppendLine($"  • Symbols analyzed: {results.AnalyzedSymbols}");
            output.AppendLine($"  • Projects failed: {results.FailedProjects}");
            output.AppendLine();

            // Show all warnings if any
            if (results.Warnings.Any())
            {
                output.AppendLine("⚠️ **Analysis Warnings:**");
                foreach (var warning in results.Warnings)
                {
                    output.AppendLine($"   - {warning.Context}: {warning.Message}");
                    if (!string.IsNullOrWhiteSpace(warning.Details))
                    {
                        output.AppendLine($"     Details: {warning.Details}");
                    }
                }
                output.AppendLine();
            }

            // Group by accessibility
            var groupedByAccessibility = results.UnusedItems.GroupBy(i => i.Accessibility).OrderBy(g => g.Key);
            foreach (var accessGroup in groupedByAccessibility)
            {
                var warningIcon = accessGroup.Key == "Public" ? " ⚠️" : "";
                output.AppendLine($"## {accessGroup.Key} ({accessGroup.Count()}){warningIcon}");
                output.AppendLine();

                // Group by kind within accessibility
                var groupedByKind = accessGroup.GroupBy(i => i.Kind).OrderBy(g => g.Key);
                foreach (var kindGroup in groupedByKind)
                {
                    output.AppendLine($"### {kindGroup.Key} ({kindGroup.Count()})");
                    output.AppendLine();

                    // Group by project within kind
                    var groupedByProject = kindGroup.GroupBy(i => i.ProjectName).OrderBy(g => g.Key);
                    foreach (var projectGroup in groupedByProject)
                    {
                        if (groupedByProject.Count() > 1)
                        {
                            output.AppendLine($"#### 📦 {projectGroup.Key} ({projectGroup.Count()})");
                            output.AppendLine();
                        }

                        // Show all items in detailed mode
                        foreach (var item in projectGroup)
                        {
                            var icon = item.Kind switch
                            {
                                "NamedType" => "🔷",
                                "Method" => "⚙️",
                                "Property" => "🔧",
                                "Field" => "📦",
                                "Event" => "⚡",
                                _ => "•"
                            };

                            output.AppendLine($"{icon} **{item.Name}**");
                            output.AppendLine($"   Full Name: `{item.FullName}`");

                            if (!string.IsNullOrWhiteSpace(item.Signature))
                            {
                                output.AppendLine($"   Signature: `{item.Signature}`");
                            }

                            if (!string.IsNullOrWhiteSpace(item.DeclaringType))
                            {
                                output.AppendLine($"   Declaring Type: {item.DeclaringType}");
                            }

                            if (!string.IsNullOrWhiteSpace(item.Namespace))
                            {
                                output.AppendLine($"   Namespace: {item.Namespace}");
                            }

                            output.AppendLine($"   📄 Location: {item.FileName}:{item.LineNumber}");
                            output.AppendLine($"   📁 Path: {item.FilePath}");
                            output.AppendLine($"   Project: {item.ProjectName}");

                            if (item.IsTestMember)
                            {
                                output.AppendLine($"   🧪 Test member");
                            }

                            if (!string.IsNullOrWhiteSpace(item.Reason))
                            {
                                output.AppendLine($"   Reason: {item.Reason}");
                            }

                            output.AppendLine();
                        }
                    }
                }
            }

            // Detailed summary with breakdown
            output.AppendLine("---");
            output.AppendLine("**Detailed Summary:**");
            output.AppendLine("\n**By Accessibility:**");
            output.AppendLine($"  • Private: {results.PrivateCount}");
            output.AppendLine($"  • Internal: {results.InternalCount}");
            if (results.PublicCount > 0)
            {
                output.AppendLine($"  • Public: {results.PublicCount} ⚠️ (Breaking change - consider marking as obsolete first)");
            }

            output.AppendLine("\n**By Kind:**");
            if (results.ClassCount > 0)
                output.AppendLine($"  • Classes: {results.ClassCount}");
            if (results.MethodCount > 0)
                output.AppendLine($"  • Methods: {results.MethodCount}");
            if (results.PropertyCount > 0)
                output.AppendLine($"  • Properties: {results.PropertyCount}");
            if (results.FieldCount > 0)
                output.AppendLine($"  • Fields: {results.FieldCount}");
            if (results.EventCount > 0)
                output.AppendLine($"  • Events: {results.EventCount}");

            // Recommendations
            output.AppendLine("\n**Recommendations:**");
            if (results.PublicCount > 0)
            {
                output.AppendLine($"  • {results.PublicCount} public members are unused - these are breaking changes");
                output.AppendLine($"    Consider marking with [Obsolete] attribute before removal");
            }
            if (results.InternalCount > 0)
            {
                output.AppendLine($"  • {results.InternalCount} internal members can be safely removed within the assembly");
            }
            if (results.PrivateCount > 0)
            {
                output.AppendLine($"  • {results.PrivateCount} private members can be safely removed");
            }

            return output.ToString();
        }

        // Summary mode: Show only statistics and counts (50-70% token savings)
        private static string FormatUnusedDependenciesSummary(UnusedDependencyResults results)
        {
            if (!results.UnusedDependencies.Any())
                return $"✅ No unused dependencies found ({results.AnalyzedProjects} projects analyzed)";

            var output = new StringBuilder();
            output.AppendLine($"Unused dependencies: {results.TotalUnusedDependencies} items ({results.AnalyzedProjects} projects, {results.FailedProjects} failed)\n");

            // Statistics by type
            output.AppendLine("By Type:");
            if (results.UnusedNuGetPackages > 0)
                output.AppendLine($"  NuGet Packages: {results.UnusedNuGetPackages}");
            if (results.UnusedProjectReferences > 0)
                output.AppendLine($"  Project References: {results.UnusedProjectReferences}");
            output.AppendLine();

            // Show top 5 unused dependencies
            if (results.UnusedDependencies.Any())
            {
                output.AppendLine("Top unused dependencies:");
                var topItems = results.UnusedDependencies.Take(5);
                foreach (var item in topItems)
                {
                    var icon = item.Type == "NuGetPackage" ? "📦" : "🔗";
                    var version = !string.IsNullOrWhiteSpace(item.Version) ? $" ({item.Version})" : "";
                    output.AppendLine($"  {icon} {item.Name}{version} in {item.ProjectName}");
                }

                if (results.UnusedDependencies.Count > 5)
                {
                    output.AppendLine($"  ... and {results.UnusedDependencies.Count - 5} more (use format=normal for full list)");
                }
            }

            return output.ToString();
        }

        // Normal mode: Balanced format with grouped listings
        private static string FormatUnusedDependenciesNormal(UnusedDependencyResults results)
        {
            if (!results.UnusedDependencies.Any())
                return $"✅ No unused dependencies found!\n\n**Analysis Summary:**\n  • Projects analyzed: {results.AnalyzedProjects}\n  • Projects failed: {results.FailedProjects}";

            var output = new StringBuilder();
            output.AppendLine($"**Unused Dependencies Analysis**\n");
            output.AppendLine($"Found {results.TotalUnusedDependencies} unused dependenc{(results.TotalUnusedDependencies > 1 ? "ies" : "y")} ({results.AnalyzedProjects} projects analyzed, {results.FailedProjects} failed):\n");

            // Show warnings if any
            if (results.Warnings.Any())
            {
                output.AppendLine("⚠️ **Warnings:**");
                foreach (var warning in results.Warnings.Take(5))
                {
                    output.AppendLine($"   - {warning.Context}: {warning.Message}");
                }
                if (results.Warnings.Count > 5)
                {
                    output.AppendLine($"   ... and {results.Warnings.Count - 5} more warnings");
                }
                output.AppendLine();
            }

            // Group by type (NuGet vs Project Reference)
            var groupedByType = results.UnusedDependencies.GroupBy(d => d.Type).OrderBy(g => g.Key);

            foreach (var typeGroup in groupedByType)
            {
                output.AppendLine($"## {typeGroup.Key}s ({typeGroup.Count()})");
                output.AppendLine();

                // Group by project
                var groupedByProject = typeGroup.GroupBy(d => d.ProjectName).OrderBy(g => g.Key);
                foreach (var projectGroup in groupedByProject)
                {
                    output.AppendLine($"### 📦 {projectGroup.Key} ({projectGroup.Count()})");
                    output.AppendLine();

                    // Show first 10 dependencies per project
                    var displayedItems = projectGroup.Take(10);
                    foreach (var item in displayedItems)
                    {
                        var icon = item.Type == "NuGetPackage" ? "📦" : "🔗";
                        var version = !string.IsNullOrWhiteSpace(item.Version) ? $" ({item.Version})" : "";

                        output.AppendLine($"{icon} **{item.Name}{version}**");
                        output.AppendLine($"   Reason: {item.Reason}");

                        if (item.ExpectedNamespaces.Any())
                        {
                            output.AppendLine($"   Expected namespaces: {string.Join(", ", item.ExpectedNamespaces.Take(3))}");
                        }
                        output.AppendLine();
                    }

                    if (projectGroup.Count() > 10)
                    {
                        output.AppendLine($"... and {projectGroup.Count() - 10} more unused {typeGroup.Key.ToLower()}{(projectGroup.Count() - 10 > 1 ? "s" : "")}");
                        output.AppendLine();
                    }
                }
            }

            // Summary
            output.AppendLine("---");
            output.AppendLine("**Summary:**");
            output.AppendLine($"  • NuGet Packages: {results.UnusedNuGetPackages}");
            output.AppendLine($"  • Project References: {results.UnusedProjectReferences}");
            output.AppendLine($"  • Total: {results.TotalUnusedDependencies}");

            return output.ToString();
        }

        // Detailed mode: Comprehensive format with all metadata
        private static string FormatUnusedDependenciesDetailed(UnusedDependencyResults results)
        {
            if (!results.UnusedDependencies.Any())
                return $"✅ No unused dependencies found!\n\n**Analysis Summary:**\n  • Projects analyzed: {results.AnalyzedProjects}\n  • Projects failed: {results.FailedProjects}";

            var output = new StringBuilder();
            output.AppendLine($"**Unused Dependencies Analysis (Detailed)**\n");
            output.AppendLine($"📊 **Analysis Summary:**");
            output.AppendLine($"  • Total unused dependencies: {results.TotalUnusedDependencies}");
            output.AppendLine($"  • Projects analyzed: {results.AnalyzedProjects}");
            output.AppendLine($"  • Projects failed: {results.FailedProjects}");
            output.AppendLine();

            // Show all warnings if any
            if (results.Warnings.Any())
            {
                output.AppendLine("⚠️ **Analysis Warnings:**");
                foreach (var warning in results.Warnings)
                {
                    output.AppendLine($"   - {warning.Context}: {warning.Message}");
                    if (!string.IsNullOrWhiteSpace(warning.Details))
                    {
                        output.AppendLine($"     Details: {warning.Details}");
                    }
                }
                output.AppendLine();
            }

            // Group by type (NuGet vs Project Reference)
            var groupedByType = results.UnusedDependencies.GroupBy(d => d.Type).OrderBy(g => g.Key);

            foreach (var typeGroup in groupedByType)
            {
                output.AppendLine($"## {typeGroup.Key}s ({typeGroup.Count()})");
                output.AppendLine();

                // Group by project
                var groupedByProject = typeGroup.GroupBy(d => d.ProjectName).OrderBy(g => g.Key);
                foreach (var projectGroup in groupedByProject)
                {
                    output.AppendLine($"### 📦 {projectGroup.Key} ({projectGroup.Count()})");
                    output.AppendLine();

                    // Show all dependencies in detailed mode
                    foreach (var item in projectGroup)
                    {
                        var icon = item.Type == "NuGetPackage" ? "📦" : "🔗";
                        var version = !string.IsNullOrWhiteSpace(item.Version) ? $" ({item.Version})" : "";

                        output.AppendLine($"{icon} **{item.Name}{version}**");
                        output.AppendLine($"   Type: {item.Type}");
                        output.AppendLine($"   Project: {item.ProjectName}");
                        output.AppendLine($"   📁 Project Path: {item.ProjectPath}");
                        output.AppendLine($"   Reason: {item.Reason}");

                        if (item.ExpectedNamespaces.Any())
                        {
                            output.AppendLine($"   Expected namespaces:");
                            foreach (var ns in item.ExpectedNamespaces)
                            {
                                output.AppendLine($"     - {ns}");
                            }
                        }

                        output.AppendLine();
                    }
                }
            }

            // Detailed summary
            output.AppendLine("---");
            output.AppendLine("**Detailed Summary:**");
            output.AppendLine();
            output.AppendLine("**By Type:**");
            output.AppendLine($"  • NuGet Packages: {results.UnusedNuGetPackages}");
            output.AppendLine($"  • Project References: {results.UnusedProjectReferences}");
            output.AppendLine($"  • Total: {results.TotalUnusedDependencies}");

            output.AppendLine();
            output.AppendLine("**Recommendations:**");
            if (results.UnusedNuGetPackages > 0)
            {
                output.AppendLine($"  • {results.UnusedNuGetPackages} NuGet package{(results.UnusedNuGetPackages > 1 ? "s" : "")} can be removed to reduce dependencies");
                output.AppendLine($"    Use: dotnet remove package <PackageName>");
            }
            if (results.UnusedProjectReferences > 0)
            {
                output.AppendLine($"  • {results.UnusedProjectReferences} project reference{(results.UnusedProjectReferences > 1 ? "s" : "")} can be removed to simplify project structure");
                output.AppendLine($"    Edit .csproj file and remove <ProjectReference> elements");
            }

            return output.ToString();
        }

        // Summary mode: Show only statistics and counts (40-60% token savings)
        private static string FormatSecurityIssuesSummary(SecurityIssueResults results)
        {
            if (!results.Issues.Any())
                return $"✅ No security issues found ({results.AnalyzedFiles} files analyzed)";

            var output = new StringBuilder();
            output.AppendLine($"Security issues: {results.TotalIssues} found ({results.AnalyzedFiles} files, {results.AnalyzedProjects} projects)\n");

            // Statistics by severity
            output.AppendLine("By Severity:");
            if (results.CriticalCount > 0)
                output.AppendLine($"  🔴 Critical: {results.CriticalCount}");
            if (results.HighCount > 0)
                output.AppendLine($"  🟠 High: {results.HighCount}");
            if (results.MediumCount > 0)
                output.AppendLine($"  🟡 Medium: {results.MediumCount}");
            if (results.LowCount > 0)
                output.AppendLine($"  🟢 Low: {results.LowCount}");
            output.AppendLine();

            // Statistics by category
            output.AppendLine("By Category:");
            if (results.SqlInjectionCount > 0)
                output.AppendLine($"  SQL Injection: {results.SqlInjectionCount}");
            if (results.HardcodedSecretsCount > 0)
                output.AppendLine($"  Hardcoded Secrets: {results.HardcodedSecretsCount}");
            if (results.WeakCryptoCount > 0)
                output.AppendLine($"  Weak Cryptography: {results.WeakCryptoCount}");
            if (results.PathTraversalCount > 0)
                output.AppendLine($"  Path Traversal: {results.PathTraversalCount}");
            if (results.DeserializationCount > 0)
                output.AppendLine($"  Insecure Deserialization: {results.DeserializationCount}");
            output.AppendLine();

            // Show top 5 critical issues
            var topIssues = results.Issues
                .Where(i => i.Severity == "Critical")
                .Take(5);

            if (topIssues.Any())
            {
                output.AppendLine("Top critical issues:");
                foreach (var issue in topIssues)
                {
                    output.AppendLine($"  🔴 {issue.Title} @ {issue.FileName}:{issue.LineNumber}");
                }

                var criticalCount = results.Issues.Count(i => i.Severity == "Critical");
                if (criticalCount > 5)
                {
                    output.AppendLine($"  ... and {criticalCount - 5} more critical issues (use format=normal for full list)");
                }
            }

            return output.ToString();
        }

        // Normal mode: Balanced format with grouped listings
        private static string FormatSecurityIssuesNormal(SecurityIssueResults results)
        {
            if (!results.Issues.Any())
                return $"✅ No security issues found!\n\n**Analysis Summary:**\n  • Files analyzed: {results.AnalyzedFiles}\n  • Projects analyzed: {results.AnalyzedProjects}";

            var output = new StringBuilder();
            output.AppendLine($"**Security Issues Analysis**\n");
            output.AppendLine($"Found {results.TotalIssues} issue{(results.TotalIssues > 1 ? "s" : "")} ({results.AnalyzedFiles} files, {results.AnalyzedProjects} projects):\n");

            // Show warnings if any
            if (results.Warnings.Any())
            {
                output.AppendLine("⚠️ **Warnings:**");
                foreach (var warning in results.Warnings.Take(5))
                {
                    output.AppendLine($"   - {warning.Context}: {warning.Message}");
                }
                output.AppendLine();
            }

            // Group by severity
            var groupedBySeverity = results.Issues
                .GroupBy(i => i.Severity)
                .OrderBy(g => g.Key == "Critical" ? 0 : g.Key == "High" ? 1 : g.Key == "Medium" ? 2 : 3);

            foreach (var severityGroup in groupedBySeverity)
            {
                var icon = severityGroup.Key switch
                {
                    "Critical" => "🔴",
                    "High" => "🟠",
                    "Medium" => "🟡",
                    "Low" => "🟢",
                    _ => "⚪"
                };

                output.AppendLine($"## {icon} {severityGroup.Key} ({severityGroup.Count()})");
                output.AppendLine();

                // Group by category
                var groupedByCategory = severityGroup.GroupBy(i => i.Category);
                foreach (var categoryGroup in groupedByCategory)
                {
                    var categoryName = categoryGroup.Key switch
                    {
                        "sql-injection" => "SQL Injection",
                        "secrets" => "Hardcoded Secrets",
                        "crypto" => "Weak Cryptography",
                        "path-traversal" => "Path Traversal",
                        "deserialization" => "Insecure Deserialization",
                        _ => categoryGroup.Key
                    };

                    output.AppendLine($"### {categoryName} ({categoryGroup.Count()})");
                    output.AppendLine();

                    // Show first 10 issues per category
                    var displayedIssues = categoryGroup.Take(10);
                    foreach (var issue in displayedIssues)
                    {
                        output.AppendLine($"**{issue.Title}**");
                        output.AppendLine($"   📄 {issue.FileName}:{issue.LineNumber}");
                        if (!string.IsNullOrWhiteSpace(issue.MethodName) && issue.MethodName != "(global)")
                        {
                            output.AppendLine($"   Method: {issue.MethodName}");
                        }
                        output.AppendLine($"   ⚠️ {issue.Description}");
                        output.AppendLine($"   💡 {issue.Recommendation}");
                        output.AppendLine();
                    }

                    if (categoryGroup.Count() > 10)
                    {
                        output.AppendLine($"... and {categoryGroup.Count() - 10} more {categoryName.ToLower()} issue{(categoryGroup.Count() - 10 > 1 ? "s" : "")}");
                        output.AppendLine();
                    }
                }
            }

            // Summary
            output.AppendLine("---");
            output.AppendLine("**Summary by Severity:**");
            if (results.CriticalCount > 0)
                output.AppendLine($"  🔴 Critical: {results.CriticalCount} (Fix immediately!)");
            if (results.HighCount > 0)
                output.AppendLine($"  🟠 High: {results.HighCount}");
            if (results.MediumCount > 0)
                output.AppendLine($"  🟡 Medium: {results.MediumCount}");
            if (results.LowCount > 0)
                output.AppendLine($"  🟢 Low: {results.LowCount}");

            output.AppendLine("\n**Summary by Category:**");
            if (results.SqlInjectionCount > 0)
                output.AppendLine($"  • SQL Injection: {results.SqlInjectionCount}");
            if (results.HardcodedSecretsCount > 0)
                output.AppendLine($"  • Hardcoded Secrets: {results.HardcodedSecretsCount}");
            if (results.WeakCryptoCount > 0)
                output.AppendLine($"  • Weak Cryptography: {results.WeakCryptoCount}");
            if (results.PathTraversalCount > 0)
                output.AppendLine($"  • Path Traversal: {results.PathTraversalCount}");
            if (results.DeserializationCount > 0)
                output.AppendLine($"  • Insecure Deserialization: {results.DeserializationCount}");

            return output.ToString();
        }

        // Detailed mode: Comprehensive format with all metadata and code snippets
        private static string FormatSecurityIssuesDetailed(SecurityIssueResults results)
        {
            if (!results.Issues.Any())
                return $"✅ No security issues found!\n\n**Analysis Summary:**\n  • Files analyzed: {results.AnalyzedFiles}\n  • Projects analyzed: {results.AnalyzedProjects}";

            var output = new StringBuilder();
            output.AppendLine($"**Security Issues Analysis (Detailed)**\n");
            output.AppendLine($"📊 **Analysis Summary:**");
            output.AppendLine($"  • Total issues: {results.TotalIssues}");
            output.AppendLine($"  • Files analyzed: {results.AnalyzedFiles}");
            output.AppendLine($"  • Projects analyzed: {results.AnalyzedProjects}");
            output.AppendLine($"  • Failed projects: {results.FailedProjects}");
            output.AppendLine();

            // Show all warnings
            if (results.Warnings.Any())
            {
                output.AppendLine("⚠️ **Analysis Warnings:**");
                foreach (var warning in results.Warnings)
                {
                    output.AppendLine($"   - {warning.Context}: {warning.Message}");
                }
                output.AppendLine();
            }

            // Group by severity
            var groupedBySeverity = results.Issues
                .GroupBy(i => i.Severity)
                .OrderBy(g => g.Key == "Critical" ? 0 : g.Key == "High" ? 1 : g.Key == "Medium" ? 2 : 3);

            foreach (var severityGroup in groupedBySeverity)
            {
                var icon = severityGroup.Key switch
                {
                    "Critical" => "🔴",
                    "High" => "🟠",
                    "Medium" => "🟡",
                    "Low" => "🟢",
                    _ => "⚪"
                };

                output.AppendLine($"## {icon} {severityGroup.Key} Severity ({severityGroup.Count()})");
                output.AppendLine();

                // Group by category
                var groupedByCategory = severityGroup.GroupBy(i => i.Category);
                foreach (var categoryGroup in groupedByCategory)
                {
                    var categoryName = categoryGroup.Key switch
                    {
                        "sql-injection" => "SQL Injection",
                        "secrets" => "Hardcoded Secrets",
                        "crypto" => "Weak Cryptography",
                        "path-traversal" => "Path Traversal",
                        "deserialization" => "Insecure Deserialization",
                        _ => categoryGroup.Key
                    };

                    output.AppendLine($"### {categoryName} ({categoryGroup.Count()})");
                    output.AppendLine();

                    // Group by project
                    var groupedByProject = categoryGroup.GroupBy(i => i.ProjectName);
                    foreach (var projectGroup in groupedByProject)
                    {
                        if (groupedByProject.Count() > 1)
                        {
                            output.AppendLine($"#### 📦 {projectGroup.Key} ({projectGroup.Count()})");
                            output.AppendLine();
                        }

                        // Show all issues in detailed mode
                        foreach (var issue in projectGroup)
                        {
                            output.AppendLine($"**{issue.Title}**");
                            output.AppendLine($"   Severity: {icon} {issue.Severity}");
                            output.AppendLine($"   Category: {categoryName}");
                            output.AppendLine($"   📁 File: {issue.FilePath}");
                            output.AppendLine($"   📄 Location: {issue.FileName}:{issue.LineNumber}");

                            if (!string.IsNullOrWhiteSpace(issue.MethodName) && issue.MethodName != "(global)")
                            {
                                output.AppendLine($"   Method: {issue.MethodName}");
                            }

                            output.AppendLine($"   Project: {issue.ProjectName}");
                            output.AppendLine();
                            output.AppendLine($"   ⚠️ **Description**: {issue.Description}");
                            output.AppendLine($"   💡 **Recommendation**: {issue.Recommendation}");

                            if (!string.IsNullOrWhiteSpace(issue.CodeSnippet))
                            {
                                output.AppendLine();
                                output.AppendLine($"   **Code Snippet**:");
                                output.AppendLine($"   ```csharp");
                                output.AppendLine($"   {issue.CodeSnippet}");
                                output.AppendLine($"   ```");
                            }

                            output.AppendLine();
                        }
                    }
                }
            }

            // Detailed summary
            output.AppendLine("---");
            output.AppendLine("**Detailed Summary:**");
            output.AppendLine();
            output.AppendLine("**By Severity:**");
            if (results.CriticalCount > 0)
                output.AppendLine($"  🔴 Critical: {results.CriticalCount} (Requires immediate attention!)");
            if (results.HighCount > 0)
                output.AppendLine($"  🟠 High: {results.HighCount} (Should be fixed soon)");
            if (results.MediumCount > 0)
                output.AppendLine($"  🟡 Medium: {results.MediumCount} (Consider fixing)");
            if (results.LowCount > 0)
                output.AppendLine($"  🟢 Low: {results.LowCount} (Low priority)");

            output.AppendLine();
            output.AppendLine("**By Category:**");
            if (results.SqlInjectionCount > 0)
                output.AppendLine($"  • SQL Injection: {results.SqlInjectionCount}");
            if (results.HardcodedSecretsCount > 0)
                output.AppendLine($"  • Hardcoded Secrets: {results.HardcodedSecretsCount}");
            if (results.WeakCryptoCount > 0)
                output.AppendLine($"  • Weak Cryptography: {results.WeakCryptoCount}");
            if (results.PathTraversalCount > 0)
                output.AppendLine($"  • Path Traversal: {results.PathTraversalCount}");
            if (results.DeserializationCount > 0)
                output.AppendLine($"  • Insecure Deserialization: {results.DeserializationCount}");

            output.AppendLine();
            output.AppendLine("**Recommendations:**");
            if (results.CriticalCount > 0)
            {
                output.AppendLine($"  • {results.CriticalCount} critical security issue{(results.CriticalCount > 1 ? "s" : "")} found - fix immediately!");
            }
            if (results.SqlInjectionCount > 0)
            {
                output.AppendLine($"  • Use parameterized queries or Entity Framework to prevent SQL injection");
            }
            if (results.HardcodedSecretsCount > 0)
            {
                output.AppendLine($"  • Move secrets to configuration files, environment variables, or Azure Key Vault");
            }
            if (results.WeakCryptoCount > 0)
            {
                output.AppendLine($"  • Replace weak cryptography with modern algorithms (SHA256, AES)");
            }

            return output.ToString();
        }

        // Summary mode: Show only statistics and counts (30-60% token savings)
        private static string FormatDuplicateCodeSummary(DuplicateCodeResults results)
        {
            if (!results.DuplicateBlocks.Any())
                return $"✅ No duplicate code found ({results.AnalyzedMethods} methods analyzed)";

            var output = new StringBuilder();
            output.AppendLine($"Duplicate code: {results.TotalDuplicateBlocks} blocks, {results.TotalDuplicateInstances} instances ({results.AnalyzedMethods} methods analyzed)\n");

            // Statistics
            output.AppendLine("By Similarity:");
            if (results.HighSimilarityCount > 0)
                output.AppendLine($"  High (95%+): {results.HighSimilarityCount} blocks");
            if (results.MediumSimilarityCount > 0)
                output.AppendLine($"  Medium (85-94%): {results.MediumSimilarityCount} blocks");
            if (results.LowSimilarityCount > 0)
                output.AppendLine($"  Low (<85%): {results.LowSimilarityCount} blocks");
            output.AppendLine();

            // Show top 5 duplicate blocks
            var topBlocks = results.DuplicateBlocks.Take(5);
            if (topBlocks.Any())
            {
                output.AppendLine("Top duplicate blocks:");
                foreach (var block in topBlocks)
                {
                    output.AppendLine($"  • {block.LineCount} lines, {block.Instances.Count} instances ({block.SimilarityPercentage}% similar)");
                    foreach (var instance in block.Instances.Take(2))
                    {
                        output.AppendLine($"    - {instance.MethodName} @ {instance.FileName}:{instance.StartLine}");
                    }
                    if (block.Instances.Count > 2)
                    {
                        output.AppendLine($"    ... and {block.Instances.Count - 2} more");
                    }
                }

                if (results.TotalDuplicateBlocks > 5)
                {
                    output.AppendLine($"\n  ... and {results.TotalDuplicateBlocks - 5} more duplicate blocks (use format=normal for full list)");
                }
            }

            return output.ToString();
        }

        // Normal mode: Balanced format with grouped listings
        private static string FormatDuplicateCodeNormal(DuplicateCodeResults results)
        {
            if (!results.DuplicateBlocks.Any())
                return $"✅ No duplicate code found!\n\n**Analysis Summary:**\n  • Methods analyzed: {results.AnalyzedMethods}\n  • Files analyzed: {results.AnalyzedFiles}\n  • Projects analyzed: {results.AnalyzedProjects}";

            var output = new StringBuilder();
            output.AppendLine($"**Duplicate Code Analysis**\n");
            output.AppendLine($"Found {results.TotalDuplicateBlocks} duplicate block{(results.TotalDuplicateBlocks > 1 ? "s" : "")} ({results.TotalDuplicateInstances} instances, {results.AnalyzedMethods} methods analyzed):\n");

            // Show warnings if any
            if (results.Warnings.Any())
            {
                output.AppendLine("⚠️ **Warnings:**");
                foreach (var warning in results.Warnings.Take(5))
                {
                    output.AppendLine($"   - {warning.Context}: {warning.Message}");
                }
                output.AppendLine();
            }

            // Show top 15 duplicate blocks
            var displayedBlocks = results.DuplicateBlocks.Take(15);
            int blockNum = 1;

            foreach (var block in displayedBlocks)
            {
                output.AppendLine($"## Block #{blockNum}: {block.LineCount} lines, {block.Instances.Count} instances ({block.SimilarityPercentage}% similar)");
                output.AppendLine();

                // Show code snippet from first instance
                if (block.Instances.Any() && !string.IsNullOrWhiteSpace(block.Instances[0].CodeSnippet))
                {
                    output.AppendLine("**Code Preview:**");
                    output.AppendLine("```csharp");
                    output.AppendLine(block.Instances[0].CodeSnippet);
                    output.AppendLine("```");
                    output.AppendLine();
                }

                output.AppendLine("**Instances:**");
                foreach (var instance in block.Instances)
                {
                    output.AppendLine($"  • **{instance.MethodName}**");
                    output.AppendLine($"    📄 {instance.FileName}:{instance.StartLine}-{instance.EndLine}");
                    output.AppendLine($"    Project: {instance.ProjectName}");
                }

                output.AppendLine();
                blockNum++;
            }

            if (results.TotalDuplicateBlocks > 15)
            {
                output.AppendLine($"... and {results.TotalDuplicateBlocks - 15} more duplicate blocks (use format=detailed for all blocks)");
                output.AppendLine();
            }

            // Summary
            output.AppendLine("---");
            output.AppendLine("**Summary:**");
            output.AppendLine($"  • Total duplicate blocks: {results.TotalDuplicateBlocks}");
            output.AppendLine($"  • Total instances: {results.TotalDuplicateInstances}");
            output.AppendLine($"  • High similarity (95%+): {results.HighSimilarityCount}");
            output.AppendLine($"  • Medium similarity (85-94%): {results.MediumSimilarityCount}");
            output.AppendLine($"  • Low similarity (<85%): {results.LowSimilarityCount}");

            return output.ToString();
        }

        // Detailed mode: Comprehensive format with all blocks and metadata
        private static string FormatDuplicateCodeDetailed(DuplicateCodeResults results)
        {
            if (!results.DuplicateBlocks.Any())
                return $"✅ No duplicate code found!\n\n**Analysis Summary:**\n  • Methods analyzed: {results.AnalyzedMethods}\n  • Files analyzed: {results.AnalyzedFiles}\n  • Projects analyzed: {results.AnalyzedProjects}";

            var output = new StringBuilder();
            output.AppendLine($"**Duplicate Code Analysis (Detailed)**\n");
            output.AppendLine($"📊 **Analysis Summary:**");
            output.AppendLine($"  • Total duplicate blocks: {results.TotalDuplicateBlocks}");
            output.AppendLine($"  • Total instances: {results.TotalDuplicateInstances}");
            output.AppendLine($"  • Methods analyzed: {results.AnalyzedMethods}");
            output.AppendLine($"  • Files analyzed: {results.AnalyzedFiles}");
            output.AppendLine($"  • Projects analyzed: {results.AnalyzedProjects}");
            output.AppendLine($"  • Failed projects: {results.FailedProjects}");
            output.AppendLine();

            // Show all warnings
            if (results.Warnings.Any())
            {
                output.AppendLine("⚠️ **Analysis Warnings:**");
                foreach (var warning in results.Warnings)
                {
                    output.AppendLine($"   - {warning.Context}: {warning.Message}");
                }
                output.AppendLine();
            }

            // Show all duplicate blocks
            int blockNum = 1;
            foreach (var block in results.DuplicateBlocks)
            {
                var similarityIcon = block.SimilarityPercentage >= 95 ? "🔴" : block.SimilarityPercentage >= 85 ? "🟠" : "🟡";

                output.AppendLine($"## {similarityIcon} Block #{blockNum}: {block.LineCount} lines, {block.Instances.Count} instances");
                output.AppendLine();
                output.AppendLine($"**Similarity**: {block.SimilarityPercentage}%");
                output.AppendLine($"**Line Count**: {block.LineCount}");
                output.AppendLine($"**Hash**: {block.Hash.Substring(0, Math.Min(16, block.Hash.Length))}...");
                output.AppendLine();

                // Show code snippet from first instance
                if (block.Instances.Any() && !string.IsNullOrWhiteSpace(block.Instances[0].CodeSnippet))
                {
                    output.AppendLine("**Code Preview:**");
                    output.AppendLine("```csharp");
                    output.AppendLine(block.Instances[0].CodeSnippet);
                    output.AppendLine("```");
                    output.AppendLine();
                }

                output.AppendLine("**All Instances:**");
                output.AppendLine();

                foreach (var instance in block.Instances)
                {
                    output.AppendLine($"  **{instance.MethodName}**");
                    output.AppendLine($"     File: {instance.FileName}");
                    output.AppendLine($"     📁 Path: {instance.FilePath}");
                    output.AppendLine($"     📄 Lines: {instance.StartLine}-{instance.EndLine} ({instance.LineCount} lines)");
                    output.AppendLine($"     Project: {instance.ProjectName}");
                    output.AppendLine();
                }

                blockNum++;
            }

            // Detailed summary
            output.AppendLine("---");
            output.AppendLine("**Detailed Summary:**");
            output.AppendLine();
            output.AppendLine("**By Similarity:**");
            output.AppendLine($"  🔴 High (95%+): {results.HighSimilarityCount} blocks");
            output.AppendLine($"  🟠 Medium (85-94%): {results.MediumSimilarityCount} blocks");
            output.AppendLine($"  🟡 Low (<85%): {results.LowSimilarityCount} blocks");

            output.AppendLine();
            output.AppendLine("**Recommendations:**");
            if (results.TotalDuplicateBlocks > 0)
            {
                output.AppendLine($"  • {results.TotalDuplicateBlocks} duplicate code block{(results.TotalDuplicateBlocks > 1 ? "s" : "")} found");
                output.AppendLine($"  • Consider extracting common code into shared methods or base classes");
                output.AppendLine($"  • High similarity blocks (95%+) are prime candidates for refactoring");
                output.AppendLine($"  • Review each duplicate carefully - some may be intentional");
            }

            return output.ToString();
        }

        // ==================== Documentation Coverage Formatting ====================

        // Summary mode: Quick overview with key metrics
        private static string FormatDocumentationCoverageSummary(DocumentationCoverageResults results)
        {
            if (results.TotalSymbols == 0)
                return "✅ No symbols found to analyze.";

            var coverageIcon = results.CoveragePercentage >= 80 ? "✅" : results.CoveragePercentage >= 50 ? "⚠️" : "❌";

            var output = new StringBuilder();
            output.AppendLine($"{coverageIcon} **Documentation Coverage: {results.CoveragePercentage:F1}%**\n");
            output.AppendLine($"📊 **Summary:**");
            output.AppendLine($"  • Documented: {results.DocumentedSymbols}/{results.TotalSymbols} symbols");
            output.AppendLine($"  • Undocumented: {results.UndocumentedCount}");
            output.AppendLine();

            if (results.UndocumentedCount > 0)
            {
                output.AppendLine("**Undocumented by Kind:**");
                if (results.UndocumentedClasses > 0)
                    output.AppendLine($"  • Classes/Types: {results.UndocumentedClasses}");
                if (results.UndocumentedMethods > 0)
                    output.AppendLine($"  • Methods: {results.UndocumentedMethods}");
                if (results.UndocumentedProperties > 0)
                    output.AppendLine($"  • Properties: {results.UndocumentedProperties}");
                if (results.UndocumentedFields > 0)
                    output.AppendLine($"  • Fields: {results.UndocumentedFields}");
                if (results.UndocumentedEvents > 0)
                    output.AppendLine($"  • Events: {results.UndocumentedEvents}");
                output.AppendLine();

                // Show top 5 undocumented symbols
                var topUndocumented = results.UndocumentedSymbols.Take(5).ToList();
                if (topUndocumented.Any())
                {
                    output.AppendLine("**Examples (first 5):**");
                    foreach (var symbol in topUndocumented)
                    {
                        output.AppendLine($"  • {symbol.Kind}: {symbol.Name} ({symbol.FileName}:{symbol.LineNumber})");
                    }
                }
            }

            if (results.Warnings.Any())
            {
                output.AppendLine("\n⚠️ **Warnings:**");
                foreach (var warning in results.Warnings.Take(3))
                {
                    output.AppendLine($"   - {warning.Message}");
                }
            }

            return output.ToString();
        }

        // Normal mode: Balanced view with grouping
        private static string FormatDocumentationCoverageNormal(DocumentationCoverageResults results)
        {
            if (results.TotalSymbols == 0)
                return "✅ No symbols found to analyze.";

            var coverageIcon = results.CoveragePercentage >= 80 ? "✅" : results.CoveragePercentage >= 50 ? "⚠️" : "❌";

            var output = new StringBuilder();
            output.AppendLine($"**Documentation Coverage Analysis**\n");
            output.AppendLine($"{coverageIcon} **Overall Coverage: {results.CoveragePercentage:F1}%**");
            output.AppendLine();
            output.AppendLine($"📊 **Analysis Summary:**");
            output.AppendLine($"  • Total symbols: {results.TotalSymbols}");
            output.AppendLine($"  • Documented: {results.DocumentedSymbols}");
            output.AppendLine($"  • Undocumented: {results.UndocumentedCount}");
            output.AppendLine($"  • Projects analyzed: {results.AnalyzedProjects}");
            output.AppendLine($"  • Files analyzed: {results.AnalyzedFiles}");
            if (results.FailedProjects > 0)
                output.AppendLine($"  • Failed projects: {results.FailedProjects}");
            output.AppendLine();

            if (results.UndocumentedCount > 0)
            {
                output.AppendLine("**Breakdown by Kind:**");
                if (results.UndocumentedClasses > 0)
                    output.AppendLine($"  📦 Classes/Types: {results.UndocumentedClasses}");
                if (results.UndocumentedMethods > 0)
                    output.AppendLine($"  🔧 Methods: {results.UndocumentedMethods}");
                if (results.UndocumentedProperties > 0)
                    output.AppendLine($"  🔑 Properties: {results.UndocumentedProperties}");
                if (results.UndocumentedFields > 0)
                    output.AppendLine($"  📋 Fields: {results.UndocumentedFields}");
                if (results.UndocumentedEvents > 0)
                    output.AppendLine($"  ⚡ Events: {results.UndocumentedEvents}");
                output.AppendLine();

                // Group by namespace
                var groupedByNamespace = results.UndocumentedSymbols
                    .GroupBy(s => string.IsNullOrEmpty(s.Namespace) ? "(global)" : s.Namespace)
                    .OrderByDescending(g => g.Count())
                    .Take(10);

                output.AppendLine("**Top Namespaces with Undocumented Symbols:**");
                foreach (var group in groupedByNamespace)
                {
                    output.AppendLine($"\n**{group.Key}** ({group.Count()} undocumented)");

                    // Show breakdown by kind within namespace
                    var byKind = group.GroupBy(s => s.Kind).OrderByDescending(g => g.Count());
                    foreach (var kindGroup in byKind)
                    {
                        output.AppendLine($"  • {kindGroup.Key}: {kindGroup.Count()}");

                        // Show first 3 examples
                        foreach (var symbol in kindGroup.Take(3))
                        {
                            output.AppendLine($"    - {symbol.Name} ({symbol.FileName}:{symbol.LineNumber})");
                        }
                    }
                }
            }
            else
            {
                output.AppendLine("✅ **Excellent!** All symbols are documented.");
            }

            if (results.Warnings.Any())
            {
                output.AppendLine("\n⚠️ **Warnings:**");
                foreach (var warning in results.Warnings)
                {
                    output.AppendLine($"   - {warning.Context}: {warning.Message}");
                }
            }

            return output.ToString();
        }

        // Detailed mode: Comprehensive format with suggested documentation
        private static string FormatDocumentationCoverageDetailed(DocumentationCoverageResults results)
        {
            if (results.TotalSymbols == 0)
                return "✅ No symbols found to analyze.";

            var coverageIcon = results.CoveragePercentage >= 80 ? "✅" : results.CoveragePercentage >= 50 ? "⚠️" : "❌";

            var output = new StringBuilder();
            output.AppendLine($"**Documentation Coverage Analysis (Detailed)**\n");
            output.AppendLine($"{coverageIcon} **Overall Coverage: {results.CoveragePercentage:F1}%**");
            output.AppendLine();
            output.AppendLine($"📊 **Complete Statistics:**");
            output.AppendLine($"  • Total symbols analyzed: {results.TotalSymbols}");
            output.AppendLine($"  • Documented symbols: {results.DocumentedSymbols}");
            output.AppendLine($"  • Undocumented symbols: {results.UndocumentedCount}");
            output.AppendLine($"  • Projects analyzed: {results.AnalyzedProjects}");
            output.AppendLine($"  • Files analyzed: {results.AnalyzedFiles}");
            if (results.FailedProjects > 0)
                output.AppendLine($"  • Failed projects: {results.FailedProjects}");
            output.AppendLine();

            output.AppendLine("**By Symbol Kind:**");
            output.AppendLine($"  📦 Classes/Types: {results.UndocumentedClasses}");
            output.AppendLine($"  🔧 Methods: {results.UndocumentedMethods}");
            output.AppendLine($"  🔑 Properties: {results.UndocumentedProperties}");
            output.AppendLine($"  📋 Fields: {results.UndocumentedFields}");
            output.AppendLine($"  ⚡ Events: {results.UndocumentedEvents}");
            output.AppendLine();

            // Show all warnings
            if (results.Warnings.Any())
            {
                output.AppendLine("⚠️ **Analysis Warnings:**");
                foreach (var warning in results.Warnings)
                {
                    output.AppendLine($"   - {warning.Context}: {warning.Message}");
                }
                output.AppendLine();
            }

            if (results.UndocumentedCount == 0)
            {
                output.AppendLine("✅ **Perfect!** All symbols have XML documentation.");
                return output.ToString();
            }

            // Group by project, then namespace
            var groupedByProject = results.UndocumentedSymbols
                .GroupBy(s => s.ProjectName)
                .OrderBy(g => g.Key);

            foreach (var projectGroup in groupedByProject)
            {
                output.AppendLine($"## 📁 Project: {projectGroup.Key}");
                output.AppendLine($"Undocumented symbols: {projectGroup.Count()}");
                output.AppendLine();

                var groupedByNamespace = projectGroup
                    .GroupBy(s => string.IsNullOrEmpty(s.Namespace) ? "(global)" : s.Namespace)
                    .OrderBy(g => g.Key);

                foreach (var namespaceGroup in groupedByNamespace)
                {
                    output.AppendLine($"### Namespace: {namespaceGroup.Key}");
                    output.AppendLine();

                    var groupedByKind = namespaceGroup
                        .GroupBy(s => s.Kind)
                        .OrderBy(g => g.Key);

                    foreach (var kindGroup in groupedByKind)
                    {
                        output.AppendLine($"#### {kindGroup.Key}s ({kindGroup.Count()})");
                        output.AppendLine();

                        foreach (var symbol in kindGroup)
                        {
                            output.AppendLine($"**{symbol.Name}**");
                            output.AppendLine($"  📍 Location: {symbol.FileName}:{symbol.LineNumber}");
                            output.AppendLine($"  🔒 Accessibility: {symbol.Accessibility}");

                            if (!string.IsNullOrEmpty(symbol.ContainingType))
                                output.AppendLine($"  📦 Containing Type: {symbol.ContainingType}");

                            if (!string.IsNullOrEmpty(symbol.Signature))
                                output.AppendLine($"  ✍️ Signature: `{symbol.Signature}`");

                            if (symbol.Parameters?.Any() == true)
                            {
                                output.AppendLine($"  📝 Parameters: {string.Join(", ", symbol.Parameters)}");
                            }

                            if (!string.IsNullOrEmpty(symbol.ReturnType))
                                output.AppendLine($"  ↩️ Returns: {symbol.ReturnType}");

                            // Show suggested documentation
                            if (!string.IsNullOrEmpty(symbol.SuggestedDocumentation))
                            {
                                output.AppendLine();
                                output.AppendLine("  **Suggested Documentation:**");
                                output.AppendLine("  ```csharp");
                                foreach (var line in symbol.SuggestedDocumentation.Split('\n'))
                                {
                                    output.AppendLine($"  {line}");
                                }
                                output.AppendLine("  ```");
                            }

                            output.AppendLine();
                        }
                    }
                }
            }

            // Final recommendations
            output.AppendLine("---");
            output.AppendLine("**Recommendations:**");
            if (results.CoveragePercentage < 50)
            {
                output.AppendLine("  ⚠️ **Low coverage detected!** Consider documenting at least public APIs.");
            }
            else if (results.CoveragePercentage < 80)
            {
                output.AppendLine("  📝 **Moderate coverage.** Focus on documenting public classes and methods first.");
            }
            else
            {
                output.AppendLine("  ✅ **Good coverage!** Continue documenting remaining symbols.");
            }

            if (results.UndocumentedClasses > 0)
                output.AppendLine($"  • Add XML documentation to {results.UndocumentedClasses} class{(results.UndocumentedClasses > 1 ? "es" : "")}");
            if (results.UndocumentedMethods > 0)
                output.AppendLine($"  • Add XML documentation to {results.UndocumentedMethods} method{(results.UndocumentedMethods > 1 ? "s" : "")}");
            if (results.UndocumentedProperties > 0)
                output.AppendLine($"  • Add XML documentation to {results.UndocumentedProperties} propert{(results.UndocumentedProperties > 1 ? "ies" : "y")}");

            output.AppendLine("  • Use suggested documentation as a starting point");
            output.AppendLine("  • Customize documentation to describe actual behavior and intent");

            return output.ToString();
        }

        // ==================== TODO Comments Formatting ====================

        // Summary mode: Quick overview with counts by type
        private static string FormatTODOCommentsSummary(TODOCommentResults results)
        {
            if (results.TotalComments == 0)
                return "✅ No TODO/FIXME/HACK comments found.";

            var output = new StringBuilder();
            output.AppendLine($"📝 **TODO Comments Found: {results.TotalComments}**\n");
            output.AppendLine($"**By Type:**");
            if (results.TODOCount > 0)
                output.AppendLine($"  📌 TODO: {results.TODOCount}");
            if (results.FIXMECount > 0)
                output.AppendLine($"  🔧 FIXME: {results.FIXMECount}");
            if (results.HACKCount > 0)
                output.AppendLine($"  ⚠️ HACK: {results.HACKCount}");
            if (results.BUGCount > 0)
                output.AppendLine($"  🐛 BUG: {results.BUGCount}");
            if (results.NOTECount > 0)
                output.AppendLine($"  📋 NOTE: {results.NOTECount}");
            if (results.OtherCount > 0)
                output.AppendLine($"  ➕ Other: {results.OtherCount}");

            output.AppendLine();
            output.AppendLine($"**Files Analyzed:** {results.AnalyzedFiles}");
            output.AppendLine($"**Projects:** {results.AnalyzedProjects}");

            // Show top 5 most urgent (FIXME, BUG, HACK)
            var urgentComments = results.Comments
                .Where(c => c.Type == "FIXME" || c.Type == "BUG" || c.Type == "HACK")
                .Take(5)
                .ToList();

            if (urgentComments.Any())
            {
                output.AppendLine();
                output.AppendLine("**Most Urgent (first 5):**");
                foreach (var comment in urgentComments)
                {
                    var icon = comment.Type switch
                    {
                        "FIXME" => "🔧",
                        "BUG" => "🐛",
                        "HACK" => "⚠️",
                        _ => "📌"
                    };
                    output.AppendLine($"  {icon} {comment.Type}: {TruncateMessage(comment.Message, 60)} ({comment.FileName}:{comment.LineNumber})");
                }
            }

            if (results.Warnings.Any())
            {
                output.AppendLine("\n⚠️ **Warnings:**");
                foreach (var warning in results.Warnings.Take(3))
                {
                    output.AppendLine($"   - {warning.Message}");
                }
            }

            return output.ToString();
        }

        // Normal mode: Grouped by type with file locations
        private static string FormatTODOCommentsNormal(TODOCommentResults results)
        {
            if (results.TotalComments == 0)
                return "✅ No TODO/FIXME/HACK comments found.";

            var output = new StringBuilder();
            output.AppendLine($"**TODO Comments Analysis**\n");
            output.AppendLine($"📊 **Summary:** {results.TotalComments} comments found");
            output.AppendLine();

            output.AppendLine("**Breakdown by Type:**");
            if (results.TODOCount > 0)
                output.AppendLine($"  📌 TODO: {results.TODOCount}");
            if (results.FIXMECount > 0)
                output.AppendLine($"  🔧 FIXME: {results.FIXMECount}");
            if (results.HACKCount > 0)
                output.AppendLine($"  ⚠️ HACK: {results.HACKCount}");
            if (results.BUGCount > 0)
                output.AppendLine($"  🐛 BUG: {results.BUGCount}");
            if (results.NOTECount > 0)
                output.AppendLine($"  📋 NOTE: {results.NOTECount}");
            if (results.OtherCount > 0)
                output.AppendLine($"  ➕ Other: {results.OtherCount}");

            output.AppendLine();
            output.AppendLine($"📁 **Files:** {results.AnalyzedFiles} | **Projects:** {results.AnalyzedProjects}");
            if (results.FailedProjects > 0)
                output.AppendLine($"⚠️ **Failed Projects:** {results.FailedProjects}");

            // Group by type
            var commentsByType = results.Comments.GroupBy(c => c.Type).OrderByDescending(g => GetTypePriority(g.Key));

            foreach (var typeGroup in commentsByType)
            {
                var icon = typeGroup.Key switch
                {
                    "TODO" => "📌",
                    "FIXME" => "🔧",
                    "HACK" => "⚠️",
                    "BUG" => "🐛",
                    "NOTE" => "📋",
                    _ => "➕"
                };

                output.AppendLine();
                output.AppendLine($"## {icon} {typeGroup.Key} Comments ({typeGroup.Count()})");
                output.AppendLine();

                // Group by project
                var byProject = typeGroup.GroupBy(c => c.ProjectName).OrderBy(g => g.Key);
                foreach (var projectGroup in byProject)
                {
                    output.AppendLine($"**{projectGroup.Key}** ({projectGroup.Count()}):");

                    // Show up to 10 comments per project for this type
                    foreach (var comment in projectGroup.Take(10))
                    {
                        var authorInfo = !string.IsNullOrWhiteSpace(comment.Author) ? $" [{comment.Author}]" : "";
                        output.AppendLine($"  • {comment.FileName}:{comment.LineNumber}{authorInfo}");
                        output.AppendLine($"    {TruncateMessage(comment.Message, 80)}");
                    }

                    if (projectGroup.Count() > 10)
                    {
                        output.AppendLine($"  ... and {projectGroup.Count() - 10} more");
                    }

                    output.AppendLine();
                }
            }

            if (results.Warnings.Any())
            {
                output.AppendLine("⚠️ **Warnings:**");
                foreach (var warning in results.Warnings)
                {
                    output.AppendLine($"   - {warning.Context}: {warning.Message}");
                }
            }

            return output.ToString();
        }

        // Detailed mode: Full information with code context
        private static string FormatTODOCommentsDetailed(TODOCommentResults results)
        {
            if (results.TotalComments == 0)
                return "✅ No TODO/FIXME/HACK comments found.";

            var output = new StringBuilder();
            output.AppendLine($"**TODO Comments Analysis (Detailed)**\n");
            output.AppendLine($"📊 **Total Comments:** {results.TotalComments}");
            output.AppendLine();

            output.AppendLine("**Statistics:**");
            output.AppendLine($"  📌 TODO: {results.TODOCount}");
            output.AppendLine($"  🔧 FIXME: {results.FIXMECount}");
            output.AppendLine($"  ⚠️ HACK: {results.HACKCount}");
            output.AppendLine($"  🐛 BUG: {results.BUGCount}");
            output.AppendLine($"  📋 NOTE: {results.NOTECount}");
            if (results.OtherCount > 0)
                output.AppendLine($"  ➕ Other: {results.OtherCount}");
            output.AppendLine();

            output.AppendLine($"📁 **Files Analyzed:** {results.AnalyzedFiles}");
            output.AppendLine($"📦 **Projects:** {results.AnalyzedProjects}");
            if (results.FailedProjects > 0)
                output.AppendLine($"⚠️ **Failed Projects:** {results.FailedProjects}");

            if (results.Warnings.Any())
            {
                output.AppendLine();
                output.AppendLine("⚠️ **Analysis Warnings:**");
                foreach (var warning in results.Warnings)
                {
                    output.AppendLine($"   - {warning.Context}: {warning.Message}");
                }
            }

            output.AppendLine();

            // Group by project, then by type
            var groupedByProject = results.Comments.GroupBy(c => c.ProjectName).OrderBy(g => g.Key);

            foreach (var projectGroup in groupedByProject)
            {
                output.AppendLine($"## 📁 Project: {projectGroup.Key}");
                output.AppendLine($"Comments: {projectGroup.Count()}");
                output.AppendLine();

                var groupedByType = projectGroup.GroupBy(c => c.Type).OrderByDescending(g => GetTypePriority(g.Key));

                foreach (var typeGroup in groupedByType)
                {
                    var icon = typeGroup.Key switch
                    {
                        "TODO" => "📌",
                        "FIXME" => "🔧",
                        "HACK" => "⚠️",
                        "BUG" => "🐛",
                        "NOTE" => "📋",
                        _ => "➕"
                    };

                    output.AppendLine($"### {icon} {typeGroup.Key} ({typeGroup.Count()})");
                    output.AppendLine();

                    foreach (var comment in typeGroup)
                    {
                        output.AppendLine($"**{comment.FileName}:{comment.LineNumber}**");
                        if (!string.IsNullOrWhiteSpace(comment.Author))
                            output.AppendLine($"  👤 Author: {comment.Author}");
                        output.AppendLine($"  💬 Message: {comment.Message}");

                        if (!string.IsNullOrWhiteSpace(comment.CodeContext))
                        {
                            output.AppendLine();
                            output.AppendLine("  **Code Context:**");
                            output.AppendLine("  ```csharp");
                            foreach (var line in comment.CodeContext.Split('\n'))
                            {
                                output.AppendLine($"  {line}");
                            }
                            output.AppendLine("  ```");
                        }

                        output.AppendLine();
                    }
                }
            }

            // Recommendations
            output.AppendLine("---");
            output.AppendLine("**Recommendations:**");
            if (results.BUGCount > 0)
                output.AppendLine($"  🐛 Address {results.BUGCount} BUG comment{(results.BUGCount > 1 ? "s" : "")} immediately");
            if (results.FIXMECount > 0)
                output.AppendLine($"  🔧 Fix {results.FIXMECount} FIXME item{(results.FIXMECount > 1 ? "s" : "")} in upcoming sprints");
            if (results.HACKCount > 0)
                output.AppendLine($"  ⚠️ Refactor {results.HACKCount} HACK{(results.HACKCount > 1 ? "s" : "")} to proper solutions");
            if (results.TODOCount > 0)
                output.AppendLine($"  📌 Plan work for {results.TODOCount} TODO item{(results.TODOCount > 1 ? "s" : "")}");

            output.AppendLine("  • Consider creating issues/tickets for high-priority items");
            output.AppendLine("  • Review and remove outdated comments");

            return output.ToString();
        }

        // Helper: Get priority for comment type sorting
        private static int GetTypePriority(string type)
        {
            return type switch
            {
                "BUG" => 5,
                "FIXME" => 4,
                "HACK" => 3,
                "TODO" => 2,
                "NOTE" => 1,
                _ => 0
            };
        }

        // Helper: Truncate message for summary views
        private static string TruncateMessage(string message, int maxLength)
        {
            if (string.IsNullOrEmpty(message))
                return string.Empty;

            if (message.Length <= maxLength)
                return message;

            return message.Substring(0, maxLength - 3) + "...";
        }

        // ==================== Large Files Formatting ====================

        // Summary mode: Top large files only
        private static string FormatLargeFilesSummary(LargeFileResults results, int threshold)
        {
            if (results.TotalLargeFiles == 0)
                return $"✅ No files found above {threshold} lines.";

            var output = new StringBuilder();
            output.AppendLine($"📄 **Large Files Found: {results.TotalLargeFiles}** (> {threshold} lines)\n");
            output.AppendLine($"**Statistics:**");
            output.AppendLine($"  • Average: {results.AverageLineCount} lines");
            output.AppendLine($"  • Largest: {results.MaxLineCount} lines");
            output.AppendLine($"  • Total Size: {FormatFileSize(results.TotalSizeInBytes)}");
            output.AppendLine();

            // Show top 10 largest files
            output.AppendLine("**Top 10 Largest Files:**");
            foreach (var file in results.LargeFiles.Take(10))
            {
                output.AppendLine($"  {file.LineCount,6} lines - {file.FileName} ({file.ProjectName})");
            }

            if (results.TotalLargeFiles > 10)
            {
                output.AppendLine($"  ... and {results.TotalLargeFiles - 10} more files");
            }

            if (results.Warnings.Any())
            {
                output.AppendLine("\n⚠️ **Warnings:**");
                foreach (var warning in results.Warnings.Take(3))
                {
                    output.AppendLine($"   - {warning.Message}");
                }
            }

            return output.ToString();
        }

        // Normal mode: Grouped by project with metrics
        private static string FormatLargeFilesNormal(LargeFileResults results, int threshold)
        {
            if (results.TotalLargeFiles == 0)
                return $"✅ No files found above {threshold} lines.";

            var output = new StringBuilder();
            output.AppendLine($"**Large Files Analysis**\n");
            output.AppendLine($"📊 **Summary:** {results.TotalLargeFiles} files > {threshold} lines");
            output.AppendLine();

            output.AppendLine("**Overall Statistics:**");
            output.AppendLine($"  • Files Analyzed: {results.AnalyzedFiles}");
            output.AppendLine($"  • Large Files: {results.TotalLargeFiles}");
            output.AppendLine($"  • Average Lines: {results.AverageLineCount}");
            output.AppendLine($"  • Max Lines: {results.MaxLineCount}");
            output.AppendLine($"  • Total Size: {FormatFileSize(results.TotalSizeInBytes)}");
            if (results.FailedProjects > 0)
                output.AppendLine($"  • Failed Projects: {results.FailedProjects}");
            output.AppendLine();

            // Group by project
            var byProject = results.LargeFiles.GroupBy(f => f.ProjectName).OrderByDescending(g => g.Count());

            foreach (var projectGroup in byProject)
            {
                output.AppendLine($"## 📁 {projectGroup.Key} ({projectGroup.Count()} files)");
                output.AppendLine();

                foreach (var file in projectGroup.Take(15))
                {
                    var sizeStr = FormatFileSize(file.SizeInBytes);
                    output.AppendLine($"  **{file.FileName}**");
                    output.AppendLine($"    📏 Lines: {file.LineCount} | 💾 Size: {sizeStr} | 📦 Types: {file.TypeCount} | 🔧 Methods: {file.MethodCount}");
                }

                if (projectGroup.Count() > 15)
                {
                    output.AppendLine($"  ... and {projectGroup.Count() - 15} more files");
                }

                output.AppendLine();
            }

            if (results.Warnings.Any())
            {
                output.AppendLine("⚠️ **Warnings:**");
                foreach (var warning in results.Warnings)
                {
                    output.AppendLine($"   - {warning.Context}: {warning.Message}");
                }
            }

            return output.ToString();
        }

        // Detailed mode: Full information with refactoring suggestions
        private static string FormatLargeFilesDetailed(LargeFileResults results, int threshold)
        {
            if (results.TotalLargeFiles == 0)
                return $"✅ No files found above {threshold} lines.";

            var output = new StringBuilder();
            output.AppendLine($"**Large Files Analysis (Detailed)**\n");
            output.AppendLine($"📊 **Threshold:** {threshold} lines");
            output.AppendLine();

            output.AppendLine("**Complete Statistics:**");
            output.AppendLine($"  • Files Analyzed: {results.AnalyzedFiles}");
            output.AppendLine($"  • Projects: {results.AnalyzedProjects}");
            output.AppendLine($"  • Large Files Found: {results.TotalLargeFiles}");
            output.AppendLine($"  • Average Lines: {results.AverageLineCount}");
            output.AppendLine($"  • Max Lines: {results.MaxLineCount}");
            output.AppendLine($"  • Total Size: {FormatFileSize(results.TotalSizeInBytes)}");
            if (results.FailedProjects > 0)
                output.AppendLine($"  • Failed Projects: {results.FailedProjects}");

            if (results.Warnings.Any())
            {
                output.AppendLine();
                output.AppendLine("⚠️ **Analysis Warnings:**");
                foreach (var warning in results.Warnings)
                {
                    output.AppendLine($"   - {warning.Context}: {warning.Message}");
                }
            }

            output.AppendLine();

            // Group by size category
            var hugeFiles = results.LargeFiles.Where(f => f.LineCount >= 2000).ToList();
            var veryLargeFiles = results.LargeFiles.Where(f => f.LineCount >= 1000 && f.LineCount < 2000).ToList();
            var largeFiles = results.LargeFiles.Where(f => f.LineCount < 1000).ToList();

            if (hugeFiles.Any())
            {
                output.AppendLine($"## 🔴 Huge Files (>= 2000 lines): {hugeFiles.Count}");
                output.AppendLine("**These files urgently need refactoring!**");
                output.AppendLine();

                foreach (var file in hugeFiles)
                {
                    OutputFileDetails(output, file);
                }
            }

            if (veryLargeFiles.Any())
            {
                output.AppendLine($"## 🟠 Very Large Files (1000-1999 lines): {veryLargeFiles.Count}");
                output.AppendLine("**Consider refactoring these files.**");
                output.AppendLine();

                foreach (var file in veryLargeFiles)
                {
                    OutputFileDetails(output, file);
                }
            }

            if (largeFiles.Any())
            {
                output.AppendLine($"## 🟡 Large Files ({threshold}-999 lines): {largeFiles.Count}");
                output.AppendLine("**Monitor these files for growth.**");
                output.AppendLine();

                foreach (var file in largeFiles.Take(20))
                {
                    OutputFileDetails(output, file);
                }

                if (largeFiles.Count > 20)
                {
                    output.AppendLine($"... and {largeFiles.Count - 20} more files");
                    output.AppendLine();
                }
            }

            // Recommendations
            output.AppendLine("---");
            output.AppendLine("**Refactoring Recommendations:**");
            if (hugeFiles.Any())
                output.AppendLine($"  🔴 **Urgent**: Refactor {hugeFiles.Count} huge file{(hugeFiles.Count > 1 ? "s" : "")} (>= 2000 lines)");
            if (veryLargeFiles.Any())
                output.AppendLine($"  🟠 **High Priority**: Consider refactoring {veryLargeFiles.Count} very large file{(veryLargeFiles.Count > 1 ? "s" : "")} (1000-1999 lines)");
            if (largeFiles.Any())
                output.AppendLine($"  🟡 **Monitor**: Watch {largeFiles.Count} large file{(largeFiles.Count > 1 ? "s" : "")} ({threshold}-999 lines) for growth");

            output.AppendLine();
            output.AppendLine("**Refactoring Strategies:**");
            output.AppendLine("  • Extract classes: Split into multiple files by responsibility");
            output.AppendLine("  • Extract methods: Break down large methods into smaller ones");
            output.AppendLine("  • Use partial classes: Divide functionality across files");
            output.AppendLine("  • Move nested types: Extract nested classes to separate files");
            output.AppendLine("  • Separate concerns: Apply Single Responsibility Principle");

            return output.ToString();
        }

        // Helper: Output file details for detailed mode
        private static void OutputFileDetails(StringBuilder output, LargeFile file)
        {
            output.AppendLine($"**{file.FileName}**");
            output.AppendLine($"  📍 Project: {file.ProjectName}");
            output.AppendLine($"  📏 Lines: {file.LineCount:N0}");
            output.AppendLine($"  💾 Size: {FormatFileSize(file.SizeInBytes)}");
            output.AppendLine($"  📦 Types: {file.TypeCount}");
            output.AppendLine($"  🔧 Methods: {file.MethodCount}");

            // Suggest refactoring if metrics are concerning
            var suggestions = new List<string>();
            if (file.LineCount >= 2000)
                suggestions.Add("Very large file - urgent refactoring needed");
            if (file.TypeCount > 5)
                suggestions.Add($"Multiple types ({file.TypeCount}) - consider splitting");
            if (file.MethodCount > 50)
                suggestions.Add($"Many methods ({file.MethodCount}) - check cohesion");

            if (suggestions.Any())
            {
                output.AppendLine($"  💡 Suggestions:");
                foreach (var suggestion in suggestions)
                {
                    output.AppendLine($"     - {suggestion}");
                }
            }

            output.AppendLine($"  📁 Path: {file.FilePath}");
            output.AppendLine();
        }

        // Helper: Format file size
        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        // ==================== Deprecated APIs Formatting ====================

        // Summary mode: Counts and top deprecated APIs
        private static string FormatDeprecatedAPIsSummary(DeprecatedAPIResults results)
        {
            if (results.TotalDeprecatedAPIs == 0)
                return "✅ No deprecated APIs found.";

            var output = new StringBuilder();
            output.AppendLine($"⚠️ **Deprecated APIs Found: {results.TotalDeprecatedAPIs}** ({results.TotalUsages} usages)\n");
            output.AppendLine($"**Summary:**");
            if (results.ErrorAPIs > 0)
                output.AppendLine($"  🔴 Errors (IsError=true): {results.ErrorAPIs}");
            if (results.WarningAPIs > 0)
                output.AppendLine($"  ⚠️ Warnings: {results.WarningAPIs}");
            output.AppendLine($"  📁 Files Analyzed: {results.AnalyzedFiles}");
            output.AppendLine($"  📦 Projects: {results.AnalyzedProjects}");
            output.AppendLine();

            // Show top 5 most used deprecated APIs
            output.AppendLine("**Most Used Deprecated APIs (Top 5):**");
            foreach (var api in results.DeprecatedAPIs.Take(5))
            {
                var icon = api.IsError ? "🔴" : "⚠️";
                output.AppendLine($"  {icon} **{api.APIName}** - {api.Usages.Count} usage{(api.Usages.Count > 1 ? "s" : "")}");
                if (!string.IsNullOrWhiteSpace(api.ObsoleteMessage))
                    output.AppendLine($"     Message: {TruncateMessage(api.ObsoleteMessage, 60)}");
                if (!string.IsNullOrWhiteSpace(api.Suggestion))
                    output.AppendLine($"     💡 {api.Suggestion}");
            }

            if (results.Warnings.Any())
            {
                output.AppendLine("\n⚠️ **Warnings:**");
                foreach (var warning in results.Warnings.Take(3))
                {
                    output.AppendLine($"   - {warning.Message}");
                }
            }

            return output.ToString();
        }

        // Normal mode: Grouped by API with usage locations
        private static string FormatDeprecatedAPIsNormal(DeprecatedAPIResults results)
        {
            if (results.TotalDeprecatedAPIs == 0)
                return "✅ No deprecated APIs found.";

            var output = new StringBuilder();
            output.AppendLine($"**Deprecated API Analysis**\n");
            output.AppendLine($"📊 **Summary:** {results.TotalDeprecatedAPIs} deprecated APIs, {results.TotalUsages} total usages");
            output.AppendLine();

            output.AppendLine("**Breakdown:**");
            if (results.ErrorAPIs > 0)
                output.AppendLine($"  🔴 Error-level (IsError=true): {results.ErrorAPIs}");
            if (results.WarningAPIs > 0)
                output.AppendLine($"  ⚠️ Warning-level: {results.WarningAPIs}");
            output.AppendLine($"  📁 Files: {results.AnalyzedFiles} | 📦 Projects: {results.AnalyzedProjects}");
            if (results.FailedProjects > 0)
                output.AppendLine($"  ❌ Failed Projects: {results.FailedProjects}");
            output.AppendLine();

            // Group by error/warning
            var errorAPIs = results.DeprecatedAPIs.Where(api => api.IsError).ToList();
            var warningAPIs = results.DeprecatedAPIs.Where(api => !api.IsError).ToList();

            if (errorAPIs.Any())
            {
                output.AppendLine($"## 🔴 Error-Level Deprecated APIs ({errorAPIs.Count})");
                output.AppendLine("**These must be fixed immediately!**");
                output.AppendLine();

                foreach (var api in errorAPIs)
                {
                    OutputAPIDetails(output, api, showTop: 5);
                }
            }

            if (warningAPIs.Any())
            {
                output.AppendLine($"## ⚠️ Warning-Level Deprecated APIs ({warningAPIs.Count})");
                output.AppendLine("**Consider migrating these soon.**");
                output.AppendLine();

                foreach (var api in warningAPIs.Take(10))
                {
                    OutputAPIDetails(output, api, showTop: 5);
                }

                if (warningAPIs.Count > 10)
                {
                    output.AppendLine($"... and {warningAPIs.Count - 10} more deprecated APIs");
                    output.AppendLine();
                }
            }

            if (results.Warnings.Any())
            {
                output.AppendLine("⚠️ **Analysis Warnings:**");
                foreach (var warning in results.Warnings)
                {
                    output.AppendLine($"   - {warning.Context}: {warning.Message}");
                }
            }

            return output.ToString();
        }

        // Detailed mode: Full information with code context
        private static string FormatDeprecatedAPIsDetailed(DeprecatedAPIResults results)
        {
            if (results.TotalDeprecatedAPIs == 0)
                return "✅ No deprecated APIs found.";

            var output = new StringBuilder();
            output.AppendLine($"**Deprecated API Analysis (Detailed)**\n");
            output.AppendLine($"📊 **Total:** {results.TotalDeprecatedAPIs} deprecated APIs, {results.TotalUsages} usages");
            output.AppendLine();

            output.AppendLine("**Complete Statistics:**");
            output.AppendLine($"  🔴 Error-level APIs: {results.ErrorAPIs}");
            output.AppendLine($"  ⚠️ Warning-level APIs: {results.WarningAPIs}");
            output.AppendLine($"  📁 Files Analyzed: {results.AnalyzedFiles}");
            output.AppendLine($"  📦 Projects: {results.AnalyzedProjects}");
            if (results.FailedProjects > 0)
                output.AppendLine($"  ❌ Failed Projects: {results.FailedProjects}");

            if (results.Warnings.Any())
            {
                output.AppendLine();
                output.AppendLine("⚠️ **Analysis Warnings:**");
                foreach (var warning in results.Warnings)
                {
                    output.AppendLine($"   - {warning.Context}: {warning.Message}");
                }
            }

            output.AppendLine();

            // Group by error/warning
            var errorAPIs = results.DeprecatedAPIs.Where(api => api.IsError).ToList();
            var warningAPIs = results.DeprecatedAPIs.Where(api => !api.IsError).ToList();

            if (errorAPIs.Any())
            {
                output.AppendLine($"## 🔴 Error-Level Deprecated APIs ({errorAPIs.Count})");
                output.AppendLine("**Must be fixed - will become compilation errors!**");
                output.AppendLine();

                foreach (var api in errorAPIs)
                {
                    OutputAPIDetailsVerbose(output, api);
                }
            }

            if (warningAPIs.Any())
            {
                output.AppendLine($"## ⚠️ Warning-Level Deprecated APIs ({warningAPIs.Count})");
                output.AppendLine("**Should be migrated to avoid future issues.**");
                output.AppendLine();

                foreach (var api in warningAPIs)
                {
                    OutputAPIDetailsVerbose(output, api);
                }
            }

            // Migration recommendations
            output.AppendLine("---");
            output.AppendLine("**Migration Recommendations:**");
            if (errorAPIs.Any())
                output.AppendLine($"  🔴 **Urgent**: Fix {errorAPIs.Sum(a => a.Usages.Count)} usage{(errorAPIs.Sum(a => a.Usages.Count) > 1 ? "s" : "")} of error-level deprecated APIs");
            if (warningAPIs.Any())
                output.AppendLine($"  ⚠️ **High Priority**: Migrate {warningAPIs.Sum(a => a.Usages.Count)} usage{(warningAPIs.Sum(a => a.Usages.Count) > 1 ? "s" : "")} of warning-level deprecated APIs");

            output.AppendLine();
            output.AppendLine("**Migration Steps:**");
            output.AppendLine("  1. Review the suggested replacements above");
            output.AppendLine("  2. Test the new APIs in a development environment");
            output.AppendLine("  3. Update code and dependencies incrementally");
            output.AppendLine("  4. Run tests to ensure functionality is preserved");
            output.AppendLine("  5. Update documentation if needed");

            return output.ToString();
        }

        // Helper: Output API details for normal mode
        private static void OutputAPIDetails(StringBuilder output, DeprecatedAPI api, int showTop = 5)
        {
            var icon = api.IsError ? "🔴" : "⚠️";
            output.AppendLine($"### {icon} {api.APIName}");
            output.AppendLine($"**Full Name:** `{api.FullName}`");
            output.AppendLine($"**Usages:** {api.Usages.Count}");

            if (!string.IsNullOrWhiteSpace(api.ObsoleteMessage))
                output.AppendLine($"**Message:** {api.ObsoleteMessage}");

            if (!string.IsNullOrWhiteSpace(api.Suggestion))
                output.AppendLine($"**💡 Suggestion:** {api.Suggestion}");

            output.AppendLine();
            output.AppendLine($"**Usage Locations (showing top {Math.Min(showTop, api.Usages.Count)}):**");

            foreach (var usage in api.Usages.Take(showTop))
            {
                output.AppendLine($"  • {usage.ProjectName} / {usage.FileName}:{usage.LineNumber}");
            }

            if (api.Usages.Count > showTop)
            {
                output.AppendLine($"  ... and {api.Usages.Count - showTop} more usage{(api.Usages.Count - showTop > 1 ? "s" : "")}");
            }

            output.AppendLine();
        }

        // Helper: Output API details for detailed mode with code context
        private static void OutputAPIDetailsVerbose(StringBuilder output, DeprecatedAPI api)
        {
            var icon = api.IsError ? "🔴" : "⚠️";
            output.AppendLine($"### {icon} {api.APIName}");
            output.AppendLine($"**Full Name:** `{api.FullName}`");
            output.AppendLine($"**Total Usages:** {api.Usages.Count}");
            output.AppendLine($"**Status:** {(api.IsError ? "Error (IsError=true)" : "Warning")}");

            if (!string.IsNullOrWhiteSpace(api.ObsoleteMessage))
                output.AppendLine($"**Obsolete Message:** {api.ObsoleteMessage}");

            if (!string.IsNullOrWhiteSpace(api.Suggestion))
            {
                output.AppendLine();
                output.AppendLine($"**💡 Migration Suggestion:**");
                output.AppendLine($"  {api.Suggestion}");
            }

            output.AppendLine();
            output.AppendLine("**All Usage Locations:**");
            output.AppendLine();

            // Group by project
            var byProject = api.Usages.GroupBy(u => u.ProjectName).OrderBy(g => g.Key);

            foreach (var projectGroup in byProject)
            {
                output.AppendLine($"**{projectGroup.Key}** ({projectGroup.Count()} usage{(projectGroup.Count() > 1 ? "s" : "")}):");

                foreach (var usage in projectGroup)
                {
                    output.AppendLine($"  📍 {usage.FileName}:{usage.LineNumber}");

                    if (!string.IsNullOrWhiteSpace(usage.CodeContext))
                    {
                        output.AppendLine("  ```csharp");
                        foreach (var line in usage.CodeContext.Split('\n'))
                        {
                            output.AppendLine($"  {line}");
                        }
                        output.AppendLine("  ```");
                    }

                    output.AppendLine();
                }
            }

            output.AppendLine();
        }

        #region GetFileStatistics Formatting

        /// <summary>
        /// Format file statistics in summary mode (key metrics only)
        /// </summary>
        private static string FormatFileStatisticsSummary(FileStatisticsResults results)
        {
            var output = new StringBuilder();

            if (results.Statistics == null)
            {
                output.AppendLine("❌ **No statistics available**");
                if (results.Warnings.Any())
                {
                    output.AppendLine();
                    output.AppendLine("**Warnings:**");
                    foreach (var warning in results.Warnings)
                    {
                        output.AppendLine($"  • {warning.Context}: {warning.Message}");
                    }
                }
                return output.ToString();
            }

            var stats = results.Statistics;

            output.AppendLine($"# File Statistics Summary: {stats.FileName}");
            output.AppendLine();

            // Key metrics
            output.AppendLine("## 📊 Key Metrics");
            output.AppendLine($"**Total Lines:** {stats.TotalLines:N0} ({FormatFileSize(stats.SizeInBytes)})");
            output.AppendLine($"  • Code: {stats.CodeLines:N0} ({GetPercentage(stats.CodeLines, stats.TotalLines)})");
            output.AppendLine($"  • Comments: {stats.CommentLines:N0} ({GetPercentage(stats.CommentLines, stats.TotalLines)})");
            output.AppendLine($"  • Blank: {stats.BlankLines:N0} ({GetPercentage(stats.BlankLines, stats.TotalLines)})");
            output.AppendLine();

            output.AppendLine("## 🧩 Code Elements");
            var totalTypes = stats.ClassCount + stats.InterfaceCount + stats.StructCount + stats.EnumCount;
            output.AppendLine($"**Types:** {totalTypes} (Classes: {stats.ClassCount}, Interfaces: {stats.InterfaceCount}, Structs: {stats.StructCount}, Enums: {stats.EnumCount})");
            output.AppendLine($"**Members:** Methods: {stats.MethodCount}, Properties: {stats.PropertyCount}, Fields: {stats.FieldCount}");
            output.AppendLine();

            output.AppendLine("## 🔀 Complexity");
            output.AppendLine($"**Total Cyclomatic Complexity:** {stats.CyclomaticComplexity}");
            if (stats.MethodCount > 0)
            {
                var avgComplexity = stats.CyclomaticComplexity / (double)stats.MethodCount;
                output.AppendLine($"**Average per Method:** {avgComplexity:F1}");
            }
            if (!string.IsNullOrEmpty(stats.MostComplexMethod))
            {
                output.AppendLine($"**Most Complex Method:** `{stats.MostComplexMethod}` (complexity: {stats.MaxMethodComplexity})");
            }
            output.AppendLine();

            output.AppendLine("## 📚 Documentation");
            var totalMembers = stats.DocumentedMembers + stats.UndocumentedMembers;
            if (totalMembers > 0)
            {
                output.AppendLine($"**Coverage:** {stats.DocumentationCoverage:F1}% ({stats.DocumentedMembers}/{totalMembers} public members documented)");
            }
            else
            {
                output.AppendLine("**Coverage:** No public members to document");
            }

            return output.ToString();
        }

        /// <summary>
        /// Format file statistics in normal mode (balanced view)
        /// </summary>
        private static string FormatFileStatisticsNormal(FileStatisticsResults results)
        {
            var output = new StringBuilder();

            if (results.Statistics == null)
            {
                output.AppendLine("❌ **No statistics available**");
                if (results.Warnings.Any())
                {
                    output.AppendLine();
                    output.AppendLine("**Warnings:**");
                    foreach (var warning in results.Warnings)
                    {
                        output.AppendLine($"  • {warning.Context}: {warning.Message}");
                    }
                }
                return output.ToString();
            }

            var stats = results.Statistics;

            output.AppendLine($"# File Statistics: {stats.FileName}");
            output.AppendLine($"**Path:** {stats.FilePath}");
            if (!string.IsNullOrEmpty(stats.ProjectName) && stats.ProjectName != "(standalone)")
            {
                output.AppendLine($"**Project:** {stats.ProjectName}");
            }
            output.AppendLine();

            // Line counts
            output.AppendLine("## 📏 Line Counts");
            output.AppendLine($"**Total Lines:** {stats.TotalLines:N0}");
            output.AppendLine($"**Code Lines:** {stats.CodeLines:N0} ({GetPercentage(stats.CodeLines, stats.TotalLines)})");
            output.AppendLine($"**Comment Lines:** {stats.CommentLines:N0} ({GetPercentage(stats.CommentLines, stats.TotalLines)})");
            output.AppendLine($"**Blank Lines:** {stats.BlankLines:N0} ({GetPercentage(stats.BlankLines, stats.TotalLines)})");
            output.AppendLine($"**File Size:** {FormatFileSize(stats.SizeInBytes)}");
            output.AppendLine();

            // Code elements breakdown
            output.AppendLine("## 🧩 Code Elements");
            var totalTypes = stats.ClassCount + stats.InterfaceCount + stats.StructCount + stats.EnumCount;
            output.AppendLine($"**Total Types:** {totalTypes}");
            if (stats.ClassCount > 0) output.AppendLine($"  • Classes: {stats.ClassCount}");
            if (stats.InterfaceCount > 0) output.AppendLine($"  • Interfaces: {stats.InterfaceCount}");
            if (stats.StructCount > 0) output.AppendLine($"  • Structs: {stats.StructCount}");
            if (stats.EnumCount > 0) output.AppendLine($"  • Enums: {stats.EnumCount}");
            output.AppendLine();

            output.AppendLine($"**Total Members:** {stats.MethodCount + stats.PropertyCount + stats.FieldCount}");
            if (stats.MethodCount > 0) output.AppendLine($"  • Methods: {stats.MethodCount}");
            if (stats.PropertyCount > 0) output.AppendLine($"  • Properties: {stats.PropertyCount}");
            if (stats.FieldCount > 0) output.AppendLine($"  • Fields: {stats.FieldCount}");
            output.AppendLine();

            // Complexity details
            output.AppendLine("## 🔀 Complexity Metrics");
            output.AppendLine($"**Total Cyclomatic Complexity:** {stats.CyclomaticComplexity}");
            if (stats.MethodCount > 0)
            {
                var avgComplexity = stats.CyclomaticComplexity / (double)stats.MethodCount;
                output.AppendLine($"**Average per Method:** {avgComplexity:F1}");
                output.AppendLine($"**Max Method Complexity:** {stats.MaxMethodComplexity}");
            }
            if (!string.IsNullOrEmpty(stats.MostComplexMethod))
            {
                output.AppendLine($"**Most Complex Method:** `{stats.MostComplexMethod}` (complexity: {stats.MaxMethodComplexity})");

                var complexityRating = GetComplexityRating(stats.MaxMethodComplexity);
                output.AppendLine($"**Complexity Rating:** {complexityRating}");
            }
            output.AppendLine();

            // Dependencies
            output.AppendLine("## 📦 Dependencies");
            output.AppendLine($"**Using Directives:** {stats.UsingDirectivesCount}");
            if (stats.Namespaces.Any())
            {
                var topNamespaces = stats.Namespaces.Take(10).ToList();
                output.AppendLine($"**Top Namespaces (showing {topNamespaces.Count}):**");
                foreach (var ns in topNamespaces)
                {
                    output.AppendLine($"  • {ns}");
                }
                if (stats.Namespaces.Count > 10)
                {
                    output.AppendLine($"  ... and {stats.Namespaces.Count - 10} more");
                }
            }
            output.AppendLine();

            // Documentation coverage
            output.AppendLine("## 📚 Documentation Coverage");
            var totalMembers = stats.DocumentedMembers + stats.UndocumentedMembers;
            if (totalMembers > 0)
            {
                output.AppendLine($"**Coverage:** {stats.DocumentationCoverage:F1}%");
                output.AppendLine($"**Documented:** {stats.DocumentedMembers} public members");
                output.AppendLine($"**Undocumented:** {stats.UndocumentedMembers} public members");

                var docRating = GetDocumentationRating(stats.DocumentationCoverage);
                output.AppendLine($"**Rating:** {docRating}");
            }
            else
            {
                output.AppendLine("**Coverage:** No public members found");
            }

            return output.ToString();
        }

        /// <summary>
        /// Format file statistics in detailed mode (comprehensive view with recommendations)
        /// </summary>
        private static string FormatFileStatisticsDetailed(FileStatisticsResults results)
        {
            var output = new StringBuilder();

            if (results.Statistics == null)
            {
                output.AppendLine("❌ **No statistics available**");
                if (results.Warnings.Any())
                {
                    output.AppendLine();
                    output.AppendLine("**Warnings:**");
                    foreach (var warning in results.Warnings)
                    {
                        output.AppendLine($"  • {warning.Context}: {warning.Message}");
                    }
                }
                return output.ToString();
            }

            var stats = results.Statistics;

            output.AppendLine($"# Detailed File Statistics: {stats.FileName}");
            output.AppendLine($"**Full Path:** `{stats.FilePath}`");
            if (!string.IsNullOrEmpty(stats.ProjectName) && stats.ProjectName != "(standalone)")
            {
                output.AppendLine($"**Project:** {stats.ProjectName}");
            }
            output.AppendLine();

            // Overall quality score
            var qualityScore = CalculateQualityScore(stats);
            output.AppendLine("## ⭐ Overall Quality Score");
            output.AppendLine($"**Score:** {qualityScore.Score}/100 - {qualityScore.Rating}");
            output.AppendLine($"**Details:** {qualityScore.Details}");
            output.AppendLine();

            // Line counts with detailed breakdown
            output.AppendLine("## 📏 Line Analysis");
            output.AppendLine($"**Total Lines:** {stats.TotalLines:N0}");
            output.AppendLine($"**Code Lines:** {stats.CodeLines:N0} ({GetPercentage(stats.CodeLines, stats.TotalLines)})");
            output.AppendLine($"**Comment Lines:** {stats.CommentLines:N0} ({GetPercentage(stats.CommentLines, stats.TotalLines)})");
            output.AppendLine($"**Blank Lines:** {stats.BlankLines:N0} ({GetPercentage(stats.BlankLines, stats.TotalLines)})");
            output.AppendLine($"**File Size:** {FormatFileSize(stats.SizeInBytes)}");

            var commentRatio = stats.TotalLines > 0 ? (stats.CommentLines * 100.0 / stats.CodeLines) : 0;
            output.AppendLine($"**Comment/Code Ratio:** {commentRatio:F1}%");
            output.AppendLine();

            // Code elements detailed breakdown
            output.AppendLine("## 🧩 Code Elements Breakdown");
            var totalTypes = stats.ClassCount + stats.InterfaceCount + stats.StructCount + stats.EnumCount;
            output.AppendLine($"**Total Types:** {totalTypes}");
            output.AppendLine($"  • Classes: {stats.ClassCount}");
            output.AppendLine($"  • Interfaces: {stats.InterfaceCount}");
            output.AppendLine($"  • Structs: {stats.StructCount}");
            output.AppendLine($"  • Enums: {stats.EnumCount}");
            output.AppendLine();

            var totalMembers = stats.MethodCount + stats.PropertyCount + stats.FieldCount;
            output.AppendLine($"**Total Members:** {totalMembers}");
            output.AppendLine($"  • Methods: {stats.MethodCount}");
            output.AppendLine($"  • Properties: {stats.PropertyCount}");
            output.AppendLine($"  • Fields: {stats.FieldCount}");

            if (totalTypes > 0)
            {
                var avgMembersPerType = totalMembers / (double)totalTypes;
                output.AppendLine($"**Average Members per Type:** {avgMembersPerType:F1}");
            }
            output.AppendLine();

            // Complexity detailed analysis
            output.AppendLine("## 🔀 Complexity Analysis");
            output.AppendLine($"**Total Cyclomatic Complexity:** {stats.CyclomaticComplexity}");
            if (stats.MethodCount > 0)
            {
                var avgComplexity = stats.CyclomaticComplexity / (double)stats.MethodCount;
                output.AppendLine($"**Average per Method:** {avgComplexity:F1}");
                output.AppendLine($"**Max Method Complexity:** {stats.MaxMethodComplexity}");

                if (!string.IsNullOrEmpty(stats.MostComplexMethod))
                {
                    output.AppendLine($"**Most Complex Method:** `{stats.MostComplexMethod}` (complexity: {stats.MaxMethodComplexity})");
                }

                var complexityRating = GetComplexityRating(stats.MaxMethodComplexity);
                output.AppendLine($"**Complexity Rating:** {complexityRating}");

                // Recommendations based on complexity
                if (stats.MaxMethodComplexity > 10)
                {
                    output.AppendLine();
                    output.AppendLine("⚠️ **Complexity Warning:**");
                    output.AppendLine($"  The method `{stats.MostComplexMethod}` has high complexity ({stats.MaxMethodComplexity}).");
                    output.AppendLine("  Consider refactoring into smaller, more maintainable methods.");
                }
            }
            output.AppendLine();

            // Dependencies - complete list
            output.AppendLine("## 📦 Dependencies");
            output.AppendLine($"**Using Directives Count:** {stats.UsingDirectivesCount}");
            if (stats.Namespaces.Any())
            {
                output.AppendLine($"**All Namespaces ({stats.Namespaces.Count}):**");

                // Group namespaces by category
                var systemNs = stats.Namespaces.Where(ns => ns.StartsWith("System")).ToList();
                var microsoftNs = stats.Namespaces.Where(ns => ns.StartsWith("Microsoft") && !ns.StartsWith("System")).ToList();
                var otherNs = stats.Namespaces.Where(ns => !ns.StartsWith("System") && !ns.StartsWith("Microsoft")).ToList();

                if (systemNs.Any())
                {
                    output.AppendLine($"  **System ({systemNs.Count}):**");
                    foreach (var ns in systemNs)
                    {
                        output.AppendLine($"    • {ns}");
                    }
                }

                if (microsoftNs.Any())
                {
                    output.AppendLine($"  **Microsoft ({microsoftNs.Count}):**");
                    foreach (var ns in microsoftNs)
                    {
                        output.AppendLine($"    • {ns}");
                    }
                }

                if (otherNs.Any())
                {
                    output.AppendLine($"  **Other ({otherNs.Count}):**");
                    foreach (var ns in otherNs)
                    {
                        output.AppendLine($"    • {ns}");
                    }
                }
            }
            output.AppendLine();

            // Documentation coverage detailed
            output.AppendLine("## 📚 Documentation Coverage");
            var totalDocMembers = stats.DocumentedMembers + stats.UndocumentedMembers;
            if (totalDocMembers > 0)
            {
                output.AppendLine($"**Coverage:** {stats.DocumentationCoverage:F1}%");
                output.AppendLine($"**Documented Members:** {stats.DocumentedMembers}");
                output.AppendLine($"**Undocumented Members:** {stats.UndocumentedMembers}");
                output.AppendLine($"**Total Public Members:** {totalDocMembers}");

                var docRating = GetDocumentationRating(stats.DocumentationCoverage);
                output.AppendLine($"**Rating:** {docRating}");

                if (stats.DocumentationCoverage < 80)
                {
                    output.AppendLine();
                    output.AppendLine("💡 **Documentation Recommendation:**");
                    output.AppendLine($"  Add XML documentation comments for {stats.UndocumentedMembers} undocumented public members.");
                    output.AppendLine("  Good documentation improves code maintainability and IDE experience.");
                }
            }
            else
            {
                output.AppendLine("**Coverage:** No public members found");
            }
            output.AppendLine();

            // File quality recommendations
            output.AppendLine("## 💡 Recommendations");
            var recommendations = GenerateRecommendations(stats);
            if (recommendations.Any())
            {
                foreach (var recommendation in recommendations)
                {
                    output.AppendLine($"  {recommendation}");
                }
            }
            else
            {
                output.AppendLine("  ✅ No specific recommendations - file quality looks good!");
            }

            return output.ToString();
        }

        // Helper: Get percentage string
        private static string GetPercentage(int part, int total)
        {
            if (total == 0) return "0%";
            return $"{part * 100.0 / total:F1}%";
        }

        // Helper: Get complexity rating
        private static string GetComplexityRating(int complexity)
        {
            if (complexity <= 5)
                return "✅ Low - Easy to maintain";
            if (complexity <= 10)
                return "⚠️ Moderate - Acceptable complexity";
            if (complexity <= 20)
                return "🔶 High - Consider refactoring";
            return "🔴 Very High - Refactoring recommended";
        }

        // Helper: Get documentation rating
        private static string GetDocumentationRating(double coverage)
        {
            if (coverage >= 90)
                return "✅ Excellent";
            if (coverage >= 70)
                return "✅ Good";
            if (coverage >= 50)
                return "⚠️ Fair";
            if (coverage >= 30)
                return "🔶 Poor";
            return "🔴 Very Poor";
        }

        // Helper: Calculate overall quality score
        private static (int Score, string Rating, string Details) CalculateQualityScore(FileStatistics stats)
        {
            int score = 100;
            var issues = new List<string>();

            // Deduct for size issues
            if (stats.TotalLines > 1000)
            {
                score -= 10;
                issues.Add("Large file (>1000 lines)");
            }
            else if (stats.TotalLines > 500)
            {
                score -= 5;
                issues.Add("Moderately large file");
            }

            // Deduct for complexity
            if (stats.MethodCount > 0)
            {
                var avgComplexity = stats.CyclomaticComplexity / (double)stats.MethodCount;
                if (avgComplexity > 10)
                {
                    score -= 20;
                    issues.Add("High average complexity");
                }
                else if (avgComplexity > 5)
                {
                    score -= 10;
                    issues.Add("Moderate complexity");
                }
            }

            if (stats.MaxMethodComplexity > 20)
            {
                score -= 15;
                issues.Add($"Very complex method ({stats.MaxMethodComplexity})");
            }
            else if (stats.MaxMethodComplexity > 10)
            {
                score -= 10;
                issues.Add("High method complexity");
            }

            // Deduct for poor documentation
            var totalMembers = stats.DocumentedMembers + stats.UndocumentedMembers;
            if (totalMembers > 0)
            {
                if (stats.DocumentationCoverage < 30)
                {
                    score -= 20;
                    issues.Add("Very poor documentation");
                }
                else if (stats.DocumentationCoverage < 50)
                {
                    score -= 15;
                    issues.Add("Poor documentation");
                }
                else if (stats.DocumentationCoverage < 70)
                {
                    score -= 10;
                    issues.Add("Fair documentation");
                }
            }

            // Bonus for good comment ratio
            if (stats.CodeLines > 0)
            {
                var commentRatio = stats.CommentLines * 100.0 / stats.CodeLines;
                if (commentRatio > 20 && commentRatio < 50)
                {
                    score = Math.Min(100, score + 5);
                }
            }

            score = Math.Max(0, score);

            string rating;
            if (score >= 90)
                rating = "Excellent ⭐⭐⭐⭐⭐";
            else if (score >= 75)
                rating = "Good ⭐⭐⭐⭐";
            else if (score >= 60)
                rating = "Fair ⭐⭐⭐";
            else if (score >= 40)
                rating = "Poor ⭐⭐";
            else
                rating = "Needs Improvement ⭐";

            var details = issues.Any() ? string.Join(", ", issues) : "No major issues";

            return (score, rating, details);
        }

        // Helper: Generate recommendations
        private static List<string> GenerateRecommendations(FileStatistics stats)
        {
            var recommendations = new List<string>();

            // File size recommendations
            if (stats.TotalLines > 1000)
            {
                recommendations.Add("🔶 **Large File:** Consider splitting this file into multiple smaller files for better maintainability.");
            }

            // Complexity recommendations
            if (stats.MethodCount > 0)
            {
                var avgComplexity = stats.CyclomaticComplexity / (double)stats.MethodCount;
                if (avgComplexity > 10)
                {
                    recommendations.Add("🔶 **High Complexity:** Average method complexity is high. Refactor complex methods into smaller units.");
                }
            }

            if (stats.MaxMethodComplexity > 15)
            {
                recommendations.Add($"🔴 **Complex Method:** `{stats.MostComplexMethod}` has complexity {stats.MaxMethodComplexity}. Break it down into smaller methods.");
            }

            // Documentation recommendations
            var totalMembers = stats.DocumentedMembers + stats.UndocumentedMembers;
            if (totalMembers > 0 && stats.DocumentationCoverage < 70)
            {
                recommendations.Add($"📚 **Documentation:** Add XML comments for {stats.UndocumentedMembers} undocumented public members.");
            }

            // Code density recommendations
            if (stats.CodeLines > 0)
            {
                var commentRatio = stats.CommentLines * 100.0 / stats.CodeLines;
                if (commentRatio < 5)
                {
                    recommendations.Add("💬 **Comments:** Consider adding more inline comments to explain complex logic.");
                }
            }

            // Type count recommendations
            var totalTypes = stats.ClassCount + stats.InterfaceCount + stats.StructCount + stats.EnumCount;
            if (totalTypes > 5)
            {
                recommendations.Add($"🧩 **Multiple Types:** File contains {totalTypes} types. Consider one type per file for better organization.");
            }

            // Member count recommendations
            var totalTypeMembers = stats.MethodCount + stats.PropertyCount + stats.FieldCount;
            if (totalTypes > 0)
            {
                var avgMembersPerType = totalTypeMembers / (double)totalTypes;
                if (avgMembersPerType > 30)
                {
                    recommendations.Add("🔧 **Large Types:** Some types have many members. Consider extracting related functionality.");
                }
            }

            return recommendations;
        }

        // Formatter: Package Analysis - Summary
        private static string FormatPackageAnalysisSummary(PackageAnalysisResults results)
        {
            var output = new StringBuilder();
            output.AppendLine($"📦 Package Analysis Summary ({Path.GetFileName(results.AllPackages.FirstOrDefault()?.ProjectPath ?? "N/A")})");
            output.AppendLine();

            // Show warnings if any
            if (results.Warnings.Any())
            {
                output.AppendLine("⚠️ Warnings:");
                foreach (var warning in results.Warnings)
                {
                    output.AppendLine($"  - {warning.Message}");
                }
                output.AppendLine();
            }

            // Overall statistics
            output.AppendLine("📊 Overall Statistics:");
            output.AppendLine($"  Total Packages: {results.TotalPackages} ({results.UniquePackages} unique)");
            output.AppendLine($"  Analyzed Projects: {results.AnalyzedProjects}");
            if (results.FailedProjects > 0)
                output.AppendLine($"  Failed Projects: {results.FailedProjects}");
            output.AppendLine();

            // Security vulnerabilities
            if (results.Vulnerabilities.Any())
            {
                output.AppendLine($"🔴 Security Vulnerabilities: {results.VulnerablePackages}");
                output.AppendLine($"  Critical: {results.CriticalVulnerabilities}, High: {results.HighVulnerabilities}");
                output.AppendLine();
            }

            // Available updates
            if (results.AvailableUpdates.Any())
            {
                var majorUpdates = results.AvailableUpdates.Count(u => u.IsBreakingChange);
                output.AppendLine($"📦 Updates Available: {results.AvailableUpdates.Count}");
                if (majorUpdates > 0)
                    output.AppendLine($"  ⚠️ Major version updates (breaking): {majorUpdates}");
                output.AppendLine();
            }

            // Version conflicts
            if (results.VersionConflicts.Any())
            {
                output.AppendLine($"⚠️ Version Conflicts: {results.ConflictingPackages} packages");
                output.AppendLine();
            }

            // Unused packages
            if (results.UnusedPackagesCount > 0)
            {
                output.AppendLine($"🗑️ Unused Packages: {results.UnusedPackagesCount}");
                output.AppendLine();
            }

            // Status summary
            if (!results.Vulnerabilities.Any() && !results.VersionConflicts.Any() && results.AvailableUpdates.Count < 5)
            {
                output.AppendLine("✅ Package health looks good!");
            }
            else
            {
                output.AppendLine("⚠️ Action recommended - See detailed view for more information");
            }

            return output.ToString();
        }

        // Formatter: Package Analysis - Normal
        private static string FormatPackageAnalysisNormal(PackageAnalysisResults results)
        {
            var output = new StringBuilder();
            output.AppendLine($"📦 Package Analysis: {Path.GetFileName(results.AllPackages.FirstOrDefault()?.ProjectPath ?? "N/A")}");
            output.AppendLine();

            // Statistics
            output.AppendLine("📊 Statistics:");
            output.AppendLine($"  Total Packages: {results.TotalPackages} ({results.UniquePackages} unique)");
            output.AppendLine($"  Up to Date: {results.UpToDatePackages}");
            output.AppendLine($"  Updates Available: {results.AvailableUpdates.Count}");
            output.AppendLine($"  Version Conflicts: {results.ConflictingPackages}");
            output.AppendLine($"  Unused Packages: {results.UnusedPackagesCount}");
            output.AppendLine($"  Analyzed Projects: {results.AnalyzedProjects}");
            output.AppendLine();

            // Security vulnerabilities
            if (results.Vulnerabilities.Any())
            {
                output.AppendLine($"🔴 Security Vulnerabilities ({results.VulnerablePackages}):");
                foreach (var vuln in results.Vulnerabilities.OrderByDescending(v => v.Severity).Take(5))
                {
                    output.AppendLine($"  {GetSeverityIcon(vuln.Severity)} {vuln.PackageName} {vuln.AffectedVersion}");
                    output.AppendLine($"     → {vuln.VulnerabilityId}: {vuln.Description}");
                    output.AppendLine($"     → Fix: Upgrade to {vuln.RecommendedVersion}+");
                    output.AppendLine($"     → Affected projects: {string.Join(", ", vuln.AffectedProjects)}");
                    output.AppendLine();
                }
            }

            // Outdated packages (top 10)
            if (results.AvailableUpdates.Any())
            {
                output.AppendLine($"📦 Outdated Packages (showing top 10 of {results.AvailableUpdates.Count}):");
                foreach (var update in results.AvailableUpdates.OrderByDescending(u => u.MajorVersionsAhead).Take(10))
                {
                    var icon = update.IsBreakingChange ? "🔴" : "🔶";
                    var updateType = update.IsBreakingChange ? "MAJOR" :
                                     update.MinorVersionsAhead > 0 ? "MINOR" : "PATCH";
                    output.AppendLine($"  {icon} {update.PackageName}");
                    output.AppendLine($"     Current: {update.CurrentVersion} → Latest: {update.LatestVersion} ({updateType})");
                    output.AppendLine($"     Projects: {string.Join(", ", update.AffectedProjects)}");
                    output.AppendLine();
                }
            }

            // Version conflicts
            if (results.VersionConflicts.Any())
            {
                output.AppendLine($"⚠️ Version Conflicts ({results.ConflictingPackages}):");
                foreach (var conflict in results.VersionConflicts.Take(5))
                {
                    output.AppendLine($"  {conflict.PackageName}:");
                    foreach (var usage in conflict.VersionUsages)
                    {
                        output.AppendLine($"    - {usage.ProjectName}: {usage.Version}");
                    }
                    output.AppendLine($"    → Recommendation: Standardize to {conflict.RecommendedVersion}");
                    output.AppendLine();
                }
            }

            // Unused packages (top 10)
            if (results.UnusedPackagesCount > 0)
            {
                output.AppendLine($"🗑️ Unused Packages (showing top 10 of {results.UnusedPackagesCount}):");
                foreach (var pkg in results.UnusedPackages.Take(10))
                {
                    output.AppendLine($"  {pkg.Name} ({pkg.Version}) - {pkg.ProjectName}");
                    if (pkg.ExpectedNamespaces.Any())
                    {
                        output.AppendLine($"    Expected namespaces: {string.Join(", ", pkg.ExpectedNamespaces)}");
                    }
                }
                output.AppendLine();
            }

            // Warnings
            if (results.Warnings.Any())
            {
                output.AppendLine("⚠️ Warnings:");
                foreach (var warning in results.Warnings)
                {
                    output.AppendLine($"  - {warning.Message}");
                }
                output.AppendLine();
            }

            return output.ToString();
        }

        // Formatter: Package Analysis - Detailed
        private static string FormatPackageAnalysisDetailed(PackageAnalysisResults results)
        {
            var output = new StringBuilder();
            output.AppendLine($"📦 Comprehensive Package Analysis");
            output.AppendLine($"Solution: {Path.GetFileName(results.AllPackages.FirstOrDefault()?.ProjectPath ?? "N/A")}");
            output.AppendLine();

            // Full statistics
            output.AppendLine("📊 Complete Statistics:");
            output.AppendLine($"  Total Packages: {results.TotalPackages}");
            output.AppendLine($"  Unique Packages: {results.UniquePackages}");
            output.AppendLine($"  Up to Date: {results.UpToDatePackages}");
            output.AppendLine($"  Updates Available: {results.AvailableUpdates.Count}");
            output.AppendLine($"  Version Conflicts: {results.ConflictingPackages}");
            output.AppendLine($"  Unused Packages: {results.UnusedPackagesCount}");
            output.AppendLine($"  Analyzed Projects: {results.AnalyzedProjects}");
            output.AppendLine($"  Failed Projects: {results.FailedProjects}");
            output.AppendLine();

            // All vulnerabilities
            if (results.Vulnerabilities.Any())
            {
                output.AppendLine($"🔴 Security Vulnerabilities ({results.VulnerablePackages}):");
                output.AppendLine($"  Critical: {results.CriticalVulnerabilities}, High: {results.HighVulnerabilities}, " +
                                  $"Medium: {results.MediumVulnerabilities}, Low: {results.LowVulnerabilities}");
                output.AppendLine();
                foreach (var vuln in results.Vulnerabilities.OrderByDescending(v => v.Severity))
                {
                    output.AppendLine($"  {GetSeverityIcon(vuln.Severity)} {vuln.Severity} - {vuln.PackageName} {vuln.AffectedVersion}");
                    output.AppendLine($"     ID: {vuln.VulnerabilityId}");
                    output.AppendLine($"     Description: {vuln.Description}");
                    output.AppendLine($"     Recommended Version: {vuln.RecommendedVersion}+");
                    output.AppendLine($"     Affected Projects: {string.Join(", ", vuln.AffectedProjects)}");
                    output.AppendLine();
                }
            }

            // All available updates
            if (results.AvailableUpdates.Any())
            {
                output.AppendLine($"📦 All Available Updates ({results.AvailableUpdates.Count}):");
                foreach (var update in results.AvailableUpdates.OrderByDescending(u => u.MajorVersionsAhead)
                                                                .ThenByDescending(u => u.MinorVersionsAhead))
                {
                    var icon = update.IsBreakingChange ? "🔴" : "🔶";
                    output.AppendLine($"  {icon} {update.PackageName}");
                    output.AppendLine($"     Current: {update.CurrentVersion}");
                    output.AppendLine($"     Latest: {update.LatestVersion}");
                    output.AppendLine($"     Version gap: +{update.MajorVersionsAhead} major, +{update.MinorVersionsAhead} minor, +{update.PatchVersionsAhead} patch");
                    output.AppendLine($"     Breaking change: {(update.IsBreakingChange ? "YES ⚠️" : "No")}");
                    output.AppendLine($"     Affected projects: {string.Join(", ", update.AffectedProjects)}");
                    output.AppendLine();
                }
            }

            // All version conflicts
            if (results.VersionConflicts.Any())
            {
                output.AppendLine($"⚠️ All Version Conflicts ({results.ConflictingPackages}):");
                foreach (var conflict in results.VersionConflicts)
                {
                    output.AppendLine($"  {conflict.PackageName}:");
                    foreach (var usage in conflict.VersionUsages)
                    {
                        output.AppendLine($"    - {usage.ProjectName}: {usage.Version}");
                    }
                    output.AppendLine($"    → Recommended version: {conflict.RecommendedVersion}");
                    output.AppendLine();
                }
            }

            // All unused packages
            if (results.UnusedPackagesCount > 0)
            {
                output.AppendLine($"🗑️ All Unused Packages ({results.UnusedPackagesCount}):");
                var groupedByProject = results.UnusedPackages.GroupBy(p => p.ProjectName);
                foreach (var projectGroup in groupedByProject)
                {
                    output.AppendLine($"  {projectGroup.Key}:");
                    foreach (var pkg in projectGroup)
                    {
                        output.AppendLine($"    - {pkg.Name} ({pkg.Version})");
                        if (pkg.ExpectedNamespaces.Any())
                        {
                            output.AppendLine($"      Expected namespaces: {string.Join(", ", pkg.ExpectedNamespaces)}");
                        }
                    }
                    output.AppendLine();
                }
            }

            // Warnings
            if (results.Warnings.Any())
            {
                output.AppendLine("⚠️ Analysis Warnings:");
                foreach (var warning in results.Warnings)
                {
                    output.AppendLine($"  [{warning.Context}] {warning.Message}");
                    if (!string.IsNullOrWhiteSpace(warning.Details))
                    {
                        output.AppendLine($"    Details: {warning.Details}");
                    }
                }
                output.AppendLine();
            }

            return output.ToString();
        }

        // Helper: Get severity icon
        private static string GetSeverityIcon(string severity)
        {
            return severity.ToLower() switch
            {
                "critical" => "🔴",
                "high" => "🔶",
                "medium" => "🟡",
                "low" => "🔵",
                _ => "⚪"
            };
        }

        // Formatter: Test Coverage - Summary
        private static string FormatTestCoverageSummary(TestCoverageResults results)
        {
            var output = new StringBuilder();
            output.AppendLine("🧪 Test Coverage Summary");
            output.AppendLine();

            // Show warnings if any
            if (results.Warnings.Any())
            {
                output.AppendLine("⚠️ Warnings:");
                foreach (var warning in results.Warnings)
                {
                    output.AppendLine($"  - {warning.Message}");
                }
                output.AppendLine();
            }

            // Overall coverage
            output.AppendLine("📊 Overall Coverage:");
            output.AppendLine($"  Type Coverage: {results.TestedTypes}/{results.TotalTypes} ({results.OverallTypeCoverage:F1}%)");
            output.AppendLine($"  Member Coverage: {results.TestedPublicMembers}/{results.TotalPublicMembers} ({results.OverallMemberCoverage:F1}%)");
            output.AppendLine($"  Analyzed Projects: {results.AnalyzedProjects}");
            if (results.FailedProjects > 0)
                output.AppendLine($"  Failed Projects: {results.FailedProjects}");
            output.AppendLine();

            // Risk analysis
            if (results.CriticalRiskTypes > 0 || results.HighRiskTypes > 0)
            {
                output.AppendLine("⚠️ Risk Analysis:");
                if (results.CriticalRiskTypes > 0)
                    output.AppendLine($"  🔴 Critical Risk: {results.CriticalRiskTypes} types (high complexity, no tests)");
                if (results.HighRiskTypes > 0)
                    output.AppendLine($"  🔶 High Risk: {results.HighRiskTypes} types (medium complexity, no tests)");
                if (results.MediumRiskTypes > 0)
                    output.AppendLine($"  🟡 Medium Risk: {results.MediumRiskTypes} types");
                output.AppendLine();
            }

            // Status summary
            if (results.OverallTypeCoverage >= 80)
            {
                output.AppendLine("✅ Excellent test coverage!");
            }
            else if (results.OverallTypeCoverage >= 60)
            {
                output.AppendLine("⚠️ Good coverage, but room for improvement");
            }
            else if (results.OverallTypeCoverage >= 40)
            {
                output.AppendLine("⚠️ Moderate coverage - consider adding more tests");
            }
            else
            {
                output.AppendLine("🔴 Low coverage - significant testing needed");
            }

            return output.ToString();
        }

        // Formatter: Test Coverage - Normal
        private static string FormatTestCoverageNormal(TestCoverageResults results, string groupBy)
        {
            var output = new StringBuilder();
            output.AppendLine("🧪 Test Coverage Analysis");
            output.AppendLine();

            // Overall statistics
            output.AppendLine("📊 Overall Statistics:");
            output.AppendLine($"  Types: {results.TestedTypes}/{results.TotalTypes} tested ({results.OverallTypeCoverage:F1}%)");
            output.AppendLine($"  Public Members: {results.TestedPublicMembers}/{results.TotalPublicMembers} tested ({results.OverallMemberCoverage:F1}%)");
            output.AppendLine($"  Uncovered Types: {results.UncoveredTypes}");
            output.AppendLine($"  Uncovered Members: {results.UncoveredPublicMembers}");
            output.AppendLine($"  Analyzed Projects: {results.AnalyzedProjects}");
            output.AppendLine();

            // Risk analysis
            output.AppendLine("⚠️ Risk Analysis:");
            output.AppendLine($"  🔴 Critical Risk: {results.CriticalRiskTypes} types");
            output.AppendLine($"  🔶 High Risk: {results.HighRiskTypes} types");
            output.AppendLine($"  🟡 Medium Risk: {results.MediumRiskTypes} types");
            output.AppendLine($"  ✅ Low Risk: {results.LowRiskTypes} types");
            output.AppendLine();

            // Coverage by group
            if (groupBy.ToLower() == "project" && results.ProjectStatistics.Any())
            {
                output.AppendLine($"📦 Coverage by Project (showing top 10):");
                foreach (var stat in results.ProjectStatistics.Values
                    .OrderBy(s => s.TypeCoveragePercentage)
                    .Take(10))
                {
                    var icon = stat.TypeCoveragePercentage >= 80 ? "✅" :
                               stat.TypeCoveragePercentage >= 60 ? "⚠️" : "🔴";
                    output.AppendLine($"  {icon} {stat.Name}:");
                    output.AppendLine($"     Types: {stat.TestedTypes}/{stat.TotalTypes} ({stat.TypeCoveragePercentage:F1}%)");
                    output.AppendLine($"     Members: {stat.TestedPublicMembers}/{stat.TotalPublicMembers} ({stat.MemberCoveragePercentage:F1}%)");
                }
                output.AppendLine();
            }
            else if (groupBy.ToLower() == "namespace" && results.NamespaceStatistics.Any())
            {
                output.AppendLine($"📁 Coverage by Namespace (showing top 10):");
                foreach (var stat in results.NamespaceStatistics.Values
                    .OrderBy(s => s.TypeCoveragePercentage)
                    .Take(10))
                {
                    var icon = stat.TypeCoveragePercentage >= 80 ? "✅" :
                               stat.TypeCoveragePercentage >= 60 ? "⚠️" : "🔴";
                    output.AppendLine($"  {icon} {stat.Name}:");
                    output.AppendLine($"     Types: {stat.TestedTypes}/{stat.TotalTypes} ({stat.TypeCoveragePercentage:F1}%)");
                    output.AppendLine($"     Members: {stat.TestedPublicMembers}/{stat.TotalPublicMembers} ({stat.MemberCoveragePercentage:F1}%)");
                }
                output.AppendLine();
            }

            // High-risk types (top 10)
            var highRiskTypes = results.TypeCoverages
                .Where(t => t.RiskLevel == "Critical" || t.RiskLevel == "High")
                .OrderByDescending(t => t.CyclomaticComplexity)
                .Take(10)
                .ToList();

            if (highRiskTypes.Any())
            {
                output.AppendLine($"🔴 High-Risk Uncovered Types (showing top 10 of {results.CriticalRiskTypes + results.HighRiskTypes}):");
                foreach (var type in highRiskTypes)
                {
                    var riskIcon = type.RiskLevel == "Critical" ? "🔴" : "🔶";
                    output.AppendLine($"  {riskIcon} {type.FullTypeName} @ {type.FilePath}:{type.LineNumber}");
                    output.AppendLine($"     Complexity: {type.CyclomaticComplexity} | Max Method: {type.MaxMethodComplexity}");
                    output.AppendLine($"     Uncovered Members: {type.UncoveredMembers.Count}/{type.TotalPublicMembers}");
                    output.AppendLine();
                }
            }

            // Warnings
            if (results.Warnings.Any())
            {
                output.AppendLine("⚠️ Warnings:");
                foreach (var warning in results.Warnings)
                {
                    output.AppendLine($"  - {warning.Message}");
                }
                output.AppendLine();
            }

            return output.ToString();
        }

        // Formatter: Test Coverage - Detailed
        private static string FormatTestCoverageDetailed(TestCoverageResults results, string groupBy)
        {
            var output = new StringBuilder();
            output.AppendLine("🧪 Comprehensive Test Coverage Analysis");
            output.AppendLine();

            // Full statistics
            output.AppendLine("📊 Complete Statistics:");
            output.AppendLine($"  Total Types: {results.TotalTypes}");
            output.AppendLine($"  Tested Types: {results.TestedTypes}");
            output.AppendLine($"  Uncovered Types: {results.UncoveredTypes}");
            output.AppendLine($"  Type Coverage: {results.OverallTypeCoverage:F1}%");
            output.AppendLine();
            output.AppendLine($"  Total Public Members: {results.TotalPublicMembers}");
            output.AppendLine($"  Tested Members: {results.TestedPublicMembers}");
            output.AppendLine($"  Uncovered Members: {results.UncoveredPublicMembers}");
            output.AppendLine($"  Member Coverage: {results.OverallMemberCoverage:F1}%");
            output.AppendLine();
            output.AppendLine($"  Analyzed Projects: {results.AnalyzedProjects}");
            output.AppendLine($"  Failed Projects: {results.FailedProjects}");
            output.AppendLine();

            // Detailed risk analysis
            output.AppendLine("⚠️ Risk Analysis:");
            output.AppendLine($"  🔴 Critical Risk: {results.CriticalRiskTypes} types (high complexity, no tests)");
            output.AppendLine($"  🔶 High Risk: {results.HighRiskTypes} types (medium complexity, no tests)");
            output.AppendLine($"  🟡 Medium Risk: {results.MediumRiskTypes} types");
            output.AppendLine($"  ✅ Low Risk: {results.LowRiskTypes} types (well tested)");
            output.AppendLine();

            // All group statistics
            if (groupBy.ToLower() == "project" && results.ProjectStatistics.Any())
            {
                output.AppendLine($"📦 Coverage by Project ({results.ProjectStatistics.Count} projects):");
                foreach (var stat in results.ProjectStatistics.Values.OrderBy(s => s.TypeCoveragePercentage))
                {
                    var icon = stat.TypeCoveragePercentage >= 80 ? "✅" :
                               stat.TypeCoveragePercentage >= 60 ? "⚠️" : "🔴";
                    output.AppendLine($"  {icon} {stat.Name}:");
                    output.AppendLine($"     Types: {stat.TestedTypes}/{stat.TotalTypes} ({stat.TypeCoveragePercentage:F1}%)");
                    output.AppendLine($"     Members: {stat.TestedPublicMembers}/{stat.TotalPublicMembers} ({stat.MemberCoveragePercentage:F1}%)");
                    output.AppendLine($"     Uncovered: {stat.UncoveredTypes} types, {stat.UncoveredPublicMembers} members");
                    output.AppendLine();
                }
            }
            else if (groupBy.ToLower() == "namespace" && results.NamespaceStatistics.Any())
            {
                output.AppendLine($"📁 Coverage by Namespace ({results.NamespaceStatistics.Count} namespaces):");
                foreach (var stat in results.NamespaceStatistics.Values.OrderBy(s => s.TypeCoveragePercentage))
                {
                    var icon = stat.TypeCoveragePercentage >= 80 ? "✅" :
                               stat.TypeCoveragePercentage >= 60 ? "⚠️" : "🔴";
                    output.AppendLine($"  {icon} {stat.Name}:");
                    output.AppendLine($"     Types: {stat.TestedTypes}/{stat.TotalTypes} ({stat.TypeCoveragePercentage:F1}%)");
                    output.AppendLine($"     Members: {stat.TestedPublicMembers}/{stat.TotalPublicMembers} ({stat.MemberCoveragePercentage:F1}%)");
                    output.AppendLine($"     Uncovered: {stat.UncoveredTypes} types, {stat.UncoveredPublicMembers} members");
                    output.AppendLine();
                }
            }

            // All critical and high risk types
            var criticalHighRiskTypes = results.TypeCoverages
                .Where(t => t.RiskLevel == "Critical" || t.RiskLevel == "High")
                .OrderByDescending(t => t.CyclomaticComplexity)
                .ToList();

            if (criticalHighRiskTypes.Any())
            {
                output.AppendLine($"🔴 All Critical & High-Risk Types ({criticalHighRiskTypes.Count}):");
                foreach (var type in criticalHighRiskTypes)
                {
                    var riskIcon = type.RiskLevel == "Critical" ? "🔴" : "🔶";
                    output.AppendLine($"  {riskIcon} {type.RiskLevel} - {type.FullTypeName}");
                    output.AppendLine($"     Location: {type.FilePath}:{type.LineNumber}");
                    output.AppendLine($"     Project: {type.ProjectName}");
                    output.AppendLine($"     Complexity: Total={type.CyclomaticComplexity}, Max Method={type.MaxMethodComplexity}");
                    output.AppendLine($"     Coverage: {type.TestedPublicMembers}/{type.TotalPublicMembers} members ({type.CoveragePercentage:F1}%)");
                    output.AppendLine($"     Tests: {(type.HasTests ? $"{type.TestCount} tests in {type.TestClasses.Count} test classes" : "No tests found")}");

                    if (type.UncoveredMembers.Any() && type.UncoveredMembers.Count <= 10)
                    {
                        output.AppendLine($"     Uncovered members:");
                        foreach (var member in type.UncoveredMembers)
                        {
                            output.AppendLine($"       - {member.Signature} (complexity: {member.CyclomaticComplexity})");
                        }
                    }
                    else if (type.UncoveredMembers.Count > 10)
                    {
                        output.AppendLine($"     Uncovered members: {type.UncoveredMembers.Count} (too many to list)");
                    }
                    output.AppendLine();
                }
            }

            // Warnings
            if (results.Warnings.Any())
            {
                output.AppendLine("⚠️ Analysis Warnings:");
                foreach (var warning in results.Warnings)
                {
                    output.AppendLine($"  [{warning.Context}] {warning.Message}");
                    if (!string.IsNullOrWhiteSpace(warning.Details))
                    {
                        output.AppendLine($"    Details: {warning.Details}");
                    }
                }
                output.AppendLine();
            }

            return output.ToString();
        }

        // Formatter: Change Impact - Summary
        private static string FormatChangeImpactSummary(ChangeImpactResults results)
        {
            var output = new StringBuilder();
            output.AppendLine($"🔄 Change Impact Summary: {results.TargetSymbol}");
            output.AppendLine();

            // Show warnings if any
            if (results.Warnings.Any())
            {
                output.AppendLine("⚠️ Warnings:");
                foreach (var warning in results.Warnings)
                {
                    output.AppendLine($"  - {warning.Message}");
                }
                output.AppendLine();
                return output.ToString();
            }

            // Symbol info
            output.AppendLine($"Symbol: {results.TargetSymbolFullName}");
            output.AppendLine($"Type: {results.SymbolKind} ({results.Accessibility})");
            if (!string.IsNullOrEmpty(results.ProjectName))
                output.AppendLine($"Location: {results.ProjectName}");
            output.AppendLine();

            // Impact statistics
            output.AppendLine("📊 Impact:");
            output.AppendLine($"  Total References: {results.TotalImpactedSymbols} ({results.DirectReferences} direct, {results.IndirectReferences} indirect)");
            output.AppendLine($"  Impacted Projects: {results.ImpactedProjects}");
            output.AppendLine($"  Impacted Files: {results.ImpactedFiles}");
            output.AppendLine();

            // Risk assessment
            var riskIcon = results.RiskLevel.ToLower() switch
            {
                "critical" => "🔴",
                "high" => "🔶",
                "medium" => "🟡",
                _ => "✅"
            };
            output.AppendLine($"{riskIcon} Risk Level: {results.RiskLevel}");
            if (!string.IsNullOrEmpty(results.RiskReason))
                output.AppendLine($"  Reason: {results.RiskReason}");

            if (results.IsBreakingChange)
            {
                output.AppendLine($"  ⚠️ BREAKING CHANGE");
            }
            output.AppendLine();

            // Top recommendations
            if (results.Recommendations.Any())
            {
                output.AppendLine("💡 Key Recommendations:");
                foreach (var rec in results.Recommendations.Take(3))
                {
                    output.AppendLine($"  {rec}");
                }
                output.AppendLine();
            }

            return output.ToString();
        }

        // Formatter: Change Impact - Normal
        private static string FormatChangeImpactNormal(ChangeImpactResults results)
        {
            var output = new StringBuilder();
            output.AppendLine($"🔄 Change Impact Analysis: {results.TargetSymbol}");
            output.AppendLine();

            // Show warnings if any
            if (results.Warnings.Any())
            {
                output.AppendLine("⚠️ Warnings:");
                foreach (var warning in results.Warnings)
                {
                    output.AppendLine($"  [{warning.Context}] {warning.Message}");
                }
                output.AppendLine();
                return output.ToString();
            }

            // Symbol information
            output.AppendLine("🎯 Target Symbol:");
            output.AppendLine($"  Name: {results.TargetSymbolFullName}");
            output.AppendLine($"  Kind: {results.SymbolKind}");
            output.AppendLine($"  Accessibility: {results.Accessibility}");
            if (!string.IsNullOrEmpty(results.DeclaringType))
                output.AppendLine($"  Declaring Type: {results.DeclaringType}");
            if (!string.IsNullOrEmpty(results.Namespace))
                output.AppendLine($"  Namespace: {results.Namespace}");
            output.AppendLine($"  Location: {results.FilePath}:{results.LineNumber}");
            output.AppendLine();

            // Impact statistics
            output.AppendLine("📊 Impact Statistics:");
            output.AppendLine($"  Total Impacted Symbols: {results.TotalImpactedSymbols}");
            output.AppendLine($"  Direct References: {results.DirectReferences}");
            output.AppendLine($"  Indirect References: {results.IndirectReferences}");
            output.AppendLine($"  Impacted Projects: {results.ImpactedProjects} ({string.Join(", ", results.ImpactedProjectNames.Take(5))}{(results.ImpactedProjectNames.Count > 5 ? ", ..." : "")})");
            output.AppendLine($"  Impacted Files: {results.ImpactedFiles}");
            output.AppendLine();

            // Impact by project
            if (results.ImpactByProject.Any())
            {
                output.AppendLine("📦 Impact by Project:");
                foreach (var kvp in results.ImpactByProject.OrderByDescending(k => k.Value).Take(5))
                {
                    output.AppendLine($"  {kvp.Key}: {kvp.Value} references");
                }
                output.AppendLine();
            }

            // Risk assessment
            var riskIcon = results.RiskLevel.ToLower() switch
            {
                "critical" => "🔴",
                "high" => "🔶",
                "medium" => "🟡",
                _ => "✅"
            };
            output.AppendLine($"{riskIcon} Risk Assessment:");
            output.AppendLine($"  Level: {results.RiskLevel}");
            output.AppendLine($"  Reason: {results.RiskReason}");
            output.AppendLine($"  Public API: {(results.IsPublicAPI ? "Yes" : "No")}");
            output.AppendLine($"  Breaking Change: {(results.IsBreakingChange ? "Yes ⚠️" : "No")}");
            if (results.BreakingChangeReasons.Any())
            {
                foreach (var reason in results.BreakingChangeReasons)
                {
                    output.AppendLine($"    - {reason}");
                }
            }
            output.AppendLine();

            // Dependency chains (top 5)
            if (results.DependencyChains.Any())
            {
                output.AppendLine($"🔗 Dependency Chains (showing top 5 of {results.DependencyChains.Count}):");
                foreach (var chain in results.DependencyChains.Take(5))
                {
                    var chainStr = string.Join(" → ", chain.Chain);
                    var crossProject = chain.CrossesProjectBoundary ? " [CROSS-PROJECT]" : "";
                    output.AppendLine($"  {chainStr}{crossProject}");
                }
                output.AppendLine();
            }

            // High impact symbols (top 10)
            var highImpactSymbols = results.ImpactedSymbols
                .OrderBy(s => s.Distance)
                .ThenBy(s => s.ProjectName)
                .Take(10)
                .ToList();

            if (highImpactSymbols.Any())
            {
                output.AppendLine($"📍 Impacted Locations (showing top 10 of {results.TotalImpactedSymbols}):");
                foreach (var symbol in highImpactSymbols)
                {
                    var distanceStr = symbol.Distance == 0 ? "Direct" : $"Indirect (distance: {symbol.Distance})";
                    output.AppendLine($"  [{distanceStr}] {symbol.FullSymbolName}");
                    output.AppendLine($"    {symbol.FileName}:{symbol.LineNumber} ({symbol.ProjectName})");
                }
                output.AppendLine();
            }

            // Recommendations
            if (results.Recommendations.Any())
            {
                output.AppendLine("💡 Recommendations:");
                foreach (var rec in results.Recommendations)
                {
                    output.AppendLine($"  {rec}");
                }
                output.AppendLine();
            }

            return output.ToString();
        }

        // Formatter: Change Impact - Detailed
        private static string FormatChangeImpactDetailed(ChangeImpactResults results)
        {
            var output = new StringBuilder();
            output.AppendLine($"🔄 Comprehensive Change Impact Analysis");
            output.AppendLine($"Target: {results.TargetSymbol}");
            output.AppendLine();

            // Show warnings if any
            if (results.Warnings.Any())
            {
                output.AppendLine("⚠️ Warnings:");
                foreach (var warning in results.Warnings)
                {
                    output.AppendLine($"  [{warning.Context}] {warning.Message}");
                    if (!string.IsNullOrWhiteSpace(warning.Details))
                    {
                        output.AppendLine($"    Details: {warning.Details}");
                    }
                }
                output.AppendLine();
                return output.ToString();
            }

            // Complete symbol information
            output.AppendLine("🎯 Target Symbol Details:");
            output.AppendLine($"  Full Name: {results.TargetSymbolFullName}");
            output.AppendLine($"  Kind: {results.SymbolKind}");
            output.AppendLine($"  Accessibility: {results.Accessibility}");
            if (!string.IsNullOrEmpty(results.DeclaringType))
                output.AppendLine($"  Declaring Type: {results.DeclaringType}");
            if (!string.IsNullOrEmpty(results.Namespace))
                output.AppendLine($"  Namespace: {results.Namespace}");
            if (!string.IsNullOrEmpty(results.ProjectName))
                output.AppendLine($"  Project: {results.ProjectName}");
            output.AppendLine($"  File: {results.FilePath}");
            output.AppendLine($"  Line: {results.LineNumber}");
            output.AppendLine();

            // Complete impact statistics
            output.AppendLine("📊 Complete Impact Statistics:");
            output.AppendLine($"  Total Impacted Symbols: {results.TotalImpactedSymbols}");
            output.AppendLine($"  Direct References: {results.DirectReferences}");
            output.AppendLine($"  Indirect References: {results.IndirectReferences}");
            output.AppendLine($"  Impacted Projects: {results.ImpactedProjects}");
            output.AppendLine($"  Impacted Files: {results.ImpactedFiles}");
            output.AppendLine();

            // Impact by project (all)
            if (results.ImpactByProject.Any())
            {
                output.AppendLine($"📦 Impact by Project ({results.ImpactByProject.Count} projects):");
                foreach (var kvp in results.ImpactByProject.OrderByDescending(k => k.Value))
                {
                    output.AppendLine($"  {kvp.Key}: {kvp.Value} references");
                }
                output.AppendLine();
            }

            // Impact by symbol kind
            if (results.ImpactByKind.Any())
            {
                output.AppendLine("🔍 Impact by Symbol Kind:");
                foreach (var kvp in results.ImpactByKind.OrderByDescending(k => k.Value))
                {
                    output.AppendLine($"  {kvp.Key}: {kvp.Value}");
                }
                output.AppendLine();
            }

            // Complete risk assessment
            var riskIcon = results.RiskLevel.ToLower() switch
            {
                "critical" => "🔴",
                "high" => "🔶",
                "medium" => "🟡",
                _ => "✅"
            };
            output.AppendLine($"{riskIcon} Risk Assessment:");
            output.AppendLine($"  Risk Level: {results.RiskLevel}");
            output.AppendLine($"  Risk Reason: {results.RiskReason}");
            output.AppendLine($"  Public API: {(results.IsPublicAPI ? "Yes - Visible to external consumers" : "No - Internal use only")}");
            output.AppendLine($"  Breaking Change: {(results.IsBreakingChange ? "Yes ⚠️" : "No")}");
            if (results.BreakingChangeReasons.Any())
            {
                output.AppendLine($"  Breaking Change Reasons:");
                foreach (var reason in results.BreakingChangeReasons)
                {
                    output.AppendLine($"    - {reason}");
                }
            }
            output.AppendLine();

            // All dependency chains
            if (results.DependencyChains.Any())
            {
                output.AppendLine($"🔗 All Dependency Chains ({results.DependencyChains.Count}):");
                foreach (var chain in results.DependencyChains)
                {
                    var chainStr = string.Join(" → ", chain.Chain);
                    var crossProject = chain.CrossesProjectBoundary ? " [CROSS-PROJECT]" : "";
                    var projectStr = $" (Projects: {string.Join(", ", chain.ProjectsInvolved)})";
                    output.AppendLine($"  {chainStr}{crossProject}{projectStr}");
                }
                output.AppendLine();
            }

            // All impacted symbols grouped by project
            if (results.ImpactedSymbols.Any())
            {
                output.AppendLine($"📍 All Impacted Locations ({results.TotalImpactedSymbols}):");
                var byProject = results.ImpactedSymbols.GroupBy(s => s.ProjectName).OrderBy(g => g.Key);
                foreach (var projectGroup in byProject)
                {
                    output.AppendLine($"  Project: {projectGroup.Key}");
                    foreach (var symbol in projectGroup.OrderBy(s => s.Distance).ThenBy(s => s.FilePath))
                    {
                        var distanceStr = symbol.Distance == 0 ? "Direct" : $"Indirect (distance: {symbol.Distance})";
                        output.AppendLine($"    [{distanceStr}] {symbol.FullSymbolName}");
                        output.AppendLine($"      Location: {symbol.FileName}:{symbol.LineNumber}");
                        output.AppendLine($"      Kind: {symbol.SymbolKind}, Impact: {symbol.ImpactType}");
                        if (!string.IsNullOrWhiteSpace(symbol.CodeContext))
                        {
                            output.AppendLine($"      Context: {symbol.CodeContext}");
                        }
                    }
                    output.AppendLine();
                }
            }

            // All recommendations
            if (results.Recommendations.Any())
            {
                output.AppendLine($"💡 All Recommendations ({results.Recommendations.Count}):");
                foreach (var rec in results.Recommendations)
                {
                    output.AppendLine($"  {rec}");
                }
                output.AppendLine();
            }

            return output.ToString();
        }

        // Formatter: Performance Issues - Summary
        private static string FormatPerformanceIssuesSummary(PerformanceIssueResults results)
        {
            var output = new StringBuilder();
            output.AppendLine($"⚡ Performance Issues Summary");
            output.AppendLine();

            // Show warnings if any
            if (results.Warnings.Any())
            {
                output.AppendLine("⚠️ Analysis Warnings:");
                foreach (var warning in results.Warnings)
                {
                    output.AppendLine($"  [{warning.Context}] {warning.Message}");
                }
                return output.ToString();
            }

            output.AppendLine($"📊 Key Metrics:");
            output.AppendLine($"  Total Issues: {results.TotalIssues}");
            output.AppendLine($"  Critical: {results.CriticalIssues}");
            output.AppendLine($"  High: {results.HighIssues}");
            output.AppendLine($"  Medium: {results.MediumIssues}");
            output.AppendLine($"  Projects: {results.AnalyzedProjects}");
            output.AppendLine($"  Files: {results.AnalyzedFiles}");
            output.AppendLine();

            // Top 5 most common issue types
            if (results.IssuesByType.Any())
            {
                output.AppendLine("🔝 Top Issue Types:");
                foreach (var kvp in results.IssuesByType.OrderByDescending(k => k.Value).Take(5))
                {
                    output.AppendLine($"  {kvp.Key}: {kvp.Value}");
                }
            }

            return output.ToString();
        }

        // Formatter: Performance Issues - Normal
        private static string FormatPerformanceIssuesNormal(PerformanceIssueResults results)
        {
            var output = new StringBuilder();
            output.AppendLine($"⚡ Performance Issues Analysis");
            output.AppendLine();

            // Show warnings if any
            if (results.Warnings.Any())
            {
                output.AppendLine("⚠️ Analysis Warnings:");
                foreach (var warning in results.Warnings)
                {
                    output.AppendLine($"  [{warning.Context}] {warning.Message}");
                }
                output.AppendLine();
                return output.ToString();
            }

            // Statistics
            output.AppendLine($"📊 Analysis Statistics:");
            output.AppendLine($"  Total Issues: {results.TotalIssues}");
            output.AppendLine($"  Critical: {results.CriticalIssues}");
            output.AppendLine($"  High: {results.HighIssues}");
            output.AppendLine($"  Medium: {results.MediumIssues}");
            output.AppendLine($"  Low: {results.LowIssues}");
            output.AppendLine($"  Analyzed Projects: {results.AnalyzedProjects}");
            output.AppendLine($"  Analyzed Files: {results.AnalyzedFiles}");
            output.AppendLine();

            // Issues by type
            if (results.IssuesByType.Any())
            {
                output.AppendLine($"📋 Issues by Type:");
                foreach (var kvp in results.IssuesByType.OrderByDescending(k => k.Value))
                {
                    output.AppendLine($"  {kvp.Key}: {kvp.Value}");
                }
                output.AppendLine();
            }

            // Issues by project (top 10)
            if (results.IssuesByProject.Any())
            {
                output.AppendLine($"📦 Issues by Project (top 10):");
                foreach (var kvp in results.IssuesByProject.OrderByDescending(k => k.Value).Take(10))
                {
                    output.AppendLine($"  {kvp.Key}: {kvp.Value}");
                }
                output.AppendLine();
            }

            // Top 10 critical issues
            var criticalIssues = results.Issues.Where(i => i.Severity == "Critical").Take(10).ToList();
            if (criticalIssues.Any())
            {
                output.AppendLine($"🔴 Critical Issues (showing {criticalIssues.Count}):");
                foreach (var issue in criticalIssues)
                {
                    output.AppendLine($"  [{issue.IssueType}] {issue.Title}");
                    output.AppendLine($"    Location: {issue.FileName}:{issue.LineNumber} in {issue.MethodName}()");
                    output.AppendLine($"    Impact: {issue.EstimatedImpact:F1}/10");
                    output.AppendLine($"    Fix: {issue.Recommendation}");
                    output.AppendLine();
                }
            }

            // Top 10 high severity issues
            var highIssues = results.Issues.Where(i => i.Severity == "High").Take(10).ToList();
            if (highIssues.Any())
            {
                output.AppendLine($"🔶 High Severity Issues (showing {highIssues.Count}):");
                foreach (var issue in highIssues)
                {
                    output.AppendLine($"  [{issue.IssueType}] {issue.Title}");
                    output.AppendLine($"    Location: {issue.FileName}:{issue.LineNumber} in {issue.MethodName}()");
                    output.AppendLine($"    Impact: {issue.EstimatedImpact:F1}/10");
                    output.AppendLine($"    Fix: {issue.Recommendation}");
                    output.AppendLine();
                }
            }

            return output.ToString();
        }

        // Formatter: Performance Issues - Detailed
        private static string FormatPerformanceIssuesDetailed(PerformanceIssueResults results)
        {
            var output = new StringBuilder();
            output.AppendLine($"⚡ Comprehensive Performance Issues Analysis");
            output.AppendLine();

            // Show warnings if any
            if (results.Warnings.Any())
            {
                output.AppendLine("⚠️ Analysis Warnings:");
                foreach (var warning in results.Warnings)
                {
                    output.AppendLine($"  [{warning.Context}] {warning.Message}");
                    if (!string.IsNullOrWhiteSpace(warning.Details))
                    {
                        output.AppendLine($"    Details: {warning.Details}");
                    }
                }
                output.AppendLine();
                return output.ToString();
            }

            // Complete statistics
            output.AppendLine($"📊 Complete Analysis Statistics:");
            output.AppendLine($"  Total Issues: {results.TotalIssues}");
            output.AppendLine($"  Critical: {results.CriticalIssues}");
            output.AppendLine($"  High: {results.HighIssues}");
            output.AppendLine($"  Medium: {results.MediumIssues}");
            output.AppendLine($"  Low: {results.LowIssues}");
            output.AppendLine($"  Analyzed Projects: {results.AnalyzedProjects}");
            output.AppendLine($"  Failed Projects: {results.FailedProjects}");
            output.AppendLine($"  Analyzed Files: {results.AnalyzedFiles}");
            output.AppendLine();

            // Complete issues by type
            if (results.IssuesByType.Any())
            {
                output.AppendLine($"📋 All Issues by Type:");
                foreach (var kvp in results.IssuesByType.OrderByDescending(k => k.Value))
                {
                    output.AppendLine($"  {kvp.Key}: {kvp.Value}");
                }
                output.AppendLine();
            }

            // Complete issues by project
            if (results.IssuesByProject.Any())
            {
                output.AppendLine($"📦 All Issues by Project:");
                foreach (var kvp in results.IssuesByProject.OrderByDescending(k => k.Value))
                {
                    output.AppendLine($"  {kvp.Key}: {kvp.Value}");
                }
                output.AppendLine();
            }

            // Group issues by severity and type
            var issuesBySeverity = results.Issues.GroupBy(i => i.Severity).OrderByDescending(g =>
                g.Key == "Critical" ? 4 : g.Key == "High" ? 3 : g.Key == "Medium" ? 2 : 1);

            foreach (var severityGroup in issuesBySeverity)
            {
                var icon = severityGroup.Key switch
                {
                    "Critical" => "🔴",
                    "High" => "🔶",
                    "Medium" => "🟡",
                    _ => "⚪"
                };

                output.AppendLine($"{icon} {severityGroup.Key} Severity Issues ({severityGroup.Count()}):");
                output.AppendLine();

                // Group by issue type within severity
                var byType = severityGroup.GroupBy(i => i.IssueType);
                foreach (var typeGroup in byType)
                {
                    output.AppendLine($"  [{typeGroup.Key}] ({typeGroup.Count()} issues):");
                    foreach (var issue in typeGroup)
                    {
                        output.AppendLine($"    • {issue.Title}");
                        output.AppendLine($"      Location: {issue.FilePath}:{issue.LineNumber}");
                        output.AppendLine($"      Project: {issue.ProjectName}");
                        output.AppendLine($"      Method: {issue.MethodName}()");
                        output.AppendLine($"      Description: {issue.Description}");
                        output.AppendLine($"      Code: {issue.CodeSnippet}");
                        output.AppendLine($"      Recommendation: {issue.Recommendation}");
                        output.AppendLine($"      Fix Example: {issue.FixExample}");
                        output.AppendLine($"      Estimated Impact: {issue.EstimatedImpact:F1}/10");
                        output.AppendLine();
                    }
                }
            }

            // Top files by issue count
            if (results.IssuesByFile.Any())
            {
                output.AppendLine($"📄 Files with Most Issues (top 20):");
                foreach (var kvp in results.IssuesByFile.OrderByDescending(k => k.Value).Take(20))
                {
                    output.AppendLine($"  {Path.GetFileName(kvp.Key)}: {kvp.Value} issues");
                    output.AppendLine($"    Path: {kvp.Key}");
                }
                output.AppendLine();
            }

            // Summary recommendations by issue type
            var recommendations = results.Issues
                .GroupBy(i => i.IssueType)
                .Select(g => new { Type = g.Key, Count = g.Count(), Example = g.First() })
                .OrderByDescending(x => x.Count);

            output.AppendLine($"💡 Recommendations Summary:");
            foreach (var rec in recommendations)
            {
                output.AppendLine($"  [{rec.Type}] ({rec.Count} occurrences)");
                output.AppendLine($"    General Fix: {rec.Example.Recommendation}");
                output.AppendLine($"    Example: {rec.Example.FixExample}");
                output.AppendLine();
            }

            return output.ToString();
        }

        // Formatter: Naming Conventions - Summary
        private static string FormatNamingConventionsSummary(NamingConventionResults results)
        {
            var output = new StringBuilder();
            output.AppendLine($"📏 Naming Conventions Summary");
            output.AppendLine();

            // Show warnings if any
            if (results.Warnings.Any())
            {
                output.AppendLine("⚠️ Analysis Warnings:");
                foreach (var warning in results.Warnings)
                {
                    output.AppendLine($"  [{warning.Context}] {warning.Message}");
                }
                return output.ToString();
            }

            output.AppendLine($"📊 Key Metrics:");
            output.AppendLine($"  Analyzed Symbols: {results.AnalyzedSymbols:N0}");
            output.AppendLine($"  Violations: {results.TotalViolations:N0}");
            output.AppendLine($"  Compliance Score: {results.ComplianceScore:F1}%");
            output.AppendLine($"  High Severity: {results.HighSeverityViolations}");
            output.AppendLine($"  Medium Severity: {results.MediumSeverityViolations}");
            output.AppendLine($"  Low Severity: {results.LowSeverityViolations}");
            output.AppendLine();

            // Top 5 violation types
            if (results.ViolationsByType.Any())
            {
                output.AppendLine("🔝 Top Violation Types:");
                foreach (var kvp in results.ViolationsByType.OrderByDescending(k => k.Value).Take(5))
                {
                    output.AppendLine($"  {kvp.Key}: {kvp.Value}");
                }
            }

            return output.ToString();
        }

        // Formatter: Naming Conventions - Normal
        private static string FormatNamingConventionsNormal(NamingConventionResults results)
        {
            var output = new StringBuilder();
            output.AppendLine($"📏 Naming Convention Analysis");
            output.AppendLine();

            // Show warnings if any
            if (results.Warnings.Any())
            {
                output.AppendLine("⚠️ Analysis Warnings:");
                foreach (var warning in results.Warnings)
                {
                    output.AppendLine($"  [{warning.Context}] {warning.Message}");
                }
                output.AppendLine();
                return output.ToString();
            }

            // Statistics
            output.AppendLine($"📊 Analysis Statistics:");
            output.AppendLine($"  Analyzed Symbols: {results.AnalyzedSymbols:N0}");
            output.AppendLine($"  Total Violations: {results.TotalViolations:N0}");
            output.AppendLine($"  Compliance Score: {results.ComplianceScore:F1}%");
            output.AppendLine($"  High Severity: {results.HighSeverityViolations}");
            output.AppendLine($"  Medium Severity: {results.MediumSeverityViolations}");
            output.AppendLine($"  Low Severity: {results.LowSeverityViolations}");
            output.AppendLine($"  Analyzed Projects: {results.AnalyzedProjects}");
            output.AppendLine($"  Analyzed Files: {results.AnalyzedFiles}");
            output.AppendLine();

            // Violations by type
            if (results.ViolationsByType.Any())
            {
                output.AppendLine($"📋 Violations by Type:");
                foreach (var kvp in results.ViolationsByType.OrderByDescending(k => k.Value))
                {
                    output.AppendLine($"  {kvp.Key}: {kvp.Value}");
                }
                output.AppendLine();
            }

            // Violations by symbol kind
            if (results.ViolationsBySymbolKind.Any())
            {
                output.AppendLine($"🔤 Violations by Symbol Kind:");
                foreach (var kvp in results.ViolationsBySymbolKind.OrderByDescending(k => k.Value))
                {
                    output.AppendLine($"  {kvp.Key}: {kvp.Value}");
                }
                output.AppendLine();
            }

            // Top 10 high severity violations
            var highViolations = results.Violations.Where(v => v.Severity == "High").Take(10).ToList();
            if (highViolations.Any())
            {
                output.AppendLine($"🔴 High Severity Violations (showing {highViolations.Count}):");
                foreach (var violation in highViolations)
                {
                    output.AppendLine($"  [{violation.ViolationType}] {violation.SymbolKind}: {violation.CurrentName}");
                    output.AppendLine($"    Location: {violation.FileName}:{violation.LineNumber}");
                    output.AppendLine($"    Expected: {violation.ExpectedConvention}");
                    output.AppendLine($"    Suggested: {violation.SuggestedName}");
                    output.AppendLine($"    Reason: {violation.Reason}");
                    output.AppendLine();
                }
            }

            // Top 10 medium severity violations
            var mediumViolations = results.Violations.Where(v => v.Severity == "Medium").Take(10).ToList();
            if (mediumViolations.Any())
            {
                output.AppendLine($"🟡 Medium Severity Violations (showing {mediumViolations.Count}):");
                foreach (var violation in mediumViolations)
                {
                    output.AppendLine($"  [{violation.ViolationType}] {violation.SymbolKind}: {violation.CurrentName}");
                    output.AppendLine($"    Location: {violation.FileName}:{violation.LineNumber}");
                    output.AppendLine($"    Suggested: {violation.SuggestedName}");
                    output.AppendLine();
                }
            }

            return output.ToString();
        }

        // Formatter: Naming Conventions - Detailed
        private static string FormatNamingConventionsDetailed(NamingConventionResults results)
        {
            var output = new StringBuilder();
            output.AppendLine($"📏 Comprehensive Naming Convention Analysis");
            output.AppendLine();

            // Show warnings if any
            if (results.Warnings.Any())
            {
                output.AppendLine("⚠️ Analysis Warnings:");
                foreach (var warning in results.Warnings)
                {
                    output.AppendLine($"  [{warning.Context}] {warning.Message}");
                    if (!string.IsNullOrWhiteSpace(warning.Details))
                    {
                        output.AppendLine($"    Details: {warning.Details}");
                    }
                }
                output.AppendLine();
                return output.ToString();
            }

            // Complete statistics
            output.AppendLine($"📊 Complete Analysis Statistics:");
            output.AppendLine($"  Analyzed Symbols: {results.AnalyzedSymbols:N0}");
            output.AppendLine($"  Total Violations: {results.TotalViolations:N0}");
            output.AppendLine($"  Compliance Score: {results.ComplianceScore:F1}%");
            output.AppendLine($"  High Severity: {results.HighSeverityViolations}");
            output.AppendLine($"  Medium Severity: {results.MediumSeverityViolations}");
            output.AppendLine($"  Low Severity: {results.LowSeverityViolations}");
            output.AppendLine($"  Analyzed Projects: {results.AnalyzedProjects}");
            output.AppendLine($"  Failed Projects: {results.FailedProjects}");
            output.AppendLine($"  Analyzed Files: {results.AnalyzedFiles}");
            output.AppendLine();

            // Complete violations by type
            if (results.ViolationsByType.Any())
            {
                output.AppendLine($"📋 All Violations by Type:");
                foreach (var kvp in results.ViolationsByType.OrderByDescending(k => k.Value))
                {
                    output.AppendLine($"  {kvp.Key}: {kvp.Value}");
                }
                output.AppendLine();
            }

            // Complete violations by symbol kind
            if (results.ViolationsBySymbolKind.Any())
            {
                output.AppendLine($"🔤 All Violations by Symbol Kind:");
                foreach (var kvp in results.ViolationsBySymbolKind.OrderByDescending(k => k.Value))
                {
                    output.AppendLine($"  {kvp.Key}: {kvp.Value}");
                }
                output.AppendLine();
            }

            // Complete violations by project
            if (results.ViolationsByProject.Any())
            {
                output.AppendLine($"📦 All Violations by Project:");
                foreach (var kvp in results.ViolationsByProject.OrderByDescending(k => k.Value))
                {
                    output.AppendLine($"  {kvp.Key}: {kvp.Value}");
                }
                output.AppendLine();
            }

            // Group violations by severity and type
            var violationsBySeverity = results.Violations.GroupBy(v => v.Severity).OrderByDescending(g =>
                g.Key == "High" ? 3 : g.Key == "Medium" ? 2 : 1);

            foreach (var severityGroup in violationsBySeverity)
            {
                var icon = severityGroup.Key switch
                {
                    "High" => "🔴",
                    "Medium" => "🟡",
                    _ => "⚪"
                };

                output.AppendLine($"{icon} {severityGroup.Key} Severity Violations ({severityGroup.Count()}):");
                output.AppendLine();

                // Group by violation type within severity
                var byType = severityGroup.GroupBy(v => v.ViolationType);
                foreach (var typeGroup in byType)
                {
                    output.AppendLine($"  [{typeGroup.Key}] ({typeGroup.Count()} violations):");
                    foreach (var violation in typeGroup)
                    {
                        output.AppendLine($"    • {violation.SymbolKind}: {violation.CurrentName}");
                        output.AppendLine($"      Location: {violation.FilePath}:{violation.LineNumber}");
                        output.AppendLine($"      Project: {violation.ProjectName}");
                        output.AppendLine($"      Accessibility: {violation.Accessibility}");
                        if (!string.IsNullOrWhiteSpace(violation.DeclaringType))
                        {
                            output.AppendLine($"      Declaring Type: {violation.DeclaringType}");
                        }
                        if (!string.IsNullOrWhiteSpace(violation.Namespace))
                        {
                            output.AppendLine($"      Namespace: {violation.Namespace}");
                        }
                        output.AppendLine($"      Expected Convention: {violation.ExpectedConvention}");
                        output.AppendLine($"      Suggested Name: {violation.SuggestedName}");
                        output.AppendLine($"      Reason: {violation.Reason}");
                        output.AppendLine();
                    }
                }
            }

            // Top files by violation count
            if (results.ViolationsByFile.Any())
            {
                output.AppendLine($"📄 Files with Most Violations (top 20):");
                foreach (var kvp in results.ViolationsByFile.OrderByDescending(k => k.Value).Take(20))
                {
                    output.AppendLine($"  {Path.GetFileName(kvp.Key)}: {kvp.Value} violations");
                    output.AppendLine($"    Path: {kvp.Key}");
                }
                output.AppendLine();
            }

            // Convention recommendations summary
            var conventionSummary = results.Violations
                .GroupBy(v => v.ViolationType)
                .Select(g => new
                {
                    Type = g.Key,
                    Count = g.Count(),
                    Example = g.First()
                })
                .OrderByDescending(x => x.Count);

            output.AppendLine($"💡 Convention Guidelines Summary:");
            foreach (var convention in conventionSummary)
            {
                output.AppendLine($"  [{convention.Type}] ({convention.Count} violations)");
                output.AppendLine($"    Convention: {convention.Example.ExpectedConvention}");
                output.AppendLine($"    Guideline: {convention.Example.Reason}");
                output.AppendLine();
            }

            return output.ToString();
        }

        // Formatter: API Changes - Summary
        private static string FormatAPIChangesSummary(APIChangeResults results)
        {
            var output = new StringBuilder();
            output.AppendLine($"🔄 API Changes Summary");
            output.AppendLine($"Comparing: {results.OldVersionLabel} → {results.NewVersionLabel}");
            output.AppendLine();

            // Show warnings if any
            if (results.Warnings.Any())
            {
                output.AppendLine("⚠️ Analysis Warnings:");
                foreach (var warning in results.Warnings)
                {
                    output.AppendLine($"  [{warning.Context}] {warning.Message}");
                }
                return output.ToString();
            }

            output.AppendLine($"📊 Key Metrics:");
            output.AppendLine($"  Total Changes: {results.TotalChanges}");
            output.AppendLine($"  Breaking Changes: {results.BreakingChanges}");
            output.AppendLine($"  Non-Breaking Changes: {results.NonBreakingChanges}");
            output.AppendLine($"  Added Symbols: {results.AddedSymbols}");
            output.AppendLine($"  Removed Symbols: {results.RemovedSymbols}");
            output.AppendLine($"  Modified Symbols: {results.ModifiedSymbols}");
            output.AppendLine();

            // Semantic versioning recommendation
            var versionIcon = results.RecommendedVersionBump switch
            {
                "Major" => "🔴",
                "Minor" => "🟡",
                "Patch" => "🟢",
                _ => "⚪"
            };
            output.AppendLine($"{versionIcon} Versioning Recommendation:");
            output.AppendLine($"  {results.RecommendedVersionBump} version bump");
            output.AppendLine($"  Reason: {results.VersioningReason}");

            return output.ToString();
        }

        // Formatter: API Changes - Normal
        private static string FormatAPIChangesNormal(APIChangeResults results)
        {
            var output = new StringBuilder();
            output.AppendLine($"🔄 API Change Analysis");
            output.AppendLine($"Comparing: {results.OldVersionLabel} → {results.NewVersionLabel}");
            output.AppendLine();

            // Show warnings if any
            if (results.Warnings.Any())
            {
                output.AppendLine("⚠️ Analysis Warnings:");
                foreach (var warning in results.Warnings)
                {
                    output.AppendLine($"  [{warning.Context}] {warning.Message}");
                }
                output.AppendLine();
                return output.ToString();
            }

            // Statistics
            output.AppendLine($"📊 Analysis Statistics:");
            output.AppendLine($"  Total Changes: {results.TotalChanges}");
            output.AppendLine($"  Breaking Changes: {results.BreakingChanges}");
            output.AppendLine($"  Non-Breaking Changes: {results.NonBreakingChanges}");
            output.AppendLine($"  Internal Changes: {results.InternalChanges}");
            output.AppendLine($"  Added Symbols: {results.AddedSymbols}");
            output.AppendLine($"  Removed Symbols: {results.RemovedSymbols}");
            output.AppendLine($"  Modified Symbols: {results.ModifiedSymbols}");
            output.AppendLine($"  Analyzed Old Symbols: {results.AnalyzedOldSymbols:N0}");
            output.AppendLine($"  Analyzed New Symbols: {results.AnalyzedNewSymbols:N0}");
            output.AppendLine();

            // Semantic versioning recommendation
            var versionIcon = results.RecommendedVersionBump switch
            {
                "Major" => "🔴",
                "Minor" => "🟡",
                "Patch" => "🟢",
                _ => "⚪"
            };
            output.AppendLine($"{versionIcon} Versioning Recommendation:");
            output.AppendLine($"  Recommended Bump: {results.RecommendedVersionBump}");
            output.AppendLine($"  Reason: {results.VersioningReason}");
            output.AppendLine();

            // Changes by type
            if (results.ChangesByType.Any())
            {
                output.AppendLine($"📋 Changes by Type:");
                foreach (var kvp in results.ChangesByType.OrderByDescending(k => k.Value))
                {
                    output.AppendLine($"  {kvp.Key}: {kvp.Value}");
                }
                output.AppendLine();
            }

            // Changes by symbol kind
            if (results.ChangesBySymbolKind.Any())
            {
                output.AppendLine($"🔤 Changes by Symbol Kind:");
                foreach (var kvp in results.ChangesBySymbolKind.OrderByDescending(k => k.Value))
                {
                    output.AppendLine($"  {kvp.Key}: {kvp.Value}");
                }
                output.AppendLine();
            }

            // Top 10 critical/breaking changes
            var breakingChanges = results.Changes
                .Where(c => c.ImpactLevel == "Breaking")
                .OrderByDescending(c => c.Severity == "Critical" ? 2 : 1)
                .Take(10)
                .ToList();

            if (breakingChanges.Any())
            {
                output.AppendLine($"🔴 Breaking Changes (showing {breakingChanges.Count}):");
                foreach (var change in breakingChanges)
                {
                    var severityIcon = change.Severity == "Critical" ? "🔴" : "🟠";
                    output.AppendLine($"  {severityIcon} [{change.ChangeType}] {change.SymbolKind}: {change.SymbolName}");
                    output.AppendLine($"    {change.Description}");
                    if (!string.IsNullOrWhiteSpace(change.OldSignature))
                    {
                        output.AppendLine($"    Old: {change.OldSignature}");
                    }
                    if (!string.IsNullOrWhiteSpace(change.NewSignature))
                    {
                        output.AppendLine($"    New: {change.NewSignature}");
                    }
                    output.AppendLine($"    Migration: {change.MigrationGuidance}");
                    output.AppendLine();
                }
            }

            // Top 10 additions
            var additions = results.Changes
                .Where(c => c.ChangeType == "Added")
                .Take(10)
                .ToList();

            if (additions.Any())
            {
                output.AppendLine($"✅ New Additions (showing {additions.Count}):");
                foreach (var change in additions)
                {
                    output.AppendLine($"  + {change.SymbolKind}: {change.SymbolName}");
                    if (!string.IsNullOrWhiteSpace(change.NewSignature))
                    {
                        output.AppendLine($"    Signature: {change.NewSignature}");
                    }
                    output.AppendLine();
                }
            }

            return output.ToString();
        }

        // Formatter: API Changes - Detailed
        private static string FormatAPIChangesDetailed(APIChangeResults results)
        {
            var output = new StringBuilder();
            output.AppendLine($"🔄 Comprehensive API Change Analysis");
            output.AppendLine($"Comparing: {results.OldVersionLabel} → {results.NewVersionLabel}");
            output.AppendLine($"Old Version: {results.OldVersionPath}");
            output.AppendLine($"New Version: {results.NewVersionPath}");
            output.AppendLine();

            // Show warnings if any
            if (results.Warnings.Any())
            {
                output.AppendLine("⚠️ Analysis Warnings:");
                foreach (var warning in results.Warnings)
                {
                    output.AppendLine($"  [{warning.Context}] {warning.Message}");
                    if (!string.IsNullOrWhiteSpace(warning.Details))
                    {
                        output.AppendLine($"    Details: {warning.Details}");
                    }
                }
                output.AppendLine();
                return output.ToString();
            }

            // Complete statistics
            output.AppendLine($"📊 Complete Analysis Statistics:");
            output.AppendLine($"  Total Changes: {results.TotalChanges}");
            output.AppendLine($"  Breaking Changes: {results.BreakingChanges}");
            output.AppendLine($"  Non-Breaking Changes: {results.NonBreakingChanges}");
            output.AppendLine($"  Internal Changes: {results.InternalChanges}");
            output.AppendLine($"  Added Symbols: {results.AddedSymbols}");
            output.AppendLine($"  Removed Symbols: {results.RemovedSymbols}");
            output.AppendLine($"  Modified Symbols: {results.ModifiedSymbols}");
            output.AppendLine($"  Critical Severity: {results.CriticalChanges}");
            output.AppendLine($"  High Severity: {results.HighSeverityChanges}");
            output.AppendLine($"  Medium Severity: {results.MediumSeverityChanges}");
            output.AppendLine($"  Low Severity: {results.LowSeverityChanges}");
            output.AppendLine($"  Analyzed Old Symbols: {results.AnalyzedOldSymbols:N0}");
            output.AppendLine($"  Analyzed New Symbols: {results.AnalyzedNewSymbols:N0}");
            output.AppendLine();

            // Semantic versioning recommendation
            var versionIcon = results.RecommendedVersionBump switch
            {
                "Major" => "🔴",
                "Minor" => "🟡",
                "Patch" => "🟢",
                _ => "⚪"
            };
            output.AppendLine($"{versionIcon} Semantic Versioning Recommendation:");
            output.AppendLine($"  Recommended Bump: {results.RecommendedVersionBump}");
            output.AppendLine($"  Reason: {results.VersioningReason}");
            output.AppendLine();

            // Complete changes by type
            if (results.ChangesByType.Any())
            {
                output.AppendLine($"📋 All Changes by Type:");
                foreach (var kvp in results.ChangesByType.OrderByDescending(k => k.Value))
                {
                    output.AppendLine($"  {kvp.Key}: {kvp.Value}");
                }
                output.AppendLine();
            }

            // Complete changes by symbol kind
            if (results.ChangesBySymbolKind.Any())
            {
                output.AppendLine($"🔤 All Changes by Symbol Kind:");
                foreach (var kvp in results.ChangesBySymbolKind.OrderByDescending(k => k.Value))
                {
                    output.AppendLine($"  {kvp.Key}: {kvp.Value}");
                }
                output.AppendLine();
            }

            // Changes by namespace
            if (results.ChangesByNamespace.Any())
            {
                output.AppendLine($"📦 Changes by Namespace:");
                foreach (var kvp in results.ChangesByNamespace.OrderByDescending(k => k.Value))
                {
                    output.AppendLine($"  {kvp.Key}: {kvp.Value}");
                }
                output.AppendLine();
            }

            // Group changes by impact level and change type
            var changesByImpact = results.Changes.GroupBy(c => c.ImpactLevel).OrderByDescending(g =>
                g.Key == "Breaking" ? 3 : g.Key == "NonBreaking" ? 2 : 1);

            foreach (var impactGroup in changesByImpact)
            {
                var icon = impactGroup.Key switch
                {
                    "Breaking" => "🔴",
                    "NonBreaking" => "🟢",
                    _ => "⚪"
                };

                output.AppendLine($"{icon} {impactGroup.Key} Changes ({impactGroup.Count()}):");
                output.AppendLine();

                // Group by change type within impact level
                var byType = impactGroup.GroupBy(c => c.ChangeType);
                foreach (var typeGroup in byType)
                {
                    output.AppendLine($"  [{typeGroup.Key}] ({typeGroup.Count()} changes):");
                    foreach (var change in typeGroup)
                    {
                        output.AppendLine($"    • {change.SymbolKind}: {change.FullSymbolName}");
                        output.AppendLine($"      Severity: {change.Severity}");
                        output.AppendLine($"      Description: {change.Description}");

                        if (!string.IsNullOrWhiteSpace(change.OldSignature))
                        {
                            output.AppendLine($"      Old Signature: {change.OldSignature}");
                        }
                        if (!string.IsNullOrWhiteSpace(change.NewSignature))
                        {
                            output.AppendLine($"      New Signature: {change.NewSignature}");
                        }
                        if (!string.IsNullOrWhiteSpace(change.OldAccessibility))
                        {
                            output.AppendLine($"      Accessibility: {change.OldAccessibility} → {change.NewAccessibility}");
                        }
                        if (!string.IsNullOrWhiteSpace(change.Namespace))
                        {
                            output.AppendLine($"      Namespace: {change.Namespace}");
                        }
                        if (!string.IsNullOrWhiteSpace(change.DeclaringType))
                        {
                            output.AppendLine($"      Declaring Type: {change.DeclaringType}");
                        }

                        output.AppendLine($"      Migration Guidance: {change.MigrationGuidance}");

                        if (change.AffectedAreas.Any())
                        {
                            output.AppendLine($"      Affected Areas: {string.Join(", ", change.AffectedAreas)}");
                        }

                        output.AppendLine();
                    }
                }
            }

            // Migration summary
            output.AppendLine($"📝 Migration Summary:");
            if (results.BreakingChanges > 0)
            {
                output.AppendLine($"  ⚠️ {results.BreakingChanges} breaking change(s) require code updates");
                output.AppendLine($"  • Review all removed and modified APIs");
                output.AppendLine($"  • Update consumer code to use new signatures");
                output.AppendLine($"  • Test thoroughly before deployment");
            }
            if (results.AddedSymbols > 0)
            {
                output.AppendLine($"  ✅ {results.AddedSymbols} new API(s) available");
                output.AppendLine($"  • Update documentation with new features");
                output.AppendLine($"  • Consider deprecation notices for replaced APIs");
            }
            if (results.TotalChanges == 0)
            {
                output.AppendLine($"  ✅ No API changes detected - versions are API-compatible");
            }

            return output.ToString();
        }

        #endregion

        #region Phase 1 Tools

        [McpServerTool, Description("Find magic numbers and hardcoded literals that should be extracted as constants")]
        public static async Task<string> FindMagicNumbers(
            [Description("Path to solution file (.sln)")] string solutionPath,
            [Description("Output format: summary (counts only), normal (grouped list), detailed (with suggestions). Default: normal")]
            string format = "normal",
            [Description("Include string literals (default: true)")] bool includeStrings = true,
            [Description("Include numeric literals (default: true)")] bool includeNumbers = true,
            [Description("Minimum string length to consider (default: 3)")] int minStringLength = 3,
            IServiceProvider? serviceProvider = null)
        {
            try
            {
                var phase1Service = serviceProvider?.GetService(typeof(Phase1AnalysisService)) as Phase1AnalysisService;
                if (phase1Service == null)
                {
                    return "Error: Phase1AnalysisService not available. Please ensure the service is registered.";
                }

                var results = await phase1Service.FindMagicNumbersAsync(
                    solutionPath,
                    includeStrings,
                    includeNumbers,
                    minStringLength);

                return FormatMagicNumberResults(results, format);
            }
            catch (Exception ex)
            {
                return $"Error: An unexpected error occurred while finding magic numbers: {ex.Message}";
            }
        }

        [McpServerTool, Description("Find code smells and anti-patterns in the solution")]
        public static async Task<string> FindCodeSmells(
            [Description("Path to solution file (.sln)")] string solutionPath,
            [Description("Output format: summary (counts only), normal (grouped list), detailed (with metrics). Default: normal")]
            string format = "normal",
            [Description("Comma-separated smell types: LongMethod, LargeClass, LongParameterList, FeatureEnvy, DataClumps, PrimitiveObsession, SwitchStatements, SpeculativeGenerality, MessageChains, MiddleMan. Default: all")]
            string smellTypes = "all",
            [Description("Severity filter: High, Medium, Low, All (default: All)")] string severity = "All",
            IServiceProvider? serviceProvider = null)
        {
            try
            {
                var phase1Service = serviceProvider?.GetService(typeof(Phase1AnalysisService)) as Phase1AnalysisService;
                if (phase1Service == null)
                {
                    return "Error: Phase1AnalysisService not available. Please ensure the service is registered.";
                }

                var smellTypeArray = smellTypes.Equals("all", StringComparison.OrdinalIgnoreCase)
                    ? new[] { "LongMethod", "LargeClass", "LongParameterList", "FeatureEnvy", "DataClumps", "PrimitiveObsession", "SwitchStatements", "SpeculativeGenerality", "MessageChains", "MiddleMan" }
                    : smellTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                var results = await phase1Service.FindCodeSmellsAsync(solutionPath, smellTypeArray, severity);

                return FormatCodeSmellResults(results, format);
            }
            catch (Exception ex)
            {
                return $"Error: An unexpected error occurred while finding code smells: {ex.Message}";
            }
        }

        [McpServerTool, Description("Analyze architecture layer violations based on defined rules (Clean Architecture, DDD, etc.)")]
        public static async Task<string> AnalyzeLayerViolations(
            [Description("Path to solution file (.sln)")] string solutionPath,
            [Description("JSON string defining layers and rules. Example: {\"layers\":[{\"name\":\"Presentation\",\"projects\":[\"*.Web\"]},{\"name\":\"Domain\",\"projects\":[\"*.Domain\"]}],\"rules\":[{\"from\":\"Presentation\",\"to\":\"Domain\",\"allowed\":true}]}")]
            string layerDefinitionsJson,
            [Description("Output format: summary (counts only), normal (grouped list), detailed (with recommendations). Default: normal")]
            string format = "normal",
            IServiceProvider? serviceProvider = null)
        {
            try
            {
                var phase1Service = serviceProvider?.GetService(typeof(Phase1AnalysisService)) as Phase1AnalysisService;
                if (phase1Service == null)
                {
                    return "Error: Phase1AnalysisService not available. Please ensure the service is registered.";
                }

                var results = await phase1Service.AnalyzeLayerViolationsAsync(solutionPath, layerDefinitionsJson);

                return FormatLayerViolationResults(results, format);
            }
            catch (Exception ex)
            {
                return $"Error: An unexpected error occurred while analyzing layer violations: {ex.Message}";
            }
        }

        [McpServerTool, Description("Safely rename a symbol with preview and conflict detection")]
        public static async Task<string> RenameSymbolSafely(
            [Description("Current symbol name to rename")] string symbolName,
            [Description("New name for the symbol")] string newName,
            [Description("Path to solution file (.sln)")] string solutionPath,
            [Description("Preview only (true) or execute rename (false). Default: true")] bool previewOnly = true,
            IServiceProvider? serviceProvider = null)
        {
            try
            {
                var phase1Service = serviceProvider?.GetService(typeof(Phase1AnalysisService)) as Phase1AnalysisService;
                if (phase1Service == null)
                {
                    return "Error: Phase1AnalysisService not available. Please ensure the service is registered.";
                }

                var results = await phase1Service.RenameSymbolAsync(solutionPath, symbolName, newName, previewOnly);

                return FormatRenameSymbolResults(results);
            }
            catch (Exception ex)
            {
                return $"Error: An unexpected error occurred while renaming symbol: {ex.Message}";
            }
        }

        #endregion

        #region Phase 1 Formatters

        private static string FormatMagicNumberResults(MagicNumberResults results, string format)
        {
            var output = new StringBuilder();

            output.AppendLine($"# Magic Numbers Analysis\n");
            output.AppendLine($"📊 Summary:");
            output.AppendLine($"  • Total magic numbers: {results.TotalMagicNumbers}");
            output.AppendLine($"  • Numeric literals: {results.NumericLiterals}");
            output.AppendLine($"  • String literals: {results.StringLiterals}");
            output.AppendLine($"  • Analyzed projects: {results.AnalyzedProjects}");
            output.AppendLine($"  • Analyzed files: {results.AnalyzedFiles}");

            if (results.FailedProjects > 0)
            {
                output.AppendLine($"  ⚠️ Failed projects: {results.FailedProjects}");
            }

            output.AppendLine($"\n🎯 Priority:");
            output.AppendLine($"  • High priority: {results.HighPriority} (used 3+ times)");
            output.AppendLine($"  • Medium priority: {results.MediumPriority} (used 2 times)");
            output.AppendLine($"  • Low priority: {results.LowPriority} (used once)");
            output.AppendLine();

            if (format == "summary")
                return output.ToString();

            // Group by type
            var byType = results.MagicNumbers.GroupBy(m => m.Type);

            foreach (var typeGroup in byType)
            {
                output.AppendLine($"## {typeGroup.Key} Literals ({typeGroup.Count()})\n");

                // Group by value to show duplicates
                var byValue = typeGroup.GroupBy(m => m.Value)
                    .OrderByDescending(g => g.Count())
                    .ThenBy(g => g.Key);

                int shown = 0;
                foreach (var valueGroup in byValue)
                {
                    if (shown >= 50 && format == "normal") break; // Limit in normal mode

                    var first = valueGroup.First();
                    var count = valueGroup.Count();
                    var priority = count >= 3 ? "🔴 High" : count >= 2 ? "🟡 Medium" : "🟢 Low";

                    output.AppendLine($"### Value: `{first.Value}` ({count} occurrence{(count > 1 ? "s" : "")}) - {priority}\n");

                    if (format == "detailed")
                    {
                        output.AppendLine($"**Suggested constant:** `{first.SuggestedConstantName}`\n");
                    }

                    // Show first few occurrences
                    int occurrenceShown = 0;
                    foreach (var magic in valueGroup.OrderBy(m => m.ProjectName).ThenBy(m => m.FilePath))
                    {
                        if (occurrenceShown >= 3 && format == "normal") break;

                        output.AppendLine($"  • {magic.FileName}:{magic.LineNumber} in `{magic.ContainingType}.{magic.ContainingMember}`");
                        if (format == "detailed")
                        {
                            output.AppendLine($"    ```csharp");
                            output.AppendLine($"    {magic.CodeContext}");
                            output.AppendLine($"    ```");
                        }

                        occurrenceShown++;
                        shown++;
                    }

                    if (valueGroup.Count() > occurrenceShown)
                    {
                        output.AppendLine($"    ... and {valueGroup.Count() - occurrenceShown} more occurrence(s)");
                    }

                    output.AppendLine();
                }

                if (byValue.Count() > shown)
                {
                    output.AppendLine($"*... and {byValue.Count() - shown} more unique values*\n");
                }
            }

            // Warnings
            if (results.Warnings.Any())
            {
                output.AppendLine($"## ⚠️ Warnings ({results.Warnings.Count})\n");
                foreach (var warning in results.Warnings.Take(10))
                {
                    output.AppendLine($"  • {warning.Context}: {warning.Message}");
                }
                if (results.Warnings.Count > 10)
                {
                    output.AppendLine($"  ... and {results.Warnings.Count - 10} more warnings");
                }
            }

            return output.ToString();
        }

        private static string FormatCodeSmellResults(CodeSmellResults results, string format)
        {
            var output = new StringBuilder();

            output.AppendLine($"# Code Smells Analysis\n");
            output.AppendLine($"📊 Summary:");
            output.AppendLine($"  • Total smells: {results.TotalSmells}");
            output.AppendLine($"  • High severity: {results.HighSeverity} 🔴");
            output.AppendLine($"  • Medium severity: {results.MediumSeverity} 🟡");
            output.AppendLine($"  • Low severity: {results.LowSeverity} 🟢");
            output.AppendLine($"  • Analyzed projects: {results.AnalyzedProjects}");
            output.AppendLine($"  • Analyzed files: {results.AnalyzedFiles}");
            output.AppendLine($"  • Analyzed symbols: {results.AnalyzedSymbols}");

            if (results.FailedProjects > 0)
            {
                output.AppendLine($"  ⚠️ Failed projects: {results.FailedProjects}");
            }

            output.AppendLine();

            if (results.SmellsByType.Any())
            {
                output.AppendLine($"📈 By Type:");
                foreach (var kvp in results.SmellsByType.OrderByDescending(k => k.Value))
                {
                    output.AppendLine($"  • {kvp.Key}: {kvp.Value}");
                }
                output.AppendLine();
            }

            if (format == "summary" || !results.Smells.Any())
            {
                if (!results.Smells.Any())
                {
                    output.AppendLine("✅ No code smells detected! Your code looks clean.");
                }
                return output.ToString();
            }

            // Group by smell type for better organization
            var smellsByType = results.Smells.GroupBy(s => s.SmellType);

            foreach (var typeGroup in smellsByType.OrderByDescending(g => g.Count()))
            {
                output.AppendLine($"## {typeGroup.Key} ({typeGroup.Count()} occurrences)\n");

                // Group by severity within type
                var bySeverity = typeGroup.GroupBy(s => s.Severity)
                    .OrderByDescending(g => g.Key == "High" ? 3 : g.Key == "Medium" ? 2 : 1);

                foreach (var severityGroup in bySeverity)
                {
                    var icon = severityGroup.Key == "High" ? "🔴" : severityGroup.Key == "Medium" ? "🟡" : "🟢";
                    output.AppendLine($"### {icon} {severityGroup.Key} Severity ({severityGroup.Count()})\n");

                    int shown = 0;
                    foreach (var smell in severityGroup.OrderBy(s => s.ProjectName).ThenBy(s => s.FileName))
                    {
                        if (format == "normal" && shown >= 10) break; // Limit in normal mode

                        output.AppendLine($"**{smell.Title}**");
                        output.AppendLine($"  • Location: `{smell.FileName}:{smell.LineNumber}` in `{smell.SymbolName}`");
                        output.AppendLine($"  • Project: {smell.ProjectName}");

                        if (format == "detailed")
                        {
                            output.AppendLine($"  • Description: {smell.Description}");

                            if (smell.Metrics.Any())
                            {
                                output.AppendLine($"  • Metrics:");
                                foreach (var metric in smell.Metrics)
                                {
                                    output.AppendLine($"    - {metric.Key}: {metric.Value}");
                                }
                            }

                            output.AppendLine($"  • Recommendation: {smell.Recommendation}");

                            if (!string.IsNullOrEmpty(smell.CodeSnippet))
                            {
                                output.AppendLine($"  • Code:");
                                output.AppendLine($"    ```csharp");
                                output.AppendLine($"    {smell.CodeSnippet}");
                                output.AppendLine($"    ```");
                            }
                        }

                        output.AppendLine();
                        shown++;
                    }

                    if (format == "normal" && severityGroup.Count() > shown)
                    {
                        output.AppendLine($"*... and {severityGroup.Count() - shown} more {severityGroup.Key.ToLower()} severity issues*\n");
                    }
                }
            }

            // Warnings section
            if (results.Warnings.Any())
            {
                output.AppendLine($"## ⚠️ Warnings ({results.Warnings.Count})\n");
                foreach (var warning in results.Warnings.Take(10))
                {
                    output.AppendLine($"  • {warning.Context}: {warning.Message}");
                }
                if (results.Warnings.Count > 10)
                {
                    output.AppendLine($"  ... and {results.Warnings.Count - 10} more warnings");
                }
                output.AppendLine();
            }

            // Summary recommendations
            if (format == "detailed" && results.Smells.Any())
            {
                output.AppendLine($"## 💡 Quick Recommendations\n");

                if (results.SmellsByType.ContainsKey("LongMethod"))
                {
                    output.AppendLine($"• **Long Methods**: Extract smaller, well-named methods. Aim for <20 lines per method.");
                }

                if (results.SmellsByType.ContainsKey("LargeClass"))
                {
                    output.AppendLine($"• **Large Classes**: Apply Single Responsibility Principle. Split into focused classes.");
                }

                if (results.SmellsByType.ContainsKey("LongParameterList"))
                {
                    output.AppendLine($"• **Long Parameter Lists**: Use Parameter Object pattern or Builder pattern.");
                }

                if (results.SmellsByType.ContainsKey("FeatureEnvy"))
                {
                    output.AppendLine($"• **Feature Envy**: Move methods to the classes they use most (Move Method refactoring).");
                }

                if (results.SmellsByType.ContainsKey("DataClumps"))
                {
                    output.AppendLine($"• **Data Clumps**: Create value objects or DTOs for repeated parameter groups.");
                }

                if (results.SmellsByType.ContainsKey("PrimitiveObsession"))
                {
                    output.AppendLine($"• **Primitive Obsession**: Introduce domain-specific value objects instead of primitives.");
                }

                if (results.SmellsByType.ContainsKey("SwitchStatements"))
                {
                    output.AppendLine($"• **Switch Statements**: Consider polymorphism (Strategy, State, or Command patterns).");
                }

                if (results.SmellsByType.ContainsKey("MessageChains"))
                {
                    output.AppendLine($"• **Message Chains**: Use Hide Delegate pattern to reduce coupling.");
                }

                if (results.SmellsByType.ContainsKey("MiddleMan"))
                {
                    output.AppendLine($"• **Middle Man**: Remove unnecessary delegation or add real behavior.");
                }

                if (results.SmellsByType.ContainsKey("SpeculativeGenerality"))
                {
                    output.AppendLine($"• **Speculative Generality**: Remove unused abstractions. Follow YAGNI principle.");
                }
            }

            return output.ToString();
        }

        private static string FormatLayerViolationResults(LayerViolationResults results, string format)
        {
            var output = new StringBuilder();

            output.AppendLine($"# Architecture Layer Violations\n");

            // Summary section
            output.AppendLine($"📊 Summary:");
            output.AppendLine($"  • Total violations: {results.TotalViolations}");
            output.AppendLine($"  • Critical: {results.CriticalViolations} 🔴");
            output.AppendLine($"  • High severity: {results.HighSeverityViolations} 🟠");
            output.AppendLine($"  • Medium severity: {results.MediumSeverityViolations} 🟡");
            output.AppendLine($"  • Compliance score: {results.ComplianceScore:F1}% {(results.ComplianceScore >= 90 ? "✅" : results.ComplianceScore >= 70 ? "⚠️" : "❌")}");
            output.AppendLine($"  • Analyzed projects: {results.AnalyzedProjects}");
            output.AppendLine();

            // Layer definitions
            if (results.Layers.Any())
            {
                output.AppendLine($"🏗️ Architecture Layers:");
                foreach (var layer in results.Layers)
                {
                    var projectCount = layer.MatchedProjects.Count;
                    output.AppendLine($"  • {layer.Name}: {projectCount} project(s)");
                    if (format == "detailed" && projectCount > 0)
                    {
                        foreach (var project in layer.MatchedProjects)
                        {
                            output.AppendLine($"    - {project}");
                        }
                    }
                }
                output.AppendLine();
            }

            // Violations by type
            if (results.ViolationsByType.Any())
            {
                output.AppendLine($"📈 Violations by Type:");
                foreach (var kvp in results.ViolationsByType.OrderByDescending(x => x.Value))
                {
                    output.AppendLine($"  • {kvp.Key}: {kvp.Value}");
                }
                output.AppendLine();
            }

            if (format == "summary" || !results.Violations.Any())
                return output.ToString();

            // Violations section
            output.AppendLine($"## Violations\n");

            // Group by severity
            var criticalViolations = results.Violations.Where(v => v.Severity == "Critical").ToList();
            var highViolations = results.Violations.Where(v => v.Severity == "High").ToList();
            var mediumViolations = results.Violations.Where(v => v.Severity == "Medium").ToList();

            // Critical violations
            if (criticalViolations.Any())
            {
                output.AppendLine($"### 🔴 Critical Violations ({criticalViolations.Count})\n");
                var displayCount = format == "detailed" ? criticalViolations.Count : Math.Min(10, criticalViolations.Count);
                for (int i = 0; i < displayCount; i++)
                {
                    var v = criticalViolations[i];
                    output.AppendLine($"**{i + 1}. {v.ViolationType}**");
                    output.AppendLine($"  - **Description:** {v.Description}");
                    if (v.ViolatingReferences.Any())
                    {
                        output.AppendLine($"  - **Cycle:** {string.Join(" → ", v.ViolatingReferences)} → {v.ViolatingReferences.First()}");
                    }
                    output.AppendLine($"  - **Recommendation:** {v.Recommendation}");
                    output.AppendLine();
                }
                if (format != "detailed" && criticalViolations.Count > 10)
                {
                    output.AppendLine($"*...and {criticalViolations.Count - 10} more critical violations*\n");
                }
            }

            // High severity violations
            if (highViolations.Any())
            {
                output.AppendLine($"### 🟠 High Severity Violations ({highViolations.Count})\n");
                var displayCount = format == "detailed" ? highViolations.Count : Math.Min(10, highViolations.Count);
                for (int i = 0; i < displayCount; i++)
                {
                    var v = highViolations[i];
                    output.AppendLine($"**{i + 1}. {v.ViolationType}: {v.FromLayer} → {v.ToLayer}**");
                    output.AppendLine($"  - **Projects:** {v.FromProject} → {v.ToProject}");
                    output.AppendLine($"  - **Description:** {v.Description}");
                    if (format == "detailed")
                    {
                        output.AppendLine($"  - **Recommendation:** {v.Recommendation}");
                    }
                    output.AppendLine();
                }
                if (format != "detailed" && highViolations.Count > 10)
                {
                    output.AppendLine($"*...and {highViolations.Count - 10} more high severity violations*\n");
                }
            }

            // Medium severity violations
            if (mediumViolations.Any())
            {
                output.AppendLine($"### 🟡 Medium Severity Violations ({mediumViolations.Count})\n");
                var displayCount = format == "detailed" ? mediumViolations.Count : Math.Min(5, mediumViolations.Count);
                for (int i = 0; i < displayCount; i++)
                {
                    var v = mediumViolations[i];
                    output.AppendLine($"**{i + 1}. {v.ViolationType}: {v.FromLayer} → {v.ToLayer}**");
                    output.AppendLine($"  - **Projects:** {v.FromProject} → {v.ToProject}");
                    if (format == "detailed")
                    {
                        output.AppendLine($"  - **Description:** {v.Description}");
                        output.AppendLine($"  - **Recommendation:** {v.Recommendation}");
                    }
                    output.AppendLine();
                }
                if (format != "detailed" && mediumViolations.Count > 5)
                {
                    output.AppendLine($"*...and {mediumViolations.Count - 5} more medium severity violations*\n");
                }
            }

            // Recommendations section for detailed format
            if (format == "detailed" && results.Violations.Any())
            {
                output.AppendLine($"## 💡 Quick Recommendations\n");

                if (criticalViolations.Any())
                {
                    output.AppendLine($"### Circular Dependencies");
                    output.AppendLine($"- Break circular dependencies immediately - they prevent proper testing and deployment");
                    output.AppendLine($"- Use Dependency Inversion Principle (define interfaces in lower layers)");
                    output.AppendLine($"- Consider extracting shared code to a common layer");
                    output.AppendLine();
                }

                if (highViolations.Any())
                {
                    output.AppendLine($"### Direct Dependency Violations");
                    output.AppendLine($"- Review architectural rules - ensure they match your intended design");
                    output.AppendLine($"- Refactor violating code to follow the layered architecture");
                    output.AppendLine($"- Use dependency injection to reverse dependencies where needed");
                    output.AppendLine();
                }

                if (mediumViolations.Any())
                {
                    output.AppendLine($"### Undefined Dependencies");
                    output.AppendLine($"- Define explicit rules for all layer-to-layer dependencies");
                    output.AppendLine($"- Document your architecture constraints in the layer definitions JSON");
                    output.AppendLine($"- Consider whether these dependencies should be allowed or refactored");
                    output.AppendLine();
                }
            }

            // Warnings
            if (results.Warnings.Any())
            {
                output.AppendLine($"## ⚠️ Warnings\n");
                foreach (var warning in results.Warnings)
                {
                    output.AppendLine($"- **{warning.Context}:** {warning.Message}");
                }
                output.AppendLine();
            }

            return output.ToString();
        }

        private static string FormatRenameSymbolResults(RenameSymbolResults results)
        {
            var output = new StringBuilder();

            output.AppendLine($"# Rename Symbol: {results.Target.CurrentName} → {results.Target.NewName}\n");

            // Error handling
            if (!results.Success && !string.IsNullOrEmpty(results.ErrorMessage))
            {
                output.AppendLine($"❌ **Error:** {results.ErrorMessage}\n");
                if (results.HasConflicts)
                {
                    output.AppendLine($"## Conflicts\n");
                    foreach (var conflict in results.Conflicts)
                    {
                        output.AppendLine($"- {conflict}");
                    }
                    output.AppendLine();
                }
                return output.ToString();
            }

            // Summary section
            output.AppendLine($"📊 Summary:");
            output.AppendLine($"  • Mode: {(results.IsPreview ? "Preview (no changes made) 👁️" : "Executed (changes applied) ✅")}");
            output.AppendLine($"  • Symbol: {results.Target.SymbolKind} in {results.Target.ProjectName}");
            output.AppendLine($"  • Total locations: {results.TotalLocations}");
            output.AppendLine($"  • Files affected: {results.FilesAffected}");
            output.AppendLine($"  • Projects affected: {results.ProjectsAffected}");

            var riskEmoji = results.RiskLevel switch
            {
                "Critical" => "🔴",
                "High" => "🟠",
                "Medium" => "🟡",
                _ => "🟢"
            };
            output.AppendLine($"  • Risk level: {results.RiskLevel} {riskEmoji} ({results.RiskReason})");
            output.AppendLine();

            // Target information
            output.AppendLine($"## Target Symbol\n");
            output.AppendLine($"- **Kind:** {results.Target.SymbolKind}");
            output.AppendLine($"- **Current name:** {results.Target.CurrentName}");
            output.AppendLine($"- **New name:** {results.Target.NewName}");
            output.AppendLine($"- **Full name:** {results.Target.FullName}");
            output.AppendLine($"- **Location:** {results.Target.FilePath}:{results.Target.LineNumber}");
            output.AppendLine();

            // Conflicts
            if (results.HasConflicts)
            {
                output.AppendLine($"## ⚠️ Conflicts Detected ({results.Conflicts.Count})\n");
                foreach (var conflict in results.Conflicts)
                {
                    output.AppendLine($"- {conflict}");
                }
                output.AppendLine();
                output.AppendLine($"**Action Required:** Resolve conflicts before executing rename.\n");
            }

            // Warnings
            if (results.Warnings.Any())
            {
                output.AppendLine($"## ⚠️ Warnings ({results.Warnings.Count})\n");
                foreach (var warning in results.Warnings)
                {
                    output.AppendLine($"- {warning}");
                }
                output.AppendLine();
            }

            // File changes
            if (results.FileChanges.Any())
            {
                output.AppendLine($"## File Changes ({results.FilesAffected} files)\n");

                foreach (var fileChange in results.FileChanges.Take(20))
                {
                    output.AppendLine($"### {fileChange.FileName} ({fileChange.ChangeCount} {(fileChange.ChangeCount == 1 ? "change" : "changes")})\n");
                    output.AppendLine($"**Path:** `{fileChange.FilePath}`\n");

                    // Group by definition vs references
                    var definition = fileChange.Locations.Where(l => l.IsDefinition).ToList();
                    var references = fileChange.Locations.Where(l => !l.IsDefinition).ToList();

                    if (definition.Any())
                    {
                        output.AppendLine($"**Definition:**");
                        foreach (var loc in definition)
                        {
                            output.AppendLine($"- Line {loc.LineNumber}: `{loc.LineContext.Trim()}`");
                        }
                        output.AppendLine();
                    }

                    if (references.Any())
                    {
                        output.AppendLine($"**References ({references.Count}):**");
                        var displayCount = Math.Min(5, references.Count);
                        for (int i = 0; i < displayCount; i++)
                        {
                            var loc = references[i];
                            output.AppendLine($"- Line {loc.LineNumber}, Col {loc.ColumnNumber}: `{loc.LineContext.Trim()}`");
                        }
                        if (references.Count > 5)
                        {
                            output.AppendLine($"  *...and {references.Count - 5} more references*");
                        }
                        output.AppendLine();
                    }
                }

                if (results.FileChanges.Count > 20)
                {
                    output.AppendLine($"*...and {results.FileChanges.Count - 20} more files*\n");
                }
            }

            // Recommendations
            output.AppendLine($"## 💡 Recommendations\n");

            if (results.IsPreview)
            {
                output.AppendLine($"### Next Steps (Preview Mode)");
                output.AppendLine($"1. **Review all changes** above to ensure correctness");
                output.AppendLine($"2. **Check for semantic issues** - ensure new name makes sense");
                output.AppendLine($"3. **Verify no conflicts** - resolve any naming conflicts first");

                if (!results.HasConflicts)
                {
                    output.AppendLine($"4. **Execute rename** - set `previewOnly=false` to apply changes");
                }
                else
                {
                    output.AppendLine($"4. **Resolve conflicts** - fix naming conflicts before executing");
                }
                output.AppendLine();
            }
            else
            {
                output.AppendLine($"### Post-Rename Actions");
                output.AppendLine($"1. **Rebuild solution** - ensure all projects compile");
                output.AppendLine($"2. **Run tests** - verify functionality is preserved");
                output.AppendLine($"3. **Check version control** - review changes before committing");
                output.AppendLine($"4. **Update documentation** - if the symbol is part of public API");
                output.AppendLine();
            }

            // Risk-specific recommendations
            if (results.RiskLevel == "Critical" || results.RiskLevel == "High")
            {
                output.AppendLine($"### Risk Mitigation ({results.RiskLevel} Risk)");

                if (results.Target.SymbolKind == "NamedType")
                {
                    output.AppendLine($"- Type renames affect many parts of codebase");
                    output.AppendLine($"- Consider incremental rename in phases");
                }

                if (results.TotalLocations > 100)
                {
                    output.AppendLine($"- Large number of references - consider breaking into smaller renames");
                    output.AppendLine($"- Run tests frequently during rename process");
                }

                if (results.ProjectsAffected > 5)
                {
                    output.AppendLine($"- Multi-project impact - coordinate with team");
                    output.AppendLine($"- Ensure all dependent projects are updated together");
                }

                if (results.RiskReason.Contains("public API"))
                {
                    output.AppendLine($"- Public API change - consider versioning and deprecation");
                    output.AppendLine($"- Update public documentation and release notes");
                }

                output.AppendLine();
            }

            // Success message
            if (results.Success)
            {
                if (results.IsPreview)
                {
                    output.AppendLine($"**Preview completed successfully!** Review changes above before executing.");
                }
                else
                {
                    output.AppendLine($"**Rename executed successfully!** ✅ Remember to rebuild and test.");
                }
            }

            return output.ToString();
        }

        #endregion

    }
}