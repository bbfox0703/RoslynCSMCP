using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Core.Models;
using System.Collections.Concurrent;

namespace RoslynMcpServer.Core.Services
{
    /// <summary>
    /// Service for analyzing unsafe code risks in C# code.
    /// Detects stackalloc overuse, unguarded pointer arithmetic, void* casts,
    /// cross-thread pointer passing, oversized fixed blocks, and unsafe+async mixing.
    /// </summary>
    public class UnsafeCodeAnalyzer
    {
        private readonly ILogger<UnsafeCodeAnalyzer> _logger;
        private readonly CodeAnalysisService _codeAnalysis;

        // stackalloc threshold: warn when > 1024 bytes (1 KB)
        private const int StackAllocSafeThreshold = 1024;

        public UnsafeCodeAnalyzer(
            ILogger<UnsafeCodeAnalyzer> logger,
            CodeAnalysisService codeAnalysis)
        {
            _logger = logger;
            _codeAnalysis = codeAnalysis;
        }

        /// <summary>
        /// Analyzes a solution for unsafe code risks.
        /// </summary>
        public async Task<UnsafeCodeAnalysisResults> AnalyzeAsync(
            string solutionPath,
            string[] issueTypes)
        {
            var results = new UnsafeCodeAnalysisResults();
            var allIssues = new ConcurrentBag<UnsafeCodeIssue>();

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
                    "Unsafe code analysis complete: {IssueCount} issues found across {ProjectCount} projects",
                    results.TotalIssues,
                    results.AnalyzedProjects);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing unsafe code");
                results.Warnings.Add(new OperationWarning
                {
                    Context = "Analysis",
                    Message = $"Error: {ex.Message}"
                });
            }

