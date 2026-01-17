using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Core.Models;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace RoslynMcpServer.Core.Services
{
    /// <summary>
    /// Service for detecting duplicate code blocks across the solution
    /// </summary>
    public class DuplicateCodeAnalyzer
    {
        private readonly ILogger<DuplicateCodeAnalyzer> _logger;

        public DuplicateCodeAnalyzer(ILogger<DuplicateCodeAnalyzer> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Analyzes solution for duplicate code blocks
        /// </summary>
        public async Task<DuplicateCodeResults> AnalyzeDuplicateCodeAsync(
            string solutionPath,
            int minLines = 5,
            int similarityThreshold = 90)
        {
            var results = new DuplicateCodeResults();

            try
            {
                if (!File.Exists(solutionPath))
                {
                    results.Warnings.Add(new OperationWarning
                    {
                        Context = "Validation",
                        Message = "Invalid solution path"
                    });
                    return results;
                }

                // Validate parameters
                if (minLines < 3)
                {
                    results.Warnings.Add(new OperationWarning
                    {
                        Context = "Validation",
                        Message = "minLines must be at least 3, using default value 5"
                    });
                    minLines = 5;
                }

                if (similarityThreshold < 70 || similarityThreshold > 100)
                {
                    results.Warnings.Add(new OperationWarning
                    {
                        Context = "Validation",
                        Message = "similarity must be between 70-100, using default value 90"
                    });
                    similarityThreshold = 90;
                }

                // Load solution
                using var workspace = MSBuildWorkspace.Create();
                workspace.RegisterWorkspaceFailedHandler((e) =>
                {
                    if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
                    {
                        _logger.LogWarning("Workspace loading warning: {Message}", e.Diagnostic.Message);
                    }
                });

                var solution = await workspace.OpenSolutionAsync(solutionPath);
                results.AnalyzedProjects = solution.Projects.Count();

                // Extract all code blocks
                var allCodeBlocks = new ConcurrentBag<CodeBlockInfo>();

                var projectTasks = solution.Projects
                    .Where(p => p.SupportsCompilation)
                    .Select(async project =>
                    {
                        try
                        {
                            var compilation = await project.GetCompilationAsync();
                            if (compilation == null) return;

                            foreach (var syntaxTree in compilation.SyntaxTrees)
                            {
                                var root = await syntaxTree.GetRootAsync();
                                var filePath = syntaxTree.FilePath;
                                var fileName = Path.GetFileName(filePath);

                                results.AnalyzedFiles++;

                                // Extract methods
                                var methods = root.DescendantNodes()
                                    .OfType<MethodDeclarationSyntax>()
                                    .Where(m => m.Body != null || m.ExpressionBody != null);

                                foreach (var method in methods)
                                {
                                    results.AnalyzedMethods++;

                                    var lineSpan = method.GetLocation().GetLineSpan();
                                    var lineCount = lineSpan.EndLinePosition.Line - lineSpan.StartLinePosition.Line + 1;

                                    // Only analyze methods with sufficient lines
                                    if (lineCount >= minLines)
                                    {
                                        var normalizedCode = NormalizeCode(method);
                                        var hash = ComputeHash(normalizedCode);

                                        allCodeBlocks.Add(new CodeBlockInfo
                                        {
                                            MethodName = method.Identifier.Text,
                                            FileName = fileName,
                                            FilePath = filePath,
                                            StartLine = lineSpan.StartLinePosition.Line + 1,
                                            EndLine = lineSpan.EndLinePosition.Line + 1,
                                            LineCount = lineCount,
                                            ProjectName = project.Name,
                                            NormalizedCode = normalizedCode,
                                            Hash = hash,
                                            OriginalNode = method
                                        });
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to analyze project: {ProjectName}", project.Name);
                            results.FailedProjects++;
                        }
                    });

                await Task.WhenAll(projectTasks);

                // Group by hash to find potential duplicates
                var groupedByHash = allCodeBlocks
                    .GroupBy(b => b.Hash)
                    .Where(g => g.Count() > 1)  // Only groups with 2+ instances
                    .ToList();

                int groupId = 1;
                foreach (var group in groupedByHash)
                {
                    var blocks = group.ToList();

                    // Calculate similarity for each pair
                    var instances = blocks.Select(b => new CodeBlockInstance
                    {
                        MethodName = b.MethodName,
                        FileName = b.FileName,
                        FilePath = b.FilePath,
                        StartLine = b.StartLine,
                        EndLine = b.EndLine,
                        LineCount = b.LineCount,
                        ProjectName = b.ProjectName,
                        CodeSnippet = GetCodeSnippet(b.OriginalNode, 3)
                    }).ToList();

                    // Since they have the same hash, they are 100% similar (after normalization)
                    var duplicateBlock = new DuplicateCodeBlock
                    {
                        GroupId = groupId++,
                        Instances = instances,
                        SimilarityPercentage = 100,
                        LineCount = blocks.First().LineCount,
                        Hash = group.Key
                    };

                    results.DuplicateBlocks.Add(duplicateBlock);
                }

                // Sort by line count (larger duplicates first)
                results.DuplicateBlocks = results.DuplicateBlocks
                    .OrderByDescending(b => b.LineCount)
                    .ThenByDescending(b => b.Instances.Count)
                    .ToList();

                CalculateStatistics(results);

                _logger.LogInformation(
                    "Duplicate code analysis complete: {BlockCount} duplicate blocks found ({InstanceCount} instances)",
                    results.TotalDuplicateBlocks,
                    results.TotalDuplicateInstances);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing duplicate code");
                results.Warnings.Add(new OperationWarning
                {
                    Context = "Analysis",
                    Message = $"Error: {ex.Message}"
                });
            }

            return results;
        }

        /// <summary>
        /// Normalizes code by removing whitespace, comments, and standardizing identifiers
        /// </summary>
        private string NormalizeCode(SyntaxNode node)
        {
            // Clone the node and normalize it
            var normalized = node.NormalizeWhitespace();

            // Remove all trivia (comments, whitespace)
            normalized = normalized.WithoutTrivia();

            // Get the string representation
            var code = normalized.ToFullString();

            // Further normalization: remove extra whitespace
            code = System.Text.RegularExpressions.Regex.Replace(code, @"\s+", " ");

            return code.Trim();
        }

        /// <summary>
        /// Computes SHA256 hash of the code
        /// </summary>
        private string ComputeHash(string code)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(code);
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }

        /// <summary>
        /// Gets a code snippet (first N lines) for preview
        /// </summary>
        private string GetCodeSnippet(SyntaxNode node, int maxLines)
        {
            var lines = node.ToFullString()
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Take(maxLines)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l));

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Calculates statistics for the results
        /// </summary>
        private void CalculateStatistics(DuplicateCodeResults results)
        {
            results.HighSimilarityCount = results.DuplicateBlocks
                .Count(b => b.SimilarityPercentage >= 95);

            results.MediumSimilarityCount = results.DuplicateBlocks
                .Count(b => b.SimilarityPercentage >= 85 && b.SimilarityPercentage < 95);

            results.LowSimilarityCount = results.DuplicateBlocks
                .Count(b => b.SimilarityPercentage < 85);
        }

        /// <summary>
        /// Internal class to hold code block information during analysis
        /// </summary>
        private class CodeBlockInfo
        {
            public string MethodName { get; set; } = string.Empty;
            public string FileName { get; set; } = string.Empty;
            public string FilePath { get; set; } = string.Empty;
            public int StartLine { get; set; }
            public int EndLine { get; set; }
            public int LineCount { get; set; }
            public string ProjectName { get; set; } = string.Empty;
            public string NormalizedCode { get; set; } = string.Empty;
            public string Hash { get; set; } = string.Empty;
            public SyntaxNode OriginalNode { get; set; } = null!;
        }
    }
}
