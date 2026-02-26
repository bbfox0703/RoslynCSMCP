using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Core.Models;
using System.Collections.Concurrent;

namespace RoslynMcpServer.Core.Services
{
    /// <summary>
    /// Service for detecting magic numbers (unexplained numeric literals) in C# code.
    /// Covers: hard-coded array indices, arithmetic offsets, collection capacities,
    /// comparison thresholds, and return codes — all candidates for named constants.
    /// Complements PInvokeCompatibilityAnalyzer's MagicOffset (unsafe pointer arithmetic)
    /// by covering general non-unsafe contexts.
    /// </summary>
    public class MagicNumberAnalyzer
    {
        private readonly ILogger<MagicNumberAnalyzer> _logger;
        private readonly CodeAnalysisService _codeAnalysis;

        // Values that are universally acceptable as inline literals
        private static readonly HashSet<long> _trivialValues = new()
        {
            0, 1, -1, 2
        };

        // Powers of two — acceptable in bitwise/shift contexts
        private static readonly HashSet<long> _powersOfTwo = new()
        {
            2, 4, 8, 16, 32, 64, 128, 256, 512, 1024, 2048, 4096,
            8192, 16384, 32768, 65536, 131072, 262144, 524288, 1048576
        };

        public MagicNumberAnalyzer(
            ILogger<MagicNumberAnalyzer> logger,
            CodeAnalysisService codeAnalysis)
        {
            _logger = logger;
            _codeAnalysis = codeAnalysis;
        }

        /// <summary>
        /// Analyzes a solution for magic numbers.
        /// </summary>
        public async Task<MagicNumberAnalysisResults> AnalyzeAsync(
            string solutionPath,
            string[] issueTypes)
        {
            var results = new MagicNumberAnalysisResults();
            var allIssues = new ConcurrentBag<MagicNumberIssue>();

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
                    "Magic number analysis complete: {IssueCount} issues found across {ProjectCount} projects",
                    results.TotalIssues,
                    results.AnalyzedProjects);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing magic numbers");
                results.Warnings.Add(new OperationWarning
                {
                    Context = "Analysis",
                    Message = $"Error: {ex.Message}"
                });
            }

