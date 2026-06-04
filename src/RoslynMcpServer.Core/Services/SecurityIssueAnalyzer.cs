using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Core.Models;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace RoslynMcpServer.Core.Services
{
    /// <summary>
    /// Service for analyzing security issues and anti-patterns in code
    /// </summary>
    public class SecurityIssueAnalyzer
    {
        private readonly ILogger<SecurityIssueAnalyzer> _logger;

        // Security pattern keywords
        private static readonly string[] SecretKeywords = { "password", "secret", "apikey", "api_key", "token", "connectionstring" };

        // A string is treated as SQL only when it opens with a SQL command AND carries a SQL
        // clause/structure — this rejects prose like "... updated" or "deleted item".
        private static readonly Regex SqlCommandRegex = new(
            @"\b(SELECT|INSERT|UPDATE|DELETE|MERGE|CREATE|ALTER|DROP|TRUNCATE|EXEC|EXECUTE)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SqlClauseRegex = new(
            @"\b(FROM|INTO|SET|WHERE|VALUES|TABLE|JOIN|GROUP\s+BY|ORDER\s+BY)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Weak algorithms / insecure deserializers, matched by fully-qualified type (or a base
        // type in the chain) rather than a substring, so "DES" can't match "ResultDescriptor".
        private static readonly Dictionary<string, string> WeakCryptoTypes = new()
        {
            ["System.Security.Cryptography.MD5"] = "MD5 is cryptographically broken — use SHA256 or SHA512",
            ["System.Security.Cryptography.SHA1"] = "SHA1 is deprecated — use SHA256 or SHA512",
            ["System.Security.Cryptography.DES"] = "DES is insecure — use AES instead",
            ["System.Security.Cryptography.TripleDES"] = "TripleDES is deprecated — use AES instead",
            ["System.Security.Cryptography.RC2"] = "RC2 is insecure — use AES instead",
        };
        private static readonly Dictionary<string, string> InsecureDeserializerTypes = new()
        {
            ["System.Runtime.Serialization.Formatters.Binary.BinaryFormatter"] = "BinaryFormatter is insecure and deprecated — use System.Text.Json or protobuf",
            ["System.Web.Script.Serialization.JavaScriptSerializer"] = "JavaScriptSerializer is deprecated — use System.Text.Json",
            ["System.Runtime.Serialization.NetDataContractSerializer"] = "NetDataContractSerializer is insecure — use DataContractSerializer or System.Text.Json",
        };

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
        internal static List<SecurityIssue> AnalyzeSqlInjection(
            SyntaxNode root,
            SemanticModel semanticModel,
            string fileName,
            string filePath,
            string projectName)
        {
            var issues = new List<SecurityIssue>();

            // Consider only top-level string-building expressions: an interpolated string, or
            // the outermost node of a string '+' concatenation chain (inner '+' nodes are
            // folded into their parent's template, so we skip them here).
            foreach (var node in root.DescendantNodes())
            {
                bool isInterpolated = node is InterpolatedStringExpressionSyntax interpNode &&
                                      interpNode.Parent is not InterpolationSyntax;
                bool isConcat = node is BinaryExpressionSyntax be && be.IsKind(SyntaxKind.AddExpression) &&
                                !(be.Parent is BinaryExpressionSyntax pe && pe.IsKind(SyntaxKind.AddExpression));
                if (!isInterpolated && !isConcat)
                    continue;

                // The whole expression must be a string.
                var exprType = semanticModel.GetTypeInfo(node).Type ?? semanticModel.GetTypeInfo(node).ConvertedType;
                if (exprType?.SpecialType != SpecialType.System_String)
                    continue;

                var (template, hasDynamicPart) = BuildSqlTemplate(node, semanticModel);

                // Injection requires a runtime (non-constant) value spliced into SQL text.
                if (!hasDynamicPart) continue;
                if (!LooksLikeSql(template)) continue;

                var lineSpan = node.GetLocation().GetLineSpan();
                var snippet = node.ToString();

                issues.Add(new SecurityIssue
                {
                    Category = "sql-injection",
                    Severity = "Critical",
                    Title = "Potential SQL Injection",
                    Description = isInterpolated
                        ? "String interpolation splices a non-constant value into a SQL statement - vulnerable to SQL injection"
                        : "String concatenation splices a non-constant value into a SQL statement - vulnerable to SQL injection",
                    Recommendation = "Use parameterized queries or an ORM (Entity Framework) instead of building SQL from runtime values",
                    MethodName = GetContainingMethodName(node),
                    FileName = fileName,
                    FilePath = filePath,
                    LineNumber = lineSpan.StartLinePosition.Line + 1,
                    CodeSnippet = snippet.Length > 100 ? snippet.Substring(0, 100) + "..." : snippet,
                    ProjectName = projectName
                });
            }

            return issues;
        }

        /// <summary>
        /// Reconstructs the static "shape" of a string-building expression: constant parts are
        /// kept verbatim, runtime parts become a "?" placeholder. Returns the template and
        /// whether any runtime (non-constant) part was found.
        /// </summary>
        private static (string Template, bool HasDynamicPart) BuildSqlTemplate(SyntaxNode node, SemanticModel model)
        {
            var sb = new System.Text.StringBuilder();
            bool dynamic = false;

            void AppendExpr(ExpressionSyntax expr)
            {
                // Recurse through 'a + b + c' so the whole concatenation is one template.
                if (expr is BinaryExpressionSyntax b && b.IsKind(SyntaxKind.AddExpression))
                {
                    AppendExpr(b.Left);
                    AppendExpr(b.Right);
                    return;
                }

                if (expr is InterpolatedStringExpressionSyntax interp)
                {
                    foreach (var content in interp.Contents)
                    {
                        if (content is InterpolatedStringTextSyntax t)
                            sb.Append(t.TextToken.ValueText);
                        else if (content is InterpolationSyntax hole)
                            AppendValue(hole.Expression);
                    }
                    return;
                }

                AppendValue(expr);
            }

            void AppendValue(ExpressionSyntax expr)
            {
                var constant = model.GetConstantValue(expr);
                if (constant.HasValue && constant.Value is string s)
                {
                    sb.Append(s);
                }
                else if (constant.HasValue)
                {
                    sb.Append(constant.Value); // constant non-string (e.g. number) — still static
                }
                else
                {
                    dynamic = true;
                    sb.Append(" ? ");
                }
            }

            AppendExpr((ExpressionSyntax)node);
            return (sb.ToString(), dynamic);
        }

        /// <summary>True when a string's static shape opens with a SQL command and has a SQL clause.</summary>
        private static bool LooksLikeSql(string template) =>
            SqlCommandRegex.IsMatch(template) && SqlClauseRegex.IsMatch(template);

        /// <summary>
        /// Analyzes hardcoded secrets
        /// </summary>
        internal static List<SecurityIssue> AnalyzeHardcodedSecrets(
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
        internal static List<SecurityIssue> AnalyzeWeakCryptography(
            SyntaxNode root,
            SemanticModel semanticModel,
            string fileName,
            string filePath,
            string projectName) =>
            AnalyzeTypeUsages(
                root, semanticModel, fileName, filePath, projectName,
                WeakCryptoTypes, category: "crypto", severity: "High",
                titlePrefix: "Weak Cryptography");

        /// <summary>
        /// Flags usages of a known set of types (matched by fully-qualified name, including
        /// base types so concrete providers like MD5CryptoServiceProvider are caught), de-duplicated
        /// per source line so a single usage is reported once rather than once per identifier token.
        /// </summary>
        private static List<SecurityIssue> AnalyzeTypeUsages(
            SyntaxNode root,
            SemanticModel semanticModel,
            string fileName,
            string filePath,
            string projectName,
            IReadOnlyDictionary<string, string> knownTypes,
            string category,
            string severity,
            string titlePrefix)
        {
            var issues = new List<SecurityIssue>();
            var seen = new HashSet<(int Line, string Type)>();

            foreach (var node in root.DescendantNodes())
            {
                // Only look at type-bearing usages: object creation and name references.
                ITypeSymbol? type = node switch
                {
                    ObjectCreationExpressionSyntax oc => semanticModel.GetTypeInfo(oc).Type,
                    IdentifierNameSyntax id => semanticModel.GetSymbolInfo(id).Symbol as ITypeSymbol,
                    _ => null
                };
                if (type is null) continue;

                var (matchedKey, message) = MatchKnownType(type, knownTypes);
                if (matchedKey is null) continue;

                var lineSpan = node.GetLocation().GetLineSpan();
                int line = lineSpan.StartLinePosition.Line + 1;
                var shortName = matchedKey.Substring(matchedKey.LastIndexOf('.') + 1);
                if (!seen.Add((line, matchedKey))) continue; // already reported on this line

                issues.Add(new SecurityIssue
                {
                    Category = category,
                    Severity = severity,
                    Title = $"{titlePrefix}: {shortName}",
                    Description = message!,
                    Recommendation = message!.Contains('—') ? message.Split('—')[1].Trim() : message,
                    MethodName = GetContainingMethodName(node),
                    FileName = fileName,
                    FilePath = filePath,
                    LineNumber = line,
                    CodeSnippet = node.ToString(),
                    ProjectName = projectName
                });
            }

            return issues;
        }

        /// <summary>
        /// Returns the matched known-type key and its message if <paramref name="type"/> or any
        /// of its base types is in <paramref name="knownTypes"/> (by fully-qualified name).
        /// </summary>
        private static (string? Key, string? Message) MatchKnownType(
            ITypeSymbol type, IReadOnlyDictionary<string, string> knownTypes)
        {
            for (ITypeSymbol? t = type; t is not null; t = t.BaseType)
            {
                var fullName = t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    .Replace("global::", string.Empty);
                if (knownTypes.TryGetValue(fullName, out var message))
                    return (fullName, message);
            }
            return (null, null);
        }

        /// <summary>
        /// Analyzes path traversal vulnerabilities
        /// </summary>
        internal static List<SecurityIssue> AnalyzePathTraversal(
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
        internal static List<SecurityIssue> AnalyzeDeserialization(
            SyntaxNode root,
            SemanticModel semanticModel,
            string fileName,
            string filePath,
            string projectName) =>
            AnalyzeTypeUsages(
                root, semanticModel, fileName, filePath, projectName,
                InsecureDeserializerTypes, category: "deserialization", severity: "Critical",
                titlePrefix: "Insecure Deserialization");

        /// <summary>
        /// Gets the containing method name for a syntax node
        /// </summary>
        private static string GetContainingMethodName(SyntaxNode node)
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
