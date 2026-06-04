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
    }

    public class TypeMetric
    {
        public string Name { get; set; } = string.Empty;
        public int Lines { get; set; }
        public string FilePath { get; set; } = string.Empty;
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

        public async Task<string> GetMetricsAsync(
            string solutionPath,
            string groupBy = "project")
        {
            try
            {
                var solution = await _codeAnalysisService.GetSolutionAsync(solutionPath);
                var metrics = await AnalyzeMetrics(solution);
                return FormatMetrics(metrics, solutionPath, groupBy);
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

                    // Analyze types for size
                    foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                    {
                        var typeLines = typeDecl.GetLocation().GetLineSpan();
                        var lineCount = typeLines.EndLinePosition.Line - typeLines.StartLinePosition.Line + 1;

                        allTypes.Add(new TypeMetric
                        {
                            Name = typeDecl.Identifier.Text,
                            Lines = lineCount,
                            FilePath = document.FilePath ?? ""
                        });
                    }

                    // Count methods and analyze complexity
                    var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();
                    metrics.TotalMethods += methods.Count;
                    projectMetric.Methods += methods.Count;

                    foreach (var method in methods)
                    {
                        var complexity = CalculateComplexity(method);
                        allComplexities.Add(new ComplexityMetric
                        {
                            MethodName = method.Identifier.Text,
                            Complexity = complexity,
                            FilePath = document.FilePath ?? "",
                            LineNumber = method.GetLocation().GetLineSpan().StartLinePosition.Line + 1
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

            return metrics;
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

        private string FormatMetrics(CodeMetrics metrics, string solutionPath, string groupBy)
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
            if (groupBy.ToLower() == "project" && metrics.ProjectMetrics.Any())
            {
                builder.AppendLine("📁 Project Breakdown:");
                foreach (var kvp in metrics.ProjectMetrics.OrderByDescending(p => p.Value.Lines))
                {
                    builder.AppendLine($"  {kvp.Key}:");
                    builder.AppendLine($"    Files: {kvp.Value.Files}, Lines: {kvp.Value.Lines:N0}, Classes: {kvp.Value.Classes}, Methods: {kvp.Value.Methods}");
                }
            }

            return builder.ToString();
        }
    }
}
