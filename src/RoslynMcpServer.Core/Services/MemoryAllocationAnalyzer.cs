using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Core.Models;
using System.Collections.Concurrent;

namespace RoslynMcpServer.Core.Services
{
    /// <summary>
    /// Service for detecting memory allocation hotspots and optimization opportunities in C# code.
    /// Detects: byte array allocations in loops (ArrayPool), boxing of value types,
    /// string.Substring usage (Span/AsSpan), byte[] parameters (ReadOnlySpan),
    /// and string concatenation in loops (StringBuilder).
    /// </summary>
    public class MemoryAllocationAnalyzer
    {
        private readonly ILogger<MemoryAllocationAnalyzer> _logger;
        private readonly CodeAnalysisService _codeAnalysis;

        public MemoryAllocationAnalyzer(
            ILogger<MemoryAllocationAnalyzer> logger,
            CodeAnalysisService codeAnalysis)
        {
            _logger = logger;
            _codeAnalysis = codeAnalysis;
        }

        /// <summary>
        /// Analyzes a solution for memory allocation hotspots.
        /// </summary>
        public async Task<MemoryAllocationResults> AnalyzeAsync(
            string solutionPath,
            string[] issueTypes)
        {
            var results = new MemoryAllocationResults();
            var allIssues = new ConcurrentBag<MemoryAllocationIssue>();

            try
            {
                var solution = await _codeAnalysis.GetSolutionAsync(solutionPath);

                var projects = solution.Projects
                    .Where(p => p.SupportsCompilation)
                    .ToList();

                results.AnalyzedProjects = projects.Count;

                var projectTasks = projects.Select(async project =>
                {
                    try
                    {
                        var issues = await AnalyzeProjectAsync(project, issueTypes);
                        foreach (var issue in issues)
                            allIssues.Add(issue);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to analyze project: {ProjectName}", project.Name);
                        results.FailedProjects++;
                    }
                });

                await Task.WhenAll(projectTasks);

                results.Issues = allIssues.ToList();
                results.AnalyzedFiles = results.Issues.Select(i => i.FilePath).Distinct().Count();

                results.IssuesByType = results.Issues
                    .GroupBy(i => i.IssueType)
                    .ToDictionary(g => g.Key, g => g.Count());

                results.IssuesByProject = results.Issues
                    .GroupBy(i => i.ProjectName)
                    .ToDictionary(g => g.Key, g => g.Count());

                _logger.LogInformation(
                    "Memory allocation analysis complete: {IssueCount} issues found across {ProjectCount} projects",
                    results.TotalIssues,
                    results.AnalyzedProjects);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing memory allocations");
                results.Warnings.Add(new OperationWarning
                {
                    Context = "Analysis",
                    Message = $"Error: {ex.Message}"
                });
            }

            return results;
        }

        private async Task<List<MemoryAllocationIssue>> AnalyzeProjectAsync(
            Project project, string[] issueTypes)
        {
            var issues = new List<MemoryAllocationIssue>();

            var compilation = await project.GetCompilationAsync();
            if (compilation == null)
                return issues;

            foreach (var syntaxTree in compilation.SyntaxTrees)
            {
                // Skip generated files
                if (syntaxTree.FilePath.Contains(".g.cs") ||
                    syntaxTree.FilePath.Contains("\\obj\\") ||
                    syntaxTree.FilePath.Contains("/obj/"))
                    continue;

                try
                {
                    var semanticModel = compilation.GetSemanticModel(syntaxTree);
                    var root = await syntaxTree.GetRootAsync();

                    if (ShouldCheck(issueTypes, "ArrayPoolOpportunity"))
                        issues.AddRange(CheckArrayPoolOpportunity(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "Boxing"))
                        issues.AddRange(CheckBoxing(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "SubstringSpan"))
                        issues.AddRange(CheckSubstringSpan(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "ReadOnlySpan"))
                        issues.AddRange(CheckReadOnlySpan(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "StringBuilderOpportunity"))
                        issues.AddRange(CheckStringBuilderOpportunity(root, semanticModel, syntaxTree, project.Name));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to analyze syntax tree: {FilePath}", syntaxTree.FilePath);
                }
            }

            return issues;
        }

