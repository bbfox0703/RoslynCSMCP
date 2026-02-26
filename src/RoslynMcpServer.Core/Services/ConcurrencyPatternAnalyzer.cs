using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Core.Models;
using System.Collections.Concurrent;

namespace RoslynMcpServer.Core.Services
{
    /// <summary>
    /// Service for analyzing concurrency anti-patterns in C# code.
    /// Detects missing ConfigureAwait, undisposed timers, unbounded concurrency,
    /// missing/unpropagated CancellationTokens, thread-unsafe collections,
    /// unprotected static fields, and await-inside-lock patterns.
    /// </summary>
    public class ConcurrencyPatternAnalyzer
    {
        private readonly ILogger<ConcurrencyPatternAnalyzer> _logger;
        private readonly CodeAnalysisService _codeAnalysis;

        public ConcurrencyPatternAnalyzer(
            ILogger<ConcurrencyPatternAnalyzer> logger,
            CodeAnalysisService codeAnalysis)
        {
            _logger = logger;
            _codeAnalysis = codeAnalysis;
        }

        /// <summary>
        /// Analyzes a solution for concurrency anti-patterns.
        /// </summary>
        public async Task<ConcurrencyAnalysisResults> AnalyzeAsync(
            string solutionPath,
            string[] issueTypes)
        {
            var results = new ConcurrencyAnalysisResults();
            var allIssues = new ConcurrentBag<ConcurrencyIssue>();

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
                    "Concurrency analysis complete: {IssueCount} issues found across {ProjectCount} projects",
                    results.TotalIssues,
                    results.AnalyzedProjects);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing concurrency patterns");
                results.Warnings.Add(new OperationWarning
                {
                    Context = "Analysis",
                    Message = $"Error: {ex.Message}"
                });
            }

