using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Models;
using System.Collections.Concurrent;

namespace RoslynMcpServer.Services
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
                        results.FailedProjects++;
                    }
                });

                await Task.WhenAll(projectTasks);

                results.Issues = allIssues.ToList();
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
                        issues.AddRange(await AnalyzeLinqMisuseAsync(root, semanticModel, syntaxTree, project.Name));
                    }

                    if (issueTypes == null || issueTypes.Contains("StringConcatenation"))
                    {
                        issues.AddRange(AnalyzeStringConcatenation(root, syntaxTree, project.Name));
                    }

                    if (issueTypes == null || issueTypes.Contains("SyncOverAsync"))
                    {
                        issues.AddRange(AnalyzeSyncOverAsync(root, syntaxTree, project.Name));
                    }

                    if (issueTypes == null || issueTypes.Contains("DisposableNotDisposed"))
                    {
                        issues.AddRange(await AnalyzeDisposableNotDisposedAsync(root, semanticModel, syntaxTree, project.Name));
                    }

                    if (issueTypes == null || issueTypes.Contains("ExceptionHandling"))
                    {
                        issues.AddRange(AnalyzeExceptionHandling(root, syntaxTree, project.Name));
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
        private async Task<List<PerformanceIssue>> AnalyzeLinqMisuseAsync(
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

            return await Task.FromResult(issues);
        }

        /// <summary>
        /// Analyzes string concatenation in loops
        /// </summary>
        private List<PerformanceIssue> AnalyzeStringConcatenation(
            SyntaxNode root,
            SyntaxTree syntaxTree,
            string projectName)
        {
            var issues = new List<PerformanceIssue>();

            // Find all loops
            var loops = root.DescendantNodes()
                .Where(n => n is ForStatementSyntax || n is ForEachStatementSyntax || n is WhileStatementSyntax);

            foreach (var loop in loops)
            {
                // Look for string concatenation assignments (+=)
                var assignments = loop.DescendantNodes()
                    .OfType<AssignmentExpressionSyntax>()
                    .Where(a => a.IsKind(SyntaxKind.AddAssignmentExpression));

                foreach (var assignment in assignments)
                {
                    // Check if left side is potentially a string
                    var leftType = assignment.Left.ToString();
                    if (leftType.Contains("string") || leftType.Contains("str") || leftType.Contains("text"))
                    {
                        issues.Add(CreateIssue(
                            "StringConcatenation",
                            "High",
                            "String concatenation in loop",
                            "String concatenation in loops creates many intermediate string objects, causing poor performance.",
                            syntaxTree,
                            assignment,
                            projectName,
                            "Use StringBuilder for string concatenation in loops",
                            "var sb = new StringBuilder(); sb.Append(value);",
                            8.0));
                    }
                }
            }

            return issues;
        }

        /// <summary>
        /// Analyzes sync over async patterns
        /// </summary>
        private List<PerformanceIssue> AnalyzeSyncOverAsync(
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
        /// Analyzes IDisposable not disposed
        /// </summary>
        private async Task<List<PerformanceIssue>> AnalyzeDisposableNotDisposedAsync(
            SyntaxNode root,
            SemanticModel semanticModel,
            SyntaxTree syntaxTree,
            string projectName)
        {
            var issues = new List<PerformanceIssue>();

            // Find variable declarations
            var variableDeclarations = root.DescendantNodes().OfType<VariableDeclarationSyntax>();

            foreach (var declaration in variableDeclarations)
            {
                try
                {
                    var typeInfo = semanticModel.GetTypeInfo(declaration.Type);
                    var type = typeInfo.Type;

                    if (type != null && ImplementsIDisposable(type))
                    {
                        // Check if it's in a using statement
                        var usingStatement = declaration.Ancestors().OfType<UsingStatementSyntax>().FirstOrDefault();
                        var usingDeclaration = declaration.Parent as LocalDeclarationStatementSyntax;
                        var hasUsingModifier = usingDeclaration?.UsingKeyword != null;

                        if (usingStatement == null && !hasUsingModifier)
                        {
                            issues.Add(CreateIssue(
                                "DisposableNotDisposed",
                                "High",
                                "IDisposable not properly disposed",
                                $"Variable '{declaration.Variables.First().Identifier}' implements IDisposable but is not wrapped in using statement.",
                                syntaxTree,
                                declaration,
                                projectName,
                                "Wrap IDisposable objects in using statements or declare with using keyword",
                                "using var resource = new DisposableResource(); or using (var resource = new DisposableResource()) { }",
                                7.0));
                        }
                    }
                }
                catch
                {
                    // Skip if type info not available
                }
            }

            return await Task.FromResult(issues);
        }

        /// <summary>
        /// Analyzes exception handling anti-patterns
        /// </summary>
        private List<PerformanceIssue> AnalyzeExceptionHandling(
            SyntaxNode root,
            SyntaxTree syntaxTree,
            string projectName)
        {
            var issues = new List<PerformanceIssue>();

            // Find catch blocks that catch Exception without using it
            var catchClauses = root.DescendantNodes().OfType<CatchClauseSyntax>();

            foreach (var catchClause in catchClauses)
            {
                // Check for empty catch blocks
                if (catchClause.Block.Statements.Count == 0)
                {
                    issues.Add(CreateIssue(
                        "ExceptionHandling",
                        "High",
                        "Empty catch block",
                        "Empty catch blocks silently swallow exceptions, making debugging difficult.",
                        syntaxTree,
                        catchClause,
                        projectName,
                        "Log exceptions or remove catch block if not needed",
                        "catch (Exception ex) { _logger.LogError(ex, \"Error occurred\"); }",
                        4.0));
                }

                // Check for catch(Exception) without rethrow
                if (catchClause.Declaration?.Type.ToString() == "Exception")
                {
                    var hasThrow = catchClause.Block.DescendantNodes().OfType<ThrowStatementSyntax>().Any();
                    if (!hasThrow && catchClause.Block.Statements.Count == 0)
                    {
                        issues.Add(CreateIssue(
                            "ExceptionHandling",
                            "Medium",
                            "Catching Exception without handling",
                            "Catching base Exception type without proper handling can hide bugs.",
                            syntaxTree,
                            catchClause,
                            projectName,
                            "Catch specific exception types or add proper logging",
                            "catch (SpecificException ex) { ... }",
                            3.0));
                    }
                }
            }

            return issues;
        }

        /// <summary>
        /// Checks if a type implements IDisposable
        /// </summary>
        private bool ImplementsIDisposable(ITypeSymbol type)
        {
            return type.AllInterfaces.Any(i => i.Name == "IDisposable");
        }

        /// <summary>
        /// Checks if a method is a LINQ method
        /// </summary>
        private bool IsLinqMethod(IMethodSymbol method)
        {
            var containingType = method.ContainingType;
            return containingType?.Name == "Enumerable" &&
                   containingType.ContainingNamespace?.ToDisplayString() == "System.Linq";
        }

        /// <summary>
        /// Creates a performance issue
        /// </summary>
        private PerformanceIssue CreateIssue(
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

            // Get method name
            var methodNode = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            var methodName = methodNode?.Identifier.ToString() ?? "Unknown";

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
                MethodName = methodName,
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
