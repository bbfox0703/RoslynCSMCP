using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Core.Models;
using System.Collections.Concurrent;

namespace RoslynMcpServer.Core.Services
{
    /// <summary>
    /// Service for finding usages of deprecated/obsolete APIs
    /// </summary>
    public class DeprecatedAPIAnalyzer
    {
        private readonly ILogger<DeprecatedAPIAnalyzer> _logger;

        // Known .NET Framework obsolete APIs with migration suggestions
        private static readonly Dictionary<string, string> KnownDeprecatedAPIs = new(StringComparer.OrdinalIgnoreCase)
        {
            { "System.Runtime.Serialization.Formatters.Binary.BinaryFormatter", "Use System.Text.Json or other secure serializers" },
            { "System.Net.WebRequest", "Use HttpClient instead" },
            { "System.Net.HttpWebRequest", "Use HttpClient instead" },
            { "System.Net.ServicePointManager", "Use SocketsHttpHandler or HttpClient" },
            { "System.Security.Cryptography.MD5", "Use SHA256 or stronger algorithms" },
            { "System.Security.Cryptography.SHA1", "Use SHA256 or stronger algorithms" },
            { "System.Web.HttpUtility.HtmlEncode", "Use System.Net.WebUtility.HtmlEncode or Microsoft.AspNetCore.Html" },
            { "System.AppDomain.SetData", "Use modern configuration patterns" },
            { "System.Threading.Thread.Suspend", "Use cancellation tokens and async patterns" },
            { "System.Threading.Thread.Resume", "Use cancellation tokens and async patterns" }
        };

        public DeprecatedAPIAnalyzer(ILogger<DeprecatedAPIAnalyzer> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Analyzes solution for deprecated API usages
        /// </summary>
        public async Task<DeprecatedAPIResults> AnalyzeDeprecatedAPIsAsync(
            string solutionPath,
            bool includeFrameworkAPIs = true)
        {
            var results = new DeprecatedAPIResults();

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

                // Collect all deprecated API usages
                var allUsages = new ConcurrentBag<DeprecatedAPIUsage>();

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
                                var semanticModel = compilation.GetSemanticModel(syntaxTree);
                                var filePath = syntaxTree.FilePath;
                                var fileName = Path.GetFileName(filePath);

                                results.AnalyzedFiles++;

                                // Find all identifier usages
                                var identifiers = root.DescendantNodes()
                                    .OfType<IdentifierNameSyntax>();

                                foreach (var identifier in identifiers)
                                {
                                    var symbolInfo = semanticModel.GetSymbolInfo(identifier);
                                    var symbol = symbolInfo.Symbol;

                                    if (symbol == null)
                                        continue;

                                    // Check if symbol has ObsoleteAttribute
                                    var obsoleteAttr = symbol.GetAttributes()
                                        .FirstOrDefault(a => a.AttributeClass?.Name == "ObsoleteAttribute" ||
                                                           a.AttributeClass?.Name == "Obsolete");

                                    if (obsoleteAttr != null)
                                    {
                                        var lineSpan = identifier.GetLocation().GetLineSpan();
                                        var lineNumber = lineSpan.StartLinePosition.Line + 1;

                                        // Get obsolete message
                                        var message = string.Empty;
                                        var isError = false;

                                        if (obsoleteAttr.ConstructorArguments.Length > 0)
                                        {
                                            message = obsoleteAttr.ConstructorArguments[0].Value?.ToString() ?? string.Empty;
                                        }

                                        if (obsoleteAttr.ConstructorArguments.Length > 1)
                                        {
                                            if (obsoleteAttr.ConstructorArguments[1].Value is bool errorValue)
                                            {
                                                isError = errorValue;
                                            }
                                        }

                                        // Get code context
                                        var codeContext = GetCodeContext(syntaxTree, lineNumber, 2);

                                        var usage = new DeprecatedAPIUsage
                                        {
                                            APIName = symbol.Name,
                                            FullName = symbol.ToDisplayString(),
                                            FileName = fileName,
                                            FilePath = filePath,
                                            LineNumber = lineNumber,
                                            ProjectName = project.Name,
                                            ObsoleteMessage = message,
                                            IsError = isError,
                                            CodeContext = codeContext
                                        };

                                        allUsages.Add(usage);
                                    }
                                    else if (includeFrameworkAPIs)
                                    {
                                        // Check if it's a known deprecated framework API
                                        var fullName = symbol.ContainingType?.ToDisplayString() ?? symbol.ToDisplayString();

                                        if (KnownDeprecatedAPIs.ContainsKey(fullName))
                                        {
                                            var lineSpan = identifier.GetLocation().GetLineSpan();
                                            var lineNumber = lineSpan.StartLinePosition.Line + 1;
                                            var codeContext = GetCodeContext(syntaxTree, lineNumber, 2);

                                            var usage = new DeprecatedAPIUsage
                                            {
                                                APIName = symbol.Name,
                                                FullName = fullName,
                                                FileName = fileName,
                                                FilePath = filePath,
                                                LineNumber = lineNumber,
                                                ProjectName = project.Name,
                                                ObsoleteMessage = "Deprecated in .NET Framework",
                                                IsError = false,
                                                CodeContext = codeContext
                                            };

                                            allUsages.Add(usage);
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

                // Group by API
                var groupedByAPI = allUsages
                    .GroupBy(u => u.FullName)
                    .Select(g =>
                    {
                        var first = g.First();
                        return new DeprecatedAPI
                        {
                            APIName = first.APIName,
                            FullName = first.FullName,
                            ObsoleteMessage = first.ObsoleteMessage,
                            IsError = first.IsError,
                            Usages = g.OrderBy(u => u.ProjectName)
                                     .ThenBy(u => u.FileName)
                                     .ThenBy(u => u.LineNumber)
                                     .ToList(),
                            Suggestion = GetMigrationSuggestion(first.FullName, first.ObsoleteMessage)
                        };
                    })
                    .OrderByDescending(api => api.IsError)
                    .ThenByDescending(api => api.Usages.Count)
                    .ToList();

                results.DeprecatedAPIs = groupedByAPI;

                _logger.LogInformation(
                    "Deprecated API analysis complete: {APICount} deprecated APIs found with {UsageCount} usages",
                    results.TotalDeprecatedAPIs,
                    results.TotalUsages);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing deprecated APIs");
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
                    var marker = i == lineNumber - 1 ? " >" : "  ";
                    contextBuilder.AppendLine($"{marker}{i + 1,4}: {lineText}");
                }

                return contextBuilder.ToString().TrimEnd();
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Gets migration suggestion for a deprecated API
        /// </summary>
        private string GetMigrationSuggestion(string fullName, string obsoleteMessage)
        {
            // Check known framework APIs
            if (KnownDeprecatedAPIs.TryGetValue(fullName, out var suggestion))
            {
                return suggestion;
            }

            // Parse obsolete message for suggestions
            if (!string.IsNullOrWhiteSpace(obsoleteMessage))
            {
                if (obsoleteMessage.Contains("use", StringComparison.OrdinalIgnoreCase) ||
                    obsoleteMessage.Contains("instead", StringComparison.OrdinalIgnoreCase))
                {
                    return obsoleteMessage;
                }
            }

            return "Review documentation for migration guidance";
        }
    }
}
