using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Core.Models;
using System.Collections.Concurrent;

namespace RoslynMcpServer.Core.Services
{
    /// <summary>
    /// Service for analyzing P/Invoke and native interop compatibility, safety, and modernization opportunities.
    /// Detects [DllImport] migration candidates, Marshal safety issues, resource leaks,
    /// missing MarshalAs annotations, IntPtr/UIntPtr modernization, and more.
    /// </summary>
    public class PInvokeCompatibilityAnalyzer
    {
        private readonly ILogger<PInvokeCompatibilityAnalyzer> _logger;
        private readonly CodeAnalysisService _codeAnalysis;

        public PInvokeCompatibilityAnalyzer(
            ILogger<PInvokeCompatibilityAnalyzer> logger,
            CodeAnalysisService codeAnalysis)
        {
            _logger = logger;
            _codeAnalysis = codeAnalysis;
        }

        /// <summary>
        /// Analyzes a solution for P/Invoke and native interop issues.
        /// </summary>
        public async Task<PInvokeAnalysisResults> AnalyzeAsync(
            string solutionPath,
            string[] issueTypes)
        {
            var results = new PInvokeAnalysisResults();
            var allIssues = new ConcurrentBag<PInvokeIssue>();

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
                    "P/Invoke analysis complete: {IssueCount} issues found across {ProjectCount} projects",
                    results.TotalIssues,
                    results.AnalyzedProjects);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing P/Invoke compatibility");
                results.Warnings.Add(new OperationWarning
                {
                    Context = "Analysis",
                    Message = $"Error: {ex.Message}"
                });
            }

