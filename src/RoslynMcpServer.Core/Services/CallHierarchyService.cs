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
        public List<CallInfo> Callers { get; set; } = new();
        public List<CallInfo> Callees { get; set; } = new();
    }

    public class CallInfo
    {
        public string MethodName { get; set; } = string.Empty;
        public string ContainingType { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public int CallCount { get; set; }
    }

    public class CallHierarchyService
    {
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
                    LineNumber = targetMethod.Locations.FirstOrDefault()?.GetLineSpan().StartLinePosition.Line + 1 ?? 0
                };

                // Analyze callers (who calls this method)
                if (direction == "both" || direction == "callers")
                {
                    result.Callers = await FindCallersAsync(solution, targetMethod, maxDepth);
                }

                // Analyze callees (what this method calls)
                if (direction == "both" || direction == "callees")
                {
                    result.Callees = await FindCalleesAsync(solution, targetMethod, maxDepth);
                }

                return FormatCallHierarchy(result, direction);
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

        private async Task<List<CallInfo>> FindCallersAsync(
            Solution solution,
            IMethodSymbol methodSymbol,
            int maxDepth)
        {
            var callers = new List<CallInfo>();
            var references = await SymbolFinder.FindCallersAsync(methodSymbol, solution);

            foreach (var caller in references)
            {
                if (caller.CallingSymbol is IMethodSymbol callingMethod)
                {
                    var location = caller.Locations.FirstOrDefault();
                    if (location != null)
                    {
                        var lineSpan = location.GetLineSpan();
                        callers.Add(new CallInfo
                        {
                            MethodName = callingMethod.Name,
                            ContainingType = callingMethod.ContainingType?.Name ?? "",
                            FilePath = lineSpan.Path,
                            LineNumber = lineSpan.StartLinePosition.Line + 1,
                            CallCount = caller.Locations.Count()
                        });
                    }
                }
            }

            return callers.OrderBy(c => c.ContainingType).ThenBy(c => c.MethodName).ToList();
        }

        private async Task<List<CallInfo>> FindCalleesAsync(
            Solution solution,
            IMethodSymbol methodSymbol,
            int maxDepth)
        {
            var callees = new List<CallInfo>();

            // Get the method's syntax node
            var syntaxReference = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault();
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

            // Find all invocations in the method body
            var invocations = methodNode.DescendantNodes()
                .OfType<InvocationExpressionSyntax>();

            var callInfoMap = new Dictionary<string, CallInfo>();

            foreach (var invocation in invocations)
            {
                var symbolInfo = semanticModel.GetSymbolInfo(invocation);
                var calledSymbol = symbolInfo.Symbol as IMethodSymbol;

                if (calledSymbol != null && !calledSymbol.IsImplicitlyDeclared)
                {
                    var key = $"{calledSymbol.ContainingType?.Name}.{calledSymbol.Name}";

                    if (callInfoMap.ContainsKey(key))
                    {
                        callInfoMap[key].CallCount++;
                    }
                    else
                    {
                        var location = calledSymbol.Locations.FirstOrDefault();
                        if (location != null && location.IsInSource)
                        {
                            var lineSpan = location.GetLineSpan();
                            callInfoMap[key] = new CallInfo
                            {
                                MethodName = calledSymbol.Name,
                                ContainingType = calledSymbol.ContainingType?.Name ?? "",
                                FilePath = lineSpan.Path,
                                LineNumber = lineSpan.StartLinePosition.Line + 1,
                                CallCount = 1
                            };
                        }
                    }
                }
            }

            return callInfoMap.Values
                .OrderBy(c => c.ContainingType)
                .ThenBy(c => c.MethodName)
                .ToList();
        }

        private string FormatCallHierarchy(CallHierarchyResult result, string direction)
        {
            var builder = new StringBuilder();

            builder.AppendLine($"Call Hierarchy for: {result.FullName}");
            builder.AppendLine($"Location: {Path.GetFileName(result.FilePath)}:{result.LineNumber}");
            builder.AppendLine();

            if (direction == "both" || direction == "callers")
            {
                builder.AppendLine($"📞 Callers ({result.Callers.Count} methods call this):");
                if (result.Callers.Any())
                {
                    foreach (var caller in result.Callers)
                    {
                        var fileName = Path.GetFileName(caller.FilePath);
                        var callText = caller.CallCount > 1 ? $"({caller.CallCount} calls)" : "";
                        builder.AppendLine($"  ├─> {caller.ContainingType}.{caller.MethodName} {callText}");
                        builder.AppendLine($"      ({fileName}:{caller.LineNumber})");
                    }
                }
                else
                {
                    builder.AppendLine("  (no callers found)");
                }
                builder.AppendLine();
            }

            if (direction == "both" || direction == "callees")
            {
                builder.AppendLine($"📤 Callees ({result.Callees.Count} methods called by this):");
                if (result.Callees.Any())
                {
                    foreach (var callee in result.Callees)
                    {
                        var fileName = Path.GetFileName(callee.FilePath);
                        var callText = callee.CallCount > 1 ? $"({callee.CallCount} calls)" : "";
                        builder.AppendLine($"  ├─> {callee.ContainingType}.{callee.MethodName} {callText}");
                        builder.AppendLine($"      ({fileName}:{callee.LineNumber})");
                    }
                }
                else
                {
                    builder.AppendLine("  (no callees found)");
                }
                builder.AppendLine();
            }

            // Summary
            builder.AppendLine("Summary:");
            if (direction == "both" || direction == "callers")
            {
                builder.AppendLine($"  Incoming calls: {result.Callers.Count}");
            }
            if (direction == "both" || direction == "callees")
            {
                builder.AppendLine($"  Outgoing calls: {result.Callees.Count}");
            }

            return builder.ToString();
        }
    }
}
