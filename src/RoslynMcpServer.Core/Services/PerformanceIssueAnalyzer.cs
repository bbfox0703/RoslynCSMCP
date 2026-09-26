using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Core.Models;
using System.Collections.Concurrent;

namespace RoslynMcpServer.Core.Services
{
    /// <summary>
    /// Service for analyzing performance issues in code
    /// </summary>
    public class PerformanceIssueAnalyzer
    {
        private readonly ILogger<PerformanceIssueAnalyzer> _logger;
        private readonly CodeAnalysisService _codeAnalysis;

        public PerformanceIssueAnalyzer(
            ILogger<PerformanceIssueAnalyzer> logger,
            CodeAnalysisService codeAnalysis)
        {
            _logger = logger;
            _codeAnalysis = codeAnalysis;
        }

        /// <summary>
        /// Analyzes a solution for performance issues
        /// </summary>
        public async Task<PerformanceIssueResults> AnalyzePerformanceIssuesAsync(
            string solutionPath,
            string[]? issueTypes = null)
        {
            var results = new PerformanceIssueResults();
            var allIssues = new ConcurrentBag<PerformanceIssue>();
            int failedProjects = 0;

            try
            {
                var solution = await _codeAnalysis.GetSolutionAsync(solutionPath);

                var projects = solution.Projects
                    .Where(p => p.SupportsCompilation)
                    .ToList();

                results.AnalyzedProjects = projects.Count;

                // Analyze each project
                var projectTasks = projects.Select(async project =>
                {
                    try
                    {
                        var issues = await AnalyzeProjectAsync(project, issueTypes);
                        foreach (var issue in issues)
                        {
                            allIssues.Add(issue);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to analyze project: {ProjectName}", project.Name);
                        Interlocked.Increment(ref failedProjects);
                    }
                });

                await Task.WhenAll(projectTasks);

                results.FailedProjects = failedProjects;

                // A file compiled into several projects (multi-targeting, linked files) is reported once.
                results.Issues = allIssues
                    .GroupBy(i => (i.FilePath, i.LineNumber, i.IssueType, i.Title))
                    .Select(g => g.OrderBy(i => i.ProjectName, StringComparer.Ordinal).First())
                    .ToList();
                results.AnalyzedFiles = results.Issues.Select(i => i.FilePath).Distinct().Count();

                // Calculate statistics
                CalculateStatistics(results);

                _logger.LogInformation(
                    "Performance analysis complete: {IssueCount} issues found across {ProjectCount} projects",
                    results.TotalIssues,
                    results.AnalyzedProjects);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing performance issues");
                results.Warnings.Add(new OperationWarning
                {
                    Context = "Analysis",
                    Message = $"Error: {ex.Message}"
                });
            }

            return results;
        }

        /// <summary>
        /// Analyzes a single project for performance issues
        /// </summary>
        private async Task<List<PerformanceIssue>> AnalyzeProjectAsync(Project project, string[]? issueTypes)
        {
            var issues = new List<PerformanceIssue>();

            var compilation = await project.GetCompilationAsync();
            if (compilation == null)
                return issues;

            foreach (var syntaxTree in compilation.SyntaxTrees)
            {
                try
                {
                    var semanticModel = compilation.GetSemanticModel(syntaxTree);
                    var root = await syntaxTree.GetRootAsync();

                    // Analyze different types of issues
                    if (issueTypes == null || issueTypes.Contains("LinqMisuse"))
                    {
                        issues.AddRange(AnalyzeLinqMisuse(root, semanticModel, syntaxTree, project.Name));
                    }

                    if (issueTypes == null || issueTypes.Contains("StringConcatenation"))
                    {
                        issues.AddRange(AnalyzeStringConcatenation(root, semanticModel, syntaxTree, project.Name));
                    }

                    if (issueTypes == null || issueTypes.Contains("SyncOverAsync"))
                    {
                        issues.AddRange(AnalyzeSyncOverAsync(root, syntaxTree, project.Name));
                    }

                    if (issueTypes == null || issueTypes.Contains("DisposableNotDisposed"))
                    {
                        issues.AddRange(AnalyzeDisposableNotDisposed(root, semanticModel, syntaxTree, project.Name));
                    }

                    if (issueTypes == null || issueTypes.Contains("ExceptionHandling"))
                    {
                        issues.AddRange(AnalyzeExceptionHandling(root, semanticModel, syntaxTree, project.Name));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to analyze syntax tree: {FilePath}", syntaxTree.FilePath);
                }
            }

            return issues;
        }

        /// <summary>
        /// Analyzes LINQ misuse patterns
        /// </summary>
        internal static List<PerformanceIssue> AnalyzeLinqMisuse(
            SyntaxNode root,
            SemanticModel semanticModel,
            SyntaxTree syntaxTree,
            string projectName)
        {
            var issues = new List<PerformanceIssue>();

            // Find all invocation expressions
            var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();

            foreach (var invocation in invocations)
            {
                try
                {
                    var symbolInfo = semanticModel.GetSymbolInfo(invocation);
                    var methodSymbol = symbolInfo.Symbol as IMethodSymbol;

                    if (methodSymbol == null)
                        continue;

                    var methodName = methodSymbol.Name;

                    // Check for Count() followed by Any() or iteration
                    if (methodName == "Count" && IsLinqMethod(methodSymbol))
                    {
                        issues.Add(CreateIssue(
                            "LinqMisuse",
                            "Medium",
                            "Inefficient Count() usage",
                            "Using Count() when Any() would suffice. Count() enumerates entire collection.",
                            syntaxTree,
                            invocation,
                            projectName,
                            "Use Any() instead of Count() > 0 for existence checks",
                            "if (collection.Any()) instead of if (collection.Count() > 0)",
                            5.0));
                    }

                    // Check for multiple ToList() calls
                    if (methodName == "ToList" && IsLinqMethod(methodSymbol))
                    {
                        var parent = invocation.Parent;
                        while (parent != null)
                        {
                            if (parent is InvocationExpressionSyntax parentInvocation)
                            {
                                var parentSymbol = semanticModel.GetSymbolInfo(parentInvocation).Symbol as IMethodSymbol;
                                if (parentSymbol?.Name == "ToList" && IsLinqMethod(parentSymbol))
                                {
                                    issues.Add(CreateIssue(
                                        "LinqMisuse",
                                        "High",
                                        "Multiple ToList() calls",
                                        "Multiple ToList() calls create unnecessary copies of collections.",
                                        syntaxTree,
                                        invocation,
                                        projectName,
                                        "Remove unnecessary ToList() calls and call it once at the end",
                                        "var result = collection.Where(x => x.IsActive).ToList();",
                                        7.0));
                                    break;
                                }
                            }
                            parent = parent.Parent;
                        }
                    }

                    // Check for ToList() in foreach
                    if (methodName == "ToList" && IsLinqMethod(methodSymbol))
                    {
                        var foreachParent = invocation.Ancestors().OfType<ForEachStatementSyntax>().FirstOrDefault();
                        if (foreachParent != null)
                        {
                            issues.Add(CreateIssue(
                                "LinqMisuse",
                                "Medium",
                                "Unnecessary ToList() in foreach",
                                "ToList() in foreach creates an unnecessary intermediate list.",
                                syntaxTree,
                                invocation,
                                projectName,
                                "Remove ToList() and iterate directly over IEnumerable",
                                "foreach (var item in collection.Where(x => x.IsActive))",
                                6.0));
                        }
                    }
                }
                catch
                {
                    // Skip if analysis fails
                }
            }

            return issues;
        }

        /// <summary>
        /// Reports string accumulation inside loops: `s += x` or `s = s + x` where s is a string
        /// that outlives the innermost enclosing loop. Each assignment is reported once, however
        /// deeply the loops nest; strings declared inside the loop body start fresh on every
        /// iteration and are not reported.
        /// </summary>
        internal static List<PerformanceIssue> AnalyzeStringConcatenation(
            SyntaxNode root,
            SemanticModel semanticModel,
            SyntaxTree syntaxTree,
            string projectName)
        {
            var issues = new List<PerformanceIssue>();

            foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (!IsStringAccumulation(assignment, semanticModel))
                    continue;

                var loop = GetInnermostLoop(assignment);
                if (loop == null)
                    continue;

                // A string declared inside the loop does not accumulate across iterations.
                var target = semanticModel.GetSymbolInfo(assignment.Left).Symbol;
                if (target is ILocalSymbol local &&
                    local.DeclaringSyntaxReferences.FirstOrDefault() is { } declaration &&
                    declaration.SyntaxTree == loop.SyntaxTree &&
                    loop.Span.Contains(declaration.Span))
                {
                    continue;
                }

                issues.Add(CreateIssue(
                    "StringConcatenation",
                    "High",
                    "String concatenation in loop",
                    $"'{assignment.Left}' is a string rebuilt on every iteration, creating a new string object each time.",
                    syntaxTree,
                    assignment,
                    projectName,
                    "Use StringBuilder for string concatenation in loops",
                    "var sb = new StringBuilder(); sb.Append(value);",
                    8.0));
            }

            return issues;
        }

        private static bool IsStringAccumulation(AssignmentExpressionSyntax assignment, SemanticModel semanticModel)
        {
            if (assignment.IsKind(SyntaxKind.AddAssignmentExpression))
            {
                return semanticModel.GetTypeInfo(assignment.Left).Type?.SpecialType == SpecialType.System_String;
            }

            // s = s + x (the target must be the leftmost operand of the + chain)
            if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                assignment.Right is BinaryExpressionSyntax binary &&
                binary.IsKind(SyntaxKind.AddExpression))
            {
                if (semanticModel.GetTypeInfo(assignment.Left).Type?.SpecialType != SpecialType.System_String)
                    return false;

                ExpressionSyntax leftmost = binary;
                while (leftmost is BinaryExpressionSyntax b && b.IsKind(SyntaxKind.AddExpression))
                    leftmost = b.Left;

                var target = semanticModel.GetSymbolInfo(assignment.Left).Symbol;
                var operand = semanticModel.GetSymbolInfo(leftmost).Symbol;
                return target != null && SymbolEqualityComparer.Default.Equals(target, operand);
            }

            return false;
        }

