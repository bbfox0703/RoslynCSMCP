using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Core.Models;
using System.Collections.Concurrent;

namespace RoslynMcpServer.Core.Services
{
    /// <summary>
    /// Service for analyzing Native AOT and trimming compatibility issues in C# code.
    /// Detects reflection patterns, JSON serialization without source generation,
    /// runtime Regex that can use [GeneratedRegex], and missing trim annotations.
    /// </summary>
    public class AotCompatibilityAnalyzer
    {
        private readonly ILogger<AotCompatibilityAnalyzer> _logger;
        private readonly CodeAnalysisService _codeAnalysis;

        public AotCompatibilityAnalyzer(
            ILogger<AotCompatibilityAnalyzer> logger,
            CodeAnalysisService codeAnalysis)
        {
            _logger = logger;
            _codeAnalysis = codeAnalysis;
        }

        /// <summary>
        /// Analyzes a solution for Native AOT and trimming compatibility issues.
        /// </summary>
        public async Task<AotCompatibilityResults> AnalyzeAsync(
            string solutionPath,
            string[] categories)
        {
            var results = new AotCompatibilityResults();
            var allIssues = new ConcurrentBag<AotIssue>();

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
                        var issues = await AnalyzeProjectAsync(project, categories);
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

                results.IssuesByCategory = results.Issues
                    .GroupBy(i => i.Category)
                    .ToDictionary(g => g.Key, g => g.Count());

                results.IssuesByProject = results.Issues
                    .GroupBy(i => i.ProjectName)
                    .ToDictionary(g => g.Key, g => g.Count());

                _logger.LogInformation(
                    "AOT analysis complete: {IssueCount} issues found across {ProjectCount} projects",
                    results.TotalIssues,
                    results.AnalyzedProjects);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing AOT compatibility");
                results.Warnings.Add(new OperationWarning
                {
                    Context = "Analysis",
                    Message = $"Error: {ex.Message}"
                });
            }

            return results;
        }

        private async Task<List<AotIssue>> AnalyzeProjectAsync(
            Project project, string[] categories)
        {
            var issues = new List<AotIssue>();

            var compilation = await project.GetCompilationAsync();
            if (compilation == null)
                return issues;

            // Determine if any class inherits JsonSerializerContext (once per project)
            bool hasJsonSerializerContext = false;
            if (ShouldCheck(categories, "JsonSerialization"))
            {
                hasJsonSerializerContext = HasJsonSerializerContext(compilation);
            }

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

                    if (ShouldCheck(categories, "Reflection"))
                        issues.AddRange(CheckReflection(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(categories, "JsonSerialization"))
                        issues.AddRange(CheckJsonSerialization(root, semanticModel, syntaxTree, project.Name, hasJsonSerializerContext));

                    if (ShouldCheck(categories, "GeneratedRegex"))
                        issues.AddRange(CheckGeneratedRegex(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(categories, "TrimAnnotation"))
                        issues.AddRange(CheckTrimAnnotations(root, semanticModel, syntaxTree, project.Name));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to analyze syntax tree: {FilePath}", syntaxTree.FilePath);
                }
            }

            return issues;
        }

        private static bool ShouldCheck(string[] categories, string category) =>
            categories.Length == 0 ||
            categories.Any(c => c.Equals("all", StringComparison.OrdinalIgnoreCase)) ||
            categories.Any(c => c.Equals(category, StringComparison.OrdinalIgnoreCase));

        // ──────────────────────────────────────────────────────────────────────
        // CheckReflection
        // ──────────────────────────────────────────────────────────────────────

