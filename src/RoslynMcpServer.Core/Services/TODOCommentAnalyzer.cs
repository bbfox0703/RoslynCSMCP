using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Core.Models;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace RoslynMcpServer.Core.Services
{
    /// <summary>
    /// Service for finding TODO, FIXME, HACK, and other special comments in code
    /// </summary>
    public class TODOCommentAnalyzer
    {
        private readonly ILogger<TODOCommentAnalyzer> _logger;

        // Pattern to match TODO/FIXME/HACK/NOTE/BUG comments
        private static readonly Regex CommentPattern = new Regex(
            @"(TODO|FIXME|HACK|NOTE|BUG|XXX|OPTIMIZE|REFACTOR)\s*(?:\(([^)]+)\))?\s*:?\s*(.+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public TODOCommentAnalyzer(ILogger<TODOCommentAnalyzer> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Analyzes solution for TODO/FIXME/HACK comments
        /// </summary>
        public async Task<TODOCommentResults> AnalyzeTODOCommentsAsync(
            string solutionPath,
            string[] commentTypes = null!)
        {
            var results = new TODOCommentResults();

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

                // Default comment types if not specified
                var targetTypes = commentTypes ?? new[] { "TODO", "FIXME", "HACK", "NOTE", "BUG", "XXX", "OPTIMIZE", "REFACTOR" };
                var targetTypesSet = new HashSet<string>(targetTypes, StringComparer.OrdinalIgnoreCase);

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

                // Collect all TODO comments
                var allComments = new ConcurrentBag<TODOComment>();

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

                                // Get all trivia (comments, whitespace, etc.)
                                var allTrivia = root.DescendantTrivia();

                                foreach (var trivia in allTrivia)
                                {
                                    if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) ||
                                        trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) ||
                                        trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                                        trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
                                    {
                                        var commentText = trivia.ToString();
                                        var match = CommentPattern.Match(commentText);

                                        if (match.Success)
                                        {
                                            var commentType = match.Groups[1].Value.ToUpperInvariant();

                                            // Filter by target types
                                            if (!targetTypesSet.Contains(commentType))
                                                continue;

                                            var author = match.Groups[2].Success ? match.Groups[2].Value.Trim() : string.Empty;
                                            var message = match.Groups[3].Value.Trim();

                                            // Get line number
                                            var lineSpan = trivia.GetLocation().GetLineSpan();
                                            var lineNumber = lineSpan.StartLinePosition.Line + 1;

                                            // Get code context (3 lines before and after)
                                            var codeContext = GetCodeContext(syntaxTree, lineNumber, 3);

                                            var todoComment = new TODOComment
                                            {
                                                Type = commentType,
                                                Message = message,
                                                FileName = fileName,
                                                FilePath = filePath,
                                                LineNumber = lineNumber,
                                                ProjectName = project.Name,
                                                Author = author,
                                                CodeContext = codeContext
                                            };

                                            allComments.Add(todoComment);
                                        }
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

                results.Comments = allComments
                    .OrderBy(c => c.Type)
                    .ThenBy(c => c.ProjectName)
                    .ThenBy(c => c.FileName)
                    .ThenBy(c => c.LineNumber)
                    .ToList();

                CalculateStatistics(results);

                _logger.LogInformation(
                    "TODO comment analysis complete: {Count} comments found in {Files} files",
                    results.TotalComments,
                    results.AnalyzedFiles);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing TODO comments");
                results.Warnings.Add(new OperationWarning
                {
                    Context = "Analysis",
                    Message = $"Error: {ex.Message}"
                });
            }

            return results;
        }

        /// <summary>
        /// Gets code context around a line number
        /// </summary>
        private string GetCodeContext(SyntaxTree syntaxTree, int lineNumber, int contextLines)
        {
            try
            {
                var text = syntaxTree.GetText();
                var lines = text.Lines;

                var startLine = Math.Max(0, lineNumber - contextLines - 1);
                var endLine = Math.Min(lines.Count - 1, lineNumber + contextLines - 1);

                var contextBuilder = new System.Text.StringBuilder();
                for (int i = startLine; i <= endLine; i++)
                {
                    var line = lines[i];
                    var lineText = line.ToString();
                    contextBuilder.AppendLine($"{i + 1,4}: {lineText}");
                }

                return contextBuilder.ToString().TrimEnd();
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Calculates statistics for the results
        /// </summary>
        private void CalculateStatistics(TODOCommentResults results)
        {
            results.TODOCount = results.Comments.Count(c => c.Type == "TODO");
            results.FIXMECount = results.Comments.Count(c => c.Type == "FIXME");
            results.HACKCount = results.Comments.Count(c => c.Type == "HACK");
            results.NOTECount = results.Comments.Count(c => c.Type == "NOTE");
            results.BUGCount = results.Comments.Count(c => c.Type == "BUG");
            results.OtherCount = results.Comments.Count(c =>
                c.Type != "TODO" &&
                c.Type != "FIXME" &&
                c.Type != "HACK" &&
                c.Type != "NOTE" &&
                c.Type != "BUG");
        }
    }
}
