using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Core.Models;

namespace RoslynMcpServer.Core.Services
{
    /// <summary>
    /// Service for analyzing statistics of a single C# file
    /// </summary>
    public class FileStatisticsAnalyzer
    {
        private readonly ILogger<FileStatisticsAnalyzer> _logger;

        public FileStatisticsAnalyzer(ILogger<FileStatisticsAnalyzer> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Analyzes statistics for a single C# file
        /// </summary>
        public async Task<FileStatisticsResults> AnalyzeFileStatisticsAsync(string filePath)
        {
            var results = new FileStatisticsResults();

            try
            {
                if (!File.Exists(filePath))
                {
                    results.Warnings.Add(new OperationWarning
                    {
                        Context = "Validation",
                        Message = "File not found"
                    });
                    return results;
                }

                if (!filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    results.Warnings.Add(new OperationWarning
                    {
                        Context = "Validation",
                        Message = "File must be a C# source file (.cs)"
                    });
                    return results;
                }

                var fileInfo = new FileInfo(filePath);
                var fileName = fileInfo.Name;

                // Parse the file
                var code = await File.ReadAllTextAsync(filePath);
                var syntaxTree = CSharpSyntaxTree.ParseText(code, path: filePath);
                var root = await syntaxTree.GetRootAsync();

                // Create a basic compilation for semantic analysis
                var compilation = CSharpCompilation.Create("FileAnalysis")
                    .AddSyntaxTrees(syntaxTree)
                    .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

                var semanticModel = compilation.GetSemanticModel(syntaxTree);

                var stats = new FileStatistics
                {
                    FileName = fileName,
                    FilePath = filePath,
                    ProjectName = "(standalone)",
                    SizeInBytes = fileInfo.Length
                };

                // Analyze lines
                AnalyzeLines(syntaxTree, stats);

                // Count code elements
                CountCodeElements(root, stats);

                // Calculate complexity
                CalculateComplexity(root, stats);

                // Count using directives
                CountUsingDirectives(root, stats);

                // Check documentation
                CheckDocumentation(root, semanticModel, stats);

                results.Statistics = stats;

                _logger.LogInformation(
                    "File statistics complete: {FileName} - {Lines} lines, {Methods} methods, complexity {Complexity}",
                    fileName,
                    stats.TotalLines,
                    stats.MethodCount,
                    stats.CyclomaticComplexity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing file statistics");
                results.Warnings.Add(new OperationWarning
                {
                    Context = "Analysis",
                    Message = $"Error: {ex.Message}"
                });
            }

            return results;
        }

        /// <summary>
        /// Analyzes line counts
        /// </summary>
        private void AnalyzeLines(SyntaxTree syntaxTree, FileStatistics stats)
        {
            var text = syntaxTree.GetText();
            var lines = text.Lines;

            stats.TotalLines = lines.Count;

            foreach (var line in lines)
            {
                var lineText = line.ToString().Trim();

                if (string.IsNullOrWhiteSpace(lineText))
                {
                    stats.BlankLines++;
                }
                else if (lineText.StartsWith("//") || lineText.StartsWith("/*") || lineText.StartsWith("*"))
                {
                    stats.CommentLines++;
                }
                else
                {
                    stats.CodeLines++;
                }
            }
        }

        /// <summary>
        /// Counts code elements (classes, methods, etc.)
        /// </summary>
        private void CountCodeElements(SyntaxNode root, FileStatistics stats)
        {
            // Count classes
            stats.ClassCount = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Count();

            // Count interfaces
            stats.InterfaceCount = root.DescendantNodes()
                .OfType<InterfaceDeclarationSyntax>()
                .Count();

            // Count structs
            stats.StructCount = root.DescendantNodes()
                .OfType<StructDeclarationSyntax>()
                .Count();

            // Count enums
            stats.EnumCount = root.DescendantNodes()
                .OfType<EnumDeclarationSyntax>()
                .Count();

            // Count methods
            stats.MethodCount = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Count();

            // Count properties
            stats.PropertyCount = root.DescendantNodes()
                .OfType<PropertyDeclarationSyntax>()
                .Count();

            // Count fields
            stats.FieldCount = root.DescendantNodes()
                .OfType<FieldDeclarationSyntax>()
                .Count();
        }

        /// <summary>
        /// Calculates cyclomatic complexity
        /// </summary>
        private void CalculateComplexity(SyntaxNode root, FileStatistics stats)
        {
            var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();

            int totalComplexity = 0;
            int maxComplexity = 0;
            string? mostComplexMethod = null;

            foreach (var method in methods)
            {
                var complexity = CalculateMethodComplexity(method);
                totalComplexity += complexity;

                if (complexity > maxComplexity)
                {
                    maxComplexity = complexity;
                    mostComplexMethod = method.Identifier.Text;
                }
            }

            stats.CyclomaticComplexity = totalComplexity;
            stats.MaxMethodComplexity = maxComplexity;
            stats.MostComplexMethod = mostComplexMethod ?? string.Empty;
        }

        /// <summary>
        /// Calculates cyclomatic complexity for a method
        /// </summary>
        private int CalculateMethodComplexity(MethodDeclarationSyntax method)
        {
            int complexity = 1; // Base complexity

            var body = method.Body;
            if (body == null)
                return complexity;

            // Count decision points
            complexity += body.DescendantNodes().OfType<IfStatementSyntax>().Count();
            complexity += body.DescendantNodes().OfType<WhileStatementSyntax>().Count();
            complexity += body.DescendantNodes().OfType<ForStatementSyntax>().Count();
            complexity += body.DescendantNodes().OfType<ForEachStatementSyntax>().Count();
            complexity += body.DescendantNodes().OfType<CaseSwitchLabelSyntax>().Count();
            complexity += body.DescendantNodes().OfType<CatchClauseSyntax>().Count();
            complexity += body.DescendantNodes().OfType<ConditionalExpressionSyntax>().Count(); // ternary

            // Count logical operators
            var binaryExpressions = body.DescendantNodes().OfType<BinaryExpressionSyntax>();
            foreach (var expr in binaryExpressions)
            {
                if (expr.IsKind(SyntaxKind.LogicalAndExpression) ||
                    expr.IsKind(SyntaxKind.LogicalOrExpression))
                {
                    complexity++;
                }
            }

            return complexity;
        }

        /// <summary>
        /// Counts using directives and namespaces
        /// </summary>
        private void CountUsingDirectives(SyntaxNode root, FileStatistics stats)
        {
            var usingDirectives = root.DescendantNodes()
                .OfType<UsingDirectiveSyntax>()
                .ToList();

            stats.UsingDirectivesCount = usingDirectives.Count;

            stats.Namespaces = usingDirectives
                .Select(u => u.Name?.ToString() ?? string.Empty)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .OrderBy(n => n)
                .ToList();
        }

        /// <summary>
        /// Checks documentation coverage
        /// </summary>
        private void CheckDocumentation(SyntaxNode root, SemanticModel semanticModel, FileStatistics stats)
        {
            // Check types
            var types = root.DescendantNodes()
                .OfType<TypeDeclarationSyntax>()
                .Where(t => t.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)));

            foreach (var type in types)
            {
                var symbol = semanticModel.GetDeclaredSymbol(type);
                if (symbol != null)
                {
                    if (HasDocumentation(symbol))
                        stats.DocumentedMembers++;
                    else
                        stats.UndocumentedMembers++;
                }
            }

            // Check methods
            var methods = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Modifiers.Any(mod => mod.IsKind(SyntaxKind.PublicKeyword)));

            foreach (var method in methods)
            {
                var symbol = semanticModel.GetDeclaredSymbol(method);
                if (symbol != null)
                {
                    if (HasDocumentation(symbol))
                        stats.DocumentedMembers++;
                    else
                        stats.UndocumentedMembers++;
                }
            }

            // Check properties
            var properties = root.DescendantNodes()
                .OfType<PropertyDeclarationSyntax>()
                .Where(p => p.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)));

            foreach (var property in properties)
            {
                var symbol = semanticModel.GetDeclaredSymbol(property);
                if (symbol != null)
                {
                    if (HasDocumentation(symbol))
                        stats.DocumentedMembers++;
                    else
                        stats.UndocumentedMembers++;
                }
            }
        }

        /// <summary>
        /// Checks if a symbol has XML documentation
        /// </summary>
        private bool HasDocumentation(ISymbol symbol)
        {
            var docComment = symbol.GetDocumentationCommentXml();
            return !string.IsNullOrWhiteSpace(docComment);
        }
    }
}