            return results;
        }

        private async Task<List<ConcurrencyIssue>> AnalyzeProjectAsync(
            Project project, string[] issueTypes)
        {
            var issues = new List<ConcurrencyIssue>();

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

                    if (ShouldCheck(issueTypes, "ConfigureAwait"))
                        issues.AddRange(CheckConfigureAwait(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "TimerDispose"))
                        issues.AddRange(CheckTimerDispose(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "UnboundedConcurrency"))
                        issues.AddRange(CheckUnboundedConcurrency(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "MissingCancellationToken"))
                        issues.AddRange(CheckMissingCancellationToken(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "CancellationNotPropagated"))
                        issues.AddRange(CheckCancellationNotPropagated(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "NonThreadSafeCollection"))
                        issues.AddRange(CheckNonThreadSafeCollection(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "StaticFieldLock"))
                        issues.AddRange(CheckStaticFieldLock(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "AwaitInLock"))
                        issues.AddRange(CheckAwaitInLock(root, semanticModel, syntaxTree, project.Name));
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
        // CheckConfigureAwait — await without .ConfigureAwait(false) (Medium)
        // ──────────────────────────────────────────────────────────────────────

        private List<ConcurrencyIssue> CheckConfigureAwait(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<ConcurrencyIssue>();

            foreach (var awaitExpr in root.DescendantNodes().OfType<AwaitExpressionSyntax>())
            {
                try
                {
                    var expr = awaitExpr.Expression;

                    // Skip if already calling .ConfigureAwait(...)
                    if (expr is InvocationExpressionSyntax inv &&
                        inv.Expression is MemberAccessExpressionSyntax member &&
                        member.Name.Identifier.Text == "ConfigureAwait")
                        continue;

                    // Only flag direct Task/ValueTask awaits (not already-configured)
                    var typeInfo = model.GetTypeInfo(expr);
                    var typeName = typeInfo.Type?.ToDisplayString() ?? string.Empty;
                    bool isTask = typeName.StartsWith("System.Threading.Tasks.Task") ||
                                  typeName.StartsWith("System.Threading.Tasks.ValueTask") ||
                                  typeName.Contains("Task<") ||
                                  typeName.Contains("ValueTask<");
                    if (!isTask) continue;

                    // Skip if inside a top-level async Main or UI event handler (heuristic)
                    var containingMethod = awaitExpr.Ancestors()
                        .OfType<MethodDeclarationSyntax>()
                        .FirstOrDefault();
                    if (containingMethod == null) continue;

                    var methodName = containingMethod.Identifier.Text;
                    // Skip common UI event handler names
                    if (methodName.EndsWith("_Click") || methodName.EndsWith("_Loaded") ||
                        methodName.EndsWith("_Changed") || methodName == "Main")
                        continue;

                    var snippet = expr.ToString();
                    issues.Add(CreateIssue(
                        "ConfigureAwait", "Medium",
                        $"await without .ConfigureAwait(false) in '{methodName}'",
                        "In library code, omitting .ConfigureAwait(false) captures the synchronization context " +
                        "and can cause deadlocks in UI/ASP.NET contexts when the caller blocks on the task.",
                        tree, awaitExpr, project, methodName,
                        "Add .ConfigureAwait(false) to awaited Tasks in library/service code.",
                        $"// Before: await {snippet};\n// After:  await {snippet}.ConfigureAwait(false);"));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking ConfigureAwait in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckTimerDispose — System.Threading.Timer not stopped in Dispose (High)
        // ──────────────────────────────────────────────────────────────────────

        private List<ConcurrencyIssue> CheckTimerDispose(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<ConcurrencyIssue>();

            foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                try
                {
                    // Find System.Threading.Timer fields
                    var timerFields = classDecl.Members
                        .OfType<FieldDeclarationSyntax>()
                        .Where(f =>
                        {
                            var typeName = f.Declaration.Type.ToString();
                            return typeName == "Timer" || typeName == "System.Threading.Timer";
                        })
                        .ToList();

                    if (timerFields.Count == 0) continue;

                    // Check if class has a Dispose method that stops the timer
                    var disposeMethods = classDecl.Members
                        .OfType<MethodDeclarationSyntax>()
                        .Where(m => m.Identifier.Text == "Dispose")
                        .ToList();

                    bool hasTimerStop = disposeMethods.Any(m =>
                    {
                        var body = m.ToString();
                        return body.Contains(".Dispose()") ||
                               body.Contains(".Change(") ||
                               body.Contains("Timeout.Infinite");
                    });

                    if (hasTimerStop) continue;

                    foreach (var timerField in timerFields)
                    {
                        var fieldName = timerField.Declaration.Variables
                            .FirstOrDefault()?.Identifier.Text ?? "timer";

                        issues.Add(CreateIssue(
                            "TimerDispose", "High",
                            $"System.Threading.Timer field '{fieldName}' in '{classDecl.Identifier.Text}' not stopped in Dispose",
                            "A System.Threading.Timer that is not stopped in Dispose will continue firing " +
                            "after the object is disposed, calling back into a potentially collected object " +
                            "and causing ObjectDisposedException or use-after-free.",
                            tree, timerField, project, fieldName,
                            "Implement IDisposable and call _timer.Change(Timeout.Infinite, 0) then _timer.Dispose() in Dispose().",
                            $"public void Dispose()\n{{\n    _{fieldName}?.Change(Timeout.Infinite, 0);\n    _{fieldName}?.Dispose();\n}}"));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking Timer dispose in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckUnboundedConcurrency — Task.WhenAll(seq.Select(...)) without SemaphoreSlim (Medium)
        // ──────────────────────────────────────────────────────────────────────

        private List<ConcurrencyIssue> CheckUnboundedConcurrency(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<ConcurrencyIssue>();

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                try
                {
                    var symbolInfo = model.GetSymbolInfo(invocation);
                    if (symbolInfo.Symbol is not IMethodSymbol methodSymbol) continue;

                    var containingType = methodSymbol.ContainingType?.ToDisplayString() ?? string.Empty;
                    if (containingType != "System.Threading.Tasks.Task") continue;
                    if (methodSymbol.Name != "WhenAll") continue;

                    // Check if argument involves .Select(
                    var argText = invocation.ArgumentList.ToString();
                    if (!argText.Contains(".Select(") && !argText.Contains(".SelectMany(")) continue;

                    // Check if the containing method has SemaphoreSlim
                    var containingMethod = invocation.Ancestors()
                        .OfType<BaseMethodDeclarationSyntax>()
                        .FirstOrDefault();
                    if (containingMethod == null) continue;

                    var methodText = containingMethod.ToString();
                    if (methodText.Contains("SemaphoreSlim") || methodText.Contains("Parallel.ForEach") ||
                        methodText.Contains("ParallelOptions")) continue;

                    issues.Add(CreateIssue(
                        "UnboundedConcurrency", "Medium",
                        $"Task.WhenAll with .Select() — unbounded concurrency, no SemaphoreSlim throttle",
                        "Task.WhenAll(items.Select(async x => ...)) launches all tasks simultaneously. " +
                        "For large collections this can exhaust thread pool, connections, or memory. " +
                        "A SemaphoreSlim limits maximum parallel operations.",
                        tree, invocation, project, "Task.WhenAll",
                        "Throttle with SemaphoreSlim to limit maximum concurrent tasks.",
                        "var sem = new SemaphoreSlim(maxConcurrency, maxConcurrency);\nawait Task.WhenAll(items.Select(async item =>\n{\n    await sem.WaitAsync();\n    try { await ProcessAsync(item); }\n    finally { sem.Release(); }\n}));"));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking unbounded concurrency in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckMissingCancellationToken — async method without CancellationToken (Low)
        // ──────────────────────────────────────────────────────────────────────

        private List<ConcurrencyIssue> CheckMissingCancellationToken(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<ConcurrencyIssue>();

            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                try
                {
                    if (!method.Modifiers.Any(m => m.IsKind(SyntaxKind.AsyncKeyword))) continue;

                    // Skip private methods — only public/internal/protected are caller-facing
                    bool isPublicFacing = method.Modifiers.Any(m =>
                        m.IsKind(SyntaxKind.PublicKeyword) ||
                        m.IsKind(SyntaxKind.InternalKeyword) ||
                        m.IsKind(SyntaxKind.ProtectedKeyword));
                    if (!isPublicFacing) continue;

                    // Skip override / interface implementations (token may be on base)
                    if (method.Modifiers.Any(m => m.IsKind(SyntaxKind.OverrideKeyword))) continue;

                    bool hasCt = method.ParameterList.Parameters
                        .Any(p => p.Type?.ToString() is "CancellationToken" or
                                  "System.Threading.CancellationToken" or
                                  "CancellationToken?");
                    if (hasCt) continue;

                    // Only flag methods with await inside (i.e., truly async, not just async void)
                    bool hasAwait = method.DescendantNodes()
                        .OfType<AwaitExpressionSyntax>()
                        .Any();
                    if (!hasAwait) continue;

                    issues.Add(CreateIssue(
                        "MissingCancellationToken", "Low",
                        $"Async method '{method.Identifier.Text}' missing CancellationToken parameter",
                        "Public async methods that perform I/O or long-running operations should accept a " +
                        "CancellationToken so callers can cancel them. Without it, the operation cannot be " +
                        "cancelled and may block indefinitely.",
                        tree, method, project, method.Identifier.Text,
                        "Add CancellationToken ct = default as the last parameter.",
                        $"// Before:\npublic async Task {method.Identifier.Text}(...)\n\n" +
                        $"// After:\npublic async Task {method.Identifier.Text}(..., CancellationToken ct = default)"));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking CancellationToken parameter in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckCancellationNotPropagated — token received but not passed on (Medium)
        // ──────────────────────────────────────────────────────────────────────

        private List<ConcurrencyIssue> CheckCancellationNotPropagated(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<ConcurrencyIssue>();

            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                try
                {
                    // Find methods that have a CancellationToken parameter
                    var ctParam = method.ParameterList.Parameters
                        .FirstOrDefault(p => p.Type?.ToString() is "CancellationToken" or
                                             "System.Threading.CancellationToken" or
                                             "CancellationToken?");
                    if (ctParam == null) continue;

                    var ctName = ctParam.Identifier.Text;

                    // Check if the method body has awaits
                    if (method.Body == null && method.ExpressionBody == null) continue;

                    var awaitExprs = method.DescendantNodes()
                        .OfType<AwaitExpressionSyntax>()
                        .ToList();
                    if (awaitExprs.Count == 0) continue;

                    // Check if the token name appears in any invocation argument
                    var bodyText = (method.Body?.ToString() ?? method.ExpressionBody?.ToString()) ?? string.Empty;
                    bool tokenPropagated = bodyText.Contains(ctName);

                    if (!tokenPropagated)
                    {
                        issues.Add(CreateIssue(
                            "CancellationNotPropagated", "Medium",
                            $"CancellationToken '{ctName}' in '{method.Identifier.Text}' is not propagated to awaited calls",
                            $"The method accepts '{ctName}' but does not pass it to any async calls inside. " +
                            "This means inner operations cannot be cancelled even if the token is signalled.",
                            tree, method, project, method.Identifier.Text,
                            $"Pass '{ctName}' to all async method calls inside '{method.Identifier.Text}'.",
                            $"// Pass the token downstream:\nawait InnerOperationAsync({ctName});\nawait stream.ReadAsync(buffer, {ctName});"));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking CancellationToken propagation in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckNonThreadSafeCollection — static List<T> / Dictionary<K,V> (High)
        // ──────────────────────────────────────────────────────────────────────

        private List<ConcurrencyIssue> CheckNonThreadSafeCollection(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<ConcurrencyIssue>();

            foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
            {
                try
                {
                    bool isStatic = field.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));
                    bool isReadonly = field.Modifiers.Any(m => m.IsKind(SyntaxKind.ReadOnlyKeyword));

                    if (!isStatic) continue;

                    var typeName = field.Declaration.Type.ToString();
                    bool isNonThreadSafe =
                        typeName.StartsWith("List<") ||
                        typeName.StartsWith("Dictionary<") ||
                        typeName.StartsWith("HashSet<") ||
                        typeName.StartsWith("Queue<") ||
                        typeName.StartsWith("Stack<") ||
                        typeName == "List" || typeName == "Dictionary" || typeName == "HashSet";

                    if (!isNonThreadSafe) continue;

                    var varName = field.Declaration.Variables.FirstOrDefault()?.Identifier.Text ?? "collection";

                    // readonly means the reference is fixed but contents still mutable
                    var note = isReadonly
                        ? " (readonly reference — contents are still not thread-safe)"
                        : " (mutable static — concurrent add/remove causes corruption)";

                    issues.Add(CreateIssue(
                        "NonThreadSafeCollection", "High",
                        $"Static {typeName} field '{varName}' is not thread-safe{note}",
                        $"Static {typeName} is not thread-safe for concurrent read/write. Multiple threads " +
                        "accessing it simultaneously without locking can cause data corruption, infinite loops, or exceptions.",
                        tree, field, project, varName,
                        $"Replace with a thread-safe equivalent (ConcurrentDictionary, ConcurrentBag, etc.) or protect with lock.",
                        $"// Before: private static {typeName} {varName} = new();\n\n" +
                        $"// After (option 1 — concurrent collection):\nprivate static readonly ConcurrentDictionary<TKey, TValue> {varName} = new();\n\n" +
                        $"// After (option 2 — lock):\nprivate static readonly object _{varName}Lock = new();\nprivate static readonly {typeName} {varName} = new();\n// Usage: lock (_{varName}Lock) {{ {varName}.Add(...); }}"));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking non-thread-safe collection in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckStaticFieldLock — mutable static field without volatile/lock/ThreadStatic (Medium)
        // ──────────────────────────────────────────────────────────────────────

        private List<ConcurrencyIssue> CheckStaticFieldLock(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<ConcurrencyIssue>();

            foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
            {
                try
                {
                    bool isStatic = field.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));
                    bool isReadonly = field.Modifiers.Any(m => m.IsKind(SyntaxKind.ReadOnlyKeyword));
                    bool isConst = field.Modifiers.Any(m => m.IsKind(SyntaxKind.ConstKeyword));
                    bool isVolatile = field.Modifiers.Any(m => m.IsKind(SyntaxKind.VolatileKeyword));

                    if (!isStatic || isReadonly || isConst || isVolatile) continue;

                    // Only flag primitive/simple value types (bool, int, long, etc.)
                    var typeName = field.Declaration.Type.ToString();
                    bool isSimpleType = typeName is "bool" or "int" or "long" or "uint" or "ulong" or
                                        "short" or "ushort" or "byte" or "sbyte" or "float" or "double";
                    if (!isSimpleType) continue;

                    // Check for [ThreadStatic] attribute
                    bool hasThreadStatic = field.AttributeLists
                        .SelectMany(al => al.Attributes)
                        .Any(a => a.Name.ToString() is "ThreadStatic" or "ThreadStaticAttribute");
                    if (hasThreadStatic) continue;

                    var varName = field.Declaration.Variables.FirstOrDefault()?.Identifier.Text ?? "field";

                    issues.Add(CreateIssue(
                        "StaticFieldLock", "Medium",
                        $"Mutable static field '{varName}' ({typeName}) accessed without synchronization",
                        $"A mutable static {typeName} field without volatile, lock, or [ThreadStatic] may be " +
                        "read/written by multiple threads simultaneously, causing torn reads, stale cached values, " +
                        "or race conditions.",
                        tree, field, project, varName,
                        "Use volatile for simple flags, Interlocked for counters, or lock for complex access patterns.",
                        $"// Option 1 — volatile (visibility only, no atomicity for compound ops):\nprivate static volatile {typeName} {varName};\n\n" +
                        $"// Option 2 — Interlocked (atomic increment/compare-exchange):\nInterlocked.Increment(ref {varName});\n\n" +
                        $"// Option 3 — [ThreadStatic] (per-thread copy):\n[ThreadStatic] private static {typeName} {varName};"));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking static field lock in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckAwaitInLock — await expression inside lock statement (High)
        // ──────────────────────────────────────────────────────────────────────

        private List<ConcurrencyIssue> CheckAwaitInLock(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<ConcurrencyIssue>();

            foreach (var awaitExpr in root.DescendantNodes().OfType<AwaitExpressionSyntax>())
            {
                try
                {
                    bool insideLock = awaitExpr.Ancestors()
                        .OfType<LockStatementSyntax>()
                        .Any();

                    if (!insideLock) continue;

                    var lockStmt = awaitExpr.Ancestors()
                        .OfType<LockStatementSyntax>()
                        .First();

                    issues.Add(CreateIssue(
                        "AwaitInLock", "High",
                        $"await inside lock('{lockStmt.Expression}') — deadlock risk",
                        "C# does not allow await inside a lock block (compiler error in some cases). " +
                        "Even when allowed via workarounds, holding a lock across an await can cause deadlocks " +
                        "because the lock is held while the thread is released to the pool. " +
                        "Use SemaphoreSlim.WaitAsync() instead of lock for async mutual exclusion.",
                        tree, awaitExpr, project, lockStmt.Expression.ToString(),
                        "Replace lock with SemaphoreSlim(1,1) and use await semaphore.WaitAsync() / semaphore.Release().",
                        "// Before (problematic):\nlock (_obj) { await DoWorkAsync(); }\n\n" +
                        "// After (correct):\nprivate readonly SemaphoreSlim _sem = new(1, 1);\nawait _sem.WaitAsync();\ntry { await DoWorkAsync(); }\nfinally { _sem.Release(); }"));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking await-in-lock in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CreateIssue helper
        // ──────────────────────────────────────────────────────────────────────

        private static ConcurrencyIssue CreateIssue(
            string issueType, string severity, string title, string description,
            SyntaxTree tree, SyntaxNode node,
            string project, string symbolName,
            string recommendation, string fixExample)
        {
            var lineSpan = tree.GetLineSpan(node.Span);
            var line = lineSpan.StartLinePosition.Line + 1;
            var rawText = node.ToString();
            var codeSnippet = rawText.Length > 100 ? rawText[..100] + "…" : rawText;

            return new ConcurrencyIssue
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
