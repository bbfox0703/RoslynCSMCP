using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using System.Text;

namespace RoslynMcpServer.Core.Services
{
    public class CodeMetrics
    {
        public int TotalProjects { get; set; }
        public int TotalFiles { get; set; }
        public int TotalLines { get; set; }
        public int CodeLines { get; set; }
        public int CommentLines { get; set; }
        public int BlankLines { get; set; }

        public int TotalClasses { get; set; }
        public int TotalInterfaces { get; set; }
        public int TotalStructs { get; set; }
        public int TotalEnums { get; set; }
        public int TotalMethods { get; set; }
        public int TotalProperties { get; set; }

        public double AverageComplexity { get; set; }
        public int MaxComplexity { get; set; }
        public string? MostComplexMethod { get; set; }
        public int HighComplexityCount { get; set; }

        public List<TypeMetric> LargestTypes { get; set; } = new();
        public List<ComplexityMetric> ComplexityHotspots { get; set; } = new();
        public Dictionary<string, ProjectMetric> ProjectMetrics { get; set; } = new();
        public Dictionary<string, NamespaceMetric> NamespaceMetrics { get; set; } = new();
        public List<TypeMetric> TypeMetrics { get; set; } = new();
    }

    public class TypeMetric
    {
        public string Name { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public int Lines { get; set; }
        public int Methods { get; set; }
        public int Properties { get; set; }
        public int MaxComplexity { get; set; }
        public string FilePath { get; set; } = string.Empty;
    }

    public class NamespaceMetric
    {
        public int Files { get; set; }
        public int Types { get; set; }
        public int Lines { get; set; }
        public int Methods { get; set; }
    }

    /// <summary>
    /// Breakdown sections that GetMetricsAsync can append after the solution-wide totals
    /// </summary>
    [Flags]
    public enum MetricsBreakdown
    {
        None = 0,
        Project = 1,
        Namespace = 2,
        Type = 4
    }

    public class ComplexityMetric
    {
        public string MethodName { get; set; } = string.Empty;
        public int Complexity { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public int LineNumber { get; set; }
    }

    public class ProjectMetric
    {
        public int Files { get; set; }
        public int Lines { get; set; }
        public int Classes { get; set; }
        public int Methods { get; set; }
    }

    public class CodeMetricsService
    {
        private readonly CodeAnalysisService _codeAnalysisService;
        private readonly ILogger<CodeMetricsService> _logger;

        public CodeMetricsService(
            CodeAnalysisService codeAnalysisService,
            ILogger<CodeMetricsService> logger)
        {
            _codeAnalysisService = codeAnalysisService;
            _logger = logger;
        }

        private const int MaxNamespacesShown = 30;
        private const int MaxTypesShown = 20;
        private const string GlobalNamespace = "(global namespace)";

        /// <summary>
        /// Parses a comma-separated list of breakdowns: project, namespace, type, or none (case-insensitive).
        /// An empty value means no breakdown. Returns false for any unrecognized value.
        /// </summary>
        public static bool TryParseGroupBy(string? groupBy, out MetricsBreakdown breakdown)
        {
            breakdown = MetricsBreakdown.None;
            if (string.IsNullOrWhiteSpace(groupBy))
                return true;

            foreach (var part in groupBy.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                switch (part.ToLowerInvariant())
                {
                    case "project": breakdown |= MetricsBreakdown.Project; break;
                    case "namespace": breakdown |= MetricsBreakdown.Namespace; break;
                    case "type": breakdown |= MetricsBreakdown.Type; break;
                    case "none": break;
                    default: return false;
                }
            }
            return true;
        }

        public const string GroupByErrorMessage = "groupBy must be project, namespace, type, or none (comma-separate several, e.g. 'project,namespace')";

        /// <summary>
        /// Computes solution-wide metrics and formats them as text.
        /// </summary>
        /// <param name="groupBy">Breakdowns to append; see <see cref="TryParseGroupBy"/></param>
        /// <param name="summaryOnly">Print only the totals, omitting the largest-type and hotspot lists and all breakdowns</param>
        public async Task<string> GetMetricsAsync(
            string solutionPath,
            string groupBy = "project",
            bool summaryOnly = false)
        {
            if (!TryParseGroupBy(groupBy, out var breakdown))
                throw new ArgumentException($"{GroupByErrorMessage}; got '{groupBy}'", nameof(groupBy));

            try
            {
                var solution = await _codeAnalysisService.GetSolutionAsync(solutionPath);
                var metrics = await AnalyzeMetrics(solution);
                return FormatMetrics(metrics, solutionPath, summaryOnly ? MetricsBreakdown.None : breakdown, summaryOnly);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting code metrics");
                throw;
            }
        }

