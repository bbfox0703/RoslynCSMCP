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

        internal static readonly string[] DefaultCommentTypes = { "TODO", "FIXME", "HACK", "NOTE", "BUG", "XXX", "OPTIMIZE", "REFACTOR" };

        // A marker is a whole word: "debug" is not BUG and "notes" is not NOTE. An XML tag such as
        // <note> is not a marker either. Group 1 = marker, 2 = author in TODO(name); the message is the
        // rest of the line. The pattern does not consume the message, so later markers on the same
        // line are still visited.
        private static readonly Regex CommentPattern = new Regex(
            @"(?<!\w|</?)(TODO|FIXME|HACK|NOTE|BUG|XXX|OPTIMIZE|REFACTOR)\b(?:\s*\(([^)]*)\))?\s*:?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Comment delimiters and decoration that may precede a marker at the start of a comment line.
        private static readonly Regex LineLeadPattern = new Regex(
            @"^\s*(?:///?|/\*+|\*+)?\s*$",
            RegexOptions.Compiled);

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
                var targetTypes = commentTypes ?? DefaultCommentTypes;
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
                var analyzedFiles = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
                int failedProjects = 0;

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
                                var filePath = syntaxTree.FilePath;

                                // A file compiled into several projects (multi-targeting, linked files) is scanned once.
                                if (!analyzedFiles.TryAdd(filePath, 0))
                                    continue;

                                var root = await syntaxTree.GetRootAsync();
                                var fileName = Path.GetFileName(filePath);

                                // Get all trivia (comments, whitespace, etc.)
                                var allTrivia = root.DescendantTrivia();

                                foreach (var trivia in allTrivia)
                                {
                                    if (!trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) &&
                                        !trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) &&
                                        !trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) &&
                                        !trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
                                    {
                                        continue;
                                    }

                                    var firstLine = trivia.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

                                    foreach (var marker in FindMarkers(trivia.ToFullString(), targetTypesSet))
                                    {
                                        var lineNumber = firstLine + marker.LineOffset;

                                        allComments.Add(new TODOComment
                                        {
                                            Type = marker.Type,
                                            Message = marker.Message,
                                            FileName = fileName,
                                            FilePath = filePath,
                                            LineNumber = lineNumber,
                                            ProjectName = project.Name,
                                            Author = marker.Author,
                                            CodeContext = GetCodeContext(syntaxTree, lineNumber, 3)
                                        });
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to analyze project: {ProjectName}", project.Name);
                            Interlocked.Increment(ref failedProjects);
                        }
                    });

                await Task.WhenAll(projectTasks);

                results.AnalyzedFiles = analyzedFiles.Count;
                results.FailedProjects = failedProjects;

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

        internal readonly record struct CommentMarker(string Type, string Author, string Message, int LineOffset);

        /// <summary>
        /// Finds the markers in one comment, at most one per line: the first whole-word marker on the
        /// line whose type is in targetTypes. A marker is recognized in any case when it is the first
        /// word of the comment line ("// todo: ..."), and elsewhere on the line only in upper case,
        /// so prose such as "works around a bug" is not a BUG marker.
        /// </summary>
        internal static IEnumerable<CommentMarker> FindMarkers(string commentText, ISet<string> targetTypes)
        {
            var lines = commentText.Split('\n');
            for (int lineOffset = 0; lineOffset < lines.Length; lineOffset++)
            {
                var line = lines[lineOffset].TrimEnd('\r');

                foreach (Match match in CommentPattern.Matches(line))
                {
                    var marker = match.Groups[1];
                    var type = marker.Value.ToUpperInvariant();

                    // Filter by type before choosing the line's marker.
                    if (!targetTypes.Contains(type))
                        continue;

                    var isUpperCase = marker.Value == type;
                    var startsLine = LineLeadPattern.IsMatch(line.Substring(0, marker.Index));
                    if (!isUpperCase && !startsLine)
                        continue;

                    var author = match.Groups[2].Success ? match.Groups[2].Value.Trim() : string.Empty;
                    var message = line.Substring(match.Index + match.Length).Trim();
                    if (message.EndsWith("*/", StringComparison.Ordinal))
                        message = message.Substring(0, message.Length - 2).TrimEnd();

                    yield return new CommentMarker(type, author, message, lineOffset);
                    break;
                }
            }
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