        private List<AotIssue> CheckReflection(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<AotIssue>();

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

                    // System.Type.GetType(string) — cannot resolve by name in AOT
                    if (containingType == "System.Type" && methodName == "GetType" &&
                        methodSymbol.Parameters.Length > 0 &&
                        methodSymbol.Parameters[0].Type.SpecialType == SpecialType.System_String)
                    {
                        issues.Add(CreateIssue(
                            "Reflection", "Critical",
                            "Type.GetType(string) is AOT-incompatible",
                            "Type.GetType(string) uses runtime reflection and will fail under NativeAOT unless the type is rooted.",
                            tree, invocation, project,
                            "Use typeof(T) directly or add [DynamicDependency] to root the type.",
                            "// Before: var t = Type.GetType(\"MyApp.MyClass\");\n// After:  var t = typeof(MyApp.MyClass);"));
                    }
                    // System.Activator.CreateInstance
                    else if (containingType == "System.Activator" && methodName == "CreateInstance")
                    {
                        issues.Add(CreateIssue(
                            "Reflection", "Critical",
                            "Activator.CreateInstance is AOT-incompatible",
                            "Activator.CreateInstance uses reflection to construct types and is not supported in NativeAOT without [DynamicDependency].",
                            tree, invocation, project,
                            "Use 'new T()' with a generic constraint or a factory pattern.",
                            "// Before: Activator.CreateInstance(type);\n// After:  new MyClass(); // or use DI"));
                    }
                    // System.Reflection members: Invoke / GetValue / SetValue
                    else if ((containingType.StartsWith("System.Reflection.MethodBase") ||
                              containingType.StartsWith("System.Reflection.MethodInfo") ||
                              containingType.StartsWith("System.Reflection.PropertyInfo") ||
                              containingType.StartsWith("System.Reflection.FieldInfo")) &&
                             (methodName is "Invoke" or "GetValue" or "SetValue"))
                    {
                        issues.Add(CreateIssue(
                            "Reflection", "High",
                            $"{containingType.Split('.').Last()}.{methodName} is AOT-incompatible",
                            $"Reflection-based invocation ({methodName}) is not supported in NativeAOT trimming.",
                            tree, invocation, project,
                            "Replace with direct method calls, delegates, or source-generated alternatives.",
                            "// Use interfaces or delegates instead of MethodInfo.Invoke()"));
                    }
                    // System.Reflection.Assembly.Load*
                    else if (containingType == "System.Reflection.Assembly" &&
                             (methodName is "Load" or "LoadFrom" or "LoadFile"))
                    {
                        issues.Add(CreateIssue(
                            "Reflection", "Critical",
                            $"Assembly.{methodName} is AOT-incompatible",
                            "Dynamic assembly loading is not supported in NativeAOT.",
                            tree, invocation, project,
                            "Bundle all assemblies at build time; remove dynamic loading.",
                            "// NativeAOT does not support runtime assembly loading."));
                    }
                    // System.AppDomain.GetAssemblies
                    else if (containingType == "System.AppDomain" && methodName == "GetAssemblies")
                    {
                        issues.Add(CreateIssue(
                            "Reflection", "High",
                            "AppDomain.GetAssemblies() is AOT-incompatible",
                            "AppDomain.GetAssemblies() returns dynamically loaded assemblies, which is unsupported in NativeAOT.",
                            tree, invocation, project,
                            "Remove or replace with a static list of known types.",
                            "// Enumerate types statically via a registry pattern."));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking reflection in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckJsonSerialization
        // ──────────────────────────────────────────────────────────────────────

        private static bool HasJsonSerializerContext(Compilation compilation)
        {
            foreach (var tree in compilation.SyntaxTrees)
            {
                var root = tree.GetRoot();
                var classDecls = root.DescendantNodes().OfType<ClassDeclarationSyntax>();
                foreach (var cls in classDecls)
                {
                    if (cls.BaseList == null) continue;
                    foreach (var baseType in cls.BaseList.Types)
                    {
                        var baseTypeName = baseType.ToString();
                        if (baseTypeName.Contains("JsonSerializerContext"))
                            return true;
                    }
                }
            }
            return false;
        }

        private List<AotIssue> CheckJsonSerialization(
            SyntaxNode root, SemanticModel model, SyntaxTree tree,
            string project, bool hasJsonSerializerContext)
        {
            var issues = new List<AotIssue>();

            if (hasJsonSerializerContext)
                return issues; // Source-gen context present — no issue

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

                    if (containingType == "System.Text.Json.JsonSerializer" &&
                        (methodName is "Serialize" or "Deserialize" or "SerializeAsync" or "DeserializeAsync"))
                    {
                        issues.Add(CreateIssue(
                            "JsonSerialization", "High",
                            $"JsonSerializer.{methodName} without JsonSerializerContext",
                            "Using JsonSerializer without a source-generated JsonSerializerContext is AOT-incompatible. The reflection-based serializer cannot work in NativeAOT.",
                            tree, invocation, project,
                            "Add [JsonSerializable(typeof(YourType))] to a JsonSerializerContext subclass and pass the context to the serializer.",
                            "[JsonSerializable(typeof(MyModel))]\npublic partial class AppJsonContext : JsonSerializerContext { }\n// Usage: JsonSerializer.Serialize(obj, AppJsonContext.Default.MyModel);"));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking JSON serialization in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckGeneratedRegex
        // ──────────────────────────────────────────────────────────────────────

        private List<AotIssue> CheckGeneratedRegex(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<AotIssue>();

            // new Regex("literal")
            var objectCreations = root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>();
            foreach (var creation in objectCreations)
            {
                try
                {
                    var typeSymbol = model.GetTypeInfo(creation).Type;
                    if (typeSymbol?.ToDisplayString() != "System.Text.RegularExpressions.Regex")
                        continue;

                    var args = creation.ArgumentList?.Arguments ?? default;
                    if (args.Count == 0) continue;

                    if (args[0].Expression.IsKind(SyntaxKind.StringLiteralExpression))
                    {
                        issues.Add(CreateIssue(
                            "GeneratedRegex", "Medium",
                            "new Regex(literal) — use [GeneratedRegex] instead",
                            "Runtime-compiled Regex objects are larger and slower; [GeneratedRegex] generates AOT-safe source code at compile time.",
                            tree, creation, project,
                            "Replace with a [GeneratedRegex] partial method on a partial class.",
                            "[GeneratedRegex(@\"your-pattern\")]\nprivate static partial Regex MyRegex();"));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking Regex creation in {File}", tree.FilePath);
                }
            }

            // Regex.IsMatch / Match / Replace / Split with string literal pattern
            var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();
            foreach (var invocation in invocations)
            {
                try
                {
                    var symbolInfo = model.GetSymbolInfo(invocation);
                    if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
                        continue;

                    var containingType = methodSymbol.ContainingType?.ToDisplayString() ?? string.Empty;
                    if (containingType != "System.Text.RegularExpressions.Regex") continue;
                    if (!methodSymbol.IsStatic) continue;
                    if (methodSymbol.Name is not ("IsMatch" or "Match" or "Matches" or "Replace" or "Split")) continue;

                    var args = invocation.ArgumentList.Arguments;
                    bool hasLiteralPattern = args.Any(a => a.Expression.IsKind(SyntaxKind.StringLiteralExpression));
                    if (!hasLiteralPattern) continue;

                    issues.Add(CreateIssue(
                        "GeneratedRegex", "Medium",
                        $"Regex.{methodSymbol.Name}(literal) — use [GeneratedRegex] instead",
                        "Static Regex methods with literal patterns can be replaced with source-generated Regex for better AOT performance.",
                        tree, invocation, project,
                        "Replace static Regex call with a [GeneratedRegex] partial method.",
                        "[GeneratedRegex(@\"your-pattern\")]\nprivate static partial Regex MyRegex();\n// Usage: MyRegex().IsMatch(input);"));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking static Regex call in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckTrimAnnotations
        // ──────────────────────────────────────────────────────────────────────

        private List<AotIssue> CheckTrimAnnotations(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<AotIssue>();

            var methodDecls = root.DescendantNodes().OfType<MethodDeclarationSyntax>();

            foreach (var methodDecl in methodDecls)
            {
                try
                {
                    if (model.GetDeclaredSymbol(methodDecl) is not IMethodSymbol methodSymbol)
                        continue;

                    // Check if the method body contains reflection calls
                    if (!MethodBodyContainsReflection(methodDecl))
                        continue;

                    // Check if the method already has RequiresUnreferencedCode or DynamicallyAccessedMembers
                    bool hasAnnotation = methodSymbol.GetAttributes().Any(a =>
                    {
                        var name = a.AttributeClass?.Name ?? string.Empty;
                        return name is "RequiresUnreferencedCodeAttribute" or "RequiresUnreferencedCode" or
                                       "DynamicallyAccessedMembersAttribute" or "DynamicallyAccessedMembers";
                    });

                    if (!hasAnnotation)
                    {
                        issues.Add(CreateIssue(
                            "TrimAnnotation", "Medium",
                            $"Method '{methodSymbol.Name}' uses reflection without [RequiresUnreferencedCode]",
                            "Methods that use reflection should be annotated with [RequiresUnreferencedCode] to warn callers that the method is not trim-safe.",
                            tree, methodDecl, project,
                            "Add [RequiresUnreferencedCode(\"Reason\")] to the method signature.",
                            "[RequiresUnreferencedCode(\"This method uses reflection and is not AOT-compatible.\")]\nprivate void YourMethod() { ... }"));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking trim annotations in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        private static bool MethodBodyContainsReflection(MethodDeclarationSyntax methodDecl)
        {
            if (methodDecl.Body == null && methodDecl.ExpressionBody == null)
                return false;

            SyntaxNode? body = (SyntaxNode?)methodDecl.Body ?? methodDecl.ExpressionBody;
            if (body == null) return false;

            // Quick text heuristic — avoid full semantic analysis per-method.
            // Only match patterns that are genuinely AOT-unsafe:
            // - "Type.GetType(" targets the static string-based lookup (NOT instance obj.GetType() which is AOT-safe)
            // - ".GetValue(" and ".SetValue(" are excluded: JsonNode.GetValue<T>() and similar non-reflection APIs
            //   produce too many false positives; the real reflection uses are caught by GetProperty/GetField below
            var text = body.ToString();
            return text.Contains("Type.GetType(") ||
                   text.Contains("Activator.") ||
                   text.Contains("Assembly.Load") ||
                   text.Contains(".Invoke(") ||
                   text.Contains("GetMethod(") ||
                   text.Contains("GetProperty(") ||
                   text.Contains("GetField(") ||
                   text.Contains("GetMembers(") ||
                   text.Contains("MakeGenericType(");
        }

        // ──────────────────────────────────────────────────────────────────────
        // CreateIssue helper
        // ──────────────────────────────────────────────────────────────────────

        private static AotIssue CreateIssue(
            string category, string severity, string title, string description,
            SyntaxTree tree, SyntaxNode node,
            string project, string recommendation, string fixExample)
        {
            var lineSpan = tree.GetLineSpan(node.Span);
            var line = lineSpan.StartLinePosition.Line + 1;
            var rawText = node.ToString();
            var codeSnippet = rawText.Length > 100 ? rawText[..100] + "…" : rawText;

            return new AotIssue
            {
                Category = category,
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