            return results;
        }

        private async Task<List<PInvokeIssue>> AnalyzeProjectAsync(
            Project project, string[] issueTypes)
        {
            var issues = new List<PInvokeIssue>();

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

                    if (ShouldCheck(issueTypes, "DllImportMigration"))
                        issues.AddRange(CheckDllImportMigration(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "MarshalUnsafety"))
                        issues.AddRange(CheckMarshalUnsafety(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "MissingMarshalAs"))
                        issues.AddRange(CheckMissingMarshalAs(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "SetLastError"))
                        issues.AddRange(CheckSetLastError(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "ResourceLeak"))
                        issues.AddRange(CheckResourceLeak(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "ModernizeTypes"))
                        issues.AddRange(CheckModernizeTypes(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "MagicOffset"))
                        issues.AddRange(CheckMagicOffset(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(issueTypes, "NativeLibrary"))
                        issues.AddRange(CheckNativeLibrary(root, semanticModel, syntaxTree, project.Name));
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
        // CheckDllImportMigration — [DllImport] → [LibraryImport] (High)
        // ──────────────────────────────────────────────────────────────────────

        private List<PInvokeIssue> CheckDllImportMigration(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<PInvokeIssue>();

            var methodDecls = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
            foreach (var method in methodDecls)
            {
                try
                {
                    var hasDllImport = method.AttributeLists
                        .SelectMany(al => al.Attributes)
                        .Any(a => a.Name.ToString() is "DllImport" or "System.Runtime.InteropServices.DllImport");

                    if (!hasDllImport) continue;

                    issues.Add(CreateIssue(
                        "DllImportMigration", "High",
                        $"[DllImport] method '{method.Identifier.Text}' should migrate to [LibraryImport]",
                        "[DllImport] uses runtime marshalling which is incompatible with Native AOT. " +
                        "[LibraryImport] generates source code at compile time for AOT-safe interop.",
                        tree, method, project, method.Identifier.Text,
                        "Replace [DllImport] with [LibraryImport] and make the method partial.",
                        $"[LibraryImport(\"lib.dll\")]\nprivate static partial void {method.Identifier.Text}();"));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking DllImport in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckMarshalUnsafety — Marshal.PtrToStringAuto (High)
        // ──────────────────────────────────────────────────────────────────────

        private List<PInvokeIssue> CheckMarshalUnsafety(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<PInvokeIssue>();

            var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();
            foreach (var invocation in invocations)
            {
                try
                {
                    var symbolInfo = model.GetSymbolInfo(invocation);
                    if (symbolInfo.Symbol is not IMethodSymbol methodSymbol) continue;

                    var containingType = methodSymbol.ContainingType?.ToDisplayString() ?? string.Empty;
                    var methodName = methodSymbol.Name;

                    if (containingType == "System.Runtime.InteropServices.Marshal" &&
                        methodName == "PtrToStringAuto")
                    {
                        issues.Add(CreateIssue(
                            "MarshalUnsafety", "High",
                            "Marshal.PtrToStringAuto() is AOT-unsafe",
                            "Marshal.PtrToStringAuto() selects encoding at runtime based on OS, which is incompatible with Native AOT. " +
                            "Use Marshal.PtrToStringAnsi(), PtrToStringUni(), or PtrToStringUTF8() explicitly.",
                            tree, invocation, project, "Marshal.PtrToStringAuto",
                            "Replace with Marshal.PtrToStringAnsi(), PtrToStringUni(), or PtrToStringUTF8().",
                            "// Before: var s = Marshal.PtrToStringAuto(ptr);\n" +
                            "// After:  var s = Marshal.PtrToStringAnsi(ptr);  // or PtrToStringUTF8"));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking Marshal safety in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckMissingMarshalAs — bool param without [MarshalAs] (Medium)
        // ──────────────────────────────────────────────────────────────────────

        private List<PInvokeIssue> CheckMissingMarshalAs(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<PInvokeIssue>();

            var methodDecls = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
            foreach (var method in methodDecls)
            {
                try
                {
                    var hasDllImport = method.AttributeLists
                        .SelectMany(al => al.Attributes)
                        .Any(a => a.Name.ToString() is "DllImport" or "System.Runtime.InteropServices.DllImport");

                    if (!hasDllImport) continue;

                    foreach (var param in method.ParameterList.Parameters)
                    {
                        if (param.Type == null) continue;

                        var typeSymbol = model.GetTypeInfo(param.Type).Type;
                        if (typeSymbol?.SpecialType != SpecialType.System_Boolean) continue;

                        var hasMarshalAs = param.AttributeLists
                            .SelectMany(al => al.Attributes)
                            .Any(a => a.Name.ToString() is "MarshalAs" or "System.Runtime.InteropServices.MarshalAs");

                        if (!hasMarshalAs)
                        {
                            issues.Add(CreateIssue(
                                "MissingMarshalAs", "Medium",
                                $"bool parameter '{param.Identifier.Text}' in '{method.Identifier.Text}' missing [MarshalAs(UnmanagedType.Bool)]",
                                "A bool parameter in a [DllImport] method without [MarshalAs] will use the default marshalling, " +
                                "which may not match the native API's expected type (Win32 BOOL vs C bool).",
                                tree, param, project, param.Identifier.Text,
                                "Add [MarshalAs(UnmanagedType.Bool)] to the bool parameter.",
                                $"[DllImport(\"lib.dll\")]\nprivate static extern bool {method.Identifier.Text}(" +
                                $"[MarshalAs(UnmanagedType.Bool)] bool {param.Identifier.Text});"));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking MarshalAs in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckSetLastError — [DllImport(SetLastError=true)] without GetLastWin32Error (Medium)
        // ──────────────────────────────────────────────────────────────────────

        private List<PInvokeIssue> CheckSetLastError(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<PInvokeIssue>();

            var methodDecls = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
            foreach (var method in methodDecls)
            {
                try
                {
                    var dllImportAttr = method.AttributeLists
                        .SelectMany(al => al.Attributes)
                        .FirstOrDefault(a => a.Name.ToString() is "DllImport" or "System.Runtime.InteropServices.DllImport");

                    if (dllImportAttr == null) continue;

                    // Check if SetLastError = true is present
                    var hasSetLastError = dllImportAttr.ArgumentList?.Arguments
                        .Any(arg =>
                            arg.NameEquals?.Name.Identifier.Text == "SetLastError" &&
                            arg.Expression.ToString() == "true") ?? false;

                    if (!hasSetLastError) continue;

                    issues.Add(CreateIssue(
                        "SetLastError", "Medium",
                        $"[DllImport] with SetLastError=true: '{method.Identifier.Text}' — verify GetLastWin32Error() is called",
                        "When SetLastError=true is used, callers must immediately call Marshal.GetLastWin32Error() " +
                        "after the P/Invoke call before any other managed code runs, or the error code will be lost.",
                        tree, method, project, method.Identifier.Text,
                        "Ensure callers immediately call Marshal.GetLastWin32Error() after invoking this method.",
                        $"var result = {method.Identifier.Text}(...);\n" +
                        "if (!result)\n" +
                        "{\n" +
                        "    var error = Marshal.GetLastWin32Error();\n" +
                        "    throw new Win32Exception(error);\n" +
                        "}"));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking SetLastError in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckResourceLeak — GCHandle.Alloc / Marshal.AllocHGlobal without Free (High)
        // ──────────────────────────────────────────────────────────────────────

        private List<PInvokeIssue> CheckResourceLeak(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<PInvokeIssue>();

            var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();
            foreach (var invocation in invocations)
            {
                try
                {
                    var symbolInfo = model.GetSymbolInfo(invocation);
                    if (symbolInfo.Symbol is not IMethodSymbol methodSymbol) continue;

                    var containingType = methodSymbol.ContainingType?.ToDisplayString() ?? string.Empty;
                    var methodName = methodSymbol.Name;

                    bool isGcHandleAlloc = containingType == "System.Runtime.InteropServices.GCHandle" &&
                                          methodName == "Alloc";
                    bool isAllocHGlobal = containingType == "System.Runtime.InteropServices.Marshal" &&
                                         methodName == "AllocHGlobal";

                    if (!isGcHandleAlloc && !isAllocHGlobal) continue;

                    // Find the containing method body
                    var containingMethod = invocation.Ancestors()
                        .OfType<BaseMethodDeclarationSyntax>()
                        .FirstOrDefault();

                    if (containingMethod == null) continue;

                    var methodText = containingMethod.ToString();

                    bool hasFree = isGcHandleAlloc
                        ? methodText.Contains(".Free()") || methodText.Contains("try") && methodText.Contains("finally")
                        : methodText.Contains("FreeHGlobal") || methodText.Contains("try") && methodText.Contains("finally");

                    if (!hasFree)
                    {
                        var allocName = isGcHandleAlloc ? "GCHandle.Alloc()" : "Marshal.AllocHGlobal()";
                        var freeName = isGcHandleAlloc ? "GCHandle.Free()" : "Marshal.FreeHGlobal()";

                        issues.Add(CreateIssue(
                            "ResourceLeak", "High",
                            $"{allocName} without corresponding free call — potential resource leak",
                            $"{allocName} allocates native resources that must be released. " +
                            $"Missing {freeName} (or try/finally) may cause memory/handle leaks.",
                            tree, invocation, project, allocName,
                            $"Always call {freeName} in a finally block to ensure resources are released.",
                            $"var handle = {allocName.Replace("()", "(obj)")};\n" +
                            "try\n{\n    // use handle\n}\nfinally\n{\n" +
                            $"    {freeName};\n}}"));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking resource leaks in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckModernizeTypes — IntPtr/UIntPtr → nint/nuint (Low)
        // ──────────────────────────────────────────────────────────────────────

        private List<PInvokeIssue> CheckModernizeTypes(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<PInvokeIssue>();

            // Check parameters
            var parameters = root.DescendantNodes().OfType<ParameterSyntax>();
            foreach (var param in parameters)
            {
                try
                {
                    if (param.Type == null) continue;
                    var typeText = param.Type.ToString();
                    if (typeText is not ("IntPtr" or "UIntPtr")) continue;

                    var modern = typeText == "IntPtr" ? "nint" : "nuint";
                    issues.Add(CreateIssue(
                        "ModernizeTypes", "Low",
                        $"Parameter '{param.Identifier.Text}' uses {typeText} — consider {modern}",
                        $"C# 9+ introduced {modern} as a native integer alias for {typeText}. " +
                        "Using nint/nuint improves readability and is the modern convention.",
                        tree, param, project, param.Identifier.Text,
                        $"Replace {typeText} with {modern} for clearer intent.",
                        $"// Before: {typeText} {param.Identifier.Text}\n// After:  {modern} {param.Identifier.Text}"));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking parameter types in {File}", tree.FilePath);
                }
            }

            // Check field/variable declarations
            var localDecls = root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>();
            foreach (var decl in localDecls)
            {
                try
                {
                    var typeText = decl.Declaration.Type.ToString();
                    if (typeText is not ("IntPtr" or "UIntPtr")) continue;

                    var varName = decl.Declaration.Variables.FirstOrDefault()?.Identifier.Text ?? "variable";
                    var modern = typeText == "IntPtr" ? "nint" : "nuint";

                    issues.Add(CreateIssue(
                        "ModernizeTypes", "Low",
                        $"Local variable '{varName}' uses {typeText} — consider {modern}",
                        $"C# 9+ introduced {modern} as a native integer alias for {typeText}.",
                        tree, decl, project, varName,
                        $"Replace {typeText} with {modern}.",
                        $"// Before: {typeText} {varName} = ...\n// After:  {modern} {varName} = ..."));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking local variable types in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckMagicOffset — numeric literal in unsafe pointer arithmetic (Medium)
        // ──────────────────────────────────────────────────────────────────────

        private List<PInvokeIssue> CheckMagicOffset(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<PInvokeIssue>();

            // Only check inside unsafe blocks or methods
            var unsafeContexts = root.DescendantNodes()
                .Where(n => n is UnsafeStatementSyntax || n is MethodDeclarationSyntax m && m.Modifiers.Any(mod => mod.IsKind(SyntaxKind.UnsafeKeyword)))
                .ToList();

            if (unsafeContexts.Count == 0) return issues;

            var binaryExpressions = root.DescendantNodes()
                .OfType<BinaryExpressionSyntax>()
                .Where(b => b.IsKind(SyntaxKind.AddExpression) || b.IsKind(SyntaxKind.SubtractExpression));

            foreach (var binaryExpr in binaryExpressions)
            {
                try
                {
                    // Check if inside an unsafe context
                    bool inUnsafe = binaryExpr.Ancestors()
                        .Any(a => a is UnsafeStatementSyntax ||
                                  (a is MethodDeclarationSyntax m && m.Modifiers.Any(mod => mod.IsKind(SyntaxKind.UnsafeKeyword))));

                    if (!inUnsafe) continue;

                    // Check if one operand is a non-zero numeric literal
                    bool hasNumericLiteral =
                        (binaryExpr.Left.IsKind(SyntaxKind.NumericLiteralExpression) && binaryExpr.Left.ToString() != "0") ||
                        (binaryExpr.Right.IsKind(SyntaxKind.NumericLiteralExpression) && binaryExpr.Right.ToString() != "0");

                    if (!hasNumericLiteral) continue;

                    // Confirm the expression involves a pointer type (best-effort)
                    var exprType = model.GetTypeInfo(binaryExpr).Type;
                    bool looksLikePointerArith = exprType?.TypeKind == TypeKind.Pointer ||
                                                  binaryExpr.Left.ToString().Contains("ptr") ||
                                                  binaryExpr.Left.ToString().Contains("Ptr") ||
                                                  binaryExpr.Right.ToString().Contains("ptr") ||
                                                  binaryExpr.Right.ToString().Contains("Ptr");

                    if (!looksLikePointerArith) continue;

                    issues.Add(CreateIssue(
                        "MagicOffset", "Medium",
                        $"Magic numeric offset in unsafe pointer arithmetic: '{binaryExpr}'",
                        "Hard-coded numeric offsets in unsafe pointer arithmetic are fragile and error-prone. " +
                        "Struct layout changes can silently break the offset calculations.",
                        tree, binaryExpr, project, binaryExpr.ToString(),
                        "Replace magic numbers with named constants (const int FIELD_OFFSET = ...) or use Marshal.OffsetOf<T>().",
                        "// Before: ptr + 8\n" +
                        "// After:  ptr + Marshal.OffsetOf<MyStruct>(nameof(MyStruct.Field)).ToInt32()"));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking magic offsets in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckNativeLibrary — NativeLibrary.SetDllImportResolver without try/catch (Medium)
        // ──────────────────────────────────────────────────────────────────────

        private List<PInvokeIssue> CheckNativeLibrary(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<PInvokeIssue>();

            var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();
            foreach (var invocation in invocations)
            {
                try
                {
                    var symbolInfo = model.GetSymbolInfo(invocation);
                    if (symbolInfo.Symbol is not IMethodSymbol methodSymbol) continue;

                    var containingType = methodSymbol.ContainingType?.ToDisplayString() ?? string.Empty;
                    if (containingType != "System.Runtime.InteropServices.NativeLibrary") continue;
                    if (methodSymbol.Name != "SetDllImportResolver") continue;

                    // Check if the containing method has a try/catch
                    var containingMethod = invocation.Ancestors()
                        .OfType<BaseMethodDeclarationSyntax>()
                        .FirstOrDefault();

                    if (containingMethod == null) continue;

                    bool hasTryCatch = containingMethod.DescendantNodes()
                        .OfType<TryStatementSyntax>()
                        .Any(t => t.Catches.Count > 0);

                    if (!hasTryCatch)
                    {
                        issues.Add(CreateIssue(
                            "NativeLibrary", "Medium",
                            "NativeLibrary.SetDllImportResolver() called without error handling",
                            "NativeLibrary.SetDllImportResolver() can throw if the assembly is not found or resolver conflicts occur. " +
                            "Without a try/catch, this can crash the application during startup.",
                            tree, invocation, project, "NativeLibrary.SetDllImportResolver",
                            "Wrap NativeLibrary.SetDllImportResolver() in a try/catch to handle resolver failures gracefully.",
                            "try\n{\n    NativeLibrary.SetDllImportResolver(assembly, resolver);\n}\n" +
                            "catch (Exception ex)\n{\n    // log and handle resolver failure\n}"));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking NativeLibrary in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CreateIssue helper
        // ──────────────────────────────────────────────────────────────────────

        private static PInvokeIssue CreateIssue(
            string issueType, string severity, string title, string description,
            SyntaxTree tree, SyntaxNode node,
            string project, string symbolName,
            string recommendation, string fixExample)
        {
            var lineSpan = tree.GetLineSpan(node.Span);
            var line = lineSpan.StartLinePosition.Line + 1;
            var rawText = node.ToString();
            var codeSnippet = rawText.Length > 100 ? rawText[..100] + "…" : rawText;

            return new PInvokeIssue
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
