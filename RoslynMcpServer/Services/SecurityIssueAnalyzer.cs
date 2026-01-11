using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Models;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace RoslynMcpServer.Services
{
    /// <summary>
    /// Service for analyzing security issues and anti-patterns in code
    /// </summary>
    public class SecurityIssueAnalyzer
    {
        private readonly ILogger<SecurityIssueAnalyzer> _logger;

        // Security pattern keywords
        private static readonly string[] SecretKeywords = { "password", "secret", "apikey", "api_key", "token", "connectionstring" };
        private static readonly string[] SqlKeywords = { "select", "insert", "update", "delete", "drop", "exec", "execute" };

        public SecurityIssueAnalyzer(ILogger<SecurityIssueAnalyzer> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Analyzes solution for security issues
        /// </summary>
        public async Task<SecurityIssueResults> AnalyzeSecurityIssuesAsync(
            string solutionPath,
            string[] categories,
            string severity = "all")
        {
            var results = new SecurityIssueResults();

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

                // Normalize categories
                var categoriesToCheck = categories == null || categories.Length == 0 || categories.Contains("all")
                    ? new[] { "sql-injection", "secrets", "crypto", "path-traversal", "deserialization" }
                    : categories.Select(c => c.ToLowerInvariant()).ToArray();

                var severityFilter = severity.ToLowerInvariant();

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

                // Process projects in parallel
                var securityIssues = new ConcurrentBag<SecurityIssue>();

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

                                // Check each category
                                if (categoriesToCheck.Contains("sql-injection"))
                                {
                                    var sqlIssues = AnalyzeSqlInjection(root, semanticModel, fileName, filePath, project.Name);
                                    foreach (var issue in sqlIssues.Where(i => MatchesSeverityFilter(i.Severity, severityFilter)))
                                        securityIssues.Add(issue);
                                }

                                if (categoriesToCheck.Contains("secrets"))
                                {
                                    var secretIssues = AnalyzeHardcodedSecrets(root, semanticModel, fileName, filePath, project.Name);
                                    foreach (var issue in secretIssues.Where(i => MatchesSeverityFilter(i.Severity, severityFilter)))
                                        securityIssues.Add(issue);
                                }

                                if (categoriesToCheck.Contains("crypto"))
                                {
                                    var cryptoIssues = AnalyzeWeakCryptography(root, semanticModel, fileName, filePath, project.Name);
                                    foreach (var issue in cryptoIssues.Where(i => MatchesSeverityFilter(i.Severity, severityFilter)))
                                        securityIssues.Add(issue);
                                }

                                if (categoriesToCheck.Contains("path-traversal"))
                                {
                                    var pathIssues = AnalyzePathTraversal(root, semanticModel, fileName, filePath, project.Name);
                                    foreach (var issue in pathIssues.Where(i => MatchesSeverityFilter(i.Severity, severityFilter)))
                                        securityIssues.Add(issue);
                                }

                                if (categoriesToCheck.Contains("deserialization"))
                                {
                                    var deserIssues = AnalyzeDeserialization(root, semanticModel, fileName, filePath, project.Name);
                                    foreach (var issue in deserIssues.Where(i => MatchesSeverityFilter(i.Severity, severityFilter)))
                                        securityIssues.Add(issue);
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

                results.Issues = securityIssues.ToList();
                CalculateStatistics(results);

                _logger.LogInformation(
                    "Security analysis complete: {IssueCount} issues found in {FileCount} files",
                    results.Issues.Count,
                    results.AnalyzedFiles);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing security issues");
                results.Warnings.Add(new OperationWarning
                {
                    Context = "Analysis",
                    Message = $"Error: {ex.Message}"
                });
            }

            return results;
        }

        /// <summary>
        /// Analyzes SQL injection vulnerabilities
        /// </summary>
        private List<SecurityIssue> AnalyzeSqlInjection(
            SyntaxNode root,
            SemanticModel semanticModel,
            string fileName,
            string filePath,
            string projectName)
        {
            var issues = new List<SecurityIssue>();

            // Find string concatenation and interpolation that might be SQL queries
            var stringOperations = root.DescendantNodes()
                .Where(n => n is BinaryExpressionSyntax || n is InterpolatedStringExpressionSyntax)
                .ToList();

            foreach (var node in stringOperations)
            {
                var text = node.ToString().ToLowerInvariant();

                // Check if it contains SQL keywords
                if (SqlKeywords.Any(keyword => text.Contains(keyword)))
                {
                    var lineSpan = node.GetLocation().GetLineSpan();
                    var methodName = GetContainingMethodName(node);

                    issues.Add(new SecurityIssue
                    {
                        Category = "sql-injection",
                        Severity = "Critical",
                        Title = "Potential SQL Injection",
                        Description = node is InterpolatedStringExpressionSyntax
                            ? "String interpolation used in SQL query - vulnerable to SQL injection"
                            : "String concatenation used in SQL query - vulnerable to SQL injection",
                        Recommendation = "Use parameterized queries or ORM (Entity Framework) instead",
                        MethodName = methodName,
                        FileName = fileName,
                        FilePath = filePath,
                        LineNumber = lineSpan.StartLinePosition.Line + 1,
                        CodeSnippet = node.ToString().Length > 100 ? node.ToString().Substring(0, 100) + "..." : node.ToString(),
                        ProjectName = projectName
                    });
                }
            }

            return issues;
        }

        /// <summary>
        /// Analyzes hardcoded secrets
        /// </summary>
        private List<SecurityIssue> AnalyzeHardcodedSecrets(
            SyntaxNode root,
            SemanticModel semanticModel,
            string fileName,
            string filePath,
            string projectName)
        {
            var issues = new List<SecurityIssue>();

            // Find string literals
            var stringLiterals = root.DescendantNodes()
                .OfType<LiteralExpressionSyntax>()
                .Where(l => l.IsKind(SyntaxKind.StringLiteralExpression))
                .ToList();

            foreach (var literal in stringLiterals)
            {
                var text = literal.Token.ValueText.ToLowerInvariant();
                var parent = literal.Parent;

                // Skip if it's in an attribute or const declaration
                if (parent?.AncestorsAndSelf().Any(n => n is AttributeSyntax) == true)
                    continue;

                // Check for variable/property names containing secret keywords
                string? variableName = null;
                if (parent is EqualsValueClauseSyntax equalsValue)
                {
                    var variableDeclarator = equalsValue.Parent as VariableDeclaratorSyntax;
                    variableName = variableDeclarator?.Identifier.Text;
                }
                else if (parent is AssignmentExpressionSyntax assignment)
                {
                    variableName = assignment.Left.ToString();
                }

                if (!string.IsNullOrEmpty(variableName))
                {
                    var varNameLower = variableName.ToLowerInvariant();
                    if (SecretKeywords.Any(keyword => varNameLower.Contains(keyword)))
                    {
                        // Check if the value looks suspicious (not empty, not placeholder)
                        if (!string.IsNullOrWhiteSpace(text) &&
                            !text.Contains("todo") &&
                            !text.Contains("placeholder") &&
                            !text.Contains("example") &&
                            text.Length > 5)
                        {
                            var lineSpan = literal.GetLocation().GetLineSpan();
                            var methodName = GetContainingMethodName(literal);

                            issues.Add(new SecurityIssue
                            {
                                Category = "secrets",
                                Severity = "Critical",
                                Title = "Hardcoded Secret Detected",
                                Description = $"Variable '{variableName}' contains a hardcoded value that might be a secret",
                                Recommendation = "Use environment variables, Azure Key Vault, or configuration files instead",
                                MethodName = methodName,
                                FileName = fileName,
                                FilePath = filePath,
                                LineNumber = lineSpan.StartLinePosition.Line + 1,
                                CodeSnippet = $"{variableName} = \"***\"",
                                ProjectName = projectName
                            });
                        }
                    }
                }

                // Check for connection strings
                if (text.Contains("data source") || text.Contains("server=") || text.Contains("uid=") || text.Contains("password="))
                {
                    var lineSpan = literal.GetLocation().GetLineSpan();
                    var methodName = GetContainingMethodName(literal);

                    issues.Add(new SecurityIssue
                    {
                        Category = "secrets",
                        Severity = "Critical",
                        Title = "Hardcoded Connection String",
                        Description = "Connection string is hardcoded in source code",
                        Recommendation = "Move connection strings to appsettings.json or environment variables",
                        MethodName = methodName,
                        FileName = fileName,
                        FilePath = filePath,
                        LineNumber = lineSpan.StartLinePosition.Line + 1,
                        CodeSnippet = "Connection string: \"***\"",
                        ProjectName = projectName
                    });
                }
            }

            return issues;
        }

        /// <summary>
        /// Analyzes weak cryptography usage
        /// </summary>
        private List<SecurityIssue> AnalyzeWeakCryptography(
            SyntaxNode root,
            SemanticModel semanticModel,
            string fileName,
            string filePath,
            string projectName)
        {
            var issues = new List<SecurityIssue>();

            // Find type usages
            var identifiers = root.DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .ToList();

            foreach (var identifier in identifiers)
            {
                var symbolInfo = semanticModel.GetSymbolInfo(identifier);
                var symbol = symbolInfo.Symbol;

                if (symbol == null) continue;

                var typeName = symbol.ContainingType?.Name ?? symbol.Name;

                // Check for weak crypto algorithms
                var weakAlgorithms = new Dictionary<string, string>
                {
                    { "MD5", "MD5 is cryptographically broken - use SHA256 or SHA512" },
                    { "SHA1", "SHA1 is deprecated - use SHA256 or SHA512" },
                    { "DES", "DES is insecure - use AES instead" },
                    { "TripleDES", "TripleDES is deprecated - use AES instead" },
                    { "RC2", "RC2 is insecure - use AES instead" }
                };

                foreach (var weakAlgo in weakAlgorithms)
                {
                    if (typeName.Contains(weakAlgo.Key))
                    {
                        var lineSpan = identifier.GetLocation().GetLineSpan();
                        var methodName = GetContainingMethodName(identifier);

                        issues.Add(new SecurityIssue
                        {
                            Category = "crypto",
                            Severity = "High",
                            Title = $"Weak Cryptography: {weakAlgo.Key}",
                            Description = weakAlgo.Value,
                            Recommendation = weakAlgo.Value.Split('-')[1].Trim(),
                            MethodName = methodName,
                            FileName = fileName,
                            FilePath = filePath,
                            LineNumber = lineSpan.StartLinePosition.Line + 1,
                            CodeSnippet = identifier.ToString(),
                            ProjectName = projectName
                        });

                        break;
                    }
                }
            }

            return issues;
        }

        /// <summary>
        /// Analyzes path traversal vulnerabilities
        /// </summary>
        private List<SecurityIssue> AnalyzePathTraversal(
            SyntaxNode root,
            SemanticModel semanticModel,
            string fileName,
            string filePath,
            string projectName)
        {
            var issues = new List<SecurityIssue>();

            // Find Path.Combine, File operations with user input
            var invocations = root.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .ToList();

            foreach (var invocation in invocations)
            {
                var symbolInfo = semanticModel.GetSymbolInfo(invocation);
                var methodSymbol = symbolInfo.Symbol as IMethodSymbol;

                if (methodSymbol == null) continue;

                var className = methodSymbol.ContainingType?.Name;
                var methodName = methodSymbol.Name;

                // Check Path.Combine, File.ReadAllText, etc.
                if ((className == "Path" && methodName == "Combine") ||
                    (className == "File" && (methodName.StartsWith("Read") || methodName.StartsWith("Write"))) ||
                    (className == "Directory" && (methodName == "Delete" || methodName == "Move")))
                {
                    // Simple heuristic: if arguments are not string literals, might be vulnerable
                    var hasNonLiteralArg = invocation.ArgumentList.Arguments
                        .Any(arg => arg.Expression is not LiteralExpressionSyntax);

                    if (hasNonLiteralArg)
                    {
                        var lineSpan = invocation.GetLocation().GetLineSpan();
                        var containingMethod = GetContainingMethodName(invocation);

                        issues.Add(new SecurityIssue
                        {
                            Category = "path-traversal",
                            Severity = "High",
                            Title = "Potential Path Traversal",
                            Description = $"File operation '{className}.{methodName}' with non-literal path - validate input to prevent directory traversal",
                            Recommendation = "Validate and sanitize file paths, use Path.GetFullPath and check against allowed directories",
                            MethodName = containingMethod,
                            FileName = fileName,
                            FilePath = filePath,
                            LineNumber = lineSpan.StartLinePosition.Line + 1,
                            CodeSnippet = invocation.ToString().Length > 100 ? invocation.ToString().Substring(0, 100) + "..." : invocation.ToString(),
                            ProjectName = projectName
                        });
                    }
                }
            }

            return issues;
        }

        /// <summary>
        /// Analyzes insecure deserialization
        /// </summary>
        private List<SecurityIssue> AnalyzeDeserialization(
            SyntaxNode root,
            SemanticModel semanticModel,
            string fileName,
            string filePath,
            string projectName)
        {
            var issues = new List<SecurityIssue>();

            // Find dangerous deserializers
            var identifiers = root.DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .ToList();

            foreach (var identifier in identifiers)
            {
                var symbolInfo = semanticModel.GetSymbolInfo(identifier);
                var symbol = symbolInfo.Symbol;

                if (symbol == null) continue;

                var typeName = symbol.ContainingType?.Name ?? symbol.Name;

                // Check for insecure deserializers
                var insecureDeserializers = new Dictionary<string, string>
                {
                    { "BinaryFormatter", "BinaryFormatter is insecure and deprecated - use System.Text.Json or protobuf" },
                    { "JavaScriptSerializer", "JavaScriptSerializer is deprecated - use System.Text.Json" },
                    { "NetDataContractSerializer", "NetDataContractSerializer is insecure - use DataContractSerializer or System.Text.Json" }
                };

                foreach (var deser in insecureDeserializers)
                {
                    if (typeName.Contains(deser.Key))
                    {
                        var lineSpan = identifier.GetLocation().GetLineSpan();
                        var methodName = GetContainingMethodName(identifier);

                        issues.Add(new SecurityIssue
                        {
                            Category = "deserialization",
                            Severity = "Critical",
                            Title = $"Insecure Deserialization: {deser.Key}",
                            Description = deser.Value,
                            Recommendation = deser.Value.Split('-')[1].Trim(),
                            MethodName = methodName,
                            FileName = fileName,
                            FilePath = filePath,
                            LineNumber = lineSpan.StartLinePosition.Line + 1,
                            CodeSnippet = identifier.ToString(),
                            ProjectName = projectName
                        });

                        break;
                    }
                }
            }

            return issues;
        }

        /// <summary>
        /// Gets the containing method name for a syntax node
        /// </summary>
        private string GetContainingMethodName(SyntaxNode node)
        {
            var method = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            if (method != null)
                return method.Identifier.Text;

            var property = node.Ancestors().OfType<PropertyDeclarationSyntax>().FirstOrDefault();
            if (property != null)
                return property.Identifier.Text;

            return "(global)";
        }

        /// <summary>
        /// Checks if severity matches the filter
        /// </summary>
        private bool MatchesSeverityFilter(string severity, string filter)
        {
            if (filter == "all") return true;

            return severity.ToLowerInvariant() == filter;
        }

        /// <summary>
        /// Calculates statistics for the results
        /// </summary>
        private void CalculateStatistics(SecurityIssueResults results)
        {
            // By severity
            results.CriticalCount = results.Issues.Count(i => i.Severity == "Critical");
            results.HighCount = results.Issues.Count(i => i.Severity == "High");
            results.MediumCount = results.Issues.Count(i => i.Severity == "Medium");
            results.LowCount = results.Issues.Count(i => i.Severity == "Low");

            // By category
            results.SqlInjectionCount = results.Issues.Count(i => i.Category == "sql-injection");
            results.HardcodedSecretsCount = results.Issues.Count(i => i.Category == "secrets");
            results.WeakCryptoCount = results.Issues.Count(i => i.Category == "crypto");
            results.PathTraversalCount = results.Issues.Count(i => i.Category == "path-traversal");
            results.DeserializationCount = results.Issues.Count(i => i.Category == "deserialization");
            results.OtherCount = results.Issues.Count -
                (results.SqlInjectionCount + results.HardcodedSecretsCount + results.WeakCryptoCount +
                 results.PathTraversalCount + results.DeserializationCount);
        }
    }
}