            return results;
        }

        private async Task<List<MagicNumberIssue>> AnalyzeProjectAsync(
            Project project, string[] issueTypes)
        {
            var issues = new List<MagicNumberIssue>();

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

                    if (ShouldCheck(issueTypes, "ArrayIndexLiteral"))
                        issues.AddRange(CheckArrayIndexLiteral(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "ArithmeticLiteral"))
                        issues.AddRange(CheckArithmeticLiteral(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "HardcodedCapacity"))
                        issues.AddRange(CheckHardcodedCapacity(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "ComparisonLiteral"))
                        issues.AddRange(CheckComparisonLiteral(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "ReturnLiteral"))
                        issues.AddRange(CheckReturnLiteral(root, semanticModel, syntaxTree, project.Name));
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
        // Helper: try to parse a numeric literal expression into a long value
        // ──────────────────────────────────────────────────────────────────────

        private static bool TryGetLiteralValue(LiteralExpressionSyntax literal, out long value)
        {
            value = 0;
            if (!literal.IsKind(SyntaxKind.NumericLiteralExpression))
                return false;

            var token = literal.Token;
            try
            {
                value = Convert.ToInt64(token.Value);
                return true;
            }
            catch
            {
                // ulong too large, float, etc. — skip
                return false;
            }
        }

        private static bool IsInsideLoop(SyntaxNode node) =>
            node.Ancestors().Any(n =>
                n is ForStatementSyntax ||
                n is ForEachStatementSyntax ||
                n is WhileStatementSyntax ||
                n is DoStatementSyntax);

        private static bool IsInsideConstOrReadonlyDecl(SyntaxNode node)
        {
            // const field or local const
            var localDecl = node.Ancestors().OfType<LocalDeclarationStatementSyntax>().FirstOrDefault();
            if (localDecl != null && localDecl.Modifiers.Any(SyntaxKind.ConstKeyword))
                return true;

            var fieldDecl = node.Ancestors().OfType<FieldDeclarationSyntax>().FirstOrDefault();
            if (fieldDecl != null && fieldDecl.Modifiers.Any(SyntaxKind.ConstKeyword))
                return true;

            return false;
        }

        private static bool IsInsideAttribute(SyntaxNode node) =>
            node.Ancestors().Any(n => n is AttributeSyntax || n is AttributeArgumentSyntax);

        // ──────────────────────────────────────────────────────────────────────
        // CheckArrayIndexLiteral
        // Detects: array[42] — literal index > 1, not inside a for/foreach loop
        // ──────────────────────────────────────────────────────────────────────

        private List<MagicNumberIssue> CheckArrayIndexLiteral(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<MagicNumberIssue>();

            var elementAccesses = root.DescendantNodes()
                .OfType<ElementAccessExpressionSyntax>();

            foreach (var access in elementAccesses)
            {
                try
                {
                    // Only single-dimension access with a numeric literal index
                    if (access.ArgumentList.Arguments.Count != 1)
                        continue;

                    var indexExpr = access.ArgumentList.Arguments[0].Expression;
                    if (indexExpr is not LiteralExpressionSyntax literal)
                        continue;

                    if (!TryGetLiteralValue(literal, out long value))
                        continue;

                    // Only flag values > 1 — 0 and 1 are universally acceptable
                    if (value <= 1 || value < 0)
                        continue;

                    // Skip if inside a for/foreach (index is probably intentional)
                    if (IsInsideLoop(access))
                        continue;

                    // Skip if inside a const declaration
                    if (IsInsideConstOrReadonlyDecl(access))
                        continue;

                    // Skip attributes
                    if (IsInsideAttribute(access))
                        continue;

                    var snippet = access.ToString();
                    if (snippet.Length > 80) snippet = snippet[..80] + "…";

                    issues.Add(CreateIssue(
                        "ArrayIndexLiteral", "Medium",
                        $"Array index [{value}] is a magic number",
                        $"The numeric literal {value} is used directly as an array/indexer argument. " +
                        "Hard-coded indices make code brittle — if the data layout changes, every occurrence must be updated.",
                        tree, access, project, snippet, value,
                        $"Extract {value} to a named constant (e.g., 'const int {SuggestName("Index", value)} = {value};')."));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking array index literal in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckArithmeticLiteral
        // Detects: expr + 16, base - 8 — arithmetic with literal > 3, not power of 2 in shift
        // ──────────────────────────────────────────────────────────────────────

        private List<MagicNumberIssue> CheckArithmeticLiteral(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<MagicNumberIssue>();

            var binaryExprs = root.DescendantNodes()
                .OfType<BinaryExpressionSyntax>()
                .Where(b => b.IsKind(SyntaxKind.AddExpression) ||
                            b.IsKind(SyntaxKind.SubtractExpression));

            foreach (var binary in binaryExprs)
            {
                try
                {
                    // Find the literal operand
                    LiteralExpressionSyntax? literal = null;
                    if (binary.Right is LiteralExpressionSyntax r) literal = r;
                    else if (binary.Left is LiteralExpressionSyntax l) literal = l;

                    if (literal == null) continue;

                    if (!TryGetLiteralValue(literal, out long value))
                        continue;

                    // Only flag > 3 (allow 2, 1, 0)
                    if (Math.Abs(value) <= 3)
                        continue;

                    // Skip const declarations
                    if (IsInsideConstOrReadonlyDecl(binary))
                        continue;

                    // Skip attributes
                    if (IsInsideAttribute(binary))
                        continue;

                    // Skip if the literal is inside a for-loop initializer/increment
                    if (IsInsideLoop(binary))
                        continue;

                    // Skip if the parent is already an array creation (handled by HardcodedCapacity)
                    if (binary.Parent is ArrayRankSpecifierSyntax ||
                        binary.Parent is ArgumentSyntax arg &&
                        arg.Parent?.Parent is ObjectCreationExpressionSyntax)
                        continue;

                    var snippet = binary.ToString();
                    if (snippet.Length > 80) snippet = snippet[..80] + "…";

                    var op = binary.IsKind(SyntaxKind.AddExpression) ? "+" : "-";

                    issues.Add(CreateIssue(
                        "ArithmeticLiteral", "Medium",
                        $"Arithmetic offset literal {op}{value} — potential magic offset",
                        $"The value {value} is used directly in arithmetic. This may represent a struct field offset, " +
                        "vtable slot index, buffer stride, or calculation factor that should be named for clarity.",
                        tree, binary, project, snippet, value,
                        $"Extract {value} to a named constant (e.g., 'const int {SuggestName("Offset", value)} = {value};')."));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking arithmetic literal in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckHardcodedCapacity
        // Detects: new T[42], new List<T>(42) — literal capacity > 4
        // ──────────────────────────────────────────────────────────────────────

        private List<MagicNumberIssue> CheckHardcodedCapacity(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<MagicNumberIssue>();

            // new T[N]
            var arrayCreations = root.DescendantNodes()
                .OfType<ArrayCreationExpressionSyntax>();

            foreach (var creation in arrayCreations)
            {
                try
                {
                    if (creation.Initializer != null)
                        continue; // new T[] { ... } — initializer gives context

                    foreach (var rankSpec in creation.Type.RankSpecifiers)
                    {
                        foreach (var sizeExpr in rankSpec.Sizes)
                        {
                            if (sizeExpr is not LiteralExpressionSyntax literal)
                                continue;

                            if (!TryGetLiteralValue(literal, out long value))
                                continue;

                            if (value <= 4)
                                continue;

                            if (IsInsideConstOrReadonlyDecl(creation))
                                continue;

                            if (IsInsideAttribute(creation))
                                continue;

                            var typeName = creation.Type.ElementType.ToString();
                            var snippet = creation.ToString();
                            if (snippet.Length > 80) snippet = snippet[..80] + "…";

                            issues.Add(CreateIssue(
                                "HardcodedCapacity", "Medium",
                                $"new {typeName}[{value}] — hard-coded array size",
                                $"The array size {value} is a magic number. If this capacity ever needs to change, " +
                                "every occurrence must be found and updated manually.",
                                tree, creation, project, snippet, value,
                                $"Extract to a named constant (e.g., 'const int {SuggestName("Size", value)} = {value};')."));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking array creation capacity in {File}", tree.FilePath);
                }
            }

            // new List<T>(N), new Dictionary<K,V>(N), new HashSet<T>(N), etc.
            var objectCreations = root.DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>()
                .Where(o => o.ArgumentList != null && o.ArgumentList.Arguments.Count == 1);

            foreach (var creation in objectCreations)
            {
                try
                {
                    var typeSymbol = model.GetTypeInfo(creation).Type;
                    if (typeSymbol == null) continue;

                    var fullName = typeSymbol.OriginalDefinition?.ToDisplayString() ?? string.Empty;

                    // Generic collections that take capacity as first argument
                    if (!fullName.StartsWith("System.Collections.Generic.List<") &&
                        !fullName.StartsWith("System.Collections.Generic.Dictionary<") &&
                        !fullName.StartsWith("System.Collections.Generic.HashSet<") &&
                        !fullName.StartsWith("System.Collections.Generic.Queue<") &&
                        !fullName.StartsWith("System.Collections.Generic.Stack<") &&
                        !fullName.StartsWith("System.Text.StringBuilder"))
                        continue;

                    var argExpr = creation.ArgumentList!.Arguments[0].Expression;
                    if (argExpr is not LiteralExpressionSyntax literal)
                        continue;

                    if (!TryGetLiteralValue(literal, out long value))
                        continue;

                    if (value <= 4)
                        continue;

                    if (IsInsideConstOrReadonlyDecl(creation))
                        continue;

                    var collectionName = typeSymbol.Name;
                    var snippet = creation.ToString();
                    if (snippet.Length > 80) snippet = snippet[..80] + "…";

                    issues.Add(CreateIssue(
                        "HardcodedCapacity", "Low",
                        $"new {collectionName}({value}) — hard-coded initial capacity",
                        $"The initial capacity {value} passed to {collectionName} is a magic number. " +
                        "Named constants make the intent clearer and simplify future tuning.",
                        tree, creation, project, snippet, value,
                        $"Extract to a named constant (e.g., 'const int {SuggestName("InitialCapacity", value)} = {value};')."));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking collection capacity in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckComparisonLiteral
        // Detects: x == 42, count > 100 — non-trivial literals in equality/relational checks
        // ──────────────────────────────────────────────────────────────────────

        private List<MagicNumberIssue> CheckComparisonLiteral(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<MagicNumberIssue>();

            var binaryExprs = root.DescendantNodes()
                .OfType<BinaryExpressionSyntax>()
                .Where(b =>
                    b.IsKind(SyntaxKind.EqualsExpression) ||
                    b.IsKind(SyntaxKind.NotEqualsExpression) ||
                    b.IsKind(SyntaxKind.GreaterThanExpression) ||
                    b.IsKind(SyntaxKind.LessThanExpression) ||
                    b.IsKind(SyntaxKind.GreaterThanOrEqualExpression) ||
                    b.IsKind(SyntaxKind.LessThanOrEqualExpression));

            foreach (var binary in binaryExprs)
            {
                try
                {
                    LiteralExpressionSyntax? literal = null;
                    if (binary.Right is LiteralExpressionSyntax r) literal = r;
                    else if (binary.Left is LiteralExpressionSyntax l) literal = l;

                    if (literal == null) continue;

                    if (!TryGetLiteralValue(literal, out long value))
                        continue;

                    // Only flag values > 10 that are not common sentinels
                    if (Math.Abs(value) <= 10)
                        continue;

                    // Skip common "acceptable" boundary values
                    if (value == 100 || value == 1000 || value == 255 || value == 256)
                        continue;

                    // Skip powers of 2 (common bitmask/size checks)
                    if (_powersOfTwo.Contains(Math.Abs(value)))
                        continue;

                    // Skip hex literals (often intentional)
                    var tokenText = literal.Token.Text;
                    if (tokenText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Skip const declarations
                    if (IsInsideConstOrReadonlyDecl(binary))
                        continue;

                    // Skip attributes
                    if (IsInsideAttribute(binary))
                        continue;

                    var snippet = binary.ToString();
                    if (snippet.Length > 80) snippet = snippet[..80] + "…";

                    var op = binary.Kind() switch
                    {
                        SyntaxKind.EqualsExpression => "==",
                        SyntaxKind.NotEqualsExpression => "!=",
                        SyntaxKind.GreaterThanExpression => ">",
                        SyntaxKind.LessThanExpression => "<",
                        SyntaxKind.GreaterThanOrEqualExpression => ">=",
                        SyntaxKind.LessThanOrEqualExpression => "<=",
                        _ => "?"
                    };

                    issues.Add(CreateIssue(
                        "ComparisonLiteral", "Low",
                        $"Comparison threshold {op} {value} — potential magic number",
                        $"The literal {value} is used directly in a comparison. " +
                        "Named constants make the business rule or physical constraint explicit.",
                        tree, binary, project, snippet, value,
                        $"Extract {value} to a named constant (e.g., 'const int {SuggestName("Threshold", value)} = {value};')."));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking comparison literal in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckReturnLiteral
        // Detects: return 42; — non-trivial integer returns (magic exit/error codes)
        // ──────────────────────────────────────────────────────────────────────

        private List<MagicNumberIssue> CheckReturnLiteral(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<MagicNumberIssue>();

            var returnStmts = root.DescendantNodes()
                .OfType<ReturnStatementSyntax>()
                .Where(r => r.Expression is LiteralExpressionSyntax);

            foreach (var ret in returnStmts)
            {
                try
                {
                    var literal = (LiteralExpressionSyntax)ret.Expression!;
                    if (!TryGetLiteralValue(literal, out long value))
                        continue;

                    // Only flag > 1 (0 = success, 1 = generic error, -1 = not found are well-known)
                    if (_trivialValues.Contains(value))
                        continue;

                    // Skip hex literals (often intentional error codes)
                    var tokenText = literal.Token.Text;
                    if (tokenText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Get enclosing method return type
                    var method = ret.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
                    if (method == null)
                        continue;

                    var returnTypeSymbol = model.GetTypeInfo(method.ReturnType).Type;
                    // Only flag int/long return types (not bool, string, etc.)
                    if (returnTypeSymbol == null) continue;
                    if (returnTypeSymbol.SpecialType != SpecialType.System_Int32 &&
                        returnTypeSymbol.SpecialType != SpecialType.System_Int64 &&
                        returnTypeSymbol.SpecialType != SpecialType.System_Int16 &&
                        returnTypeSymbol.SpecialType != SpecialType.System_Byte)
                        continue;

                    if (IsInsideConstOrReadonlyDecl(ret))
                        continue;

                    var snippet = ret.ToString();
                    if (snippet.Length > 80) snippet = snippet[..80] + "…";

                    issues.Add(CreateIssue(
                        "ReturnLiteral", "Low",
                        $"return {value}; — magic return/exit code",
                        $"Returning the literal {value} directly is a magic number. " +
                        "Callers cannot understand the meaning of the return value without documentation.",
                        tree, ret, project, snippet, value,
                        $"Use a named constant or enum value (e.g., 'return ErrorCodes.{SuggestName("Error", value)};')."));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking return literal in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────────────────────────────

        private static string SuggestName(string prefix, long value)
        {
            // Try to generate a readable name suggestion
            if (value > 0 && value <= 9999)
                return $"{prefix}{value}";
            if (value < 0)
                return $"{prefix}Neg{Math.Abs(value)}";
            return prefix;
        }

        private static MagicNumberIssue CreateIssue(
            string issueType, string severity, string title, string description,
            SyntaxTree tree, SyntaxNode node,
            string project, string snippet, long literalValue, string recommendation)
        {
            var lineSpan = tree.GetLineSpan(node.Span);
            var line = lineSpan.StartLinePosition.Line + 1;

            return new MagicNumberIssue
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
                CodeSnippet = snippet.Trim(),
                LiteralValue = literalValue,
                Recommendation = recommendation
            };
        }
    }
}
