using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Core.Models;
using System.Collections.Concurrent;

namespace RoslynMcpServer.Core.Services
{
    /// <summary>
    /// Service for finding large source files that may need refactoring
    /// </summary>
    public class LargeFileAnalyzer
    {
        private readonly ILogger<LargeFileAnalyzer> _logger;

        public LargeFileAnalyzer(ILogger<LargeFileAnalyzer> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Analyzes solution for large files
        /// </summary>
        public async Task<LargeFileResults> AnalyzeLargeFilesAsync(
            string solutionPath,
            int threshold = 500)
        {
            var results = new LargeFileResults();

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

                // Validate threshold
                if (threshold < 100)
                {
                    results.Warnings.Add(new OperationWarning
                    {
                        Context = "Validation",
                        Message = "Threshold must be at least 100 lines, using default value 500"
                    });
                    threshold = 500;
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

                // Collect all large files
                var allLargeFiles = new ConcurrentBag<LargeFile>();

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
                                var fileName = Path.GetFileName(filePath);

                                results.AnalyzedFiles++;

                                // Skip generated files
                                if (fileName.EndsWith(".g.cs") ||
                                    fileName.EndsWith(".designer.cs") ||
                                    fileName.EndsWith(".Generated.cs") ||
                                    filePath.Contains("\\obj\\") ||
                                    filePath.Contains("\\bin\\"))
                                {
                                    continue;
                                }

                                // Get file info
                                FileInfo fileInfo;
                                try
                                {
                                    fileInfo = new FileInfo(filePath);
                                    if (!fileInfo.Exists)
                                        continue;
                                }
                                catch
                                {
                                    continue;
                                }

                                // Count lines
                                var text = await syntaxTree.GetTextAsync();
                                var lineCount = text.Lines.Count;

                                // Only include files above threshold
                                if (lineCount >= threshold)
                                {
                                    var root = await syntaxTree.GetRootAsync();

                                    // Count types (classes, interfaces, structs, enums)
                                    var types = root.DescendantNodes()
                                        .Where(n => n is TypeDeclarationSyntax || n is EnumDeclarationSyntax)
                                        .Count();

                                    // Count methods
                                    var methods = root.DescendantNodes()
                                        .OfType<MethodDeclarationSyntax>()
                                        .Count();

                                    var largeFile = new LargeFile
                                    {
                                        FileName = fileName,
                                        FilePath = filePath,
                                        LineCount = lineCount,
                                        SizeInBytes = fileInfo.Length,
                                        ProjectName = project.Name,
                                        TypeCount = types,
                                        MethodCount = methods
                                    };

                                    allLargeFiles.Add(largeFile);
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

                // Sort by line count (largest first)
                results.LargeFiles = allLargeFiles
                    .OrderByDescending(f => f.LineCount)
                    .ToList();

                _logger.LogInformation(
                    "Large file analysis complete: {Count} files found above {Threshold} lines",
                    results.TotalLargeFiles,
                    threshold);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing large files");
                results.Warnings.Add(new OperationWarning
                {
                    Context = "Analysis",
                    Message = $"Error: {ex.Message}"
                });
            }

            return results;
        }
    }
}