        /// <summary>
        /// Innermost loop statement around the node within the same function body.
        /// </summary>
        private static StatementSyntax? GetInnermostLoop(SyntaxNode node)
        {
            foreach (var ancestor in node.Ancestors())
            {
                switch (ancestor)
                {
                    case ForStatementSyntax:
                    case CommonForEachStatementSyntax:
                    case WhileStatementSyntax:
                    case DoStatementSyntax:
                        return (StatementSyntax)ancestor;

                    case AnonymousFunctionExpressionSyntax:
                    case LocalFunctionStatementSyntax:
                    case MemberDeclarationSyntax:
                        return null;
                }
            }

            return null;
        }

        /// <summary>
        /// Analyzes sync over async patterns
        /// </summary>
        internal static List<PerformanceIssue> AnalyzeSyncOverAsync(
            SyntaxNode root,
            SyntaxTree syntaxTree,
            string projectName)
        {
            var issues = new List<PerformanceIssue>();

            // Find async methods
            var asyncMethods = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Modifiers.Any(SyntaxKind.AsyncKeyword));

            foreach (var method in asyncMethods)
            {
                // Look for .Result or .Wait() calls
                var memberAccesses = method.DescendantNodes().OfType<MemberAccessExpressionSyntax>();

                foreach (var access in memberAccesses)
                {
                    var memberName = access.Name.ToString();
                    if (memberName == "Result" || memberName == "Wait")
                    {
                        issues.Add(CreateIssue(
                            "SyncOverAsync",
                            "Critical",
                            "Sync-over-async: blocking in async method",
                            $"Using .{memberName}() in async method can cause deadlocks and thread pool starvation.",
                            syntaxTree,
                            access,
                            projectName,
                            $"Use await instead of .{memberName}()",
                            "await someTask; instead of someTask.Result or someTask.Wait()",
                            9.0));
                    }
                }
            }

