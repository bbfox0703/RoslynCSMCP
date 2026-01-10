using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Models;
using System.Collections.Concurrent;

namespace RoslynMcpServer.Services
{
    public class FileAnalysisCache
    {
        public DateTime LastModified { get; set; }
        public string FileHash { get; set; } = string.Empty;
        public List<SymbolSearchResult> CachedSymbols { get; set; } = new();
        public List<ComplexityResult> CachedComplexity { get; set; } = new();
    }

    public class IncrementalAnalyzer
    {
        private readonly Dictionary<string, FileAnalysisCache> _fileCache;
        private readonly SemaphoreSlim _analysisLock;
        private readonly ILogger<IncrementalAnalyzer> _logger;

        public IncrementalAnalyzer(ILogger<IncrementalAnalyzer> logger)
        {
            _fileCache = new Dictionary<string, FileAnalysisCache>();
            _analysisLock = new SemaphoreSlim(Environment.ProcessorCount);
            _logger = logger;
        }

        public async Task<AnalysisResult> AnalyzeIncrementallyAsync(
            Solution solution, IEnumerable<DocumentId>? changedDocuments = null)
        {
            var documentsToAnalyze = changedDocuments?.ToHashSet() ?? 
                solution.Projects.SelectMany(p => p.Documents).Select(d => d.Id).ToHashSet();

            var result = new AnalysisResult
            {
                AnalysisStartTime = DateTime.UtcNow
            };

            var batchSize = Math.Max(1, Environment.ProcessorCount);

            // Process in batches to manage memory
            await ProcessDocumentBatches(solution, documentsToAnalyze, batchSize, result);

            result.AnalysisEndTime = DateTime.UtcNow;
            return result;
        }

        private async Task ProcessDocumentBatches(
            Solution solution, 
            HashSet<DocumentId> documentIds, 
            int batchSize, 
            AnalysisResult result)
        {
            var batches = documentIds.Batch(batchSize);

            foreach (var batch in batches)
            {
                await _analysisLock.WaitAsync();
                try
                {
                    var tasks = batch.Select(docId => 
                        AnalyzeDocumentAsync(solution.GetDocument(docId), result));
                    await Task.WhenAll(tasks);

                    // Force garbage collection after each batch
                    if (result.ProcessedDocuments % (batchSize * 10) == 0)
                    {
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                    }
                }
                finally
                {
                    _analysisLock.Release();
                }
            }
        }

        private async Task AnalyzeDocumentAsync(Document? document, AnalysisResult result)
        {
            if (document?.FilePath == null) return;

            try
            {
                // Check if document needs analysis
                if (!await ShouldAnalyzeDocument(document))
                {
                    // Use cached results
                    if (_fileCache.TryGetValue(document.FilePath, out var cache))
                    {
                        result.Symbols.AddRange(cache.CachedSymbols);
                        result.ComplexityIssues.AddRange(cache.CachedComplexity);
                    }
                    return;
                }

                var syntaxTree = await document.GetSyntaxTreeAsync();
                if (syntaxTree == null) return;

                var root = await syntaxTree.GetRootAsync();
                var semanticModel = await document.GetSemanticModelAsync();

                if (semanticModel == null) return;

                // Analyze symbols
                var symbols = ExtractSymbols(root, document, semanticModel);
                result.Symbols.AddRange(symbols);

                // Analyze complexity
                var complexityIssues = AnalyzeComplexity(root, document.FilePath);
                result.ComplexityIssues.AddRange(complexityIssues);

                // Update cache
                UpdateFileCache(document.FilePath, symbols, complexityIssues);

                result.ProcessedDocuments++;
            }
            catch (Exception ex)
            {
                // Log error but continue processing other documents
                _logger.LogError(ex, "Error analyzing document {DocumentPath}", document.FilePath);
            }
        }

