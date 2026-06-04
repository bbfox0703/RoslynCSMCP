using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Core.Models;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace RoslynMcpServer.Core.Services
{
    /// <summary>
    /// Service for analyzing Native AOT and trimming compatibility issues in C# code.
    /// Detects reflection patterns, JSON serialization without source generation,
    /// runtime Regex that can use [GeneratedRegex], missing trim annotations,
    /// single-file-trimmed assembly APIs, Avalonia AppBuilder mis-configuration,
    /// AOT-hostile XAML (.axaml) patterns, and AOT-hostile .csproj build settings.
    /// Landmine catalogue derived from the Avalonia + .NET Native AOT pitfalls doc.
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
                        issues.AddRange(CheckJsonSerialization(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(categories, "GeneratedRegex"))
                        issues.AddRange(CheckGeneratedRegex(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(categories, "TrimAnnotation"))
                        issues.AddRange(CheckTrimAnnotations(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(categories, "AvaloniaRuntime"))
                        issues.AddRange(CheckAvaloniaRuntime(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(categories, "DllImport"))
                        issues.AddRange(CheckDllImport(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(categories, "AssemblyApi"))
                        issues.AddRange(CheckAssemblyApi(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(categories, "AvaloniaAppBuilder"))
                        issues.AddRange(CheckAvaloniaAppBuilder(root, semanticModel, syntaxTree, project.Name));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to analyze syntax tree: {FilePath}", syntaxTree.FilePath);
                }
            }

            // Project-level file scans (not part of the C# compilation): XAML + .csproj.
            if (ShouldCheck(categories, "Xaml"))
                issues.AddRange(CheckXaml(project));

            if (ShouldCheck(categories, "BuildConfig"))
                issues.AddRange(CheckBuildConfig(project));

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

        /// <summary>
        /// Detects per-call JSON serialization that uses a reflection-based overload.
        /// An overload is AOT-safe only when it takes a source-generated JsonTypeInfo&lt;T&gt;
        /// or a JsonSerializerContext — the presence of a context elsewhere in the project
        /// does NOT make a reflection-overload call site safe (every call must pass it).
        /// Also flags JsonValue.Create, which trips IL2026/IL3050 even under a context.
        /// </summary>
        private List<AotIssue> CheckJsonSerialization(
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

                    if (containingType == "System.Text.Json.JsonSerializer" &&
                        (methodName is "Serialize" or "Deserialize" or "SerializeAsync" or "DeserializeAsync"))
                    {
                        // AOT-safe overloads accept a JsonTypeInfo<T> or a JsonSerializerContext.
                        if (UsesSourceGenJsonOverload(methodSymbol))
                            continue;

                        issues.Add(CreateIssue(
                            "JsonSerialization", "High",
                            $"JsonSerializer.{methodName} reflection overload is AOT-incompatible",
                            "This JsonSerializer overload uses runtime reflection and fails under NativeAOT. " +
                            "A JsonSerializerContext existing elsewhere in the project does not help — every call " +
                            "must pass JsonContext.Default.YourType (a JsonTypeInfo<T>), never the Type-argument or options-only overload.",
                            tree, invocation, project,
                            "Pass the source-generated JsonTypeInfo (Context.Default.YourType) to the call.",
                            "[JsonSerializable(typeof(MyModel))]\npublic partial class AppJsonContext : JsonSerializerContext { }\n// Usage: JsonSerializer.Serialize(obj, AppJsonContext.Default.MyModel);"));
                    }
                    // System.Text.Json.Nodes.JsonValue.Create — reflective even under a context.
                    else if (containingType == "System.Text.Json.Nodes.JsonValue" && methodName == "Create")
                    {
                        issues.Add(CreateIssue(
                            "JsonSerialization", "Medium",
                            "JsonValue.Create(...) trips IL2026/IL3050 under AOT",
                            "JsonValue.Create still trips IL2026 + IL3050 even when a JsonSerializerContext is present, " +
                            "because it falls back to reflection-based converters for the value type.",
                            tree, invocation, project,
                            "Use a JsonObject initializer (new JsonObject { [\"k\"] = \"v\" }) or Utf8JsonWriter for hot paths.",
                            "// Before: node[\"k\"] = JsonValue.Create(s);\n// After:  var o = new JsonObject { [\"k\"] = s };"));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking JSON serialization in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        /// <summary>
        /// True when a JsonSerializer overload is AOT-safe — i.e. it accepts a
        /// source-generated JsonTypeInfo&lt;T&gt; or a JsonSerializerContext parameter.
        /// </summary>
        private static bool UsesSourceGenJsonOverload(IMethodSymbol methodSymbol) =>
            methodSymbol.Parameters.Any(p =>
            {
                var t = p.Type.OriginalDefinition.ToDisplayString();
                return t == "System.Text.Json.Serialization.Metadata.JsonTypeInfo" ||
                       t == "System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>" ||
                       t == "System.Text.Json.Serialization.JsonSerializerContext";
            });

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
            // - "MethodInfo.Invoke(" / "MethodBase.Invoke(" target reflection invocation
            //   (plain ".Invoke(" excluded — it matches delegate.Invoke(), Action.Invoke() etc. which are AOT-safe)
            var text = body.ToString();
            return text.Contains("Type.GetType(") ||
                   text.Contains("Activator.") ||
                   text.Contains("Assembly.Load") ||
                   text.Contains("MethodInfo.Invoke(") ||
                   text.Contains("MethodBase.Invoke(") ||
                   text.Contains("GetMethod(") ||
                   text.Contains("GetProperty(") ||
                   text.Contains("GetField(") ||
                   text.Contains("GetMembers(") ||
                   text.Contains("MakeGenericType(");
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckAvaloniaRuntime
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Detects Avalonia ResourceInclude/StyleInclude created in C# code-behind.
        /// These use runtime XAML loading (AvaloniaXamlLoader.Load) which fails under AOT
        /// because compiled XAML resources cannot be resolved at runtime.
        /// Must be defined in XAML instead to ensure compile-time resolution.
        /// </summary>
        private List<AotIssue> CheckAvaloniaRuntime(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<AotIssue>();

            var objectCreations = root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>();
            foreach (var creation in objectCreations)
            {
                try
                {
                    var typeSymbol = model.GetTypeInfo(creation).Type;
                    if (typeSymbol == null) continue;

                    var typeName = typeSymbol.ToDisplayString();
                    if (typeName is "Avalonia.Markup.Xaml.Styling.ResourceInclude" or
                                    "Avalonia.Markup.Xaml.Styling.StyleInclude")
                    {
                        var shortName = typeSymbol.Name; // "ResourceInclude" or "StyleInclude"
                        issues.Add(CreateIssue(
                            "AvaloniaRuntime", "Critical",
                            $"new {shortName}() in code-behind is AOT-incompatible",
                            $"{shortName} created in C# code-behind uses AvaloniaXamlLoader.Load() for runtime XAML loading, " +
                            "which fails under NativeAOT because compiled XAML resources cannot be resolved at runtime. " +
                            "Define it in XAML instead for compile-time safety.",
                            tree, creation, project,
                            $"Move the {shortName} definition to XAML (App.axaml or equivalent). " +
                            "Remove unwanted resources at runtime instead of adding them dynamically.",
                            $"<!-- Define in XAML (compile-time safe) -->\n" +
                            $"<{shortName} Source=\"avares://MyApp/Resources/MyResource.axaml\" />\n\n" +
                            "// C# code-behind: remove unwanted resources instead of adding\n" +
                            "Resources.MergedDictionaries.RemoveAt(indexToRemove);"));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking Avalonia runtime patterns in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckDllImport
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Detects [DllImport] attributes that should use [LibraryImport] for AOT compatibility.
        /// [DllImport] uses runtime marshaling which is not supported under NativeAOT.
        /// [LibraryImport] uses source generation for compile-time marshaling.
        /// </summary>
        private List<AotIssue> CheckDllImport(
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

                    bool hasDllImport = methodSymbol.GetAttributes().Any(a =>
                        a.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.DllImportAttribute");

                    if (!hasDllImport) continue;

                    bool hasLibraryImport = methodSymbol.GetAttributes().Any(a =>
                        a.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.LibraryImportAttribute");

                    if (hasLibraryImport) continue; // Already migrated

                    issues.Add(CreateIssue(
                        "DllImport", "High",
                        $"[DllImport] on '{methodSymbol.Name}' — use [LibraryImport] for AOT",
                        "[DllImport] uses runtime P/Invoke marshaling which is not supported under NativeAOT. " +
                        "[LibraryImport] is a source-generator-based alternative that generates marshaling code at compile time.",
                        tree, methodDecl, project,
                        "Replace [DllImport] with [LibraryImport] and make the method 'static partial'.",
                        "// Before:\n" +
                        "// [DllImport(\"kernel32.dll\")]\n" +
                        "// static extern bool CloseHandle(IntPtr handle);\n\n" +
                        "// After:\n" +
                        "[LibraryImport(\"kernel32.dll\")]\n" +
                        "[return: MarshalAs(UnmanagedType.Bool)]\n" +
                        "internal static partial bool CloseHandle(IntPtr handle);"));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking DllImport in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckAssemblyApi
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Detects Assembly.GetExecutingAssembly() — trimmed away in single-file AOT
        /// publishes, returning no version. Assembly.GetEntryAssembly() survives.
        /// </summary>
        private List<AotIssue> CheckAssemblyApi(
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
                    if (containingType != "System.Reflection.Assembly") continue;
                    if (methodSymbol.Name != "GetExecutingAssembly") continue;

                    issues.Add(CreateIssue(
                        "AssemblyApi", "High",
                        "Assembly.GetExecutingAssembly() is trimmed in single-file AOT",
                        "Single-file Native AOT publishes trim Assembly.GetExecutingAssembly() — it returns null/no version at runtime. Use GetEntryAssembly() instead.",
                        tree, invocation, project,
                        "Replace Assembly.GetExecutingAssembly() with Assembly.GetEntryAssembly().",
                        "// Before: var v = Assembly.GetExecutingAssembly().GetName().Version;\n// After:  var v = Assembly.GetEntryAssembly()?.GetName().Version;"));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking assembly API in {File}", tree.FilePath);
                }
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckAvaloniaAppBuilder
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Detects Avalonia AppBuilder.Configure&lt;T&gt;() chains that are mis-configured
        /// for Native AOT on win-x64:
        ///   - missing .UseHarfBuzz() → process fast-fails with 0xC0000409 BEFORE any log
        ///     write (looks identical to "EXE doesn't run").
        ///   - missing Win32CompositionMode.RedirectionSurface → MicroCom CCW vtable init
        ///     fails → NullReferenceException at compositor startup.
        ///   - .UsePlatformDetect() drags X11/FreeDesktop/DBus into the assembly graph,
        ///     producing "will always throw" ILC diagnostics for trimmed Linux entrypoints.
        /// Only fires when an AppBuilder.Configure chain is present in the file.
        /// </summary>
        private List<AotIssue> CheckAvaloniaAppBuilder(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var issues = new List<AotIssue>();

            InvocationExpressionSyntax? configureCall = null;
            bool hasUsePlatformDetect = false, hasUseHarfBuzz = false, hasRedirectionSurface = false;

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Expression is not MemberAccessExpressionSyntax member)
                    continue;

                var name = member.Name.Identifier.ValueText;
                switch (name)
                {
                    case "Configure":
                        // Confirm it's Avalonia.AppBuilder.Configure, not some other Configure().
                        try
                        {
                            var sym = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                            var ct = sym?.ContainingType?.ToDisplayString();
                            if (ct == "Avalonia.AppBuilder")
                                configureCall = invocation;
                        }
                        catch { /* best effort */ }
                        break;
                    case "UsePlatformDetect": hasUsePlatformDetect = true; break;
                    case "UseHarfBuzz": hasUseHarfBuzz = true; break;
                }
            }

            // RedirectionSurface appears as a member access on the Win32CompositionMode enum.
            if (root.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                    .Any(m => m.Name.Identifier.ValueText == "RedirectionSurface"))
                hasRedirectionSurface = true;

            if (configureCall == null)
                return issues;

            if (hasUsePlatformDetect)
            {
                issues.Add(CreateIssue(
                    "AvaloniaAppBuilder", "Medium",
                    "AppBuilder.UsePlatformDetect() drags Linux backends under AOT on win-x64",
                    ".UsePlatformDetect() pulls Avalonia.X11/FreeDesktop/Tmds.DBus into the graph; ILC then emits " +
                    "\"will always throw\" diagnostics for the Linux entrypoints whose DBus types were trimmed away.",
                    tree, configureCall, project,
                    "On win-x64, reference backends explicitly: .UseWin32().UseSkia().UseHarfBuzz() — don't UsePlatformDetect().",
                    ".UseWin32()\n.UseSkia()\n.UseHarfBuzz();"));
            }
            else
            {
                if (!hasUseHarfBuzz)
                    issues.Add(CreateIssue(
                        "AvaloniaAppBuilder", "Critical",
                        "AppBuilder chain is missing .UseHarfBuzz() — 0xC0000409 fast-fail under AOT",
                        "Without .UseHarfBuzz() there is no text-shaping system; the process fast-fails with 0xC0000409 " +
                        "BEFORE any log write, looking identical to \"the EXE doesn't run\".",
                        tree, configureCall, project,
                        "Add .UseHarfBuzz() to the AppBuilder chain.",
                        "AppBuilder.Configure<App>()\n    .UseWin32()\n    .UseSkia()\n    .UseHarfBuzz();"));

                if (!hasRedirectionSurface)
                    issues.Add(CreateIssue(
                        "AvaloniaAppBuilder", "High",
                        "AppBuilder is missing Win32CompositionMode.RedirectionSurface",
                        "Under AOT, MicroCom CCW vtable init fails → QueryInterface returns a null vtable → " +
                        "NullReferenceException at compositor startup. RedirectionSurface forces the legacy GDI compositor path.",
                        tree, configureCall, project,
                        "Set Win32PlatformOptions.CompositionMode = [Win32CompositionMode.RedirectionSurface].",
                        ".With(new Win32PlatformOptions\n{\n    CompositionMode = [Win32CompositionMode.RedirectionSurface],\n})"));
            }

            return issues;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckXaml
        // ──────────────────────────────────────────────────────────────────────

        // Value-level markup-extension test, applied to a parsed attribute value (NOT the
        // raw file): "{Binding ...}" / "{CompiledBinding ...}", allowing inner whitespace.
        private static readonly Regex BindingMarkupRegex = new(
            @"\{\s*(?:Compiled)?Binding\b",
            RegexOptions.Compiled);

        // Legacy element-level regexes — used ONLY as a fallback when a XAML file is not
        // well-formed XML and XDocument cannot parse it.
        private static readonly Regex RunWithBindingRegex = new(
            @"<Run\b[^>]*\bText\s*=\s*""\{\s*(?:Compiled)?Binding",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex RootElementRegex = new(
            @"<(Window|UserControl|Page)\b[^>]*>",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex DataTemplateOpenRegex = new(
            @"<DataTemplate\b[^>]*>",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex DataGridTemplateColumnRegex = new(
            @"<DataGridTemplateColumn\b[^>]*>",
            RegexOptions.Compiled | RegexOptions.Singleline);

        /// <summary>
        /// Scans .axaml/.xaml files (which are NOT part of the C# compilation) for the
        /// AOT-hostile XAML patterns in the pitfalls catalogue. XAML is XML, so the file
        /// is parsed with <see cref="XDocument"/> (line info preserved) rather than matched
        /// with brittle text regexes; a malformed file falls back to the legacy regex scan.
        /// </summary>
        private List<AotIssue> CheckXaml(Project project)
        {
            var issues = new List<AotIssue>();

            var projectDir = string.IsNullOrEmpty(project.FilePath)
                ? null : Path.GetDirectoryName(project.FilePath);
            if (string.IsNullOrEmpty(projectDir) || !Directory.Exists(projectDir))
                return issues;

            IEnumerable<string> xamlFiles;
            try
            {
                xamlFiles = Directory.EnumerateFiles(projectDir, "*.axaml", SearchOption.AllDirectories)
                    .Concat(Directory.EnumerateFiles(projectDir, "*.xaml", SearchOption.AllDirectories))
                    .Where(f => !IsInBuildOutput(f));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to enumerate XAML in {Dir}", projectDir);
                return issues;
            }

            foreach (var file in xamlFiles)
            {
                string text;
                try { text = File.ReadAllText(file); }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to read XAML {File}", file); continue; }

                issues.AddRange(AnalyzeXaml(text, file, project.Name));
            }

            return issues;
        }

        /// <summary>
        /// Analyzes one XAML file's text: parses it as XML (preferred) and falls back to the
        /// legacy regex scan only if the file is not well-formed. Exposed for unit testing
        /// without an MSBuild workspace.
        /// </summary>
        internal static List<AotIssue> AnalyzeXaml(string text, string filePath, string projectName)
        {
            var issues = new List<AotIssue>();

            XDocument doc;
            try
            {
                doc = XDocument.Parse(text, LoadOptions.SetLineInfo);
            }
            catch (XmlException)
            {
                return CheckXamlRegexFallback(filePath, text, projectName);
            }

            AnalyzeXamlDocument(doc, filePath, projectName, issues);
            return issues;
        }

        /// <summary>XML-based XAML analysis over a parsed document.</summary>
        private static void AnalyzeXamlDocument(XDocument doc, string file, string projectName, List<AotIssue> issues)
        {
            var elements = doc.Descendants().ToList();

            // §0.25 / §4.10 — <Run Text="{Binding}"/> inside a TextBlock → StackOverflow under AOT.
            foreach (var run in elements.Where(e => e.Name.LocalName == "Run"))
            {
                var textAttr = run.Attributes().FirstOrDefault(a => a.Name.LocalName == "Text");
                if (textAttr is null || !IsBindingValue(textAttr.Value)) continue;

                issues.Add(CreateFileIssue(
                    "Xaml", "Critical",
                    "<Run Text=\"{Binding ...}\"/> causes StackOverflow under AOT",
                    "A bound <Run> inside a <TextBlock> recurses into a StackOverflow under Native AOT.",
                    file, LineOf(run), projectName,
                    "Bind on the <TextBlock> directly instead of an inner <Run>.",
                    "<!-- Before --> <TextBlock><Run Text=\"{Binding Name}\"/></TextBlock>\n" +
                    "<!-- After  --> <TextBlock Text=\"{Binding Name}\"/>",
                    ElementSnippet(run)));
            }

            // Does the document use bindings anywhere (any attribute value)?
            bool hasBinding = elements
                .SelectMany(e => e.Attributes())
                .Any(a => IsBindingValue(a.Value));

            // §0.11 / §4.1 — root element must carry x:DataType for compiled bindings.
            var root = doc.Root;
            if (hasBinding && root is not null &&
                root.Name.LocalName is "Window" or "UserControl" or "Page" &&
                !HasDataTypeAttr(root))
            {
                issues.Add(CreateFileIssue(
                    "Xaml", "Medium",
                    "Root element has bindings but no x:DataType",
                    "With AvaloniaUseCompiledBindingsByDefault=true, compiled bindings require x:DataType on the " +
                    "root Window/UserControl/Page. Reflection bindings die under AOT.",
                    file, LineOf(root), projectName,
                    "Add x:DataType=\"vm:YourViewModel\" to the root element.",
                    "<UserControl ... x:DataType=\"vm:FooViewModel\">",
                    ElementSnippet(root)));
            }

            // §4.1 — every DataTemplate that binds also needs x:DataType.
            foreach (var tmpl in elements.Where(e => e.Name.LocalName == "DataTemplate"))
            {
                if (HasDataTypeAttr(tmpl)) continue;
                issues.Add(CreateFileIssue(
                    "Xaml", "Medium",
                    "<DataTemplate> without x:DataType",
                    "Compiled bindings refuse a DataTemplate without x:DataType (including nested templates).",
                    file, LineOf(tmpl), projectName,
                    "Add x:DataType=\"m:RowModel\" to the DataTemplate.",
                    "<DataTemplate x:DataType=\"m:ItemRow\">",
                    ElementSnippet(tmpl)));
            }

            // §4.5 — DataGridTemplateColumn won't sort under AOT without SortMemberPath.
            foreach (var col in elements.Where(e => e.Name.LocalName == "DataGridTemplateColumn"))
            {
                if (col.Attributes().Any(a => a.Name.LocalName == "SortMemberPath")) continue;
                issues.Add(CreateFileIssue(
                    "Xaml", "Medium",
                    "<DataGridTemplateColumn> without SortMemberPath won't sort under AOT",
                    "A DataGridTemplateColumn with a compiled-binding cell template has no readable Path, so the grid " +
                    "auto-sets CanUserSort=False and the reflection sort silently no-ops. Needs SortMemberPath + " +
                    "CanUserSort=True + a CustomSortComparer wired in code-behind.",
                    file, LineOf(col), projectName,
                    "Add SortMemberPath=\"ClrProp\" + CanUserSort=\"True\", and wire CustomSortComparer at Loaded.",
                    "<DataGridTemplateColumn Header=\"Offset\" SortMemberPath=\"Offset\" CanUserSort=\"True\">",
                    ElementSnippet(col)));
            }
        }

        /// <summary>Legacy text-regex XAML scan, retained only for non-well-formed files.</summary>
        private static List<AotIssue> CheckXamlRegexFallback(string file, string text, string projectName)
        {
            var issues = new List<AotIssue>();

            foreach (Match m in RunWithBindingRegex.Matches(text))
            {
                issues.Add(CreateFileIssue(
                    "Xaml", "Critical",
                    "<Run Text=\"{Binding ...}\"/> causes StackOverflow under AOT",
                    "A bound <Run> inside a <TextBlock> recurses into a StackOverflow under Native AOT.",
                    file, LineOf(text, m.Index), projectName,
                    "Bind on the <TextBlock> directly instead of an inner <Run>.",
                    "<!-- Before --> <TextBlock><Run Text=\"{Binding Name}\"/></TextBlock>\n" +
                    "<!-- After  --> <TextBlock Text=\"{Binding Name}\"/>",
                    Snippet(text, m.Index)));
            }

            bool hasBinding = text.Contains("{Binding") || text.Contains("{CompiledBinding");

            var rootMatch = RootElementRegex.Match(text);
            if (hasBinding && rootMatch.Success && !rootMatch.Value.Contains("x:DataType"))
            {
                issues.Add(CreateFileIssue(
                    "Xaml", "Medium",
                    "Root element has bindings but no x:DataType",
                    "With AvaloniaUseCompiledBindingsByDefault=true, compiled bindings require x:DataType on the " +
                    "root Window/UserControl/Page. Reflection bindings die under AOT.",
                    file, LineOf(text, rootMatch.Index), projectName,
                    "Add x:DataType=\"vm:YourViewModel\" to the root element.",
                    "<UserControl ... x:DataType=\"vm:FooViewModel\">",
                    Snippet(text, rootMatch.Index)));
            }

            foreach (Match m in DataTemplateOpenRegex.Matches(text))
            {
                if (m.Value.Contains("x:DataType")) continue;
                issues.Add(CreateFileIssue(
                    "Xaml", "Medium",
                    "<DataTemplate> without x:DataType",
                    "Compiled bindings refuse a DataTemplate without x:DataType (including nested templates).",
                    file, LineOf(text, m.Index), projectName,
                    "Add x:DataType=\"m:RowModel\" to the DataTemplate.",
                    "<DataTemplate x:DataType=\"m:ItemRow\">",
                    Snippet(text, m.Index)));
            }

            foreach (Match m in DataGridTemplateColumnRegex.Matches(text))
            {
                if (m.Value.Contains("SortMemberPath")) continue;
                issues.Add(CreateFileIssue(
                    "Xaml", "Medium",
                    "<DataGridTemplateColumn> without SortMemberPath won't sort under AOT",
                    "A DataGridTemplateColumn with a compiled-binding cell template has no readable Path, so the grid " +
                    "auto-sets CanUserSort=False and the reflection sort silently no-ops. Needs SortMemberPath + " +
                    "CanUserSort=True + a CustomSortComparer wired in code-behind.",
                    file, LineOf(text, m.Index), projectName,
                    "Add SortMemberPath=\"ClrProp\" + CanUserSort=\"True\", and wire CustomSortComparer at Loaded.",
                    "<DataGridTemplateColumn Header=\"Offset\" SortMemberPath=\"Offset\" CanUserSort=\"True\">",
                    Snippet(text, m.Index)));
            }

            return issues;
        }

        private static bool IsBindingValue(string? value) =>
            !string.IsNullOrEmpty(value) && BindingMarkupRegex.IsMatch(value);

        private static bool HasDataTypeAttr(XElement element) =>
            element.Attributes().Any(a => a.Name.LocalName == "DataType");

        // ──────────────────────────────────────────────────────────────────────
        // CheckBuildConfig
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Reads the project's .csproj and flags AOT-hostile build settings from the
        /// pitfalls catalogue. Avalonia-specific checks only fire when the project
        /// references an Avalonia package.
        /// </summary>
        private List<AotIssue> CheckBuildConfig(Project project)
        {
            if (string.IsNullOrEmpty(project.FilePath) || !File.Exists(project.FilePath))
                return new List<AotIssue>();

            var csproj = project.FilePath;

            string text;
            try { text = File.ReadAllText(csproj); }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to read csproj {File}", csproj); return new List<AotIssue>(); }

            return AnalyzeBuildConfig(text, csproj, project.Name);
        }

        /// <summary>
        /// Analyzes a .csproj's text for AOT-hostile Avalonia build settings: parses it as XML
        /// (preferred) and falls back to the legacy regex scan only if it is not well-formed.
        /// Exposed for unit testing without an MSBuild workspace.
        /// </summary>
        internal static List<AotIssue> AnalyzeBuildConfig(string text, string csproj, string projectName)
        {
            var issues = new List<AotIssue>();

            XDocument doc;
            try
            {
                doc = XDocument.Parse(text, LoadOptions.SetLineInfo);
            }
            catch (XmlException)
            {
                return CheckBuildConfigRegexFallback(csproj, text, projectName);
            }

            var elements = doc.Descendants().ToList();

            // MSBuild element/property names are case-insensitive; XML element names are not,
            // so match on LocalName case-insensitively and ignore any MSBuild xmlns.
            static bool Is(XElement e, string name) =>
                string.Equals(e.Name.LocalName, name, StringComparison.OrdinalIgnoreCase);

            var packageRefs = elements.Where(e => Is(e, "PackageReference")).ToList();
            static string Include(XElement e) =>
                e.Attribute("Include")?.Value ?? e.Attribute("Update")?.Value ?? string.Empty;

            var firstAvaloniaRef = packageRefs.FirstOrDefault(e =>
                Include(e).StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase));
            if (firstAvaloniaRef is null)
                return issues; // not an Avalonia project — skip Avalonia-specific checks

            int firstAvaloniaLine = LineOf(firstAvaloniaRef);

            // §0.3 — Avalonia 12 MicroCom needs BuiltInComInteropSupport=false for AOT.
            var comElem = elements.FirstOrDefault(e => Is(e, "BuiltInComInteropSupport"));
            bool comFalse = comElem is not null &&
                            string.Equals(comElem.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase);
            if (!comFalse)
            {
                issues.Add(CreateFileIssue(
                    "BuildConfig", "High",
                    "BuiltInComInteropSupport is not set to false",
                    "Avalonia 12's MicroCom requires <BuiltInComInteropSupport>false</BuiltInComInteropSupport> for Native AOT.",
                    csproj, comElem is not null ? LineOf(comElem) : firstAvaloniaLine, projectName,
                    "Add <BuiltInComInteropSupport>false</BuiltInComInteropSupport> to a PropertyGroup.",
                    "<BuiltInComInteropSupport>false</BuiltInComInteropSupport>",
                    comElem is not null ? ElementSnippet(comElem) : string.Empty));
            }

            // §0.13 — don't reference Avalonia.Desktop on win-x64 (drags X11/FreeDesktop/DBus).
            var desktopRef = packageRefs.FirstOrDefault(e =>
                string.Equals(Include(e), "Avalonia.Desktop", StringComparison.OrdinalIgnoreCase));
            bool isWinX64 = elements
                .Where(e => Is(e, "RuntimeIdentifier") || Is(e, "RuntimeIdentifiers"))
                .Any(e => e.Value.Contains("win-x64", StringComparison.OrdinalIgnoreCase));
            if (desktopRef is not null && isWinX64)
            {
                issues.Add(CreateFileIssue(
                    "BuildConfig", "Medium",
                    "Avalonia.Desktop referenced on win-x64",
                    "Avalonia.Desktop drags Avalonia.X11 + Avalonia.FreeDesktop + Tmds.DBus.Protocol; ILC emits " +
                    "\"will always throw\" diagnostics for trimmed Linux entrypoints.",
                    csproj, LineOf(desktopRef), projectName,
                    "Reference Avalonia.Win32 + Avalonia.Skia + Avalonia.HarfBuzz explicitly instead of Avalonia.Desktop. " +
                    "Tradeoff: on an already-shipped app this is a behavioral change to the platform/render backend " +
                    "wiring — re-test the win-x64 publish before removing Avalonia.Desktop. It is reasonable to keep " +
                    "Avalonia.Desktop and accept this finding until that re-test is done.",
                    "<PackageReference Include=\"Avalonia.Win32\" Version=\"12.0.3\" />\n" +
                    "<PackageReference Include=\"Avalonia.Skia\" Version=\"12.0.3\" />\n" +
                    "<PackageReference Include=\"Avalonia.HarfBuzz\" Version=\"12.0.3\" />",
                    ElementSnippet(desktopRef)));
            }

            // §3.1 — Avalonia loads backends via reflection; without TrimmerRootAssembly roots ILC drops them.
            if (!elements.Any(e => Is(e, "TrimmerRootAssembly")))
            {
                issues.Add(CreateFileIssue(
                    "BuildConfig", "Medium",
                    "No <TrimmerRootAssembly> entries for Avalonia",
                    "Avalonia loads platform/render backends via reflection. Without TrimmerRootAssembly roots the trimmer " +
                    "drops them and the app NREs in the compositor thread at startup.",
                    csproj, firstAvaloniaLine, projectName,
                    "Add the canonical TrimmerRootAssembly list (Avalonia, Avalonia.Base, Avalonia.Win32, Avalonia.Skia, ...).",
                    "<TrimmerRootAssembly Include=\"Avalonia\" />\n<TrimmerRootAssembly Include=\"Avalonia.Win32\" />\n<TrimmerRootAssembly Include=\"Avalonia.Skia\" />"));
            }

            // §0.4 — compiled bindings required under AOT; reflection bindings die.
            var compiledBindingsElem = elements.FirstOrDefault(e => Is(e, "AvaloniaUseCompiledBindingsByDefault"));
            bool compiledBindingsTrue = compiledBindingsElem is not null &&
                string.Equals(compiledBindingsElem.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase);
            if (!compiledBindingsTrue)
            {
                issues.Add(CreateFileIssue(
                    "BuildConfig", "Low",
                    "AvaloniaUseCompiledBindingsByDefault is not enabled",
                    "Reflection-based bindings die under AOT. Enable compiled bindings by default (requires x:DataType everywhere).",
                    csproj, compiledBindingsElem is not null ? LineOf(compiledBindingsElem) : firstAvaloniaLine, projectName,
                    "Add <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>.",
                    "<AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>"));
            }

            return issues;
        }

        /// <summary>Legacy text-regex csproj scan, retained only for non-well-formed files.</summary>
        private static List<AotIssue> CheckBuildConfigRegexFallback(string csproj, string text, string projectName)
        {
            var issues = new List<AotIssue>();

            bool referencesAvalonia = Regex.IsMatch(text, @"PackageReference\s+Include\s*=\s*""Avalonia");
            if (!referencesAvalonia)
                return issues;

            var comTrue = Regex.Match(text, @"<BuiltInComInteropSupport>\s*true\s*</BuiltInComInteropSupport>", RegexOptions.IgnoreCase);
            bool comFalse = Regex.IsMatch(text, @"<BuiltInComInteropSupport>\s*false\s*</BuiltInComInteropSupport>", RegexOptions.IgnoreCase);
            if (!comFalse)
            {
                int idx = comTrue.Success ? comTrue.Index : FirstAvaloniaRefIndex(text);
                issues.Add(CreateFileIssue(
                    "BuildConfig", "High",
                    "BuiltInComInteropSupport is not set to false",
                    "Avalonia 12's MicroCom requires <BuiltInComInteropSupport>false</BuiltInComInteropSupport> for Native AOT.",
                    csproj, LineOf(text, idx), projectName,
                    "Add <BuiltInComInteropSupport>false</BuiltInComInteropSupport> to a PropertyGroup.",
                    "<BuiltInComInteropSupport>false</BuiltInComInteropSupport>",
                    comTrue.Success ? comTrue.Value : string.Empty));
            }

            var desktopRef = Regex.Match(text, @"PackageReference\s+Include\s*=\s*""Avalonia\.Desktop""");
            bool isWinX64 = text.Contains("win-x64");
            if (desktopRef.Success && isWinX64)
            {
                issues.Add(CreateFileIssue(
                    "BuildConfig", "Medium",
                    "Avalonia.Desktop referenced on win-x64",
                    "Avalonia.Desktop drags Avalonia.X11 + Avalonia.FreeDesktop + Tmds.DBus.Protocol; ILC emits " +
                    "\"will always throw\" diagnostics for trimmed Linux entrypoints.",
                    csproj, LineOf(text, desktopRef.Index), projectName,
                    "Reference Avalonia.Win32 + Avalonia.Skia + Avalonia.HarfBuzz explicitly instead of Avalonia.Desktop. " +
                    "Tradeoff: on an already-shipped app this is a behavioral change to the platform/render backend " +
                    "wiring — re-test the win-x64 publish before removing Avalonia.Desktop. It is reasonable to keep " +
                    "Avalonia.Desktop and accept this finding until that re-test is done.",
                    "<PackageReference Include=\"Avalonia.Win32\" Version=\"12.0.3\" />\n" +
                    "<PackageReference Include=\"Avalonia.Skia\" Version=\"12.0.3\" />\n" +
                    "<PackageReference Include=\"Avalonia.HarfBuzz\" Version=\"12.0.3\" />",
                    desktopRef.Value));
            }

            if (!text.Contains("<TrimmerRootAssembly"))
            {
                issues.Add(CreateFileIssue(
                    "BuildConfig", "Medium",
                    "No <TrimmerRootAssembly> entries for Avalonia",
                    "Avalonia loads platform/render backends via reflection. Without TrimmerRootAssembly roots the trimmer " +
                    "drops them and the app NREs in the compositor thread at startup.",
                    csproj, LineOf(text, FirstAvaloniaRefIndex(text)), projectName,
                    "Add the canonical TrimmerRootAssembly list (Avalonia, Avalonia.Base, Avalonia.Win32, Avalonia.Skia, ...).",
                    "<TrimmerRootAssembly Include=\"Avalonia\" />\n<TrimmerRootAssembly Include=\"Avalonia.Win32\" />\n<TrimmerRootAssembly Include=\"Avalonia.Skia\" />"));
            }

            bool compiledBindingsTrue = Regex.IsMatch(text,
                @"<AvaloniaUseCompiledBindingsByDefault>\s*true\s*</AvaloniaUseCompiledBindingsByDefault>", RegexOptions.IgnoreCase);
            if (!compiledBindingsTrue)
            {
                issues.Add(CreateFileIssue(
                    "BuildConfig", "Low",
                    "AvaloniaUseCompiledBindingsByDefault is not enabled",
                    "Reflection-based bindings die under AOT. Enable compiled bindings by default (requires x:DataType everywhere).",
                    csproj, LineOf(text, FirstAvaloniaRefIndex(text)), projectName,
                    "Add <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>.",
                    "<AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>"));
            }

            return issues;
        }

        private static int FirstAvaloniaRefIndex(string csprojText)
        {
            var m = Regex.Match(csprojText, @"PackageReference\s+Include\s*=\s*""Avalonia");
            return m.Success ? m.Index : 0;
        }

        /// <summary>1-based line of an XML node when line info was preserved, else 1.</summary>
        private static int LineOf(XObject node) =>
            node is IXmlLineInfo li && li.HasLineInfo() ? li.LineNumber : 1;

        /// <summary>Single-line snippet of an element's opening tag (attributes preserved).</summary>
        private static string ElementSnippet(XElement element)
        {
            // Render just the start tag rather than the whole subtree.
            var clone = new XElement(element.Name, element.Attributes());
            var xml = clone.ToString(SaveOptions.DisableFormatting);
            int selfClose = xml.IndexOf("/>", StringComparison.Ordinal);
            int open = xml.IndexOf('>');
            if (selfClose >= 0) return xml[..(selfClose + 2)];
            return open >= 0 ? xml[..(open + 1)] : xml;
        }

        private static bool IsInBuildOutput(string path) =>
            path.Contains("\\obj\\") || path.Contains("/obj/") ||
            path.Contains("\\bin\\") || path.Contains("/bin/");

        private static int LineOf(string text, int index)
        {
            int line = 1;
            int limit = Math.Min(index, text.Length);
            for (int i = 0; i < limit; i++)
                if (text[i] == '\n') line++;
            return line;
        }

        private static string Snippet(string text, int index)
        {
            int end = text.IndexOf('\n', index);
            if (end < 0) end = text.Length;
            return text[index..end].Trim();
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

        /// <summary>
        /// CreateIssue variant for findings sourced from raw files (XAML, .csproj) that
        /// are not part of the C# compilation and therefore have no SyntaxTree/SyntaxNode.
        /// </summary>
        private static AotIssue CreateFileIssue(
            string category, string severity, string title, string description,
            string filePath, int lineNumber, string project,
            string recommendation, string fixExample, string codeSnippet = "")
        {
            var snippet = codeSnippet.Length > 100 ? codeSnippet[..100] + "…" : codeSnippet;

            return new AotIssue
            {
                Category = category,
                Severity = severity,
                Title = title,
                Description = description,
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                ProjectName = project,
                LineNumber = lineNumber,
                SymbolName = string.Empty,
                CodeSnippet = snippet.Trim(),
                Recommendation = recommendation,
                FixExample = fixExample
            };
        }
    }
}
