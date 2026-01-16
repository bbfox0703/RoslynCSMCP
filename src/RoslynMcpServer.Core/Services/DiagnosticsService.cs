using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Core.Models;
using System.Text.RegularExpressions;

namespace RoslynMcpServer.Core.Services
{
    public class DiagnosticsService
    {
        private readonly ILogger<DiagnosticsService> _logger;
        private readonly SecurityValidator _securityValidator;

        public DiagnosticsService(ILogger<DiagnosticsService> logger, SecurityValidator securityValidator)
        {
            _logger = logger;
            _securityValidator = securityValidator;
        }

        /// <summary>
        /// Get compilation errors and warnings from the solution
        /// </summary>
        /// <param name="solutionPath">Path to .sln file</param>
        /// <param name="severity">Filter by severity: "Error", "Warning", "Info", or "All"</param>
        /// <param name="projectFilter">Optional project name filter (supports wildcards * and ?)</param>
        /// <param name="errorCodes">Optional array of specific error codes to include (e.g., ["CS0103", "CS0246"])</param>
        /// <returns>Compilation errors/warnings with failure tracking</returns>
        public async Task<CompilationErrorResults> GetCompilationErrorsAsync(
            string solutionPath,
            string severity = "All",
            string? projectFilter = null,
            string[]? errorCodes = null)
        {
            _logger.LogInformation("Getting compilation errors for solution: {SolutionPath}, Severity: {Severity}",
                solutionPath, severity);

            if (!_securityValidator.ValidateSolutionPath(solutionPath))
            {
                _logger.LogWarning("Invalid solution path: {SolutionPath}", solutionPath);
                throw new ArgumentException("Invalid solution path", nameof(solutionPath));
            }

            var errorResults = new CompilationErrorResults();
            var results = new List<CompilationError>();
            var analyzedProjects = 0;
            var failedProjects = 0;
            var failedDiagnostics = 0;
            var properties = new Dictionary<string, string>
            {
                ["CheckForSystemRuntimeDependency"] = "true"
            };

            using var workspace = MSBuildWorkspace.Create(properties);
            workspace.RegisterWorkspaceFailedHandler((args) =>
            {
                _logger.LogWarning("Workspace failed: {Diagnostic}", args.Diagnostic.Message);
            });

            _logger.LogInformation("Loading solution...");
            var solution = await workspace.OpenSolutionAsync(solutionPath);
            _logger.LogInformation("Solution loaded with {ProjectCount} projects", solution.Projects.Count());

            // Parse severity filter
            var includedSeverities = ParseSeverityFilter(severity);

            // Create project filter regex if specified
            Regex? projectRegex = null;
            if (!string.IsNullOrWhiteSpace(projectFilter))
            {
                projectRegex = CreateWildcardRegex(projectFilter);
            }

            // Process each project
            foreach (var project in solution.Projects)
            {
                // Skip if project doesn't match filter
                if (projectRegex != null && !projectRegex.IsMatch(project.Name))
                {
                    _logger.LogDebug("Skipping project {ProjectName} (doesn't match filter)", project.Name);
                    continue;
                }

                // Skip if project doesn't support compilation
                if (!project.SupportsCompilation)
                {
                    _logger.LogDebug("Skipping project {ProjectName} (no compilation support)", project.Name);
                    continue;
                }

                _logger.LogDebug("Analyzing project: {ProjectName}", project.Name);

                try
                {
                    var compilation = await project.GetCompilationAsync();
                    if (compilation == null)
                    {
                        _logger.LogWarning("Could not get compilation for project: {ProjectName}", project.Name);
                        failedProjects++;
                        errorResults.Warnings.Add(new OperationWarning
                        {
                            Context = $"Project: {project.Name}",
                            Message = "Could not get compilation for project",
                            Details = null
                        });
                        continue;
                    }

                    // Get all diagnostics
                    var diagnostics = compilation.GetDiagnostics();

                    // Filter by severity
                    var filteredDiagnostics = diagnostics.Where(d =>
                    {
                        if (!includedSeverities.Contains(d.Severity))
                            return false;

                        // Filter by error codes if specified
                        if (errorCodes != null && errorCodes.Length > 0)
                        {
                            return errorCodes.Contains(d.Id, StringComparer.OrdinalIgnoreCase);
                        }

                        return true;
                    });

                    // Convert diagnostics to CompilationError objects
                    foreach (var diagnostic in filteredDiagnostics)
                    {
                        var error = await ConvertDiagnosticToErrorAsync(diagnostic, project.Name);
                        if (error != null)
                        {
                            results.Add(error);
                        }
                        else
                        {
                            failedDiagnostics++;
                        }
                    }

                    _logger.LogDebug("Found {Count} diagnostics in {ProjectName}",
                        filteredDiagnostics.Count(), project.Name);
                    analyzedProjects++;
                }
                catch (Exception ex)
                {
                    failedProjects++;
                    _logger.LogError(ex, "Error analyzing project {ProjectName}", project.Name);
                    errorResults.Warnings.Add(new OperationWarning
                    {
                        Context = $"Project: {project.Name}",
                        Message = $"Failed to analyze project: {ex.Message}",
                        Details = null
                    });
                }
            }

            // Populate results
            errorResults.Errors = results;
            errorResults.AnalyzedProjects = analyzedProjects;
            errorResults.FailedProjects = failedProjects;
            errorResults.FailedDiagnostics = failedDiagnostics;

            _logger.LogInformation("Total compilation diagnostics found: {Count} ({AnalyzedProjects} projects analyzed, {FailedProjects} projects failed, {FailedDiagnostics} diagnostics failed)",
                results.Count, analyzedProjects, failedProjects, failedDiagnostics);

            if (failedProjects > 0 || failedDiagnostics > 0)
            {
                _logger.LogWarning("Some failures occurred: {FailedProjects} projects failed, {FailedDiagnostics} diagnostics failed to convert",
                    failedProjects, failedDiagnostics);
            }

            return errorResults;
        }