            return results;
        }

        private async Task<List<UnsafeCodeIssue>> AnalyzeProjectAsync(
            Project project, string[] issueTypes)
        {
            var issues = new List<UnsafeCodeIssue>();

            var compilation = await project.GetCompilationAsync();
            if (compilation == null)
                return issues;

            foreach (var syntaxTree in compilation.SyntaxTrees)
            {
                if (syntaxTree.FilePath.Contains(".g.cs") ||
                    syntaxTree.FilePath.Contains("\\obj\\") ||
                    syntaxTree.FilePath.Contains("/obj/"))
                    continue;

                try
                {
                    var semanticModel = compilation.GetSemanticModel(syntaxTree);
                    var root = await syntaxTree.GetRootAsync();

                    if (ShouldCheck(issueTypes, "StackAllocSize"))
                        issues.AddRange(CheckStackAllocSize(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "StackAllocNoSpan"))
                        issues.AddRange(CheckStackAllocNoSpan(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "StackAllocInLoop"))
                        issues.AddRange(CheckStackAllocInLoop(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "PointerArithmetic"))
                        issues.AddRange(CheckPointerArithmetic(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "VoidPointerCast"))
                        issues.AddRange(CheckVoidPointerCast(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "CrossThreadPointer"))
                        issues.AddRange(CheckCrossThreadPointer(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "OversizedFixed"))
                        issues.AddRange(CheckOversizedFixed(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "UnsafeAsync"))
                        issues.AddRange(CheckUnsafeAsync(root, semanticModel, syntaxTree, project.Name));
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
        // CheckStackAllocSize — stackalloc > 1 KB (High)
        // ──────────────────────────────────────────────────────────────────────

        private List<UnsafeCodeIssue> CheckStackAllocSize(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<UnsafeCodeIssue>();

            foreach (var stackAlloc in root.DescendantNodes().OfType<StackAllocArrayCreationExpressionSyntax>())
            {
                try
                {
                    // stackalloc T[N] — get the rank specifier size
                    var rankNode = stackAlloc.DescendantNodes()
                        .OfType<ArrayRankSpecifierSyntax>()
                        .FirstOrDefault();

                    if (rankNode == null) continue;

                    var sizeExpr = rankNode.Sizes.FirstOrDefault();
                    if (sizeExpr == null) continue;

                    // Try to evaluate a constant size
                    var constVal = model.GetConstantValue(sizeExpr);
                    if (!constVal.HasValue || constVal.Value is not int sizeInt) continue;

                    // Get element type size heuristic (assume 1 byte for byte, 4 for int, etc.)
                    // We flag any stackalloc where N > 1024 (conservative)
                    if (sizeInt > StackAllocSafeThreshold)
                    {
                        issues.Add(CreateIssue(
                            "StackAllocSize", "High",
                            $"stackalloc[{sizeInt}] exceeds safe threshold of {StackAllocSafeThreshold} bytes",
                            $"stackalloc allocates memory on the call stack. Allocating {sizeInt} or more elements " +
                            "risks stack overflow, especially in recursive or deeply nested calls. " +
                            $"Recommended maximum is {StackAllocSafeThreshold} elements.",
                            tree, stackAlloc, project, $"stackalloc[{sizeInt}]",
                            $"Consider using ArrayPool<T>.Shared.Rent({sizeInt}) on the heap instead.",
                            $"// Before: Span<byte> buf = stackalloc byte[{sizeInt}];\n" +
                            $"// After:\nvar buf = ArrayPool<byte>.Shared.Rent({sizeInt});\ntry {{ ... }}\nfinally {{ ArrayPool<byte>.Shared.Return(buf); }}"));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking stackalloc size in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckStackAllocNoSpan — stackalloc not assigned to Span<T> (Medium)
        // ──────────────────────────────────────────────────────────────────────

        private List<UnsafeCodeIssue> CheckStackAllocNoSpan(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<UnsafeCodeIssue>();

            foreach (var stackAlloc in root.DescendantNodes().OfType<StackAllocArrayCreationExpressionSyntax>())
            {
                try
                {
                    // Check if the immediate parent assignment uses Span<T> or ReadOnlySpan<T>
                    var parent = stackAlloc.Parent;

                    // Walk up through implicit/explicit casts
                    while (parent is CastExpressionSyntax || parent is ParenthesizedExpressionSyntax)
                        parent = parent?.Parent;

                    bool assignedToSpan = false;

                    if (parent is EqualsValueClauseSyntax equalsClause)
                    {
                        var declaration = equalsClause.Parent as VariableDeclaratorSyntax;
                        var varDecl = declaration?.Parent as VariableDeclarationSyntax;
                        if (varDecl != null)
                        {
                            var typeText = varDecl.Type.ToString();
                            assignedToSpan = typeText.Contains("Span<") || typeText == "var";
                            if (typeText == "var")
                            {
                                // Infer: var with stackalloc is always Span<T> in safe context
                                assignedToSpan = true;
                            }
                        }
                    }
                    else if (parent is AssignmentExpressionSyntax assignment)
                    {
                        var typeInfo = model.GetTypeInfo(assignment.Left);
                        var typeName = typeInfo.Type?.ToDisplayString() ?? string.Empty;
                        assignedToSpan = typeName.Contains("Span<");
                    }

                    if (assignedToSpan) continue;

                    // Only flag if inside unsafe context (pointer usage)
                    bool inUnsafe = stackAlloc.Ancestors()
                        .Any(a => a is UnsafeStatementSyntax ||
                                  (a is MethodDeclarationSyntax m && m.Modifiers.Any(mod => mod.IsKind(SyntaxKind.UnsafeKeyword))));

                    if (!inUnsafe) continue;

                    issues.Add(CreateIssue(
                        "StackAllocNoSpan", "Medium",
                        "stackalloc not wrapped in Span<T> — unsafe pointer usage",
                        "Using stackalloc directly as a pointer (without Span<T>) bypasses bounds checking " +
                        "and is harder to reason about. Wrapping in Span<T> retains stack allocation performance " +
                        "while adding bounds-checked access.",
                        tree, stackAlloc, project, "stackalloc",
                        "Assign stackalloc to Span<T> for bounds-checked access.",
                        "// Before: byte* ptr = stackalloc byte[128];\n" +
                        "// After:  Span<byte> span = stackalloc byte[128];"));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking stackalloc/Span in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckStackAllocInLoop — stackalloc inside loop body (Critical)
        // ──────────────────────────────────────────────────────────────────────

        private List<UnsafeCodeIssue> CheckStackAllocInLoop(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<UnsafeCodeIssue>();

            var loopTypes = new HashSet<Type>
            {
                typeof(ForStatementSyntax),
                typeof(ForEachStatementSyntax),
                typeof(WhileStatementSyntax),
                typeof(DoStatementSyntax)
            };

            foreach (var stackAlloc in root.DescendantNodes().OfType<StackAllocArrayCreationExpressionSyntax>())
            {
                try
                {
                    bool inLoop = stackAlloc.Ancestors()
                        .Any(a => loopTypes.Contains(a.GetType()));

                    if (!inLoop) continue;

                    issues.Add(CreateIssue(
                        "StackAllocInLoop", "Critical",
                        "stackalloc inside a loop — stack overflow risk",
                        "stackalloc inside a loop allocates stack memory on every iteration without releasing it " +
                        "until the method returns. This will rapidly exhaust the thread's stack and cause a StackOverflowException.",
                        tree, stackAlloc, project, "stackalloc",
                        "Move stackalloc outside the loop, or use ArrayPool<T> inside the loop.",
                        "// Before (BAD):\nfor (int i = 0; i < n; i++)\n{\n    Span<byte> buf = stackalloc byte[256]; // grows each iteration!\n}\n\n" +
                        "// After — option 1: hoist outside loop:\nSpan<byte> buf = stackalloc byte[256];\nfor (int i = 0; i < n; i++) { /* use buf */ }\n\n" +
                        "// After — option 2: ArrayPool inside loop:\nfor (int i = 0; i < n; i++)\n{\n    var buf = ArrayPool<byte>.Shared.Rent(256);\n    try { ... } finally { ArrayPool<byte>.Shared.Return(buf); }\n}"));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking stackalloc in loop in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckPointerArithmetic — pointer +/- without bounds check (Medium)
        // ──────────────────────────────────────────────────────────────────────

        private List<UnsafeCodeIssue> CheckPointerArithmetic(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<UnsafeCodeIssue>();

            var binaryExprs = root.DescendantNodes()
                .OfType<BinaryExpressionSyntax>()
                .Where(b => b.IsKind(SyntaxKind.AddExpression) || b.IsKind(SyntaxKind.SubtractExpression));

            foreach (var expr in binaryExprs)
            {
                try
                {
                    // Must be inside unsafe context
                    bool inUnsafe = expr.Ancestors()
                        .Any(a => a is UnsafeStatementSyntax ||
                                  (a is MethodDeclarationSyntax m && m.Modifiers.Any(mod => mod.IsKind(SyntaxKind.UnsafeKeyword))));
                    if (!inUnsafe) continue;

                    // Check if operand types involve a pointer
                    var leftType = model.GetTypeInfo(expr.Left).Type;
                    var rightType = model.GetTypeInfo(expr.Right).Type;

                    bool isPointerArith = (leftType?.TypeKind == TypeKind.Pointer) ||
                                          (rightType?.TypeKind == TypeKind.Pointer);
                    if (!isPointerArith) continue;

                    // Check if the containing method has any bounds check (if/Guard/throw)
                    var containingMethod = expr.Ancestors()
                        .OfType<BaseMethodDeclarationSyntax>()
                        .FirstOrDefault();

                    if (containingMethod == null) continue;

                    bool hasBoundsCheck = containingMethod.DescendantNodes()
                        .OfType<IfStatementSyntax>()
                        .Any(ifStmt =>
                        {
                            var condText = ifStmt.Condition.ToString();
                            return condText.Contains("Length") ||
                                   condText.Contains("Count") ||
                                   condText.Contains("size") ||
                                   condText.Contains("capacity") ||
                                   condText.Contains(">") ||
                                   condText.Contains("<") ||
                                   condText.Contains(">=") ||
                                   condText.Contains("<=");
                        });

                    if (hasBoundsCheck) continue;

                    issues.Add(CreateIssue(
                        "PointerArithmetic", "Medium",
                        $"Pointer arithmetic '{expr}' without bounds check",
                        "Pointer arithmetic without an explicit bounds check can result in buffer overread/overwrite, " +
                        "leading to memory corruption or security vulnerabilities.",
                        tree, expr, project, expr.ToString(),
                        "Add explicit bounds validation before performing pointer arithmetic.",
                        "// Add bounds check before pointer arithmetic:\nif ((nuint)(ptr + offset) > (nuint)(bufStart + bufLength))\n    throw new ArgumentOutOfRangeException(nameof(offset));"));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking pointer arithmetic in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckVoidPointerCast — void* cast to concrete type (High)
        // ──────────────────────────────────────────────────────────────────────

        private List<UnsafeCodeIssue> CheckVoidPointerCast(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<UnsafeCodeIssue>();

            foreach (var cast in root.DescendantNodes().OfType<CastExpressionSyntax>())
            {
                try
                {
                    // Check if we're casting FROM void*
                    var exprType = model.GetTypeInfo(cast.Expression).Type;
                    if (exprType == null) continue;

                    bool isFromVoidPtr = exprType.TypeKind == TypeKind.Pointer &&
                                         exprType is IPointerTypeSymbol ptrType &&
                                         ptrType.PointedAtType.SpecialType == SpecialType.System_Void;

                    if (!isFromVoidPtr) continue;

                    // Check if cast target is also a pointer (void* → T*)
                    var castType = model.GetTypeInfo(cast).Type;
                    bool isToTypedPtr = castType?.TypeKind == TypeKind.Pointer;

                    if (!isToTypedPtr) continue;

                    issues.Add(CreateIssue(
                        "VoidPointerCast", "High",
                        $"void* cast to typed pointer '{cast.Type}' — alignment risk",
                        "Casting void* to a typed pointer assumes the memory is properly aligned for the target type. " +
                        "Misaligned access can cause hardware faults on ARM/RISC-V and silent data corruption on x86.",
                        tree, cast, project, cast.ToString(),
                        "Verify alignment before casting, or use Unsafe.ReadUnaligned<T>() for unaligned reads.",
                        "// Before: T* typed = (T*)voidPtr;\n" +
                        "// After (safe unaligned):\nT value = Unsafe.ReadUnaligned<T>(voidPtr);\n" +
                        "// Or verify alignment first:\nif ((nuint)voidPtr % (nuint)sizeof(T) != 0)\n    throw new InvalidOperationException(\"Misaligned pointer\");"));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking void* cast in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckCrossThreadPointer — unsafe pointer captured in lambda/Task (High)
        // ──────────────────────────────────────────────────────────────────────

        private List<UnsafeCodeIssue> CheckCrossThreadPointer(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<UnsafeCodeIssue>();

            // Find lambdas/anonymous methods inside unsafe contexts that capture pointer variables
            var lambdas = root.DescendantNodes()
                .Where(n => n is LambdaExpressionSyntax || n is AnonymousMethodExpressionSyntax)
                .ToList();

            foreach (var lambda in lambdas)
            {
                try
                {
                    // Check if ancestor is unsafe
                    bool inUnsafe = lambda.Ancestors()
                        .Any(a => a is UnsafeStatementSyntax ||
                                  (a is MethodDeclarationSyntax m && m.Modifiers.Any(mod => mod.IsKind(SyntaxKind.UnsafeKeyword))));
                    if (!inUnsafe) continue;

                    // Check if lambda text references pointer-like identifiers
                    var lambdaText = lambda.ToString();
                    bool usesPointer = lambdaText.Contains("->") ||
                                       lambdaText.Contains("*") ||
                                       lambdaText.Contains("ptr") ||
                                       lambdaText.Contains("Ptr");
                    if (!usesPointer) continue;

                    // Check if it's passed to Task.Run / ThreadPool / new Thread
                    var invocationParent = lambda.Ancestors()
                        .OfType<InvocationExpressionSyntax>()
                        .FirstOrDefault();

                    if (invocationParent == null) continue;

                    var invText = invocationParent.Expression.ToString();
                    bool isCrossThread = invText.Contains("Task.Run") ||
                                         invText.Contains("Task.Factory") ||
                                         invText.Contains("ThreadPool") ||
                                         invText.Contains("new Thread") ||
                                         invText.Contains("Parallel.");
                    if (!isCrossThread) continue;

                    issues.Add(CreateIssue(
                        "CrossThreadPointer", "High",
                        "Unsafe pointer used in cross-thread lambda — potential use-after-free",
                        "Capturing an unsafe pointer in a Task.Run/ThreadPool lambda is dangerous: the original stack frame " +
                        "may be freed before the lambda executes, leading to use-after-free and memory corruption.",
                        tree, lambda, project, invText,
                        "Do not capture unsafe pointers across thread boundaries. Copy the data first.",
                        "// Before (dangerous):\nTask.Run(() => { Process(ptr); });\n\n" +
                        "// After (safe):\nvar copy = new byte[length];\nnew Span<byte>(ptr, length).CopyTo(copy);\nTask.Run(() => { Process(copy); });"));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking cross-thread pointer in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckOversizedFixed — fixed block spanning many statements (Medium)
        // ──────────────────────────────────────────────────────────────────────

        private List<UnsafeCodeIssue> CheckOversizedFixed(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<UnsafeCodeIssue>();

            const int FixedStatementCountThreshold = 10;

            foreach (var fixedStmt in root.DescendantNodes().OfType<FixedStatementSyntax>())
            {
                try
                {
                    // Count statements inside the fixed block
                    var body = fixedStmt.Statement;
                    int statementCount = body is BlockSyntax block
                        ? block.Statements.Count
                        : 1;

                    // Also count descendant invocations as a proxy for complexity
                    int invocationCount = body.DescendantNodes()
                        .OfType<InvocationExpressionSyntax>()
                        .Count();

                    int complexity = statementCount + invocationCount;
                    if (complexity <= FixedStatementCountThreshold) continue;

                    issues.Add(CreateIssue(
                        "OversizedFixed", "Medium",
                        $"fixed block is large ({complexity} statements/calls) — GC pressure risk",
                        "A fixed block pins managed objects, preventing the GC from moving them. " +
                        "Large fixed blocks increase GC pause times and heap fragmentation. " +
                        "Keep fixed blocks as small as possible.",
                        tree, fixedStmt, project, "fixed",
                        "Extract the work inside the fixed block into the minimal scope needed for the pointer.",
                        "// Before: large fixed block\nfixed (byte* ptr = array)\n{\n    // many operations...\n}\n\n" +
                        "// After: minimal fixed scope\nbyte localCopy;\nfixed (byte* ptr = array)\n    localCopy = *ptr; // only what needs the pointer\n// Do the rest with localCopy outside fixed"));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking fixed block in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckUnsafeAsync — unsafe method that is also async (High)
        // ──────────────────────────────────────────────────────────────────────

        private List<UnsafeCodeIssue> CheckUnsafeAsync(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<UnsafeCodeIssue>();

            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                try
                {
                    bool isUnsafe = method.Modifiers.Any(m => m.IsKind(SyntaxKind.UnsafeKeyword));
                    bool isAsync = method.Modifiers.Any(m => m.IsKind(SyntaxKind.AsyncKeyword));

                    if (!isUnsafe || !isAsync) continue;

                    issues.Add(CreateIssue(
                        "UnsafeAsync", "High",
                        $"Method '{method.Identifier.Text}' is both unsafe and async — pointer safety risk",
                        "An async method may resume on a different thread after an await, and the GC may move managed objects " +
                        "between suspension points. Pointers obtained before an await may be invalid after resumption. " +
                        "C# prevents using pointers directly across await points, but the combination is still fragile.",
                        tree, method, project, method.Identifier.Text,
                        "Separate the unsafe work into a synchronous helper method and call it from the async method.",
                        $"// Before:\nprivate unsafe async Task {method.Identifier.Text}()\n{{\n    byte* ptr = ...\n    await SomeOperationAsync(); // ptr may be invalid after this\n}}\n\n" +
                        $"// After:\nprivate unsafe void DoUnsafeWork(byte* ptr) {{ ... }}\n\nprivate async Task {method.Identifier.Text}()\n{{\n    DoUnsafeWork(GetPointer()); // synchronous unsafe section\n    await SomeOperationAsync();\n}}"));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking unsafe+async in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CreateIssue helper
        // ──────────────────────────────────────────────────────────────────────

        private static UnsafeCodeIssue CreateIssue(
            string issueType, string severity, string title, string description,
            SyntaxTree tree, SyntaxNode node,
            string project, string symbolName,
            string recommendation, string fixExample)
        {
            var lineSpan = tree.GetLineSpan(node.Span);
            var line = lineSpan.StartLinePosition.Line + 1;
            var rawText = node.ToString();
            var codeSnippet = rawText.Length > 100 ? rawText[..100] + "…" : rawText;

            return new UnsafeCodeIssue
            {
                IssueType = issueType,
                Severity = severity,
                Title = title,
                Description = description,
                FilePath = tree.FilePath,
                FileName = Path.GetFileName(tree.FilePath),
                ProjectName = project,
                LineNumber = line,
                SymbolName = symbolName,
                CodeSnippet = codeSnippet.Trim(),
                Recommendation = recommendation,
                FixExample = fixExample
            };
        }
    }
}