        private async Task<CodeMetrics> AnalyzeMetrics(Solution solution)
        {
            var metrics = new CodeMetrics();
            var allComplexities = new List<ComplexityMetric>();
            var allTypes = new List<TypeMetric>();
            var namespaceFiles = new Dictionary<string, HashSet<string>>();

            metrics.TotalProjects = solution.Projects.Count();

            foreach (var project in solution.Projects)
            {
                if (!project.SupportsCompilation)
                    continue;

                var projectMetric = new ProjectMetric();
                var compilation = await project.GetCompilationAsync();
                if (compilation == null)
                    continue;

                foreach (var document in project.Documents)
                {
                    if (document.FilePath == null || !document.FilePath.EndsWith(".cs"))
                        continue;

                    metrics.TotalFiles++;
                    projectMetric.Files++;

                    var syntaxTree = await document.GetSyntaxTreeAsync();
                    if (syntaxTree == null)
                        continue;

                    var root = await syntaxTree.GetRootAsync();
                    var text = await document.GetTextAsync();

                    // Count lines
                    var lineMetrics = AnalyzeLines(text.ToString());
                    metrics.TotalLines += lineMetrics.total;
                    metrics.CodeLines += lineMetrics.code;
                    metrics.CommentLines += lineMetrics.comments;
                    metrics.BlankLines += lineMetrics.blank;
                    projectMetric.Lines += lineMetrics.total;

                    // Count types
                    var typeCount = CountTypes(root);
                    metrics.TotalClasses += typeCount.classes;
                    metrics.TotalInterfaces += typeCount.interfaces;
                    metrics.TotalStructs += typeCount.structs;
                    metrics.TotalEnums += typeCount.enums;
                    projectMetric.Classes += typeCount.classes;

                    // Count methods and analyze complexity
                    var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();
                    metrics.TotalMethods += methods.Count;
                    projectMetric.Methods += methods.Count;

                    var methodComplexity = new Dictionary<MethodDeclarationSyntax, int>();
                    foreach (var method in methods)
                    {
                        var complexity = CalculateComplexity(method);
                        methodComplexity[method] = complexity;
                        allComplexities.Add(new ComplexityMetric
                        {
                            MethodName = method.Identifier.Text,
                            Complexity = complexity,
                            FilePath = document.FilePath ?? "",
                            LineNumber = method.GetLocation().GetLineSpan().StartLinePosition.Line + 1
                        });
                    }

                    // Per-type and per-namespace metrics (enums count as namespace types but have no type row)
                    foreach (var typeDecl in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
                    {
                        var typeLines = typeDecl.GetLocation().GetLineSpan();
                        var lineCount = typeLines.EndLinePosition.Line - typeLines.StartLinePosition.Line + 1;
                        var namespaceName = GetNamespaceName(typeDecl);

                        if (!metrics.NamespaceMetrics.TryGetValue(namespaceName, out var namespaceMetric))
                        {
                            namespaceMetric = new NamespaceMetric();
                            metrics.NamespaceMetrics[namespaceName] = namespaceMetric;
                            namespaceFiles[namespaceName] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        }
                        namespaceMetric.Types++;
                        namespaceFiles[namespaceName].Add(document.FilePath ?? "");

                        // Nested types are already inside their container's line span
                        if (typeDecl.Parent is not BaseTypeDeclarationSyntax)
                            namespaceMetric.Lines += lineCount;

                        if (typeDecl is not TypeDeclarationSyntax type)
                            continue;

                        var typeMethods = type.Members.OfType<MethodDeclarationSyntax>().ToList();
                        namespaceMetric.Methods += typeMethods.Count;

                        allTypes.Add(new TypeMetric
                        {
                            Name = type.Identifier.Text,
                            FullName = GetTypeFullName(type, namespaceName),
                            Lines = lineCount,
                            Methods = typeMethods.Count,
                            Properties = type.Members.OfType<PropertyDeclarationSyntax>().Count(),
                            MaxComplexity = typeMethods.Select(m => methodComplexity.GetValueOrDefault(m)).DefaultIfEmpty(0).Max(),
                            FilePath = document.FilePath ?? ""
                        });
                    }

                    // Count properties
                    metrics.TotalProperties += root.DescendantNodes().OfType<PropertyDeclarationSyntax>().Count();
                }

                if (projectMetric.Files > 0)
                {
                    metrics.ProjectMetrics[project.Name] = projectMetric;
                }
            }

            // Calculate complexity statistics
            if (allComplexities.Any())
            {
                metrics.AverageComplexity = Math.Round(allComplexities.Average(c => c.Complexity), 1);
                metrics.MaxComplexity = allComplexities.Max(c => c.Complexity);
                var mostComplex = allComplexities.OrderByDescending(c => c.Complexity).First();
                metrics.MostComplexMethod = $"{mostComplex.MethodName} (Complexity: {mostComplex.Complexity})";
                metrics.HighComplexityCount = allComplexities.Count(c => c.Complexity > 10);

                // Top 5 complexity hotspots
                metrics.ComplexityHotspots = allComplexities
                    .OrderByDescending(c => c.Complexity)
                    .Take(5)
                    .ToList();
            }

            // Top 5 largest types
            metrics.LargestTypes = allTypes
                .OrderByDescending(t => t.Lines)
                .Take(5)
                .ToList();

            metrics.TypeMetrics = allTypes;
            foreach (var (namespaceName, files) in namespaceFiles)
            {
                metrics.NamespaceMetrics[namespaceName].Files = files.Count;
            }

            return metrics;
        }

        private static string GetNamespaceName(SyntaxNode node)
        {
            var name = string.Join(".", node.Ancestors()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .Reverse()
                .Select(n => n.Name.ToString()));
            return name.Length > 0 ? name : GlobalNamespace;
        }

        private static string GetTypeFullName(TypeDeclarationSyntax type, string namespaceName)
        {
            // Containing types first, e.g. Outer.Inner<T>
            var typeChain = string.Join(".", type.AncestorsAndSelf()
                .OfType<BaseTypeDeclarationSyntax>()
                .Reverse()
                .Select(t => t is TypeDeclarationSyntax { TypeParameterList: { } typeParameters }
                    ? t.Identifier.Text + typeParameters
                    : t.Identifier.Text));

            return namespaceName == GlobalNamespace ? typeChain : $"{namespaceName}.{typeChain}";
        }

        private (int total, int code, int comments, int blank) AnalyzeLines(string text)
        {
            var lines = text.Split('\n');
            int total = lines.Length;
            int blank = 0;
            int comments = 0;
            int code = 0;

            bool inMultiLineComment = false;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    blank++;
                    continue;
                }

                // Multi-line comments
                if (trimmed.StartsWith("/*"))
                    inMultiLineComment = true;

                if (inMultiLineComment)
                {
                    comments++;
                    if (trimmed.EndsWith("*/"))
                        inMultiLineComment = false;
                    continue;
                }

                // Single-line comments
                if (trimmed.StartsWith("//"))
                {
                    comments++;
                    continue;
                }

                code++;
            }

