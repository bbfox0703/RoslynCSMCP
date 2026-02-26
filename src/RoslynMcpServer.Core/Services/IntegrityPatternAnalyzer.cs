using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Core.Models;
using System.Collections.Concurrent;

namespace RoslynMcpServer.Core.Services
{
    /// <summary>
    /// Service for detecting integrity verification and software protection patterns in C# code.
    /// Detects: SHA256/MD5 runtime sentinel checks, XOR string protection, hardcoded hash constants,
    /// anti-debug patterns, and sentinel magic byte comparisons.
    /// </summary>
    public class IntegrityPatternAnalyzer
    {
        private readonly ILogger<IntegrityPatternAnalyzer> _logger;
        private readonly CodeAnalysisService _codeAnalysis;

        public IntegrityPatternAnalyzer(
            ILogger<IntegrityPatternAnalyzer> logger,
            CodeAnalysisService codeAnalysis)
        {
            _logger = logger;
            _codeAnalysis = codeAnalysis;
        }

        /// <summary>
        /// Analyzes a solution for integrity verification and protection patterns.
        /// </summary>
        public async Task<IntegrityAnalysisResults> AnalyzeAsync(
            string solutionPath,
            string[] issueTypes)
        {
            var results = new IntegrityAnalysisResults();
            var allIssues = new ConcurrentBag<IntegrityPatternIssue>();

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
                    "Integrity pattern analysis complete: {IssueCount} patterns found across {ProjectCount} projects",
                    results.TotalIssues,
                    results.AnalyzedProjects);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing integrity patterns");
                results.Warnings.Add(new OperationWarning
                {
                    Context = "Analysis",
                    Message = $"Error: {ex.Message}"
                });
            }

