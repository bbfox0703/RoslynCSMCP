using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;
using System.Text;

namespace RoslynMcpServer.Core.Services
{
    public class CallHierarchyResult
    {
        public string MethodName { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public int MaxDepth { get; set; }
        public List<CallInfo> Callers { get; set; } = new();
        public List<CallInfo> Callees { get; set; } = new();
        public int TotalCallers { get; set; }
        public int TotalCallees { get; set; }
        public bool CallersTruncated { get; set; }
        public bool CalleesTruncated { get; set; }
    }

    public class CallInfo
    {
        public string MethodName { get; set; } = string.Empty;
        public string ContainingType { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public int CallCount { get; set; }
        public int Depth { get; set; }  // 1 = direct caller/callee of the analyzed method
        public bool IsRecursive { get; set; }  // Already on the path from the analyzed method, so not expanded again
        public bool IsRepeated { get; set; }  // Expanded elsewhere in the tree, so not expanded again
        public List<CallInfo> Children { get; set; } = new();
    }

    public class CallHierarchyService
    {
        /// <summary>Largest maxDepth honored; larger values are clamped</summary>
        public const int MaxAllowedDepth = 10;

        /// <summary>Maximum entries listed per direction before the tree is truncated</summary>
        public const int MaxEntriesPerDirection = 200;

        private readonly CodeAnalysisService _codeAnalysisService;
        private readonly ILogger<CallHierarchyService> _logger;

        public CallHierarchyService(
            CodeAnalysisService codeAnalysisService,
            ILogger<CallHierarchyService> logger)
        {
            _codeAnalysisService = codeAnalysisService;
            _logger = logger;
        }

        public async Task<string> GetCallHierarchyAsync(
            string solutionPath,
            string methodName,
            string direction = "both",
            int maxDepth = 3)
        {
            var normalizedDirection = string.IsNullOrWhiteSpace(direction) ? "both" : direction.Trim().ToLowerInvariant();
            if (normalizedDirection is not ("both" or "callers" or "callees"))
            {
                return $"Error: Invalid direction '{direction}'. Use both, callers, or callees.";
            }

            var depth = Math.Clamp(maxDepth, 1, MaxAllowedDepth);

            try
            {
                var solution = await _codeAnalysisService.GetSolutionAsync(solutionPath);

                // Find the target method
                var targetMethod = await FindMethodSymbol(solution, methodName);
                if (targetMethod == null)
                {
                    return $"Method '{methodName}' not found in solution.";
                }

                var result = new CallHierarchyResult
                {
                    MethodName = targetMethod.Name,
                    FullName = targetMethod.ToDisplayString(),
                    FilePath = targetMethod.Locations.FirstOrDefault()?.SourceTree?.FilePath ?? "",
                    LineNumber = targetMethod.Locations.FirstOrDefault()?.GetLineSpan().StartLinePosition.Line + 1 ?? 0,
                    MaxDepth = depth
                };

                // Analyze callers (who calls this method)
                if (normalizedDirection == "both" || normalizedDirection == "callers")
                {
                    var tree = await BuildTreeAsync(targetMethod, depth, symbol => FindDirectCallersAsync(solution, symbol));
                    result.Callers = tree.Roots;
                    result.TotalCallers = tree.Count;
                    result.CallersTruncated = tree.Truncated;
                }

                // Analyze callees (what this method calls)
                if (normalizedDirection == "both" || normalizedDirection == "callees")
                {
                    var tree = await BuildTreeAsync(targetMethod, depth, symbol => FindDirectCalleesAsync(solution, symbol));
                    result.Callees = tree.Roots;
                    result.TotalCallees = tree.Count;
                    result.CalleesTruncated = tree.Truncated;
                }

                return FormatCallHierarchy(result, normalizedDirection);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting call hierarchy");
                throw;
            }
        }