            return issues;
        }

        /// <summary>
        /// Reports disposable objects the code creates and then loses:
        /// locals created in a method (new, a static factory, or a Create*/Open*/Begin* call) that are
        /// never disposed, returned, stored, or passed on, and instance fields the type creates itself
        /// but never disposes (or cannot dispose because the type is not IDisposable).
        /// </summary>
        internal static List<PerformanceIssue> AnalyzeDisposableNotDisposed(
            SyntaxNode root,
            SemanticModel semanticModel,
            SyntaxTree syntaxTree,
            string projectName)
        {
            var issues = new List<PerformanceIssue>();

            foreach (var declaration in root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
            {
                foreach (var (variable, local) in DisposableUsageAnalysis.FindUndisposedLocals(declaration, semanticModel))
                {
                    issues.Add(CreateIssue(
                        "DisposableNotDisposed",
                        "High",
                        "IDisposable not properly disposed",
                        $"Local '{variable.Identifier.Text}' ({local.Type.ToDisplayString()}) is created here but never disposed, returned, stored, or passed on.",
                        syntaxTree,
                        declaration,
                        projectName,
                        "Declare it with the using keyword or dispose it in a finally block",
                        "using var resource = new DisposableResource(); or using (var resource = new DisposableResource()) { }",
                        7.0));
                }
            }

            foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
            {
                foreach (var variable in field.Declaration.Variables)
                {
                    if (semanticModel.GetDeclaredSymbol(variable) is not IFieldSymbol fieldSymbol)
                        continue;

                    var issue = AnalyzeOwnedField(fieldSymbol, variable, semanticModel, syntaxTree, projectName);
                    if (issue != null)
                        issues.Add(issue);
                }
            }

            return issues;
        }

        private static PerformanceIssue? AnalyzeOwnedField(
            IFieldSymbol field,
            VariableDeclaratorSyntax variable,
            SemanticModel semanticModel,
            SyntaxTree syntaxTree,
            string projectName)
        {
            // Static fields usually live for the whole process; const fields are never disposable.
            if (field.IsStatic || field.IsConst || !DisposableUsageAnalysis.IsDisposableType(field.Type))
                return null;

            var containingType = field.ContainingType;
            var typeParts = containingType.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).ToList();
            var compilation = semanticModel.Compilation;

            // Only fields the type creates itself are its responsibility (injected ones are not).
            var owned = DisposableUsageAnalysis.IsOwnedCreation(variable.Initializer?.Value, semanticModel) ||
                        IsAssignedOwnedValue(field, typeParts, compilation);
            if (!owned)
                return null;

            var typeIsDisposable = DisposableUsageAnalysis.IsDisposableType(containingType);
            if (typeIsDisposable && DisposableUsageAnalysis.IsDisposedOrHandedOff(field, typeParts, compilation))
                return null;

            var description = typeIsDisposable
                ? $"Field '{field.Name}' ({field.Type.ToDisplayString()}) is created by '{containingType.Name}' but never disposed, for example in its Dispose method."
                : $"Field '{field.Name}' ({field.Type.ToDisplayString()}) is created by '{containingType.Name}', which does not implement IDisposable, so it is never disposed.";
            var recommendation = typeIsDisposable
                ? $"Dispose '{field.Name}' in {containingType.Name}.Dispose()"
                : $"Implement IDisposable on '{containingType.Name}' and dispose '{field.Name}' there";

            return CreateIssue(
                "DisposableNotDisposed",
                "High",
                "IDisposable field not disposed",
                description,
                syntaxTree,
                variable,
                projectName,
                recommendation,
                "public void Dispose() { _resource.Dispose(); }",
                6.0);
        }