            return (total, code, comments, blank);
        }

        private (int classes, int interfaces, int structs, int enums) CountTypes(SyntaxNode root)
        {
            int classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Count();
            int interfaces = root.DescendantNodes().OfType<InterfaceDeclarationSyntax>().Count();
            int structs = root.DescendantNodes().OfType<StructDeclarationSyntax>().Count();
            int enums = root.DescendantNodes().OfType<EnumDeclarationSyntax>().Count();

            return (classes, interfaces, structs, enums);
        }

        private int CalculateComplexity(MethodDeclarationSyntax method)
            => ComplexityCalculator.CalculateCyclomatic(method);

        private string FormatMetrics(CodeMetrics metrics, string solutionPath, MetricsBreakdown breakdown, bool summaryOnly)
        {
            var builder = new StringBuilder();
            var solutionName = Path.GetFileName(solutionPath);

            builder.AppendLine($"Code Metrics for {solutionName}\n");

            // Overall statistics
            builder.AppendLine("📊 Overall Statistics:");
            builder.AppendLine($"  Total Projects: {metrics.TotalProjects:N0}");
            builder.AppendLine($"  Total Files: {metrics.TotalFiles:N0}");
            builder.AppendLine($"  Total Lines: {metrics.TotalLines:N0}");

            if (metrics.TotalLines > 0)
            {
                var codePercent = (metrics.CodeLines * 100.0 / metrics.TotalLines);
                var commentPercent = (metrics.CommentLines * 100.0 / metrics.TotalLines);
                var blankPercent = (metrics.BlankLines * 100.0 / metrics.TotalLines);

                builder.AppendLine($"  Code Lines: {metrics.CodeLines:N0} ({codePercent:F1}%)");
                builder.AppendLine($"  Comment Lines: {metrics.CommentLines:N0} ({commentPercent:F1}%)");
                builder.AppendLine($"  Blank Lines: {metrics.BlankLines:N0} ({blankPercent:F1}%)");
            }
            builder.AppendLine();

            // Type statistics
            builder.AppendLine("🏗️ Type Statistics:");
            builder.AppendLine($"  Total Classes: {metrics.TotalClasses:N0}");
            builder.AppendLine($"  Total Interfaces: {metrics.TotalInterfaces:N0}");
            builder.AppendLine($"  Total Structs: {metrics.TotalStructs:N0}");
            builder.AppendLine($"  Total Enums: {metrics.TotalEnums:N0}");
            builder.AppendLine($"  Total Methods: {metrics.TotalMethods:N0}");
            builder.AppendLine($"  Total Properties: {metrics.TotalProperties:N0}");
            builder.AppendLine();

            // Complexity metrics
            if (metrics.TotalMethods > 0)
            {
                builder.AppendLine("📈 Complexity Metrics:");
                builder.AppendLine($"  Average Method Complexity: {metrics.AverageComplexity:F1}");
                builder.AppendLine($"  Max Method Complexity: {metrics.MaxComplexity}");
                builder.AppendLine($"  Most Complex: {metrics.MostComplexMethod}");
                builder.AppendLine($"  Methods > 10 Complexity: {metrics.HighComplexityCount}");
                builder.AppendLine();
            }

            if (summaryOnly)
                return builder.ToString();

            // Largest types
            if (metrics.LargestTypes.Any())
            {
                builder.AppendLine("🔝 Largest Types:");
                int rank = 1;
                foreach (var type in metrics.LargestTypes)
                {
                    var fileName = Path.GetFileName(type.FilePath);
                    builder.AppendLine($"  {rank}. {type.Name} - {type.Lines} lines ({fileName})");
                    rank++;
                }
                builder.AppendLine();
            }

            // Complexity hotspots
            if (metrics.ComplexityHotspots.Any())
            {
                builder.AppendLine("⚠️ Complexity Hotspots:");
                int rank = 1;
                foreach (var hotspot in metrics.ComplexityHotspots)
                {
                    var fileName = Path.GetFileName(hotspot.FilePath);
                    builder.AppendLine($"  {rank}. {hotspot.MethodName} - Complexity: {hotspot.Complexity} ({fileName}:{hotspot.LineNumber})");
                    rank++;
                }
                builder.AppendLine();
            }

            // Project breakdown
            if (breakdown.HasFlag(MetricsBreakdown.Project) && metrics.ProjectMetrics.Any())
            {
                builder.AppendLine("📁 Project Breakdown:");
                foreach (var kvp in metrics.ProjectMetrics.OrderByDescending(p => p.Value.Lines))
                {
                    builder.AppendLine($"  {kvp.Key}:");
                    builder.AppendLine($"    Files: {kvp.Value.Files}, Lines: {kvp.Value.Lines:N0}, Classes: {kvp.Value.Classes}, Methods: {kvp.Value.Methods}");
                }
                builder.AppendLine();
            }

            // Namespace breakdown (lines are those inside type declarations)
            if (breakdown.HasFlag(MetricsBreakdown.Namespace) && metrics.NamespaceMetrics.Any())
            {
                builder.AppendLine($"🗂️ Namespace Breakdown ({metrics.NamespaceMetrics.Count} namespaces, largest first):");
                foreach (var kvp in metrics.NamespaceMetrics.OrderByDescending(n => n.Value.Lines).Take(MaxNamespacesShown))
                {
                    builder.AppendLine($"  {kvp.Key}:");
                    builder.AppendLine($"    Files: {kvp.Value.Files}, Types: {kvp.Value.Types}, Lines: {kvp.Value.Lines:N0}, Methods: {kvp.Value.Methods}");
                }
                if (metrics.NamespaceMetrics.Count > MaxNamespacesShown)
                    builder.AppendLine($"  ... and {metrics.NamespaceMetrics.Count - MaxNamespacesShown} more namespaces");
                builder.AppendLine();
            }

            // Type breakdown (one row per declaration, so partial types appear once per part)
            if (breakdown.HasFlag(MetricsBreakdown.Type) && metrics.TypeMetrics.Any())
            {
                builder.AppendLine($"🧩 Type Breakdown ({metrics.TypeMetrics.Count} type declarations, largest first):");
                foreach (var type in metrics.TypeMetrics.OrderByDescending(t => t.Lines).Take(MaxTypesShown))
                {
                    builder.AppendLine($"  {type.FullName} ({Path.GetFileName(type.FilePath)}):");
                    builder.AppendLine($"    Lines: {type.Lines:N0}, Methods: {type.Methods}, Properties: {type.Properties}, Max Method Complexity: {type.MaxComplexity}");
                }
                if (metrics.TypeMetrics.Count > MaxTypesShown)
                    builder.AppendLine($"  ... and {metrics.TypeMetrics.Count - MaxTypesShown} more types");
            }

            return builder.ToString();
        }
    }
}
