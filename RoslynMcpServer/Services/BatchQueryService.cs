using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace RoslynMcpServer.Services
{
    public class QuerySpec
    {
        public string Tool { get; set; } = string.Empty;
        public Dictionary<string, object> Parameters { get; set; } = new();
    }

    public class BatchQueryResult
    {
        public string Tool { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string Result { get; set; } = string.Empty;
        public string? Error { get; set; }
    }

    public class BatchQueryService
    {
        private readonly SymbolSearchService _symbolSearchService;
        private readonly CodeMetricsService _codeMetricsService;
        private readonly DependencyGraphService _dependencyGraphService;
        private readonly CallHierarchyService _callHierarchyService;
        private readonly CodeAnalysisService _codeAnalysisService;
        private readonly ILogger<BatchQueryService> _logger;

        public BatchQueryService(
            SymbolSearchService symbolSearchService,
            CodeMetricsService codeMetricsService,
            DependencyGraphService dependencyGraphService,
            CallHierarchyService callHierarchyService,
            CodeAnalysisService codeAnalysisService,
            ILogger<BatchQueryService> logger)
        {
            _symbolSearchService = symbolSearchService;
            _codeMetricsService = codeMetricsService;
            _dependencyGraphService = dependencyGraphService;
            _callHierarchyService = callHierarchyService;
            _codeAnalysisService = codeAnalysisService;
            _logger = logger;
        }

        public async Task<string> ExecuteBatchAsync(
            string queriesJson,
            bool parallel = true)
        {
            try
            {
                // Parse the queries
                var queries = JsonSerializer.Deserialize<List<QuerySpec>>(queriesJson);
                if (queries == null || !queries.Any())
                {
                    return "Error: No queries provided or invalid JSON format.";
                }

                List<BatchQueryResult> results;

                if (parallel)
                {
                    // Execute queries in parallel
                    var tasks = queries.Select(q => ExecuteQueryAsync(q));
                    results = (await Task.WhenAll(tasks)).ToList();
                }
                else
                {
                    // Execute queries sequentially
                    results = new List<BatchQueryResult>();
                    foreach (var query in queries)
                    {
                        results.Add(await ExecuteQueryAsync(query));
                    }
                }

                return FormatBatchResults(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing batch query");
                return $"Error: Failed to execute batch query: {ex.Message}";
            }
        }

        private async Task<BatchQueryResult> ExecuteQueryAsync(QuerySpec query)
        {
            try
            {
                var result = query.Tool.ToLower() switch
                {
                    "searchsymbols" => await ExecuteSearchSymbols(query.Parameters),
                    "findreferences" => await ExecuteFindReferences(query.Parameters),
                    "getsymbolinfo" => await ExecuteGetSymbolInfo(query.Parameters),
                    "getcodemetrics" => await ExecuteGetCodeMetrics(query.Parameters),
                    "getdependencygraph" => await ExecuteGetDependencyGraph(query.Parameters),
                    "getcallhierarchy" => await ExecuteGetCallHierarchy(query.Parameters),
                    "analyzedependencies" => await ExecuteAnalyzeDependencies(query.Parameters),
                    _ => $"Error: Unknown tool '{query.Tool}'"
                };

                return new BatchQueryResult
                {
                    Tool = query.Tool,
                    Success = !result.StartsWith("Error:"),
                    Result = result
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error executing query for tool {query.Tool}");
                return new BatchQueryResult
                {
                    Tool = query.Tool,
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        private async Task<string> ExecuteSearchSymbols(Dictionary<string, object> parameters)
        {
            var solutionPath = GetParameter<string>(parameters, "solutionPath");
            var searchPattern = GetParameter<string>(parameters, "searchPattern");
            var symbolKind = GetParameter<string>(parameters, "symbolKind", "");
            var ignoreCase = GetParameter<bool>(parameters, "ignoreCase", true);

            if (string.IsNullOrEmpty(solutionPath) || string.IsNullOrEmpty(searchPattern))
            {
                return "Error: solutionPath and searchPattern are required.";
            }

            var results = await _symbolSearchService.SearchSymbolsAsync(
                searchPattern,
                solutionPath,
                symbolKind,
                ignoreCase);

            return FormatSearchResults(results);
        }

        private string FormatSearchResults(IEnumerable<RoslynMcpServer.Models.SymbolSearchResult> results)
        {
            if (!results.Any())
                return "No symbols found.";

            var grouped = results.GroupBy(r => r.Category);
            var builder = new StringBuilder();

            builder.AppendLine($"Found {results.Count()} symbols:");
            builder.AppendLine();

            foreach (var group in grouped.OrderBy(g => g.Key))
            {
                builder.AppendLine($"{group.Key} ({group.Count()}):");
                foreach (var result in group.Take(20))
                {
                    builder.AppendLine($"  • {result.Name} in {result.Location}");
                    if (!string.IsNullOrEmpty(result.Summary))
                        builder.AppendLine($"    {result.Summary}");
                }
                if (group.Count() > 20)
                    builder.AppendLine($"    ... and {group.Count() - 20} more");
                builder.AppendLine();
            }

            return builder.ToString();
        }

        private async Task<string> ExecuteFindReferences(Dictionary<string, object> parameters)
        {
            var solutionPath = GetParameter<string>(parameters, "solutionPath");
            var symbolName = GetParameter<string>(parameters, "symbolName");
            var includeDefinition = GetParameter<bool>(parameters, "includeDefinition", true);

            if (string.IsNullOrEmpty(solutionPath) || string.IsNullOrEmpty(symbolName))
            {
                return "Error: solutionPath and symbolName are required.";
            }

            var results = await _symbolSearchService.FindReferencesAsync(
                symbolName,
                solutionPath,
                includeDefinition);

            return FormatReferences(results);
        }

        private string FormatReferences(IEnumerable<RoslynMcpServer.Models.ReferenceResult> results)
        {
            if (!results.Any())
                return "No references found.";

            var builder = new StringBuilder();
            var groupedByFile = results.GroupBy(r => r.DocumentPath).OrderBy(g => g.Key);
            var totalCount = results.Count();
            var symbolName = results.First().SymbolName;

            builder.AppendLine($"Found {totalCount} references to '{symbolName}':");
            builder.AppendLine();

            foreach (var fileGroup in groupedByFile)
            {
                var fileName = Path.GetFileName(fileGroup.Key);
                builder.AppendLine($"📄 {fileName} ({fileGroup.Count()} references):");
                foreach (var reference in fileGroup.Take(10))
                {
                    builder.AppendLine($"  Line {reference.LineNumber}: {reference.LineText}");
                }
                if (fileGroup.Count() > 10)
                    builder.AppendLine($"  ... and {fileGroup.Count() - 10} more");
                builder.AppendLine();
            }

            return builder.ToString();
        }

        private async Task<string> ExecuteGetSymbolInfo(Dictionary<string, object> parameters)
        {
            var solutionPath = GetParameter<string>(parameters, "solutionPath");
            var symbolName = GetParameter<string>(parameters, "symbolName");

            if (string.IsNullOrEmpty(solutionPath) || string.IsNullOrEmpty(symbolName))
            {
                return "Error: solutionPath and symbolName are required.";
            }

            var symbolInfo = await _symbolSearchService.GetSymbolInfoAsync(symbolName, solutionPath);
            if (symbolInfo == null)
            {
                return $"Symbol '{symbolName}' not found.";
            }

            return FormatSymbolInfo(symbolInfo);
        }

        private string FormatSymbolInfo(RoslynMcpServer.Models.SymbolInfo symbolInfo)
        {
            var builder = new StringBuilder();

            builder.AppendLine($"Symbol: {symbolInfo.Name}");
            builder.AppendLine($"Type: {symbolInfo.Kind}");
            if (!string.IsNullOrEmpty(symbolInfo.DeclaringType))
                builder.AppendLine($"Declaring Type: {symbolInfo.DeclaringType}");
            if (!string.IsNullOrEmpty(symbolInfo.Namespace))
                builder.AppendLine($"Namespace: {symbolInfo.Namespace}");
            builder.AppendLine($"Accessibility: {symbolInfo.Accessibility}");
            if (!string.IsNullOrEmpty(symbolInfo.Documentation))
                builder.AppendLine($"Documentation: {symbolInfo.Documentation}");
            if (!string.IsNullOrEmpty(symbolInfo.SourceLocation))
                builder.AppendLine($"Location: {symbolInfo.SourceLocation}");

            return builder.ToString();
        }

        private async Task<string> ExecuteGetCodeMetrics(Dictionary<string, object> parameters)
        {
            var solutionPath = GetParameter<string>(parameters, "solutionPath");
            var groupBy = GetParameter<string>(parameters, "groupBy", "project");

            if (string.IsNullOrEmpty(solutionPath))
            {
                return "Error: solutionPath is required.";
            }

            return await _codeMetricsService.GetMetricsAsync(solutionPath, groupBy);
        }

        private async Task<string> ExecuteGetDependencyGraph(Dictionary<string, object> parameters)
        {
            var solutionPath = GetParameter<string>(parameters, "solutionPath");
            var format = GetParameter<string>(parameters, "format", "text");
            var includePackages = GetParameter<bool>(parameters, "includePackages", false);

            if (string.IsNullOrEmpty(solutionPath))
            {
                return "Error: solutionPath is required.";
            }

            return await _dependencyGraphService.GetDependencyGraphAsync(
                solutionPath,
                format,
                includePackages);
        }

        private async Task<string> ExecuteGetCallHierarchy(Dictionary<string, object> parameters)
        {
            var solutionPath = GetParameter<string>(parameters, "solutionPath");
            var methodName = GetParameter<string>(parameters, "methodName");
            var direction = GetParameter<string>(parameters, "direction", "both");
            var maxDepth = GetParameter<int>(parameters, "maxDepth", 3);

            if (string.IsNullOrEmpty(solutionPath) || string.IsNullOrEmpty(methodName))
            {
                return "Error: solutionPath and methodName are required.";
            }

            return await _callHierarchyService.GetCallHierarchyAsync(
                solutionPath,
                methodName,
                direction,
                maxDepth);
        }

        private async Task<string> ExecuteAnalyzeDependencies(Dictionary<string, object> parameters)
        {
            var solutionPath = GetParameter<string>(parameters, "solutionPath");
            var maxDepth = GetParameter<int>(parameters, "maxDepth", 3);

            if (string.IsNullOrEmpty(solutionPath))
            {
                return "Error: solutionPath is required.";
            }

            return FormatDependencyAnalysis(
                await _codeAnalysisService.AnalyzeDependenciesAsync(solutionPath, maxDepth));
        }

        private T GetParameter<T>(Dictionary<string, object> parameters, string key, T defaultValue = default!)
        {
            if (!parameters.ContainsKey(key))
            {
                return defaultValue;
            }

            var value = parameters[key];

            if (value is JsonElement jsonElement)
            {
                return jsonElement.Deserialize<T>() ?? defaultValue;
            }

            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to convert parameter '{Key}' to type {Type}, using default value", key, typeof(T).Name);
                return defaultValue;
            }
        }

        private string FormatBatchResults(List<BatchQueryResult> results)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"Batch Query Results ({results.Count} queries)");
            builder.AppendLine(new string('=', 60));
            builder.AppendLine();

            int successCount = results.Count(r => r.Success);
            int failureCount = results.Count - successCount;

            for (int i = 0; i < results.Count; i++)
            {
                var result = results[i];
                builder.AppendLine($"Query {i + 1}: {result.Tool}");
                builder.AppendLine(new string('-', 60));

                if (result.Success)
                {
                    builder.AppendLine(result.Result);
                }
                else
                {
                    builder.AppendLine($"❌ Error: {result.Error ?? "Unknown error"}");
                }

                builder.AppendLine();
            }

            builder.AppendLine(new string('=', 60));
            builder.AppendLine($"Summary: {successCount} succeeded, {failureCount} failed");

            return builder.ToString();
        }

        private string FormatDependencyAnalysis(object dependencies)
        {
            // This method would format the dependency analysis result
            // For now, just return the ToString representation
            return dependencies?.ToString() ?? "No dependency information available";
        }
    }
}
