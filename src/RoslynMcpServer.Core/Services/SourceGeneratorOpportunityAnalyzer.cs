using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Core.Models;
using System.Collections.Concurrent;

namespace RoslynMcpServer.Core.Services
{
    /// <summary>
    /// Service for detecting source generator migration opportunities in C# code.
    /// Identifies patterns that can be replaced by [GeneratedRegex], [JsonSerializable],
    /// [ObservableProperty], [RelayCommand], and [LoggerMessage] source generators.
    /// </summary>
    public class SourceGeneratorOpportunityAnalyzer
    {
        private readonly ILogger<SourceGeneratorOpportunityAnalyzer> _logger;
        private readonly CodeAnalysisService _codeAnalysis;

        public SourceGeneratorOpportunityAnalyzer(
            ILogger<SourceGeneratorOpportunityAnalyzer> logger,
            CodeAnalysisService codeAnalysis)
        {
            _logger = logger;
            _codeAnalysis = codeAnalysis;
        }

        /// <summary>
        /// Analyzes a solution for source generator migration opportunities.
        /// </summary>
        public async Task<SourceGeneratorAnalysisResults> AnalyzeAsync(
            string solutionPath,
            string[] categories)
        {
            var results = new SourceGeneratorAnalysisResults();
            var allOpportunities = new ConcurrentBag<SourceGeneratorOpportunity>();

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
                        var opportunities = await AnalyzeProjectAsync(project, categories);
                        foreach (var opp in opportunities)
                            allOpportunities.Add(opp);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to analyze project: {ProjectName}", project.Name);
                        results.FailedProjects++;
                    }
                });

                await Task.WhenAll(projectTasks);

                results.Opportunities = allOpportunities.ToList();
                results.AnalyzedFiles = results.Opportunities.Select(o => o.FilePath).Distinct().Count();

                results.OpportunitiesByCategory = results.Opportunities
                    .GroupBy(o => o.Category)
                    .ToDictionary(g => g.Key, g => g.Count());

                results.OpportunitiesByProject = results.Opportunities
                    .GroupBy(o => o.ProjectName)
                    .ToDictionary(g => g.Key, g => g.Count());

                _logger.LogInformation(
                    "Source generator analysis complete: {Count} opportunities found across {Projects} projects",
                    results.TotalOpportunities,
                    results.AnalyzedProjects);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing source generator opportunities");
                results.Warnings.Add(new OperationWarning
                {
                    Context = "Analysis",
                    Message = $"Error: {ex.Message}"
                });
            }

            return results;
        }

        private async Task<List<SourceGeneratorOpportunity>> AnalyzeProjectAsync(
            Project project, string[] categories)
        {
            var opportunities = new List<SourceGeneratorOpportunity>();

            var compilation = await project.GetCompilationAsync();
            if (compilation == null)
                return opportunities;

            bool hasJsonSerializerContext = false;
            if (ShouldCheck(categories, "JsonSerializable"))
                hasJsonSerializerContext = HasJsonSerializerContext(compilation);

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

                    if (ShouldCheck(categories, "GeneratedRegex"))
                        opportunities.AddRange(CheckGeneratedRegex(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(categories, "JsonSerializable"))
                        opportunities.AddRange(CheckJsonSerializable(root, semanticModel, syntaxTree, project.Name, hasJsonSerializerContext));

                    if (ShouldCheck(categories, "ObservableProperty"))
                        opportunities.AddRange(CheckObservableProperty(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(categories, "RelayCommand"))
                        opportunities.AddRange(CheckRelayCommand(root, semanticModel, syntaxTree, project.Name));

                    if (ShouldCheck(categories, "LoggerMessage"))
                        opportunities.AddRange(CheckLoggerMessage(root, semanticModel, syntaxTree, project.Name));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to analyze syntax tree: {FilePath}", syntaxTree.FilePath);
                }
            }

            return opportunities;
        }

        private static bool ShouldCheck(string[] categories, string category) =>
            categories.Length == 0 ||
            categories.Any(c => c.Equals("all", StringComparison.OrdinalIgnoreCase)) ||
            categories.Any(c => c.Equals(category, StringComparison.OrdinalIgnoreCase));

        // ──────────────────────────────────────────────────────────────────────
        // CheckGeneratedRegex — runtime Regex → [GeneratedRegex] (Medium)
        // ──────────────────────────────────────────────────────────────────────

        private List<SourceGeneratorOpportunity> CheckGeneratedRegex(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var opportunities = new List<SourceGeneratorOpportunity>();

            // new Regex("literal")
            foreach (var creation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            {
                try
                {
                    var typeSymbol = model.GetTypeInfo(creation).Type;
                    if (typeSymbol?.ToDisplayString() != "System.Text.RegularExpressions.Regex") continue;

                    var args = creation.ArgumentList?.Arguments ?? default;
                    if (args.Count == 0) continue;
                    if (!args[0].Expression.IsKind(SyntaxKind.StringLiteralExpression)) continue;

                    opportunities.Add(CreateOpportunity(
                        "GeneratedRegex", "Medium",
                        "new Regex(literal) — replace with [GeneratedRegex]",
                        "Runtime-compiled Regex is slower and AOT-incompatible. [GeneratedRegex] source-generates " +
                        "an optimized, AOT-safe Regex at compile time.",
                        tree, creation, project, "new Regex",
                        "Add [GeneratedRegex] attribute to a partial method returning Regex.",
                        "[GeneratedRegex(@\"your-pattern\", RegexOptions.Compiled)]\nprivate static partial Regex MyRegex();\n" +
                        "// Usage: MyRegex().IsMatch(input);"));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking Regex creation in {File}", tree.FilePath);
                }
            }

            // Regex.IsMatch / Match / Matches / Replace / Split with string literal pattern
            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                try
                {
                    var symbolInfo = model.GetSymbolInfo(invocation);
                    if (symbolInfo.Symbol is not IMethodSymbol methodSymbol) continue;

                    var containingType = methodSymbol.ContainingType?.ToDisplayString() ?? string.Empty;
                    if (containingType != "System.Text.RegularExpressions.Regex") continue;
                    if (!methodSymbol.IsStatic) continue;
                    if (methodSymbol.Name is not ("IsMatch" or "Match" or "Matches" or "Replace" or "Split")) continue;

                    var args = invocation.ArgumentList.Arguments;
                    if (!args.Any(a => a.Expression.IsKind(SyntaxKind.StringLiteralExpression))) continue;

                    opportunities.Add(CreateOpportunity(
                        "GeneratedRegex", "Medium",
                        $"Regex.{methodSymbol.Name}(literal) — replace with [GeneratedRegex]",
                        "Static Regex methods with literal patterns can be replaced with [GeneratedRegex] source generators " +
                        "for compile-time code generation, better performance, and AOT compatibility.",
                        tree, invocation, project, $"Regex.{methodSymbol.Name}",
                        "Extract the pattern into a [GeneratedRegex] partial method.",
                        "[GeneratedRegex(@\"your-pattern\")]\nprivate static partial Regex MyRegex();\n" +
                        $"// Usage: MyRegex().{methodSymbol.Name}(input);"));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking static Regex call in {File}", tree.FilePath);
                }
            }

            return opportunities;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckJsonSerializable — JsonSerializer without context → [JsonSerializable] (Medium)
        // ──────────────────────────────────────────────────────────────────────

        private static bool HasJsonSerializerContext(Compilation compilation)
        {
            foreach (var tree in compilation.SyntaxTrees)
            {
                var root = tree.GetRoot();
                foreach (var cls in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    if (cls.BaseList == null) continue;
                    if (cls.BaseList.Types.Any(t => t.ToString().Contains("JsonSerializerContext")))
                        return true;
                }
            }
            return false;
        }

        private List<SourceGeneratorOpportunity> CheckJsonSerializable(
            SyntaxNode root, SemanticModel model, SyntaxTree tree,
            string project, bool hasJsonSerializerContext)
        {
            var opportunities = new List<SourceGeneratorOpportunity>();

            if (hasJsonSerializerContext)
                return opportunities; // Already using source gen

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                try
                {
                    var symbolInfo = model.GetSymbolInfo(invocation);
                    if (symbolInfo.Symbol is not IMethodSymbol methodSymbol) continue;

                    var containingType = methodSymbol.ContainingType?.ToDisplayString() ?? string.Empty;
                    if (containingType != "System.Text.Json.JsonSerializer") continue;
                    if (methodSymbol.Name is not ("Serialize" or "Deserialize" or "SerializeAsync" or "DeserializeAsync")) continue;

                    opportunities.Add(CreateOpportunity(
                        "JsonSerializable", "Medium",
                        $"JsonSerializer.{methodSymbol.Name} without JsonSerializerContext",
                        "Using JsonSerializer without a source-generated JsonSerializerContext relies on runtime reflection, " +
                        "which is incompatible with Native AOT and slower than the source-generated alternative.",
                        tree, invocation, project, $"JsonSerializer.{methodSymbol.Name}",
                        "Create a JsonSerializerContext subclass with [JsonSerializable] for each serialized type.",
                        "[JsonSerializable(typeof(MyModel))]\npublic partial class AppJsonContext : JsonSerializerContext { }\n\n" +
                        "// Usage:\nJsonSerializer.Serialize(obj, AppJsonContext.Default.MyModel);\nvar obj = JsonSerializer.Deserialize(json, AppJsonContext.Default.MyModel);"));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking JsonSerializer in {File}", tree.FilePath);
                }
            }

            return opportunities;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckObservableProperty — manual INotifyPropertyChanged → [ObservableProperty] (Low)
        // ──────────────────────────────────────────────────────────────────────

        private List<SourceGeneratorOpportunity> CheckObservableProperty(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var opportunities = new List<SourceGeneratorOpportunity>();

            foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                try
                {
                    if (classDecl.BaseList == null) continue;

                    // Check if class implements INotifyPropertyChanged
                    bool implementsInpc = classDecl.BaseList.Types
                        .Any(t => t.ToString().Contains("INotifyPropertyChanged"));
                    if (!implementsInpc) continue;

                    // Check for manually-implemented PropertyChanged event
                    bool hasPropertyChangedEvent = classDecl.Members
                        .OfType<EventFieldDeclarationSyntax>()
                        .Any(e => e.Declaration.Type.ToString().Contains("PropertyChangedEventHandler")) ||
                        classDecl.Members
                        .OfType<EventDeclarationSyntax>()
                        .Any(e => e.Type.ToString().Contains("PropertyChangedEventHandler"));

                    if (!hasPropertyChangedEvent) continue;

                    // Check for properties with manual notification in setter
                    var propsWithNotify = classDecl.Members
                        .OfType<PropertyDeclarationSyntax>()
                        .Where(p => p.AccessorList != null &&
                            p.AccessorList.Accessors.Any(a =>
                                a.IsKind(SyntaxKind.SetAccessorDeclaration) &&
                                a.Body?.ToString() is { } body &&
                                (body.Contains("PropertyChanged") || body.Contains("OnPropertyChanged") || body.Contains("RaisePropertyChanged"))))
                        .ToList();

                    if (propsWithNotify.Count == 0) continue;

                    opportunities.Add(CreateOpportunity(
                        "ObservableProperty", "Low",
                        $"Class '{classDecl.Identifier.Text}' implements INotifyPropertyChanged manually — use [ObservableProperty]",
                        $"Found {propsWithNotify.Count} property/properties with manual change notification. " +
                        "CommunityToolkit.Mvvm's [ObservableProperty] source generator eliminates this boilerplate.",
                        tree, classDecl, project, classDecl.Identifier.Text,
                        "Add CommunityToolkit.Mvvm package, inherit ObservableObject, and replace backing fields with [ObservableProperty].",
                        "// Before (manual):\npublic string Name\n{\n    get => _name;\n    set { _name = value; OnPropertyChanged(); }\n}\n\n" +
                        "// After (source generated):\n[ObservableProperty]\nprivate string _name;"));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking INotifyPropertyChanged in {File}", tree.FilePath);
                }
            }

            return opportunities;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckRelayCommand — manual ICommand → [RelayCommand] (Low)
        // ──────────────────────────────────────────────────────────────────────

        private List<SourceGeneratorOpportunity> CheckRelayCommand(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var opportunities = new List<SourceGeneratorOpportunity>();

            foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                try
                {
                    if (classDecl.BaseList == null) continue;

                    // Check if class directly implements ICommand
                    bool implementsICommand = classDecl.BaseList.Types
                        .Any(t => t.ToString() is "ICommand" or "System.Windows.Input.ICommand");
                    if (!implementsICommand) continue;

                    opportunities.Add(CreateOpportunity(
                        "RelayCommand", "Low",
                        $"Class '{classDecl.Identifier.Text}' implements ICommand manually — use [RelayCommand]",
                        "Manually implementing ICommand requires boilerplate CanExecute/Execute methods. " +
                        "CommunityToolkit.Mvvm's [RelayCommand] generates this automatically from a method.",
                        tree, classDecl, project, classDecl.Identifier.Text,
                        "Add CommunityToolkit.Mvvm package and use [RelayCommand] on the handler method in a ViewModel.",
                        "// Before (manual ICommand class):\nclass SaveCommand : ICommand { ... }\n\n" +
                        "// After (source generated in ViewModel):\n[RelayCommand]\nprivate void Save() { ... }\n" +
                        "// Generates: public IRelayCommand SaveCommand { get; }"));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking ICommand in {File}", tree.FilePath);
                }
            }

            // Also detect fields of type ICommand assigned with new SomeCommand()
            foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
            {
                try
                {
                    var typeText = field.Declaration.Type.ToString();
                    if (typeText is not ("ICommand" or "RelayCommand" or "DelegateCommand" or "ActionCommand")) continue;

                    var varName = field.Declaration.Variables.FirstOrDefault()?.Identifier.Text ?? "command";
                    opportunities.Add(CreateOpportunity(
                        "RelayCommand", "Low",
                        $"ICommand field '{varName}' — consider [RelayCommand] source generator",
                        "Manually created ICommand fields can be replaced with [RelayCommand] on a method, " +
                        "eliminating the command class and field declaration.",
                        tree, field, project, varName,
                        "Use [RelayCommand] attribute from CommunityToolkit.Mvvm on the handler method.",
                        "// Before:\nprivate ICommand _saveCommand = new RelayCommand(Save);\n\n" +
                        "// After:\n[RelayCommand]\nprivate void Save() { ... }"));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking ICommand field in {File}", tree.FilePath);
                }
            }

            return opportunities;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CheckLoggerMessage — logger.$Level($"...") → [LoggerMessage] (Low)
        // ──────────────────────────────────────────────────────────────────────

        private List<SourceGeneratorOpportunity> CheckLoggerMessage(
            SyntaxNode root, SemanticModel model, SyntaxTree tree, string project)
        {
            var opportunities = new List<SourceGeneratorOpportunity>();

            var logMethodNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "LogTrace", "LogDebug", "LogInformation", "LogWarning", "LogError", "LogCritical", "Log"
            };

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                try
                {
                    var symbolInfo = model.GetSymbolInfo(invocation);
                    if (symbolInfo.Symbol is not IMethodSymbol methodSymbol) continue;

                    if (!logMethodNames.Contains(methodSymbol.Name)) continue;

                    // Check if it's on an ILogger
                    var containingType = methodSymbol.ContainingType?.ToDisplayString() ?? string.Empty;
                    bool isLogger = containingType.Contains("ILogger") ||
                                    containingType.Contains("LoggerExtensions") ||
                                    containingType.Contains("Microsoft.Extensions.Logging");
                    if (!isLogger) continue;

                    // Check if any argument is an interpolated string
                    bool hasInterpolated = invocation.ArgumentList.Arguments
                        .Any(a => a.Expression.IsKind(SyntaxKind.InterpolatedStringExpression));

                    if (!hasInterpolated) continue;

                    opportunities.Add(CreateOpportunity(
                        "LoggerMessage", "Low",
                        $"{methodSymbol.Name}($\"...\") with string interpolation — use [LoggerMessage]",
                        "Using string interpolation in logger calls allocates strings even when the log level is disabled. " +
                        "[LoggerMessage] source generator creates a high-performance, structured logging method with zero allocation " +
                        "when the log level is not enabled.",
                        tree, invocation, project, methodSymbol.Name,
                        "Use [LoggerMessage] attribute on a partial method for high-performance, structured logging.",
                        "// Before:\n_logger.LogInformation($\"Processing {userId} at {DateTime.Now}\");\n\n" +
                        "// After:\n[LoggerMessage(LogLevel.Information, \"Processing {UserId} at {Time}\")]\nprivate partial void LogProcessing(string userId, DateTime time);\n" +
                        "// Usage: LogProcessing(userId, DateTime.Now);"));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking logger message in {File}", tree.FilePath);
                }
            }

            return opportunities;
        }

        // ──────────────────────────────────────────────────────────────────────
        // CreateOpportunity helper
        // ──────────────────────────────────────────────────────────────────────

        private static SourceGeneratorOpportunity CreateOpportunity(
            string category, string severity, string title, string description,
            SyntaxTree tree, SyntaxNode node,
            string project, string symbolName,
            string recommendation, string fixExample)
        {
            var lineSpan = tree.GetLineSpan(node.Span);
            var line = lineSpan.StartLinePosition.Line + 1;
            var rawText = node.ToString();
            var codeSnippet = rawText.Length > 100 ? rawText[..100] + "…" : rawText;

            return new SourceGeneratorOpportunity
            {
                Category = category,
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
