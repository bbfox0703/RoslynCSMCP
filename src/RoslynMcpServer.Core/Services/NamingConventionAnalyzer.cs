using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Core.Models;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace RoslynMcpServer.Core.Services
{
    /// <summary>
    /// Service for analyzing naming convention compliance
    /// </summary>
    public class NamingConventionAnalyzer
    {
        private readonly ILogger<NamingConventionAnalyzer> _logger;
        private readonly CodeAnalysisService _codeAnalysis;

        public NamingConventionAnalyzer(
            ILogger<NamingConventionAnalyzer> logger,
            CodeAnalysisService codeAnalysis)
        {
            _logger = logger;
            _codeAnalysis = codeAnalysis;
        }

        /// <summary>
        /// Analyzes naming conventions in a solution
        /// </summary>
        public async Task<NamingConventionResults> AnalyzeNamingConventionsAsync(
            string solutionPath,
            string[]? violationTypes = null,
            string scope = "all")
        {
            var results = new NamingConventionResults();
            var allViolations = new ConcurrentBag<NamingViolation>();
            int totalSymbols = 0;

            try
            {
                var solution = await _codeAnalysis.GetSolutionAsync(solutionPath);

                var projects = solution.Projects
                    .Where(p => p.SupportsCompilation)
                    .ToList();

                results.AnalyzedProjects = projects.Count;

                // Analyze each project
                var projectTasks = projects.Select(async project =>
                {
                    try
                    {
                        var (violations, symbolCount) = await AnalyzeProjectAsync(project, violationTypes, scope);
                        foreach (var violation in violations)
                        {
                            allViolations.Add(violation);
                        }
                        Interlocked.Add(ref totalSymbols, symbolCount);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to analyze project: {ProjectName}", project.Name);
                        results.FailedProjects++;
                    }
                });

                await Task.WhenAll(projectTasks);

                results.Violations = allViolations.ToList();
                results.AnalyzedSymbols = totalSymbols;
                results.AnalyzedFiles = results.Violations.Select(v => v.FilePath).Distinct().Count();

                // Calculate statistics
                CalculateStatistics(results);

                _logger.LogInformation(
                    "Naming convention analysis complete: {ViolationCount} violations found in {SymbolCount} symbols ({ComplianceScore:F1}% compliance)",
                    results.TotalViolations,
                    results.AnalyzedSymbols,
                    results.ComplianceScore);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing naming conventions");
                results.Warnings.Add(new OperationWarning
                {
                    Context = "Analysis",
                    Message = $"Error: {ex.Message}"
                });
            }

            return results;
        }

        /// <summary>
        /// Analyzes a single project for naming convention violations
        /// </summary>
        private async Task<(List<NamingViolation>, int)> AnalyzeProjectAsync(
            Project project,
            string[]? violationTypes,
            string scope)
        {
            var violations = new List<NamingViolation>();
            int symbolCount = 0;

            var compilation = await project.GetCompilationAsync();
            if (compilation == null)
                return (violations, symbolCount);

            foreach (var syntaxTree in compilation.SyntaxTrees)
            {
                try
                {
                    var semanticModel = compilation.GetSemanticModel(syntaxTree);
                    var root = await syntaxTree.GetRootAsync();

                    // Get all declared symbols in the file
                    var declaredSymbols = root.DescendantNodes()
                        .Select(node => semanticModel.GetDeclaredSymbol(node))
                        .Where(symbol => symbol != null)
                        .Cast<ISymbol>()
                        .ToList();

                    foreach (var symbol in declaredSymbols)
                    {
                        // Apply scope filter
                        if (!ShouldAnalyzeSymbol(symbol, scope))
                            continue;

                        symbolCount++;

                        // Check various naming conventions
                        if (violationTypes == null || violationTypes.Contains("InterfaceNaming"))
                        {
                            CheckInterfaceNaming(symbol, violations, syntaxTree, project.Name);
                        }

                        if (violationTypes == null || violationTypes.Contains("TypeNaming"))
                        {
                            CheckTypeNaming(symbol, violations, syntaxTree, project.Name);
                        }

                        if (violationTypes == null || violationTypes.Contains("MethodNaming"))
                        {
                            CheckMethodNaming(symbol, violations, syntaxTree, project.Name);
                        }

                        if (violationTypes == null || violationTypes.Contains("PropertyNaming"))
                        {
                            CheckPropertyNaming(symbol, violations, syntaxTree, project.Name);
                        }

                        if (violationTypes == null || violationTypes.Contains("FieldNaming"))
                        {
                            CheckFieldNaming(symbol, violations, syntaxTree, project.Name);
                        }

                        if (violationTypes == null || violationTypes.Contains("ParameterNaming"))
                        {
                            CheckParameterNaming(symbol, violations, syntaxTree, project.Name);
                        }

                        if (violationTypes == null || violationTypes.Contains("TypeParameterNaming"))
                        {
                            CheckTypeParameterNaming(symbol, violations, syntaxTree, project.Name);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to analyze syntax tree: {FilePath}", syntaxTree.FilePath);
                }
            }

            return (violations, symbolCount);
        }

        /// <summary>
        /// Checks if a symbol should be analyzed based on scope
        /// </summary>
        private bool ShouldAnalyzeSymbol(ISymbol symbol, string scope)
        {
            if (scope.ToLowerInvariant() == "all")
                return true;

            if (scope.ToLowerInvariant() == "public")
                return symbol.DeclaredAccessibility == Accessibility.Public;

            if (scope.ToLowerInvariant() == "internal")
                return symbol.DeclaredAccessibility == Accessibility.Internal ||
                       symbol.DeclaredAccessibility == Accessibility.Public;

            return true;
        }

        /// <summary>
        /// Checks interface naming conventions (should start with 'I')
        /// </summary>
        private void CheckInterfaceNaming(
            ISymbol symbol,
            List<NamingViolation> violations,
            SyntaxTree syntaxTree,
            string projectName)
        {
            if (symbol.Kind != SymbolKind.NamedType)
                return;

            var typeSymbol = (INamedTypeSymbol)symbol;
            if (typeSymbol.TypeKind != TypeKind.Interface)
                return;

            var name = symbol.Name;

            // Interface should start with 'I' followed by an uppercase letter
            if (!name.StartsWith("I") || name.Length < 2 || !char.IsUpper(name[1]))
            {
                var suggestedName = name.StartsWith("I") ? name : "I" + char.ToUpper(name[0]) + name.Substring(1);

                violations.Add(CreateViolation(
                    symbol,
                    "InterfaceNaming",
                    "High",
                    name,
                    suggestedName,
                    "IPascalCase",
                    "Interfaces should start with 'I' followed by a capital letter (e.g., IUserService)",
                    syntaxTree,
                    projectName));
            }
        }

        /// <summary>
        /// Checks type naming conventions (PascalCase)
        /// </summary>
        private void CheckTypeNaming(
            ISymbol symbol,
            List<NamingViolation> violations,
            SyntaxTree syntaxTree,
            string projectName)
        {
            if (symbol.Kind != SymbolKind.NamedType)
                return;

            var typeSymbol = (INamedTypeSymbol)symbol;
            if (typeSymbol.TypeKind == TypeKind.Interface) // Skip interfaces (handled separately)
                return;

            var name = symbol.Name;

            // Check PascalCase
            if (!IsPascalCase(name))
            {
                var suggestedName = ToPascalCase(name);

                violations.Add(CreateViolation(
                    symbol,
                    "TypeNaming",
                    "Medium",
                    name,
                    suggestedName,
                    "PascalCase",
                    $"{GetTypeKindName(typeSymbol.TypeKind)} names should use PascalCase (e.g., UserService, OrderProcessor)",
                    syntaxTree,
                    projectName));
            }
        }

        /// <summary>
        /// Checks method naming conventions (PascalCase)
        /// </summary>
        private void CheckMethodNaming(
            ISymbol symbol,
            List<NamingViolation> violations,
            SyntaxTree syntaxTree,
            string projectName)
        {
            if (symbol.Kind != SymbolKind.Method)
                return;

            var methodSymbol = (IMethodSymbol)symbol;

            // Skip special methods (constructors, operators, etc.)
            if (methodSymbol.MethodKind != MethodKind.Ordinary)
                return;

            var name = symbol.Name;

            // Check PascalCase
            if (!IsPascalCase(name))
            {
                var suggestedName = ToPascalCase(name);

                violations.Add(CreateViolation(
                    symbol,
                    "MethodNaming",
                    "Medium",
                    name,
                    suggestedName,
                    "PascalCase",
                    "Method names should use PascalCase (e.g., GetUser, ProcessOrder)",
                    syntaxTree,
                    projectName));
            }
        }

        /// <summary>
        /// Checks property naming conventions (PascalCase)
        /// </summary>
        private void CheckPropertyNaming(
            ISymbol symbol,
            List<NamingViolation> violations,
            SyntaxTree syntaxTree,
            string projectName)
        {
            if (symbol.Kind != SymbolKind.Property)
                return;

            var name = symbol.Name;

            // Check PascalCase
            if (!IsPascalCase(name))
            {
                var suggestedName = ToPascalCase(name);

                violations.Add(CreateViolation(
                    symbol,
                    "PropertyNaming",
                    "Medium",
                    name,
                    suggestedName,
                    "PascalCase",
                    "Property names should use PascalCase (e.g., UserName, OrderDate)",
                    syntaxTree,
                    projectName));
            }
        }

        /// <summary>
        /// Checks field naming conventions
        /// </summary>
        private void CheckFieldNaming(
            ISymbol symbol,
            List<NamingViolation> violations,
            SyntaxTree syntaxTree,
            string projectName)
        {
            if (symbol.Kind != SymbolKind.Field)
                return;

            var fieldSymbol = (IFieldSymbol)symbol;
            var name = symbol.Name;

            // Constants should use PascalCase or UPPER_CASE
            if (fieldSymbol.IsConst)
            {
                if (!IsPascalCase(name) && !IsUpperCase(name))
                {
                    var suggestedName = ToPascalCase(name);

                    violations.Add(CreateViolation(
                        symbol,
                        "ConstantNaming",
                        "Low",
                        name,
                        suggestedName,
                        "PascalCase or UPPER_CASE",
                        "Constant names should use PascalCase or UPPER_CASE (e.g., MaxValue, MAX_VALUE)",
                        syntaxTree,
                        projectName));
                }
                return;
            }

            // Private/protected fields should use _camelCase
            if (symbol.DeclaredAccessibility == Accessibility.Private ||
                symbol.DeclaredAccessibility == Accessibility.Protected)
            {
                if (!name.StartsWith("_") || !IsCamelCase(name.Substring(1)))
                {
                    var suggestedName = "_" + ToCamelCase(name.TrimStart('_'));

                    violations.Add(CreateViolation(
                        symbol,
                        "PrivateFieldNaming",
                        "Medium",
                        name,
                        suggestedName,
                        "_camelCase",
                        "Private/protected field names should start with underscore and use camelCase (e.g., _userName, _orderDate)",
                        syntaxTree,
                        projectName));
                }
            }
            else // Public fields should use PascalCase
            {
                if (!IsPascalCase(name))
                {
                    var suggestedName = ToPascalCase(name);

                    violations.Add(CreateViolation(
                        symbol,
                        "PublicFieldNaming",
                        "Medium",
                        name,
                        suggestedName,
                        "PascalCase",
                        "Public field names should use PascalCase (e.g., UserName, OrderDate)",
                        syntaxTree,
                        projectName));
                }
            }
        }

        /// <summary>
        /// Checks parameter naming conventions (camelCase)
        /// </summary>
        private void CheckParameterNaming(
            ISymbol symbol,
            List<NamingViolation> violations,
            SyntaxTree syntaxTree,
            string projectName)
        {
            if (symbol.Kind != SymbolKind.Parameter)
                return;

            var name = symbol.Name;

            // Parameters should use camelCase
            if (!IsCamelCase(name))
            {
                var suggestedName = ToCamelCase(name);

                violations.Add(CreateViolation(
                    symbol,
                    "ParameterNaming",
                    "Low",
                    name,
                    suggestedName,
                    "camelCase",
                    "Parameter names should use camelCase (e.g., userName, orderDate)",
                    syntaxTree,
                    projectName));
            }
        }

        /// <summary>
        /// Checks type parameter naming conventions (should start with 'T')
        /// </summary>
        private void CheckTypeParameterNaming(
            ISymbol symbol,
            List<NamingViolation> violations,
            SyntaxTree syntaxTree,
            string projectName)
        {
            if (symbol.Kind != SymbolKind.TypeParameter)
                return;

            var name = symbol.Name;

            // Type parameters should start with 'T' and use PascalCase
            if (!name.StartsWith("T") || name.Length < 2 || !char.IsUpper(name[1]))
            {
                var suggestedName = name.StartsWith("T") ? name : "T" + char.ToUpper(name[0]) + name.Substring(1);

                violations.Add(CreateViolation(
                    symbol,
                    "TypeParameterNaming",
                    "Low",
                    name,
                    suggestedName,
                    "TPascalCase",
                    "Type parameter names should start with 'T' followed by PascalCase (e.g., TKey, TValue, TEntity)",
                    syntaxTree,
                    projectName));
            }
        }

        /// <summary>
        /// Checks if a name follows PascalCase convention
        /// </summary>
        private bool IsPascalCase(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            // Should start with uppercase letter
            if (!char.IsUpper(name[0]))
                return false;

            // Should not have underscores (except leading underscore for private fields)
            if (name.Contains('_'))
                return false;

            return true;
        }

        /// <summary>
        /// Checks if a name follows camelCase convention
        /// </summary>
        private bool IsCamelCase(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            // Should start with lowercase letter
            if (!char.IsLower(name[0]))
                return false;

            // Should not have underscores
            if (name.Contains('_'))
                return false;

            return true;
        }

        /// <summary>
        /// Checks if a name is UPPER_CASE
        /// </summary>
        private bool IsUpperCase(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            return name.All(c => char.IsUpper(c) || char.IsDigit(c) || c == '_');
        }

        /// <summary>
        /// Converts a name to PascalCase
        /// </summary>
        private string ToPascalCase(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;

            // Remove leading underscores
            name = name.TrimStart('_');

            if (string.IsNullOrEmpty(name))
                return name;

            // If already starts with uppercase, return as is
            if (char.IsUpper(name[0]))
                return name;

            // Convert first character to uppercase
            return char.ToUpper(name[0]) + name.Substring(1);
        }

        /// <summary>
        /// Converts a name to camelCase
        /// </summary>
        private string ToCamelCase(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;

            // Remove leading underscores
            name = name.TrimStart('_');

            if (string.IsNullOrEmpty(name))
                return name;

            // If already starts with lowercase, return as is
            if (char.IsLower(name[0]))
                return name;

            // Convert first character to lowercase
            return char.ToLower(name[0]) + name.Substring(1);
        }

        /// <summary>
        /// Gets a friendly name for TypeKind
        /// </summary>
        private string GetTypeKindName(TypeKind typeKind)
        {
            return typeKind switch
            {
                TypeKind.Class => "Class",
                TypeKind.Struct => "Struct",
                TypeKind.Enum => "Enum",
                TypeKind.Delegate => "Delegate",
                _ => "Type"
            };
        }

        /// <summary>
        /// Creates a naming violation
        /// </summary>
        private NamingViolation CreateViolation(
            ISymbol symbol,
            string violationType,
            string severity,
            string currentName,
            string suggestedName,
            string expectedConvention,
            string reason,
            SyntaxTree syntaxTree,
            string projectName)
        {
            var location = symbol.Locations.FirstOrDefault();
            var lineSpan = location?.GetLineSpan();
            var line = lineSpan?.StartLinePosition.Line + 1 ?? 0;

            return new NamingViolation
            {
                SymbolName = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                SymbolKind = symbol.Kind.ToString(),
                ViolationType = violationType,
                Severity = severity,
                CurrentName = currentName,
                SuggestedName = suggestedName,
                ExpectedConvention = expectedConvention,
                Reason = reason,
                FilePath = syntaxTree.FilePath,
                FileName = Path.GetFileName(syntaxTree.FilePath),
                ProjectName = projectName,
                LineNumber = line,
                Accessibility = symbol.DeclaredAccessibility.ToString(),
                Namespace = symbol.ContainingNamespace?.ToDisplayString() ?? "",
                DeclaringType = symbol.ContainingType?.Name ?? ""
            };
        }

        /// <summary>
        /// Calculates statistics
        /// </summary>
        private void CalculateStatistics(NamingConventionResults results)
        {
            // Violations by type
            results.ViolationsByType = results.Violations
                .GroupBy(v => v.ViolationType)
                .ToDictionary(g => g.Key, g => g.Count());

            // Violations by symbol kind
            results.ViolationsBySymbolKind = results.Violations
                .GroupBy(v => v.SymbolKind)
                .ToDictionary(g => g.Key, g => g.Count());

            // Violations by project
            results.ViolationsByProject = results.Violations
                .GroupBy(v => v.ProjectName)
                .ToDictionary(g => g.Key, g => g.Count());

            // Violations by file
            results.ViolationsByFile = results.Violations
                .GroupBy(v => v.FilePath)
                .ToDictionary(g => g.Key, g => g.Count());
        }
    }
}