        /// <summary>
        /// Convert Roslyn Diagnostic to CompilationError model
        /// </summary>
        private async Task<CompilationError?> ConvertDiagnosticToErrorAsync(Diagnostic diagnostic, string projectName)
        {
            try
            {
                var location = diagnostic.Location;

                // Skip diagnostics without source location
                if (!location.IsInSource)
                {
                    return null;
                }

                var lineSpan = location.GetLineSpan();
                var filePath = lineSpan.Path;
                var lineNumber = lineSpan.StartLinePosition.Line + 1; // 1-based
                var columnNumber = lineSpan.StartLinePosition.Character + 1; // 1-based

                // Read the source line
                string lineText = string.Empty;
                try
                {
                    if (File.Exists(filePath))
                    {
                        var lines = await File.ReadAllLinesAsync(filePath);
                        if (lineNumber > 0 && lineNumber <= lines.Length)
                        {
                            lineText = lines[lineNumber - 1].Trim();
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not read source line from {FilePath}", filePath);
                }

                return new CompilationError
                {
                    Id = diagnostic.Id,
                    Severity = diagnostic.Severity.ToString(),
                    Message = diagnostic.GetMessage(),
                    FilePath = filePath,
                    FileName = Path.GetFileName(filePath),
                    ProjectName = projectName,
                    LineNumber = lineNumber,
                    ColumnNumber = columnNumber,
                    LineText = lineText
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error converting diagnostic to error object");
                return null;
            }
        }

        /// <summary>
        /// Parse severity filter string into DiagnosticSeverity set
        /// </summary>
        private HashSet<DiagnosticSeverity> ParseSeverityFilter(string severity)
        {
            var result = new HashSet<DiagnosticSeverity>();

            var severityUpper = severity.ToUpperInvariant();
            if (severityUpper == "ALL")
            {
                result.Add(DiagnosticSeverity.Error);
                result.Add(DiagnosticSeverity.Warning);
                result.Add(DiagnosticSeverity.Info);
                result.Add(DiagnosticSeverity.Hidden);
            }
            else if (severityUpper == "ERROR")
            {
                result.Add(DiagnosticSeverity.Error);
            }
            else if (severityUpper == "WARNING")
            {
                result.Add(DiagnosticSeverity.Warning);
            }
            else if (severityUpper == "INFO")
            {
                result.Add(DiagnosticSeverity.Info);
            }
            else
            {
                // Default to all if unrecognized
                _logger.LogWarning("Unrecognized severity filter '{Severity}', defaulting to 'All'", severity);
                result.Add(DiagnosticSeverity.Error);
                result.Add(DiagnosticSeverity.Warning);
                result.Add(DiagnosticSeverity.Info);
            }

            return result;
        }

        /// <summary>
        /// Create regex from wildcard pattern (* and ?)
        /// </summary>
        private Regex CreateWildcardRegex(string pattern)
        {
            var regexPattern = "^" + Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            return new Regex(regexPattern, RegexOptions.IgnoreCase);
        }
    }
}