        private static bool ShouldCheck(string[] issueTypes, string issueType) =>
            issueTypes.Length == 0 ||
            issueTypes.Any(t => t.Equals("all", StringComparison.OrdinalIgnoreCase)) ||
            issueTypes.Any(t => t.Equals(issueType, StringComparison.OrdinalIgnoreCase));

        // ──────────────────────────────────────────────────────────────────────
        // CheckArrayPoolOpportunity
        // Detects: new byte[N] / new T[] inside loop bodies → ArrayPool<T>.Rent
        // ──────────────────────────────────────────────────────────────────────

        private List<MemoryAllocationIssue> CheckArrayPoolOpportunity(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<MemoryAllocationIssue>();

            // Find all loop statements
            var loops = root.DescendantNodes().Where(n =>
                n is ForStatementSyntax ||
                n is ForEachStatementSyntax ||
                n is WhileStatementSyntax ||
                n is DoStatementSyntax);

            foreach (var loop in loops)
            {
                // Find array creation expressions inside the loop
                var arrayCreations = loop.DescendantNodes()
                    .OfType<ArrayCreationExpressionSyntax>();

                foreach (var creation in arrayCreations)
                {
                    try
                    {
                        var typeInfo = model.GetTypeInfo(creation);
                        if (typeInfo.Type is not IArrayTypeSymbol arrayType)
                            continue;

                        var elementType = arrayType.ElementType;

                        // Focus on byte[], char[], int[], and other value-type arrays
                        if (elementType.IsReferenceType && elementType.SpecialType == SpecialType.None)
                            continue;

                        var elementName = elementType.ToDisplayString();
                        var snippet = creation.ToString();
                        if (snippet.Length > 80) snippet = snippet[..80] + "…";

                        issues.Add(CreateIssue(
                            "ArrayPoolOpportunity", "High",
                            $"new {elementName}[] inside loop — consider ArrayPool<{elementName}>",
                            $"Allocating new {elementName}[] in a loop body causes repeated GC pressure. " +
                            $"Renting from ArrayPool<{elementName}>.Shared eliminates this allocation.",
                            tree, creation, project,
                            $"Use ArrayPool<{elementName}>.Shared.Rent(size) and return in a finally block.",
                            $"var pool = ArrayPool<{elementName}>.Shared;\n" +
                            $"var buffer = pool.Rent(size);\n" +
                            $"try {{ /* use buffer */ }}\n" +
                            $"finally {{ pool.Return(buffer); }}"));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Error checking array creation in {File}", tree.FilePath);
                    }
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckBoxing
        // Detects: value types passed as object / stored in non-generic collections
        // ──────────────────────────────────────────────────────────────────────

        private List<MemoryAllocationIssue> CheckBoxing(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<MemoryAllocationIssue>();

            // Check variable declarations: ArrayList, Hashtable, non-generic Stack/Queue
            var objectCreations = root.DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>();

            foreach (var creation in objectCreations)
            {
                try
                {
                    var typeSymbol = model.GetTypeInfo(creation).Type;
                    if (typeSymbol == null) continue;

                    var fullName = typeSymbol.ToDisplayString();

                    // Non-generic collections that box value types
                    if (fullName is "System.Collections.ArrayList" or
                                    "System.Collections.Hashtable" or
                                    "System.Collections.Stack" or
                                    "System.Collections.Queue" or
                                    "System.Collections.SortedList")
                    {
                        issues.Add(CreateIssue(
                            "Boxing", "Medium",
                            $"new {typeSymbol.Name}() causes boxing for value types",
                            $"Non-generic collection {typeSymbol.Name} boxes value types on every insert/access. " +
                            "This creates GC pressure and degrades performance.",
                            tree, creation, project,
                            $"Replace with the generic equivalent: List<T>, Dictionary<K,V>, Stack<T>, Queue<T>, SortedDictionary<K,V>.",
                            $"// Before: var list = new ArrayList();\n// After:  var list = new List<int>();"));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking boxing in {File}", tree.FilePath);
                }
            }

            // Check assignments: value type → object variable (explicit boxing)
            var assignments = root.DescendantNodes()
                .OfType<VariableDeclaratorSyntax>()
                .Where(v => v.Initializer != null);

            foreach (var declarator in assignments)
            {
                try
                {
                    var declaration = declarator.Parent as VariableDeclarationSyntax;
                    if (declaration == null) continue;

                    var declaredTypeInfo = model.GetTypeInfo(declaration.Type);
                    if (declaredTypeInfo.Type?.SpecialType != SpecialType.System_Object)
                        continue;

                    var initTypeInfo = model.GetTypeInfo(declarator.Initializer!.Value);
                    var initType = initTypeInfo.Type;
                    if (initType == null || initType.IsReferenceType) continue;
                    if (initType.SpecialType == SpecialType.System_Void) continue;

                    // Value type assigned to object variable
                    issues.Add(CreateIssue(
                        "Boxing", "Low",
                        $"Value type '{initType.ToDisplayString()}' boxed as object",
                        $"Assigning value type '{initType.ToDisplayString()}' to an object variable causes a heap allocation (boxing). " +
                        "Repeated boxing in hot paths degrades performance.",
                        tree, declarator, project,
                        $"Use a typed variable ({initType.ToDisplayString()}) or generic type parameter instead of object.",
                        $"// Before: object val = myInt;\n// After:  int val = myInt;"));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking boxing assignments in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckSubstringSpan
        // Detects: string.Substring() → AsSpan().Slice() or ReadOnlySpan<char>
        // ──────────────────────────────────────────────────────────────────────

        private List<MemoryAllocationIssue> CheckSubstringSpan(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<MemoryAllocationIssue>();

            var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();

            foreach (var invocation in invocations)
            {
                try
                {
                    var symbolInfo = model.GetSymbolInfo(invocation);
                    if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
                        continue;

                    if (methodSymbol.ContainingType?.SpecialType != SpecialType.System_String)
                        continue;

                    if (methodSymbol.Name != "Substring")
                        continue;

                    issues.Add(CreateIssue(
                        "SubstringSpan", "Medium",
                        "string.Substring() allocates a new string — use AsSpan().Slice()",
                        "string.Substring() allocates a new string object on the heap. " +
                        "In performance-sensitive code, use AsSpan().Slice() to get a zero-allocation ReadOnlySpan<char> slice.",
                        tree, invocation, project,
                        "Replace .Substring(start, length) with .AsSpan(start, length) where a ReadOnlySpan<char> is accepted.",
                        "// Before: string sub = text.Substring(2, 5);\n" +
                        "// After:  ReadOnlySpan<char> sub = text.AsSpan(2, 5);"));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking Substring in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckReadOnlySpan
        // Detects: public methods with byte[] / char[] parameters that could be ReadOnlySpan<T>
        // ──────────────────────────────────────────────────────────────────────

        private List<MemoryAllocationIssue> CheckReadOnlySpan(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<MemoryAllocationIssue>();

            var methodDecls = root.DescendantNodes().OfType<MethodDeclarationSyntax>();

            foreach (var methodDecl in methodDecls)
            {
                try
                {
                    if (model.GetDeclaredSymbol(methodDecl) is not IMethodSymbol methodSymbol)
                        continue;

                    // Only consider public/internal methods (API boundary)
                    if (methodSymbol.DeclaredAccessibility != Accessibility.Public &&
                        methodSymbol.DeclaredAccessibility != Accessibility.Internal)
                        continue;

                    // Skip if method is an override, interface implementation, or extern
                    if (methodSymbol.IsOverride || methodSymbol.IsExtern)
                        continue;

                    if (methodSymbol.ExplicitInterfaceImplementations.Length > 0)
                        continue;

                    foreach (var param in methodSymbol.Parameters)
                    {
                        if (param.Type is not IArrayTypeSymbol arrayType)
                            continue;

                        var elementSpecial = arrayType.ElementType.SpecialType;
                        if (elementSpecial != SpecialType.System_Byte &&
                            elementSpecial != SpecialType.System_Char)
                            continue;

                        // Skip if already ref/out/in
                        if (param.RefKind != RefKind.None)
                            continue;

                        var elemName = arrayType.ElementType.ToDisplayString();

                        issues.Add(CreateIssue(
                            "ReadOnlySpan", "Low",
                            $"Method '{methodSymbol.Name}' takes {elemName}[] — consider ReadOnlySpan<{elemName}>",
                            $"A {elemName}[] parameter forces callers to allocate an array even when data is already in a span or stackalloc buffer. " +
                            $"ReadOnlySpan<{elemName}> is a zero-cost abstraction that also accepts arrays, stackalloc, and Memory<T>.",
                            tree, methodDecl, project,
                            $"Change the parameter type to ReadOnlySpan<{elemName}>.",
                            $"// Before: public void Process(byte[] data)\n// After:  public void Process(ReadOnlySpan<byte> data)"));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking ReadOnlySpan parameters in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckStringBuilderOpportunity
        // Detects: string += inside loop bodies
        // ──────────────────────────────────────────────────────────────────────

        private List<MemoryAllocationIssue> CheckStringBuilderOpportunity(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<MemoryAllocationIssue>();

            // Find all loop statements
            var loops = root.DescendantNodes().Where(n =>
                n is ForStatementSyntax ||
                n is ForEachStatementSyntax ||
                n is WhileStatementSyntax ||
                n is DoStatementSyntax);

            foreach (var loop in loops)
            {
                // Find string += assignments inside the loop
                var assignments = loop.DescendantNodes()
                    .OfType<AssignmentExpressionSyntax>()
                    .Where(a => a.IsKind(SyntaxKind.AddAssignmentExpression));

                foreach (var assignment in assignments)
                {
                    try
                    {
                        var leftTypeInfo = model.GetTypeInfo(assignment.Left);
                        if (leftTypeInfo.Type?.SpecialType != SpecialType.System_String)
                            continue;

                        issues.Add(CreateIssue(
                            "StringBuilderOpportunity", "High",
                            "string += in loop — use StringBuilder to avoid O(n²) allocations",
                            "Using string += in a loop creates a new string object on every iteration, " +
                            "copying all previous characters. This is O(n²) in both time and allocations. " +
                            "StringBuilder.Append() is O(1) amortized.",
                            tree, assignment, project,
                            "Replace the loop with StringBuilder.Append() calls and call ToString() once after the loop.",
                            "// Before:\nstring result = \"\";\nforeach (var item in items) result += item;\n\n" +
                            "// After:\nvar sb = new StringBuilder();\nforeach (var item in items) sb.Append(item);\nstring result = sb.ToString();"));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Error checking string += in {File}", tree.FilePath);
                    }
                }

                // Also check: string = string + ... pattern inside loops
                var binaryAdds = loop.DescendantNodes()
                    .OfType<AssignmentExpressionSyntax>()
                    .Where(a => a.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                                a.Right.IsKind(SyntaxKind.AddExpression));

                foreach (var assignment in binaryAdds)
                {
                    try
                    {
                        var leftTypeInfo = model.GetTypeInfo(assignment.Left);
                        if (leftTypeInfo.Type?.SpecialType != SpecialType.System_String)
                            continue;

                        // Verify the right side includes the left (self-concatenation pattern)
                        var rightText = assignment.Right.ToString();
                        var leftText = assignment.Left.ToString();
                        if (!rightText.StartsWith(leftText, StringComparison.Ordinal))
                            continue;

                        issues.Add(CreateIssue(
                            "StringBuilderOpportunity", "High",
                            "string = string + ... in loop — use StringBuilder",
                            "Concatenating strings in a loop creates O(n²) allocations. " +
                            "Each concatenation copies the entire accumulated string.",
                            tree, assignment, project,
                            "Use StringBuilder.Append() in the loop body and call ToString() once.",
                            "var sb = new StringBuilder();\nforeach (var item in items) sb.Append(item);\nstring result = sb.ToString();"));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Error checking string concatenation in {File}", tree.FilePath);
                    }
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CreateIssue helper
        // ──────────────────────────────────────────────────────────────────────

        private static MemoryAllocationIssue CreateIssue(
            string issueType, string severity, string title, string description,
            SyntaxTree tree, SyntaxNode node,
            string project, string recommendation, string fixExample)
        {
            var lineSpan = tree.GetLineSpan(node.Span);
            var line = lineSpan.StartLinePosition.Line + 1;
            var rawText = node.ToString();
            var codeSnippet = rawText.Length > 100 ? rawText[..100] + "…" : rawText;

            return new MemoryAllocationIssue
            {
                IssueType = issueType,
                Severity = severity,
                Title = title,
                Description = description,
                FilePath = tree.FilePath,
                FileName = Path.GetFileName(tree.FilePath),
                ProjectName = project,
                LineNumber = line,
                SymbolName = string.Empty,
                CodeSnippet = codeSnippet.Trim(),
                Recommendation = recommendation,
                FixExample = fixExample
            };
        }
    }
}