            return results;
        }

        private async Task<List<IntegrityPatternIssue>> AnalyzeProjectAsync(
            Project project, string[] issueTypes)
        {
            var issues = new List<IntegrityPatternIssue>();

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

                    if (ShouldCheck(issueTypes, "Sha256Sentinel"))
                        issues.AddRange(CheckSha256Sentinel(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "XorStringProtection"))
                        issues.AddRange(CheckXorStringProtection(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "HardcodedChecksum"))
                        issues.AddRange(CheckHardcodedChecksum(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "AntiDebugPattern"))
                        issues.AddRange(CheckAntiDebugPattern(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "SentinelMagicBytes"))
                        issues.AddRange(CheckSentinelMagicBytes(root, semanticModel, syntaxTree, project.Name));
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
        // CheckSha256Sentinel
        // Detects SHA256/MD5/CRC runtime hash computations used as integrity sentinels
        // ──────────────────────────────────────────────────────────────────────

        private List<IntegrityPatternIssue> CheckSha256Sentinel(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<IntegrityPatternIssue>();

            var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();

            foreach (var invocation in invocations)
            {
                try
                {
                    var symbolInfo = model.GetSymbolInfo(invocation);
                    if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
                        continue;

                    var containingType = methodSymbol.ContainingType?.ToDisplayString() ?? string.Empty;
                    var methodName = methodSymbol.Name;

                    bool isCryptoHash =
                        (containingType.Contains("SHA256") || containingType.Contains("SHA1") ||
                         containingType.Contains("MD5") || containingType.Contains("SHA512")) &&
                        (methodName is "ComputeHash" or "HashData" or "TryComputeHash" or "TryHashData");

                    bool isCrc =
                        containingType.Contains("Crc") &&
                        (methodName.Contains("Compute") || methodName.Contains("Hash") || methodName.Contains("Calculate"));

                    if (!isCryptoHash && !isCrc)
                        continue;

                    var snippet = invocation.ToString();
                    if (snippet.Length > 100) snippet = snippet[..100] + "…";

                    var algoName = containingType.Split('.').Last();

                    issues.Add(CreateIssue(
                        "Sha256Sentinel", "Medium",
                        $"Runtime {algoName} hash computation — potential integrity sentinel",
                        $"A {algoName}.{methodName}() call computes a hash at runtime. " +
                        "This pattern is commonly used to verify file/assembly integrity (sentinel check). " +
                        "Ensure the hash source and reference value are protected against substitution.",
                        tree, invocation, project, snippet,
                        $"Verify that the reference hash constant is stored securely and the {algoName} computation covers the intended data region."));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking SHA256 sentinel in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckXorStringProtection
        // Detects XOR-based string or byte obfuscation patterns
        // ──────────────────────────────────────────────────────────────────────

        private List<IntegrityPatternIssue> CheckXorStringProtection(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<IntegrityPatternIssue>();

            // Pattern 1: array[i] ^= constant   or   x ^ literal
            var xorAssignments = root.DescendantNodes()
                .OfType<AssignmentExpressionSyntax>()
                .Where(a => a.IsKind(SyntaxKind.ExclusiveOrAssignmentExpression));

            foreach (var assignment in xorAssignments)
            {
                try
                {
                    // Right side should be a numeric literal (XOR key)
                    if (!assignment.Right.IsKind(SyntaxKind.NumericLiteralExpression))
                        continue;

                    // Left side should be array element access or variable
                    var leftTypeInfo = model.GetTypeInfo(assignment.Left);
                    if (leftTypeInfo.Type == null) continue;

                    var special = leftTypeInfo.Type.SpecialType;
                    if (special != SpecialType.System_Byte &&
                        special != SpecialType.System_Char &&
                        special != SpecialType.System_Int32 &&
                        special != SpecialType.System_UInt32)
                        continue;

                    // Must be inside a loop for it to be a meaningful XOR cipher
                    bool inLoop = assignment.Ancestors().Any(n =>
                        n is ForStatementSyntax ||
                        n is ForEachStatementSyntax ||
                        n is WhileStatementSyntax ||
                        n is DoStatementSyntax);

                    if (!inLoop)
                        continue;

                    var snippet = assignment.ToString();
                    if (snippet.Length > 80) snippet = snippet[..80] + "…";

                    issues.Add(CreateIssue(
                        "XorStringProtection", "Medium",
                        "XOR cipher pattern in loop — string/byte obfuscation detected",
                        "A XOR-assignment with a numeric key inside a loop is the classic XOR string/data obfuscation pattern. " +
                        "This is used to hide string literals, API names, or byte sequences from static analysis.",
                        tree, assignment, project, snippet,
                        "Review what data is being XOR-obfuscated and ensure the key is not trivially recoverable."));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking XOR assignment in {File}", tree.FilePath);
                }
            }

            // Pattern 2: binary XOR expression with char/byte literal on one side
            var xorBinaries = root.DescendantNodes()
                .OfType<BinaryExpressionSyntax>()
                .Where(b => b.IsKind(SyntaxKind.ExclusiveOrExpression));

            foreach (var xorExpr in xorBinaries)
            {
                try
                {
                    bool leftIsLiteral = xorExpr.Left.IsKind(SyntaxKind.NumericLiteralExpression) ||
                                         xorExpr.Left.IsKind(SyntaxKind.CharacterLiteralExpression);
                    bool rightIsLiteral = xorExpr.Right.IsKind(SyntaxKind.NumericLiteralExpression) ||
                                          xorExpr.Right.IsKind(SyntaxKind.CharacterLiteralExpression);

                    if (!leftIsLiteral && !rightIsLiteral)
                        continue;

                    // Check that the non-literal side is a char or byte type
                    var nonLiteralSide = leftIsLiteral ? xorExpr.Right : xorExpr.Left;
                    var typeInfo = model.GetTypeInfo(nonLiteralSide);
                    if (typeInfo.Type == null) continue;

                    var special = typeInfo.Type.SpecialType;
                    if (special != SpecialType.System_Byte &&
                        special != SpecialType.System_Char)
                        continue;

                    // Must be in a loop
                    bool inLoop = xorExpr.Ancestors().Any(n =>
                        n is ForStatementSyntax ||
                        n is ForEachStatementSyntax ||
                        n is WhileStatementSyntax);

                    if (!inLoop)
                        continue;

                    var snippet = xorExpr.ToString();
                    if (snippet.Length > 80) snippet = snippet[..80] + "…";

                    issues.Add(CreateIssue(
                        "XorStringProtection", "Low",
                        "XOR expression with literal key — byte/char decryption pattern",
                        "XOR of a byte or char value with a numeric/char literal inside a loop is a common decryption/obfuscation step.",
                        tree, xorExpr, project, snippet,
                        "Verify the purpose of this XOR operation and ensure keys are not exposed."));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking XOR binary in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckHardcodedChecksum
        // Detects byte[] initialized with exactly 16, 20, or 32 elements (MD5/SHA1/SHA256)
        // ──────────────────────────────────────────────────────────────────────

        private List<IntegrityPatternIssue> CheckHardcodedChecksum(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<IntegrityPatternIssue>();

            var arrayCreations = root.DescendantNodes()
                .OfType<ArrayCreationExpressionSyntax>()
                .Where(a => a.Initializer != null);

            foreach (var creation in arrayCreations)
            {
                try
                {
                    var typeInfo = model.GetTypeInfo(creation);
                    if (typeInfo.Type is not IArrayTypeSymbol arrayType)
                        continue;

                    if (arrayType.ElementType.SpecialType != SpecialType.System_Byte)
                        continue;

                    var elements = creation.Initializer!.Expressions;
                    var count = elements.Count;

                    // Hash sizes: MD5=16, SHA1=20, SHA256=32, SHA512=64
                    if (count != 16 && count != 20 && count != 32 && count != 64)
                        continue;

                    // All elements must be numeric literals
                    bool allLiterals = elements.All(e => e.IsKind(SyntaxKind.NumericLiteralExpression));
                    if (!allLiterals) continue;

                    var hashName = count switch
                    {
                        16 => "MD5",
                        20 => "SHA1",
                        32 => "SHA256",
                        64 => "SHA512",
                        _ => "hash"
                    };

                    var snippet = creation.ToString();
                    if (snippet.Length > 100) snippet = snippet[..100] + "…";

                    issues.Add(CreateIssue(
                        "HardcodedChecksum", "High",
                        $"Hardcoded {count}-byte array — likely {hashName} reference checksum",
                        $"A byte array of exactly {count} bytes initialized with all literal values matches the size of a {hashName} hash digest. " +
                        "This is a common pattern for embedding a reference checksum used in integrity verification.",
                        tree, creation, project, snippet,
                        $"Verify this is an intentional {hashName} reference constant and that it is kept in sync with the protected content."));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking hardcoded checksum in {File}", tree.FilePath);
                }
            }

            // Also check implicit array creation: new[] { 0x1A, 0x2B, ... }
            var implicitCreations = root.DescendantNodes()
                .OfType<ImplicitArrayCreationExpressionSyntax>()
                .Where(a => a.Initializer != null);

            foreach (var creation in implicitCreations)
            {
                try
                {
                    var typeInfo = model.GetTypeInfo(creation);
                    if (typeInfo.Type is not IArrayTypeSymbol arrayType)
                        continue;

                    if (arrayType.ElementType.SpecialType != SpecialType.System_Byte)
                        continue;

                    var elements = creation.Initializer.Expressions;
                    var count = elements.Count;

                    if (count != 16 && count != 20 && count != 32 && count != 64)
                        continue;

                    bool allLiterals = elements.All(e => e.IsKind(SyntaxKind.NumericLiteralExpression));
                    if (!allLiterals) continue;

                    var hashName = count switch
                    {
                        16 => "MD5",
                        20 => "SHA1",
                        32 => "SHA256",
                        64 => "SHA512",
                        _ => "hash"
                    };

                    var snippet = creation.ToString();
                    if (snippet.Length > 100) snippet = snippet[..100] + "…";

                    issues.Add(CreateIssue(
                        "HardcodedChecksum", "High",
                        $"Hardcoded {count}-byte array — likely {hashName} reference checksum",
                        $"An implicit byte array of exactly {count} bytes with all literal values matches the size of a {hashName} hash digest.",
                        tree, creation, project, snippet,
                        $"Verify this is an intentional {hashName} reference constant and ensure it is kept up to date."));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking implicit hardcoded checksum in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckAntiDebugPattern
        // Detects debugger detection and anti-tamper patterns
        // ──────────────────────────────────────────────────────────────────────

        private List<IntegrityPatternIssue> CheckAntiDebugPattern(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<IntegrityPatternIssue>();

            // Check member accesses: Debugger.IsAttached
            var memberAccesses = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>();

            foreach (var memberAccess in memberAccesses)
            {
                try
                {
                    var symbolInfo = model.GetSymbolInfo(memberAccess);
                    if (symbolInfo.Symbol is not IPropertySymbol propSymbol)
                        continue;

                    var containingType = propSymbol.ContainingType?.ToDisplayString() ?? string.Empty;
                    if (containingType != "System.Diagnostics.Debugger")
                        continue;

                    if (propSymbol.Name != "IsAttached")
                        continue;

                    var snippet = memberAccess.Parent?.ToString() ?? memberAccess.ToString();
                    if (snippet.Length > 100) snippet = snippet[..100] + "…";

                    issues.Add(CreateIssue(
                        "AntiDebugPattern", "High",
                        "Debugger.IsAttached — anti-debug detection pattern",
                        "Checking Debugger.IsAttached is a classic anti-debugging technique to detect and respond to debugger attachment. " +
                        "This pattern is used in software protection and anti-cheat systems.",
                        tree, memberAccess, project, snippet,
                        "Ensure this check serves a legitimate purpose (e.g., disabling debug-only code paths) and is not used for malicious anti-analysis."));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking Debugger.IsAttached in {File}", tree.FilePath);
                }
            }

            // Check invocations: Debugger.Launch(), Debugger.Break(), Environment.FailFast()
            var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();

            foreach (var invocation in invocations)
            {
                try
                {
                    var symbolInfo = model.GetSymbolInfo(invocation);
                    if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
                        continue;

                    var containingType = methodSymbol.ContainingType?.ToDisplayString() ?? string.Empty;
                    var methodName = methodSymbol.Name;

                    bool isAntiDebug =
                        (containingType == "System.Diagnostics.Debugger" &&
                         methodName is "Launch" or "Break") ||
                        (containingType == "System.Environment" &&
                         methodName == "FailFast");

                    if (!isAntiDebug)
                        continue;

                    var snippet = invocation.ToString();
                    if (snippet.Length > 100) snippet = snippet[..100] + "…";

                    var (severity, description) = methodName switch
                    {
                        "FailFast" => ("High",
                            "Environment.FailFast() terminates the process immediately, bypassing all cleanup. " +
                            "When called in response to detected tampering or debugging, this is an anti-tamper kill-switch."),
                        "Launch" => ("Medium",
                            "Debugger.Launch() attempts to attach a debugger to the process. " +
                            "In security contexts this can be used to trap reverse engineers."),
                        _ => ("Low",
                            $"Debugger.{methodName}() is a debugger control point that may indicate anti-debug logic.")
                    };

                    issues.Add(CreateIssue(
                        "AntiDebugPattern", severity,
                        $"{containingType.Split('.').Last()}.{methodName}() — anti-debug/anti-tamper pattern",
                        description,
                        tree, invocation, project, snippet,
                        "Review whether this call is part of legitimate security enforcement or a protection mechanism."));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking anti-debug invocations in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckSentinelMagicBytes
        // Detects large hex magic constants (0xDEADBEEF, 0xCAFEBABE, etc.) in comparisons
        // ──────────────────────────────────────────────────────────────────────

        private List<IntegrityPatternIssue> CheckSentinelMagicBytes(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<IntegrityPatternIssue>();

            // Find binary comparisons that contain large numeric literals
            var binaryExprs = root.DescendantNodes()
                .OfType<BinaryExpressionSyntax>()
                .Where(b =>
                    b.IsKind(SyntaxKind.EqualsExpression) ||
                    b.IsKind(SyntaxKind.NotEqualsExpression));

            foreach (var binary in binaryExprs)
            {
                try
                {
                    // Look for a numeric literal on either side
                    LiteralExpressionSyntax? literal = null;
                    if (binary.Left.IsKind(SyntaxKind.NumericLiteralExpression))
                        literal = (LiteralExpressionSyntax)binary.Left;
                    else if (binary.Right.IsKind(SyntaxKind.NumericLiteralExpression))
                        literal = (LiteralExpressionSyntax)binary.Right;

                    if (literal == null) continue;

                    var token = literal.Token;
                    // Only hex literals (0x...) with value > 0xFFFF are interesting
                    var tokenText = token.Text;
                    if (!tokenText.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                        !tokenText.StartsWith("0X", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Parse the value
                    long value = 0;
                    try
                    {
                        var hexStr = tokenText[2..].TrimEnd('u', 'U', 'l', 'L');
                        value = Convert.ToInt64(hexStr, 16);
                    }
                    catch
                    {
                        continue;
                    }

                    if (value <= 0xFFFF)
                        continue;

                    // Well-known magic constants
                    var (magicName, notes) = value switch
                    {
                        0xDEADBEEF => ("DEADBEEF", "Classic sentinel value used in memory debugging and integrity checks."),
                        0xCAFEBABE => ("CAFEBABE", "Java class file magic number; used as a sentinel in native interop."),
                        0xDEADC0DE => ("DEADC0DE", "Dead code sentinel value; used to mark invalidated memory regions."),
                        0xBAADF00D => ("BAADF00D", "Microsoft debug heap sentinel for uninitialized memory."),
                        0xFEEDFACE or 0xFEEDFACF => ("FEEDFACE/FEEDFACF", "Mach-O binary format magic number; used in native loader contexts."),
                        0xCDCDCDCD => ("CDCDCDCD", "MSVC debug heap sentinel for uninitialized heap memory."),
                        0x0BADF00D => ("0BADF00D", "Sentinel value for bad/corrupted food (memory) detection."),
                        _ => (tokenText, "Large hex constant used in comparison — possible sentinel or integrity check value.")
                    };

                    var snippet = binary.ToString();
                    if (snippet.Length > 100) snippet = snippet[..100] + "…";

                    issues.Add(CreateIssue(
                        "SentinelMagicBytes", "Low",
                        $"Magic constant {magicName} in comparison — sentinel/integrity pattern",
                        $"Comparing against the magic constant {tokenText} ({magicName}). {notes}",
                        tree, binary, project, snippet,
                        "Document the purpose of this sentinel value and ensure it is not a remnant of debug code."));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking sentinel magic bytes in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CreateIssue helper
        // ──────────────────────────────────────────────────────────────────────

        private static IntegrityPatternIssue CreateIssue(
            string issueType, string severity, string title, string description,
            SyntaxTree tree, SyntaxNode node,
            string project, string snippet, string notes)
        {
            var lineSpan = tree.GetLineSpan(node.Span);
            var line = lineSpan.StartLinePosition.Line + 1;

            return new IntegrityPatternIssue
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
                Notes = notes
            };
        }
    }
}