        private static bool IsAssignedOwnedValue(IFieldSymbol field, List<SyntaxNode> typeParts, Compilation compilation)
        {
            foreach (var part in typeParts)
            {
                SemanticModel? model = null;
                foreach (var assignment in part.DescendantNodes().OfType<AssignmentExpressionSyntax>())
                {
                    if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                        !assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression))
                        continue;

                    var leftName = assignment.Left switch
                    {
                        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                        MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } access => access.Name.Identifier.ValueText,
                        _ => null
                    };
                    if (leftName != field.Name)
                        continue;

                    model ??= compilation.GetSemanticModel(part.SyntaxTree);
                    if (!SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(assignment.Left).Symbol, field))
                        continue;

                    if (DisposableUsageAnalysis.IsOwnedCreation(assignment.Right, model))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Reports empty catch blocks, once per catch clause.
        /// </summary>
        internal static List<PerformanceIssue> AnalyzeExceptionHandling(
            SyntaxNode root,
            SemanticModel semanticModel,
            SyntaxTree syntaxTree,
            string projectName)
        {
            var issues = new List<PerformanceIssue>();

            foreach (var catchClause in root.DescendantNodes().OfType<CatchClauseSyntax>())
            {
                if (catchClause.Block.Statements.Count != 0)
                    continue;

                var catchesEverything = catchClause.Declaration == null ||
                    semanticModel.GetTypeInfo(catchClause.Declaration.Type).Type?.ToDisplayString() == "System.Exception";

                issues.Add(CreateIssue(
                    "ExceptionHandling",
                    "High",
                    "Empty catch block",
                    catchesEverything
                        ? "Empty catch block silently swallows every exception, making debugging difficult."
                        : "Empty catch block silently swallows exceptions, making debugging difficult.",
                    syntaxTree,
                    catchClause,
                    projectName,
                    catchesEverything
                        ? "Catch a specific exception type, and log or rethrow what you catch"
                        : "Log exceptions or remove catch block if not needed",
                    "catch (IOException ex) { _logger.LogError(ex, \"Error occurred\"); }",
                    4.0));
            }

            return issues;
        }

        /// <summary>
        /// Checks if a method is a LINQ method
        /// </summary>
        private static bool IsLinqMethod(IMethodSymbol method)
        {
            var containingType = method.ContainingType;
            return containingType?.Name == "Enumerable" &&
                   containingType.ContainingNamespace?.ToDisplayString() == "System.Linq";
        }

        /// <summary>
        /// Creates a performance issue
        /// </summary>
        private static PerformanceIssue CreateIssue(
            string issueType,
            string severity,
            string title,
            string description,
            SyntaxTree syntaxTree,
            SyntaxNode node,
            string projectName,
            string recommendation,
            string fixExample,
            double impact)
        {
            var lineSpan = node.GetLocation().GetLineSpan();
            var line = lineSpan.StartLinePosition.Line + 1;

            // Get code snippet
            var codeSnippet = node.ToString();
            if (codeSnippet.Length > 100)
            {
                codeSnippet = codeSnippet.Substring(0, 100) + "...";
            }

            return new PerformanceIssue
            {
                IssueType = issueType,
                Severity = severity,
                Title = title,
                Description = description,
                FilePath = syntaxTree.FilePath,
                FileName = Path.GetFileName(syntaxTree.FilePath),
                ProjectName = projectName,
                LineNumber = line,
                MethodName = DisposableUsageAnalysis.GetEnclosingMemberName(node),
                CodeSnippet = codeSnippet.Trim(),
                Recommendation = recommendation,
                FixExample = fixExample,
                EstimatedImpact = impact
            };
        }

        /// <summary>
        /// Calculates statistics
        /// </summary>
        private void CalculateStatistics(PerformanceIssueResults results)
        {
            // Issues by type
            results.IssuesByType = results.Issues
                .GroupBy(i => i.IssueType)
                .ToDictionary(g => g.Key, g => g.Count());

            // Issues by project
            results.IssuesByProject = results.Issues
                .GroupBy(i => i.ProjectName)
                .ToDictionary(g => g.Key, g => g.Count());

            // Issues by file
            results.IssuesByFile = results.Issues
                .GroupBy(i => i.FilePath)
                .ToDictionary(g => g.Key, g => g.Count());
        }
    }
}
