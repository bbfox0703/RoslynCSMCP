using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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

        public IncrementalAnalyzer()
        {
            _fileCache = new Dictionary<string, FileAnalysisCache>();
            _analysisLock = new SemaphoreSlim(Environment.ProcessorCount);
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
            catch (Exception)
            {
                // Log error but continue processing other documents
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
            catch
            {
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
            var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();

            foreach (var method in methods)
            {
                var complexity = CalculateCyclomaticComplexity(method);
                if (complexity >= 5) // Threshold
                {
                    var lineSpan = method.GetLocation().GetLineSpan();
                    complexityResults.Add(new ComplexityResult
                    {
                        MethodName = method.Identifier.ValueText,
                        FileName = Path.GetFileName(filePath),
                        LineNumber = lineSpan.StartLinePosition.Line + 1,
                        Complexity = complexity,
                        ClassName = GetContainingClassName(method),
                        Namespace = GetContainingNamespace(method)
                    });
                }
            }

            return complexityResults;
        }

        private int CalculateCyclomaticComplexity(MethodDeclarationSyntax method)
        {
            int complexity = 1; // Base complexity

            var decisionPoints = method.DescendantNodes().Where(node => 
                node.IsKind(SyntaxKind.IfStatement) ||
                node.IsKind(SyntaxKind.WhileStatement) ||
                node.IsKind(SyntaxKind.ForStatement) ||
                node.IsKind(SyntaxKind.ForEachStatement) ||
                node.IsKind(SyntaxKind.SwitchStatement) ||
                node.IsKind(SyntaxKind.CatchClause));

            complexity += decisionPoints.Count();

            // Add complexity for logical operators
            var logicalOperators = method.DescendantTokens().Where(token =>
                token.IsKind(SyntaxKind.AmpersandAmpersandToken) ||
                token.IsKind(SyntaxKind.BarBarToken));

            complexity += logicalOperators.Count();

            return complexity;
        }

        private string GetContainingClassName(MethodDeclarationSyntax method)
        {
            var classDeclaration = method.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
            return classDeclaration?.Identifier.ValueText ?? "";
        }

        private string GetContainingNamespace(MethodDeclarationSyntax method)
        {
            var namespaceDeclaration = method.Ancestors().OfType<NamespaceDeclarationSyntax>().FirstOrDefault();
            return namespaceDeclaration?.Name.ToString() ?? "";
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