        private async Task<IMethodSymbol?> FindMethodSymbol(Solution solution, string methodName)
        {
            foreach (var project in solution.Projects)
            {
                if (!project.SupportsCompilation)
                    continue;

                var compilation = await project.GetCompilationAsync();
                if (compilation == null)
                    continue;

                foreach (var document in project.Documents)
                {
                    var syntaxTree = await document.GetSyntaxTreeAsync();
                    if (syntaxTree == null)
                        continue;

                    var root = await syntaxTree.GetRootAsync();
                    var methodNodes = root.DescendantNodes()
                        .OfType<MethodDeclarationSyntax>()
                        .Where(m => m.Identifier.Text == methodName);

                    foreach (var methodNode in methodNodes)
                    {
                        var semanticModel = await document.GetSemanticModelAsync();
                        if (semanticModel == null)
                            continue;

                        var methodSymbol = semanticModel.GetDeclaredSymbol(methodNode) as IMethodSymbol;
                        if (methodSymbol != null)
                        {
                            return methodSymbol;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Builds a call tree breadth-first, so that direct callers/callees are always listed before the
        /// entry budget is spent on deeper levels. Each symbol is expanded once: later occurrences are marked
        /// as repeated, and occurrences already on the path from the root are marked as recursive.
        /// </summary>
        private static async Task<(List<CallInfo> Roots, int Count, bool Truncated)> BuildTreeAsync(
            ISymbol root,
            int maxDepth,
            Func<ISymbol, Task<List<(ISymbol Symbol, CallInfo Info)>>> findDirectRelations)
        {
            var roots = new List<CallInfo>();
            var rootKey = GetSymbolKey(root);
            var expanded = new HashSet<string> { rootKey };
            var queue = new Queue<(ISymbol Symbol, int Depth, List<CallInfo> Target, HashSet<string> Path)>();
            queue.Enqueue((root, 1, roots, new HashSet<string> { rootKey }));

            var count = 0;

            while (queue.Count > 0)
            {
                var (symbol, depth, target, path) = queue.Dequeue();

                foreach (var (relatedSymbol, info) in await findDirectRelations(symbol))
                {
                    if (count >= MaxEntriesPerDirection)
                        return (roots, count, true);

                    count++;
                    info.Depth = depth;
                    target.Add(info);

                    var key = GetSymbolKey(relatedSymbol);
                    if (path.Contains(key))
                    {
                        info.IsRecursive = true;
                        continue;
                    }

                    if (depth >= maxDepth || !CanExpand(relatedSymbol))
                        continue;

                    if (!expanded.Add(key))
                    {
                        info.IsRepeated = true;
                        continue;
                    }

                    queue.Enqueue((relatedSymbol, depth + 1, info.Children, new HashSet<string>(path) { key }));
                }
            }

            return (roots, count, false);
        }

        // Fields and events can call methods (initializers, accessors) but have no call hierarchy of their own
        private static bool CanExpand(ISymbol symbol) => symbol is IMethodSymbol or IPropertySymbol;

        /// <summary>
        /// Identity of a source symbol that is stable across compilations of the same project
        /// </summary>
        private static string GetSymbolKey(ISymbol symbol)
        {
            var definition = symbol.OriginalDefinition;
            var location = definition.Locations.FirstOrDefault();
            return $"{definition.ContainingAssembly?.Name}|{definition.ToDisplayString()}|{location?.SourceTree?.FilePath}:{location?.SourceSpan.Start}";
        }

        private static async Task<List<(ISymbol Symbol, CallInfo Info)>> FindDirectCallersAsync(
            Solution solution,
            ISymbol symbol)
        {
            var callers = new List<(ISymbol Symbol, CallInfo Info)>();
            var references = await SymbolFinder.FindCallersAsync(symbol, solution);

            foreach (var caller in references)
            {
                // Callers are methods, or properties/fields/events when the call sits in an accessor or initializer
                if (caller.CallingSymbol is not (IMethodSymbol or IPropertySymbol or IFieldSymbol or IEventSymbol))
                    continue;

                var location = caller.Locations.FirstOrDefault();
                if (location == null)
                    continue;

                var lineSpan = location.GetLineSpan();
                callers.Add((caller.CallingSymbol, new CallInfo
                {
                    MethodName = caller.CallingSymbol.Name,
                    ContainingType = caller.CallingSymbol.ContainingType?.Name ?? "",
                    FilePath = lineSpan.Path,
                    LineNumber = lineSpan.StartLinePosition.Line + 1,
                    CallCount = caller.Locations.Count()
                }));
            }

            return callers
                .OrderBy(c => c.Info.ContainingType)
                .ThenBy(c => c.Info.MethodName)
                .ThenBy(c => c.Info.FilePath)
                .ThenBy(c => c.Info.LineNumber)
                .ToList();
        }

        private static async Task<List<(ISymbol Symbol, CallInfo Info)>> FindDirectCalleesAsync(
            Solution solution,
            ISymbol symbol)
        {
            var callees = new List<(ISymbol Symbol, CallInfo Info)>();

            if (symbol is not IMethodSymbol methodSymbol)
                return callees;

            // Get the method's syntax node (the implementation part, for partial methods)
            var implementation = methodSymbol.PartialImplementationPart ?? methodSymbol;
            var syntaxReference = implementation.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxReference == null)
                return callees;

            var methodNode = await syntaxReference.GetSyntaxAsync() as MethodDeclarationSyntax;
            if (methodNode == null)
                return callees;

            // Find the document and semantic model
            var document = solution.GetDocument(syntaxReference.SyntaxTree);
            if (document == null)
                return callees;

            var semanticModel = await document.GetSemanticModelAsync();
            if (semanticModel == null)
                return callees;

            // Find all invocations in the method body; each called method (each overload separately) is one entry
            var invocations = methodNode.DescendantNodes()
                .OfType<InvocationExpressionSyntax>();

            var callInfoMap = new Dictionary<string, (ISymbol Symbol, CallInfo Info)>();

            foreach (var invocation in invocations)
            {
                var symbolInfo = semanticModel.GetSymbolInfo(invocation);
                var calledSymbol = symbolInfo.Symbol as IMethodSymbol;

                if (calledSymbol == null || calledSymbol.IsImplicitlyDeclared)
                    continue;

                // Map extension-method calls and generic instantiations to their declarations
                var calledDefinition = (calledSymbol.ReducedFrom ?? calledSymbol).OriginalDefinition;
                var key = GetSymbolKey(calledDefinition);

                if (callInfoMap.TryGetValue(key, out var existing))
                {
                    existing.Info.CallCount++;
                    continue;
                }

                var location = calledDefinition.Locations.FirstOrDefault();
                if (location != null && location.IsInSource)
                {
                    var lineSpan = location.GetLineSpan();
                    callInfoMap[key] = (calledDefinition, new CallInfo
                    {
                        MethodName = calledDefinition.Name,
                        ContainingType = calledDefinition.ContainingType?.Name ?? "",
                        FilePath = lineSpan.Path,
                        LineNumber = lineSpan.StartLinePosition.Line + 1,
                        CallCount = 1
                    });
                }
            }

            return callInfoMap.Values
                .OrderBy(c => c.Info.ContainingType)
                .ThenBy(c => c.Info.MethodName)
                .ThenBy(c => c.Info.FilePath)
                .ThenBy(c => c.Info.LineNumber)
                .ToList();
        }

        private string FormatCallHierarchy(CallHierarchyResult result, string direction)
        {
            var builder = new StringBuilder();

            builder.AppendLine($"Call Hierarchy for: {result.FullName}");
            builder.AppendLine($"Location: {Path.GetFileName(result.FilePath)}:{result.LineNumber}");
            builder.AppendLine($"Max depth: {result.MaxDepth}");
            builder.AppendLine();

            if (direction == "both" || direction == "callers")
            {
                builder.AppendLine($"📞 Callers ({result.Callers.Count} methods call this directly, {result.TotalCallers} listed across all levels):");
                if (result.Callers.Any())
                {
                    AppendCallTree(builder, result.Callers, "  ", "callers");
                }
                else
                {
                    builder.AppendLine("  (no callers found)");
                }
                if (result.CallersTruncated)
                {
                    builder.AppendLine($"  ... truncated after {MaxEntriesPerDirection} entries; lower maxDepth to see a complete tree");
                }
                builder.AppendLine();
            }

            if (direction == "both" || direction == "callees")
            {
                builder.AppendLine($"📤 Callees ({result.Callees.Count} methods called by this directly, {result.TotalCallees} listed across all levels):");
                if (result.Callees.Any())
                {
                    AppendCallTree(builder, result.Callees, "  ", "callees");
                }
                else
                {
                    builder.AppendLine("  (no callees found)");
                }
                if (result.CalleesTruncated)
                {
                    builder.AppendLine($"  ... truncated after {MaxEntriesPerDirection} entries; lower maxDepth to see a complete tree");
                }
                builder.AppendLine();
            }

            // Summary
            builder.AppendLine("Summary:");
            if (direction == "both" || direction == "callers")
            {
                builder.AppendLine($"  Incoming calls: {result.Callers.Count} direct, {result.TotalCallers} total");
            }
            if (direction == "both" || direction == "callees")
            {
                builder.AppendLine($"  Outgoing calls: {result.Callees.Count} direct, {result.TotalCallees} total");
            }

            return builder.ToString();
        }

        private static void AppendCallTree(StringBuilder builder, List<CallInfo> calls, string indent, string relation)
        {
            foreach (var call in calls)
            {
                var fileName = Path.GetFileName(call.FilePath);
                var callText = call.CallCount > 1 ? $" ({call.CallCount} calls)" : "";
                var marker = call.IsRecursive
                    ? " ↺ recursive"
                    : call.IsRepeated ? $" ({relation} listed above)" : "";

                builder.AppendLine($"{indent}├─> {call.ContainingType}.{call.MethodName}{callText}{marker}");
                builder.AppendLine($"{indent}    ({fileName}:{call.LineNumber})");

                if (call.Children.Any())
                {
                    AppendCallTree(builder, call.Children, indent + "    ", relation);
                }
            }
        }
    }
}