        private Task<bool> ShouldAnalyzeDocument(Document document)
        {
            if (document.FilePath == null) return Task.FromResult(false);

            if (!_fileCache.TryGetValue(document.FilePath, out var cache))
                return Task.FromResult(true);

            try
            {
                var fileInfo = new FileInfo(document.FilePath);
                if (!fileInfo.Exists)
                    return Task.FromResult(false);

                // Check if file was modified
                if (fileInfo.LastWriteTimeUtc > cache.LastModified)
                    return Task.FromResult(true);

                // Could also check file hash for more accuracy
                return Task.FromResult(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cannot check file timestamp for {FilePath}, will analyze to be safe", document.FilePath);
                return Task.FromResult(true); // If we can't check, analyze to be safe
            }
        }

        private List<SymbolSearchResult> ExtractSymbols(SyntaxNode root, Document document, SemanticModel semanticModel)
        {
            var symbols = new List<SymbolSearchResult>();

            foreach (var node in root.DescendantNodes())
            {
                var symbol = semanticModel.GetDeclaredSymbol(node);
                if (symbol == null) continue;

                var location = node.GetLocation();
                var lineSpan = location.GetLineSpan();

                symbols.Add(new SymbolSearchResult
                {
                    Name = symbol.Name,
                    FullName = symbol.ToDisplayString(),
                    Category = GetSymbolCategory(symbol),
                    Location = $"{document.Project.Name}:{Path.GetFileName(document.FilePath)}:{lineSpan.StartLinePosition.Line + 1}",
                    ProjectName = document.Project.Name,
                    FilePath = document.FilePath ?? "",
                    LineNumber = lineSpan.StartLinePosition.Line + 1,
                    Summary = GetSymbolSummary(symbol),
                    Accessibility = symbol.DeclaredAccessibility.ToString().ToLower(),
                    SymbolKind = symbol.Kind,
                    Namespace = symbol.ContainingNamespace?.ToDisplayString() ?? ""
                });
            }

            return symbols;
        }

        private List<ComplexityResult> AnalyzeComplexity(SyntaxNode root, string filePath)
        {
            var complexityResults = new List<ComplexityResult>();

            // Analyze methods
            var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
            foreach (var method in methods)
            {
                AnalyzeMemberComplexity(method, filePath, complexityResults);
            }

            // Analyze properties with getters/setters
            var properties = root.DescendantNodes().OfType<PropertyDeclarationSyntax>()
                .Where(p => p.ExpressionBody != null ||
                           (p.AccessorList != null && p.AccessorList.Accessors.Any(a => a.Body != null || a.ExpressionBody != null)));
            foreach (var property in properties)
            {
                AnalyzeMemberComplexity(property, filePath, complexityResults);
            }

            // Analyze constructors
            var constructors = root.DescendantNodes().OfType<ConstructorDeclarationSyntax>();
            foreach (var constructor in constructors)
            {
                AnalyzeMemberComplexity(constructor, filePath, complexityResults);
            }

            // Analyze local functions (nested functions)
            var localFunctions = root.DescendantNodes().OfType<LocalFunctionStatementSyntax>();
            foreach (var localFunction in localFunctions)
            {
                AnalyzeMemberComplexity(localFunction, filePath, complexityResults);
            }

            return complexityResults;
        }

        private void AnalyzeMemberComplexity(SyntaxNode memberNode, string filePath, List<ComplexityResult> results)
        {
            var complexity = CalculateCyclomaticComplexity(memberNode);
            if (complexity >= 5) // Threshold
            {
                var lineSpan = memberNode.GetLocation().GetLineSpan();
                var (memberName, memberType) = GetMemberNameAndType(memberNode);

                results.Add(new ComplexityResult
                {
                    MethodName = $"{memberName} ({memberType})",
                    FileName = Path.GetFileName(filePath),
                    LineNumber = lineSpan.StartLinePosition.Line + 1,
                    Complexity = complexity,
                    ClassName = GetContainingClassName(memberNode),
                    Namespace = GetContainingNamespace(memberNode)
                });
            }
        }

        private (string name, string type) GetMemberNameAndType(SyntaxNode memberNode)
        {
            return memberNode switch
            {
                MethodDeclarationSyntax method => (method.Identifier.ValueText, "Method"),
                PropertyDeclarationSyntax property => (property.Identifier.ValueText, "Property"),
                ConstructorDeclarationSyntax constructor => (constructor.Identifier.ValueText, "Constructor"),
                LocalFunctionStatementSyntax localFunc => (localFunc.Identifier.ValueText, "Local Function"),
                _ => ("Unknown", "Unknown")
            };
        }

        private int CalculateCyclomaticComplexity(SyntaxNode memberNode)
        {
            int complexity = 1; // Base complexity

            // Traditional decision points
            var decisionPoints = memberNode.DescendantNodes().Where(node =>
                node.IsKind(SyntaxKind.IfStatement) ||
                node.IsKind(SyntaxKind.WhileStatement) ||
                node.IsKind(SyntaxKind.ForStatement) ||
                node.IsKind(SyntaxKind.ForEachStatement) ||
                node.IsKind(SyntaxKind.SwitchStatement) ||
                node.IsKind(SyntaxKind.CatchClause) ||
                node.IsKind(SyntaxKind.ConditionalExpression) ||      // Ternary operator (? :)
                node.IsKind(SyntaxKind.CoalesceExpression) ||         // Null coalescing (??)
                node.IsKind(SyntaxKind.SwitchExpression));            // Switch expressions (C# 8.0+)

            complexity += decisionPoints.Count();

            // Logical operators
            var logicalOperators = memberNode.DescendantTokens().Where(token =>
                token.IsKind(SyntaxKind.AmpersandAmpersandToken) ||   // &&
                token.IsKind(SyntaxKind.BarBarToken) ||               // ||
                token.IsKind(SyntaxKind.QuestionQuestionToken));      // ??

            complexity += logicalOperators.Count();

            // Switch expression arms (each arm adds complexity)
            var switchExpressions = memberNode.DescendantNodes().OfType<SwitchExpressionSyntax>();
            foreach (var switchExpr in switchExpressions)
            {
                // Each arm except the first adds complexity (first is already counted as SwitchExpression)
                if (switchExpr.Arms.Count > 0)
                {
                    complexity += switchExpr.Arms.Count - 1;
                }
            }

            // When clauses in catch/case statements
            var whenClauses = memberNode.DescendantNodes().OfType<WhenClauseSyntax>();
            complexity += whenClauses.Count();

            return complexity;
        }

        private string GetContainingClassName(SyntaxNode memberNode)
        {
            var classDeclaration = memberNode.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
            if (classDeclaration != null)
                return classDeclaration.Identifier.ValueText;

            var structDeclaration = memberNode.Ancestors().OfType<StructDeclarationSyntax>().FirstOrDefault();
            if (structDeclaration != null)
                return structDeclaration.Identifier.ValueText;

            var recordDeclaration = memberNode.Ancestors().OfType<RecordDeclarationSyntax>().FirstOrDefault();
            if (recordDeclaration != null)
                return recordDeclaration.Identifier.ValueText;

            return "";
        }

        private string GetContainingNamespace(SyntaxNode memberNode)
        {
            var namespaceDeclaration = memberNode.Ancestors().OfType<NamespaceDeclarationSyntax>().FirstOrDefault();
            if (namespaceDeclaration != null)
                return namespaceDeclaration.Name.ToString();

            var fileScopedNamespace = memberNode.Ancestors().OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault();
            if (fileScopedNamespace != null)
                return fileScopedNamespace.Name.ToString();

            return "";
        }

        private string GetSymbolCategory(ISymbol symbol)
        {
            return symbol switch
            {
                INamedTypeSymbol namedType => namedType.TypeKind.ToString(),
                IMethodSymbol => "Method",
                IPropertySymbol => "Property",
                IFieldSymbol => "Field",
                IEventSymbol => "Event",
                INamespaceSymbol => "Namespace",
                _ => symbol.Kind.ToString()
            };
        }

        private string GetSymbolSummary(ISymbol symbol)
        {
            return symbol switch
            {
                IMethodSymbol method => $"({string.Join(", ", method.Parameters.Select(p => $"{p.Type.Name} {p.Name}"))})",
                IPropertySymbol property => $": {property.Type.Name}",
                IFieldSymbol field => $": {field.Type.Name}",
                INamedTypeSymbol type => $"{type.TypeKind} with {type.GetMembers().Length} members",
                _ => ""
            };
        }

        private void UpdateFileCache(string filePath, List<SymbolSearchResult> symbols, List<ComplexityResult> complexityResults)
        {
            var cache = new FileAnalysisCache
            {
                LastModified = DateTime.UtcNow,
                CachedSymbols = symbols,
                CachedComplexity = complexityResults
            };

            _fileCache[filePath] = cache;
        }
    }

    public static class EnumerableExtensions
    {
        public static IEnumerable<IEnumerable<T>> Batch<T>(this IEnumerable<T> source, int batchSize)
        {
            var batch = new List<T>(batchSize);
            foreach (var item in source)
            {
                batch.Add(item);
                if (batch.Count == batchSize)
                {
                    yield return batch;
                    batch = new List<T>(batchSize);
                }
            }

            if (batch.Count > 0)
                yield return batch;
        }
    }
}