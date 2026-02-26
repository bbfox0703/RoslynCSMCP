using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Core.Models;
using System.Text.RegularExpressions;

namespace RoslynMcpServer.Core.Services
{
    /// <summary>
    /// Service for Phase 1 analysis tools: FindMagicNumbers, FindCodeSmells, AnalyzeLayerViolations, RenameSymbol
    /// </summary>
    public class Phase1AnalysisService
    {
        private readonly ILogger<Phase1AnalysisService> _logger;

        public Phase1AnalysisService(ILogger<Phase1AnalysisService> logger)
        {
            _logger = logger;
        }

        #region FindMagicNumbers

        /// <summary>
        /// Find magic numbers and hardcoded literals in the solution
        /// </summary>
        public async Task<MagicNumberResults> FindMagicNumbersAsync(
            string solutionPath,
            bool includeStrings = true,
            bool includeNumbers = true,
            int minStringLength = 3)
        {
            var results = new MagicNumberResults();
            var magicNumbers = new List<MagicNumber>();

            try
            {
                using var workspace = MSBuildWorkspace.Create();
                var solution = await workspace.OpenSolutionAsync(solutionPath);

                foreach (var project in solution.Projects.Where(p => p.SupportsCompilation))
                {
                    try
                    {
                        results.AnalyzedProjects++;
                        var compilation = await project.GetCompilationAsync();
                        if (compilation == null) continue;

                        foreach (var syntaxTree in compilation.SyntaxTrees)
                        {
                            try
                            {
                                results.AnalyzedFiles++;
                                var root = await syntaxTree.GetRootAsync();
                                var semanticModel = compilation.GetSemanticModel(syntaxTree);

                                // Find numeric literals
                                if (includeNumbers)
                                {
                                    var numericLiterals = root.DescendantNodes()
                                        .OfType<LiteralExpressionSyntax>()
                                        .Where(lit => lit.IsKind(SyntaxKind.NumericLiteralExpression));

                                    foreach (var literal in numericLiterals)
                                    {
                                        var value = literal.Token.ValueText;

                                        // Skip common acceptable values
                                        if (IsAcceptableNumericLiteral(value)) continue;

                                        var magicNumber = CreateMagicNumber(
                                            literal, value, "Number", syntaxTree, semanticModel, project.Name);

                                        if (magicNumber != null)
                                        {
                                            magicNumbers.Add(magicNumber);
                                            results.NumericLiterals++;
                                        }
                                    }
                                }

                                // Find string literals
                                if (includeStrings)
                                {
                                    var stringLiterals = root.DescendantNodes()
                                        .OfType<LiteralExpressionSyntax>()
                                        .Where(lit => lit.IsKind(SyntaxKind.StringLiteralExpression));

                                    foreach (var literal in stringLiterals)
                                    {
                                        var value = literal.Token.ValueText;

                                        // Skip short strings, empty strings, and common patterns
                                        if (value.Length < minStringLength || IsAcceptableStringLiteral(value, literal))
                                            continue;

                                        var magicNumber = CreateMagicNumber(
                                            literal, value, "String", syntaxTree, semanticModel, project.Name);

                                        if (magicNumber != null)
                                        {
                                            magicNumbers.Add(magicNumber);
                                            results.StringLiterals++;
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning($"Failed to analyze file {syntaxTree.FilePath}: {ex.Message}");
                                results.Warnings.Add(new OperationWarning
                                {
                                    Context = syntaxTree.FilePath,
                                    Message = ex.Message
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        results.FailedProjects++;
                        _logger.LogWarning($"Failed to analyze project {project.Name}: {ex.Message}");
                        results.Warnings.Add(new OperationWarning
                        {
                            Context = project.Name,
                            Message = ex.Message
                        });
                    }
                }

                // Calculate priority based on occurrence count
                var occurrenceCounts = magicNumbers
                    .GroupBy(m => m.Value)
                    .ToDictionary(g => g.Key, g => g.Count());

                foreach (var magicNumber in magicNumbers)
                {
                    var count = occurrenceCounts[magicNumber.Value];
                    if (count >= 3)
                    {
                        results.HighPriority++;
                    }
                    else if (count >= 2)
                    {
                        results.MediumPriority++;
                    }
                    else
                    {
                        results.LowPriority++;
                    }
                }

                results.MagicNumbers = magicNumbers;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error analyzing magic numbers: {ex}");
                results.Warnings.Add(new OperationWarning
                {
                    Context = "Solution analysis",
                    Message = ex.Message,
                    Details = ex.StackTrace
                });
            }

            return results;
        }

        private bool IsAcceptableNumericLiteral(string value)
        {
            // Accept 0, 1, -1, 2 (common in loops and conditions)
            if (value == "0" || value == "1" || value == "-1" || value == "2")
                return true;

            return false;
        }

        private bool IsAcceptableStringLiteral(string value, LiteralExpressionSyntax literal)
        {
            // Empty or whitespace
            if (string.IsNullOrWhiteSpace(value))
                return true;

            // Common separators and formatting
            if (value.Length <= 3 && (value == "," || value == ";" || value == " " || value == "\n" || value == "\t"))
                return true;

            // Inside attribute (likely metadata)
            if (literal.Ancestors().Any(a => a is AttributeSyntax))
                return true;

            // Test assertion messages
            if (literal.Ancestors().Any(a => a is InvocationExpressionSyntax inv &&
                inv.ToString().Contains("Assert")))
                return true;

            return false;
        }

        private MagicNumber? CreateMagicNumber(
            LiteralExpressionSyntax literal,
            string value,
            string type,
            SyntaxTree syntaxTree,
            SemanticModel semanticModel,
            string projectName)
        {
            var location = literal.GetLocation();
            var lineSpan = location.GetLineSpan();

            // Find containing member
            var containingMember = literal.Ancestors()
                .FirstOrDefault(a => a is MethodDeclarationSyntax ||
                                   a is PropertyDeclarationSyntax ||
                                   a is ConstructorDeclarationSyntax);

            var containingType = literal.Ancestors()
                .OfType<TypeDeclarationSyntax>()
                .FirstOrDefault();

            if (containingMember == null || containingType == null)
                return null;

            var memberName = containingMember switch
            {
                MethodDeclarationSyntax method => method.Identifier.Text,
                PropertyDeclarationSyntax prop => prop.Identifier.Text,
                ConstructorDeclarationSyntax ctor => ctor.Identifier.Text,
                _ => "Unknown"
            };

            var suggestedName = GenerateConstantName(value, type, memberName);

            return new MagicNumber
            {
                Value = value,
                Type = type,
                FilePath = syntaxTree.FilePath,
                FileName = Path.GetFileName(syntaxTree.FilePath),
                ProjectName = projectName,
                LineNumber = lineSpan.StartLinePosition.Line + 1,
                ContainingMember = memberName,
                ContainingType = containingType.Identifier.Text,
                CodeContext = GetLineText(syntaxTree, lineSpan.StartLinePosition.Line),
                SuggestedConstantName = suggestedName,
                Reason = $"{type} literal used in {memberName}"
            };
        }

        private string GenerateConstantName(string value, string type, string context)
        {
            if (type == "Number")
            {
                // Try to infer meaning from context
                if (context.Contains("Max", StringComparison.OrdinalIgnoreCase))
                    return $"MAX_{context.ToUpper()}_VALUE";
                if (context.Contains("Min", StringComparison.OrdinalIgnoreCase))
                    return $"MIN_{context.ToUpper()}_VALUE";
                if (context.Contains("Size", StringComparison.OrdinalIgnoreCase) ||
                    context.Contains("Length", StringComparison.OrdinalIgnoreCase))
                    return $"DEFAULT_{context.ToUpper()}";

                return $"NUMERIC_CONSTANT_{value.Replace(".", "_").Replace("-", "NEG_")}";
            }
            else if (type == "String")
            {
                // Generate from the string value
                var cleaned = Regex.Replace(value, @"[^a-zA-Z0-9]", "_");
                cleaned = Regex.Replace(cleaned, @"_+", "_").Trim('_');
                if (cleaned.Length > 30)
                    cleaned = cleaned.Substring(0, 30);

                return cleaned.ToUpper() + "_TEXT";
            }

            return "CONSTANT_VALUE";
        }

        private string GetLineText(SyntaxTree syntaxTree, int lineNumber)
        {
            try
            {
                var text = syntaxTree.GetText();
                var line = text.Lines[lineNumber];
                return line.ToString().Trim();
            }
            catch
            {
                return "";
            }
        }

        #endregion

        #region FindCodeSmells

        /// <summary>
        /// Find code smells in the solution
        /// </summary>
        public async Task<CodeSmellResults> FindCodeSmellsAsync(
            string solutionPath,
            string[] smellTypes,
            string severityFilter = "All")
        {
            var results = new CodeSmellResults
            {
                SmellsByType = new Dictionary<string, int>()
            };
            var allSmells = new List<CodeSmell>();

            try
            {
                using var workspace = MSBuildWorkspace.Create();
                var solution = await workspace.OpenSolutionAsync(solutionPath);

                foreach (var project in solution.Projects.Where(p => p.SupportsCompilation))
                {
                    try
                    {
                        results.AnalyzedProjects++;
                        var compilation = await project.GetCompilationAsync();
                        if (compilation == null) continue;

                        foreach (var syntaxTree in compilation.SyntaxTrees)
                        {
                            try
                            {
                                results.AnalyzedFiles++;
                                var semanticModel = compilation.GetSemanticModel(syntaxTree);

                                // Run each detector based on requested smell types
                                var smells = new List<CodeSmell>();

                                if (smellTypes.Contains("LongMethod", StringComparer.OrdinalIgnoreCase) ||
                                    smellTypes.Contains("all", StringComparer.OrdinalIgnoreCase))
                                {
                                    smells.AddRange(CodeSmellDetectors.DetectLongMethods(syntaxTree, semanticModel, project.Name));
                                }

                                if (smellTypes.Contains("LargeClass", StringComparer.OrdinalIgnoreCase) ||
                                    smellTypes.Contains("all", StringComparer.OrdinalIgnoreCase))
                                {
                                    smells.AddRange(CodeSmellDetectors.DetectLargeClasses(syntaxTree, semanticModel, project.Name));
                                }

                                if (smellTypes.Contains("LongParameterList", StringComparer.OrdinalIgnoreCase) ||
                                    smellTypes.Contains("all", StringComparer.OrdinalIgnoreCase))
                                {
                                    smells.AddRange(CodeSmellDetectors.DetectLongParameterLists(syntaxTree, semanticModel, project.Name));
                                }

                                if (smellTypes.Contains("PrimitiveObsession", StringComparer.OrdinalIgnoreCase) ||
                                    smellTypes.Contains("all", StringComparer.OrdinalIgnoreCase))
                                {
                                    smells.AddRange(CodeSmellDetectors.DetectPrimitiveObsession(syntaxTree, semanticModel, project.Name));
                                }

                                if (smellTypes.Contains("SwitchStatements", StringComparer.OrdinalIgnoreCase) ||
                                    smellTypes.Contains("all", StringComparer.OrdinalIgnoreCase))
                                {
                                    smells.AddRange(CodeSmellDetectors.DetectSwitchStatements(syntaxTree, semanticModel, project.Name));
                                }

                                if (smellTypes.Contains("DataClumps", StringComparer.OrdinalIgnoreCase) ||
                                    smellTypes.Contains("all", StringComparer.OrdinalIgnoreCase))
                                {
                                    smells.AddRange(CodeSmellDetectors.DetectDataClumps(syntaxTree, semanticModel, project.Name));
                                }

                                if (smellTypes.Contains("FeatureEnvy", StringComparer.OrdinalIgnoreCase) ||
                                    smellTypes.Contains("all", StringComparer.OrdinalIgnoreCase))
                                {
                                    smells.AddRange(CodeSmellDetectors.DetectFeatureEnvy(syntaxTree, semanticModel, project.Name));
                                }

                                if (smellTypes.Contains("MessageChains", StringComparer.OrdinalIgnoreCase) ||
                                    smellTypes.Contains("all", StringComparer.OrdinalIgnoreCase))
                                {
                                    smells.AddRange(CodeSmellDetectors.DetectMessageChains(syntaxTree, semanticModel, project.Name));
                                }

                                if (smellTypes.Contains("MiddleMan", StringComparer.OrdinalIgnoreCase) ||
                                    smellTypes.Contains("all", StringComparer.OrdinalIgnoreCase))
                                {
                                    smells.AddRange(CodeSmellDetectors.DetectMiddleMan(syntaxTree, semanticModel, project.Name));
                                }

                                if (smellTypes.Contains("SpeculativeGenerality", StringComparer.OrdinalIgnoreCase) ||
                                    smellTypes.Contains("all", StringComparer.OrdinalIgnoreCase))
                                {
                                    smells.AddRange(CodeSmellDetectors.DetectSpeculativeGenerality(syntaxTree, semanticModel, project.Name));
                                }

                                // Apply severity filter
                                if (!severityFilter.Equals("All", StringComparison.OrdinalIgnoreCase))
                                {
                                    smells = smells.Where(s => s.Severity.Equals(severityFilter, StringComparison.OrdinalIgnoreCase)).ToList();
                                }

                                allSmells.AddRange(smells);

                                // Count symbols analyzed (approximate)
                                var root = await syntaxTree.GetRootAsync();
                                results.AnalyzedSymbols += root.DescendantNodes().OfType<BaseMethodDeclarationSyntax>().Count();
                                results.AnalyzedSymbols += root.DescendantNodes().OfType<TypeDeclarationSyntax>().Count();
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning($"Failed to analyze file {syntaxTree.FilePath}: {ex.Message}");
                                results.Warnings.Add(new OperationWarning
                                {
                                    Context = syntaxTree.FilePath,
                                    Message = ex.Message
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        results.FailedProjects++;
                        _logger.LogWarning($"Failed to analyze project {project.Name}: {ex.Message}");
                        results.Warnings.Add(new OperationWarning
                        {
                            Context = project.Name,
                            Message = ex.Message
                        });
                    }
                }

                // Aggregate results
                results.Smells = allSmells;
                results.HighSeverity = allSmells.Count(s => s.Severity == "High");
                results.MediumSeverity = allSmells.Count(s => s.Severity == "Medium");
                results.LowSeverity = allSmells.Count(s => s.Severity == "Low");

                // Group by type
                results.SmellsByType = allSmells
                    .GroupBy(s => s.SmellType)
                    .ToDictionary(g => g.Key, g => g.Count());
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error analyzing code smells: {ex}");
                results.Warnings.Add(new OperationWarning
                {
                    Context = "Solution analysis",
                    Message = ex.Message,
                    Details = ex.StackTrace
                });
            }

            return results;
        }

        #endregion

        #region AnalyzeLayerViolations

        /// <summary>
        /// Analyze architecture layer violations
        /// </summary>
        public async Task<LayerViolationResults> AnalyzeLayerViolationsAsync(
            string solutionPath,
            string layerDefinitionsJson)
        {
            var results = new LayerViolationResults
            {
                ViolationsByType = new Dictionary<string, int>()
            };

            try
            {
                // Parse JSON layer definitions
                var architectureDefinition = ParseLayerDefinitions(layerDefinitionsJson);
                if (architectureDefinition == null)
                {
                    results.Warnings.Add(new OperationWarning
                    {
                        Context = "JSON Parsing",
                        Message = "Failed to parse layer definitions JSON"
                    });
                    return results;
                }

                results.Layers = architectureDefinition.Layers;
                results.Rules = architectureDefinition.Rules;

                // Load solution
                using var workspace = MSBuildWorkspace.Create();
                var solution = await workspace.OpenSolutionAsync(solutionPath);

                // Match projects to layers using glob patterns
                var projectLayerMap = new Dictionary<string, string>();
                foreach (var layer in results.Layers)
                {
                    foreach (var project in solution.Projects)
                    {
                        if (MatchesAnyPattern(project.Name, layer.ProjectPatterns))
                        {
                            layer.MatchedProjects.Add(project.Name);
                            projectLayerMap[project.Name] = layer.Name;
                        }
                    }
                }

                results.AnalyzedProjects = projectLayerMap.Count;

                // Build dependency graph from project references
                var dependencyGraph = new Dictionary<string, List<string>>();
                foreach (var project in solution.Projects)
                {
                    if (!projectLayerMap.ContainsKey(project.Name))
                        continue;

                    var dependencies = new List<string>();
                    foreach (var reference in project.ProjectReferences)
                    {
                        var referencedProject = solution.GetProject(reference.ProjectId);
                        if (referencedProject != null && projectLayerMap.ContainsKey(referencedProject.Name))
                        {
                            dependencies.Add(referencedProject.Name);
                        }
                    }
                    dependencyGraph[project.Name] = dependencies;
                }

                // Validate dependency rules
                var violations = new List<LayerViolation>();
                foreach (var project in solution.Projects)
                {
                    if (!projectLayerMap.ContainsKey(project.Name))
                        continue;

                    var fromLayer = projectLayerMap[project.Name];

                    foreach (var reference in project.ProjectReferences)
                    {
                        var referencedProject = solution.GetProject(reference.ProjectId);
                        if (referencedProject == null || !projectLayerMap.ContainsKey(referencedProject.Name))
                            continue;

                        var toLayer = projectLayerMap[referencedProject.Name];

                        // Check if this dependency is allowed
                        var rule = results.Rules.FirstOrDefault(r =>
                            r.FromLayer == fromLayer && r.ToLayer == toLayer);

                        if (rule != null && !rule.Allowed)
                        {
                            violations.Add(new LayerViolation
                            {
                                ViolationType = "DirectDependency",
                                Severity = "High",
                                FromLayer = fromLayer,
                                ToLayer = toLayer,
                                FromProject = project.Name,
                                ToProject = referencedProject.Name,
                                Description = $"Project '{project.Name}' in layer '{fromLayer}' references '{referencedProject.Name}' in layer '{toLayer}', which violates architectural rules.",
                                Recommendation = $"Remove the dependency from {fromLayer} to {toLayer}, or refactor to follow the layered architecture."
                            });
                            results.HighSeverityViolations++;
                        }
                        else if (rule == null && fromLayer != toLayer)
                        {
                            // No explicit rule - check if it's a potential issue
                            violations.Add(new LayerViolation
                            {
                                ViolationType = "UndefinedDependency",
                                Severity = "Medium",
                                FromLayer = fromLayer,
                                ToLayer = toLayer,
                                FromProject = project.Name,
                                ToProject = referencedProject.Name,
                                Description = $"Project '{project.Name}' in layer '{fromLayer}' references '{referencedProject.Name}' in layer '{toLayer}'. No explicit rule defined for this dependency.",
                                Recommendation = $"Define an explicit rule for {fromLayer} -> {toLayer} in your architecture definition."
                            });
                            results.MediumSeverityViolations++;
                        }
                    }
                }

                // Detect circular dependencies
                var circularDependencies = DetectCircularDependencies(dependencyGraph, projectLayerMap);
                foreach (var cycle in circularDependencies)
                {
                    violations.Add(new LayerViolation
                    {
                        ViolationType = "CircularDependency",
                        Severity = "Critical",
                        FromLayer = string.Join(" -> ", cycle.Select(p => projectLayerMap.GetValueOrDefault(p, "Unknown"))),
                        ToLayer = "",
                        FromProject = cycle.First(),
                        ToProject = cycle.Last(),
                        Description = $"Circular dependency detected: {string.Join(" -> ", cycle)} -> {cycle.First()}",
                        ViolatingReferences = cycle,
                        Recommendation = "Break the circular dependency by introducing abstractions (interfaces), moving shared code to a common layer, or reversing the dependency using Dependency Inversion Principle."
                    });
                    results.CriticalViolations++;
                }

                results.Violations = violations;

                // Count by type
                results.ViolationsByType = violations
                    .GroupBy(v => v.ViolationType)
                    .ToDictionary(g => g.Key, g => g.Count());

                // Calculate compliance score
                var totalRuleChecks = results.AnalyzedProjects * results.Rules.Count;
                var violationCount = violations.Count(v => v.ViolationType == "DirectDependency");
                results.ComplianceScore = totalRuleChecks > 0
                    ? Math.Round((1.0 - (double)violationCount / totalRuleChecks) * 100, 2)
                    : 100.0;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error analyzing layer violations: {ex}");
                results.Warnings.Add(new OperationWarning
                {
                    Context = "Solution analysis",
                    Message = ex.Message,
                    Details = ex.StackTrace
                });
            }

            return results;
        }

        private ArchitectureDefinition? ParseLayerDefinitions(string json)
        {
            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<ArchitectureDefinition>(json,
                    new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to parse layer definitions JSON: {ex.Message}");
                return null;
            }
        }

        private bool MatchesAnyPattern(string projectName, List<string> patterns)
        {
            foreach (var pattern in patterns)
            {
                if (MatchesGlobPattern(projectName, pattern))
                    return true;
            }
            return false;
        }

        private bool MatchesGlobPattern(string input, string pattern)
        {
            // Convert glob pattern to regex
            // * matches any sequence of characters
            // ? matches any single character
            var regexPattern = "^" + Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".")
                + "$";

            return Regex.IsMatch(input, regexPattern, RegexOptions.IgnoreCase);
        }

        private List<List<string>> DetectCircularDependencies(
            Dictionary<string, List<string>> graph,
            Dictionary<string, string> projectLayerMap)
        {
            var cycles = new List<List<string>>();
            var visited = new HashSet<string>();
            var recursionStack = new HashSet<string>();

            foreach (var project in graph.Keys)
            {
                if (!visited.Contains(project))
                {
                    var path = new List<string>();
                    DetectCyclesRecursive(project, graph, visited, recursionStack, path, cycles);
                }
            }

            return cycles;
        }

        private bool DetectCyclesRecursive(
            string current,
            Dictionary<string, List<string>> graph,
            HashSet<string> visited,
            HashSet<string> recursionStack,
            List<string> path,
            List<List<string>> cycles)
        {
            visited.Add(current);
            recursionStack.Add(current);
            path.Add(current);

            if (graph.ContainsKey(current))
            {
                foreach (var neighbor in graph[current])
                {
                    if (!visited.Contains(neighbor))
                    {
                        if (DetectCyclesRecursive(neighbor, graph, visited, recursionStack, path, cycles))
                            return true;
                    }
                    else if (recursionStack.Contains(neighbor))
                    {
                        // Found a cycle
                        var cycleStart = path.IndexOf(neighbor);
                        var cycle = path.Skip(cycleStart).ToList();
                        cycles.Add(cycle);
                        return true;
                    }
                }
            }

            path.RemoveAt(path.Count - 1);
            recursionStack.Remove(current);
            return false;
        }

        // Helper class for JSON deserialization
        private class ArchitectureDefinition
        {
            public List<LayerDefinition> Layers { get; set; } = new();
            public List<LayerRule> Rules { get; set; } = new();
        }

        #endregion

        #region RenameSymbolSafely

        /// <summary>
        /// Rename a symbol safely with preview
        /// </summary>
        public async Task<RenameSymbolResults> RenameSymbolAsync(
            string solutionPath,
            string symbolName,
            string newName,
            bool previewOnly = true)
        {
            var results = new RenameSymbolResults
            {
                IsPreview = previewOnly,
                Success = false
            };

            try
            {
                // Validate inputs
                if (string.IsNullOrWhiteSpace(symbolName) || string.IsNullOrWhiteSpace(newName))
                {
                    results.ErrorMessage = "Symbol name and new name are required.";
                    return results;
                }

                if (symbolName.Equals(newName, StringComparison.OrdinalIgnoreCase))
                {
                    results.ErrorMessage = "New name must be different from current name.";
                    return results;
                }

                // Load solution
                using var workspace = MSBuildWorkspace.Create();
                var solution = await workspace.OpenSolutionAsync(solutionPath);

                // Find the symbol by name
                var targetSymbol = await FindSymbolByNameAsync(solution, symbolName);
                if (targetSymbol == null)
                {
                    results.ErrorMessage = $"Symbol '{symbolName}' not found in solution.";
                    return results;
                }

                // Populate target information
                var symbolLocations = targetSymbol.Locations.FirstOrDefault();
                results.Target = new RenameTarget
                {
                    CurrentName = targetSymbol.Name,
                    NewName = newName,
                    FullName = targetSymbol.ToDisplayString(),
                    SymbolKind = targetSymbol.Kind.ToString(),
                    FilePath = symbolLocations?.SourceTree?.FilePath ?? string.Empty,
                    LineNumber = symbolLocations?.GetLineSpan().StartLinePosition.Line + 1 ?? 0,
                    ProjectName = targetSymbol.ContainingAssembly?.Name ?? string.Empty
                };

                // Check for conflicts - see if new name already exists in the same scope
                var conflicts = await DetectNameConflictsAsync(solution, targetSymbol, newName);
                results.Conflicts.AddRange(conflicts);

                // Find all references to the symbol
                var references = await Microsoft.CodeAnalysis.FindSymbols.SymbolFinder.FindReferencesAsync(
                    targetSymbol, solution);

                var fileChanges = new Dictionary<string, FileRenameChange>();

                foreach (var referencedSymbol in references)
                {
                    // Add definition location
                    foreach (var location in referencedSymbol.Definition.Locations)
                    {
                        if (location.IsInSource && location.SourceTree != null)
                        {
                            AddRenameLocation(fileChanges, location, true, targetSymbol.Name, newName);
                        }
                    }

                    // Add reference locations
                    foreach (var referenceLocation in referencedSymbol.Locations)
                    {
                        if (!referenceLocation.IsImplicit && referenceLocation.Location.IsInSource &&
                            referenceLocation.Location.SourceTree != null)
                        {
                            AddRenameLocation(fileChanges, referenceLocation.Location, false,
                                targetSymbol.Name, newName);
                        }
                    }
                }

                results.FileChanges = fileChanges.Values.OrderBy(f => f.FilePath).ToList();
                results.TotalLocations = results.FileChanges.Sum(f => f.ChangeCount);
                results.FilesAffected = results.FileChanges.Count;
                results.ProjectsAffected = results.FileChanges
                    .Select(f => f.ProjectName)
                    .Distinct()
                    .Count();

                // Assess risk
                AssessRenameRisk(results, targetSymbol);

                // Add warnings
                if (results.TotalLocations > 100)
                {
                    results.Warnings.Add($"Large number of references ({results.TotalLocations}) - consider breaking into smaller refactorings");
                }

                if (results.ProjectsAffected > 5)
                {
                    results.Warnings.Add($"Affects {results.ProjectsAffected} projects - ensure all projects compile after rename");
                }

                // Execute rename if not preview mode
                if (!previewOnly && !results.HasConflicts)
                {
                    try
                    {
                        // Use Roslyn's Renamer to apply changes
                        var newSolution = await Microsoft.CodeAnalysis.Rename.Renamer.RenameSymbolAsync(
                            solution, targetSymbol, new Microsoft.CodeAnalysis.Rename.SymbolRenameOptions(), newName);

                        // Apply changes to workspace
                        if (workspace.TryApplyChanges(newSolution))
                        {
                            results.Success = true;
                        }
                        else
                        {
                            results.Success = false;
                            results.ErrorMessage = "Failed to apply rename changes to solution.";
                        }
                    }
                    catch (Exception ex)
                    {
                        results.Success = false;
                        results.ErrorMessage = $"Error executing rename: {ex.Message}";
                    }
                }
                else
                {
                    // Preview mode always succeeds (if no errors up to this point)
                    results.Success = !results.HasConflicts;
                    if (results.HasConflicts)
                    {
                        results.ErrorMessage = "Conflicts detected. Resolve conflicts before executing rename.";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in RenameSymbolAsync: {ex}");
                results.Success = false;
                results.ErrorMessage = ex.Message;
            }

            return results;
        }

        private async Task<ISymbol?> FindSymbolByNameAsync(Solution solution, string symbolName)
        {
            foreach (var project in solution.Projects.Where(p => p.SupportsCompilation))
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation == null) continue;

                // Search in all syntax trees
                foreach (var tree in compilation.SyntaxTrees)
                {
                    var semanticModel = compilation.GetSemanticModel(tree);
                    var root = await tree.GetRootAsync();

                    // Look for type declarations
                    var typeDeclarations = root.DescendantNodes()
                        .OfType<TypeDeclarationSyntax>()
                        .Where(t => t.Identifier.Text == symbolName);

                    foreach (var typeDecl in typeDeclarations)
                    {
                        var symbol = semanticModel.GetDeclaredSymbol(typeDecl);
                        if (symbol != null) return symbol;
                    }

                    // Look for method declarations
                    var methodDeclarations = root.DescendantNodes()
                        .OfType<MethodDeclarationSyntax>()
                        .Where(m => m.Identifier.Text == symbolName);

                    foreach (var methodDecl in methodDeclarations)
                    {
                        var symbol = semanticModel.GetDeclaredSymbol(methodDecl);
                        if (symbol != null) return symbol;
                    }

                    // Look for property declarations
                    var propertyDeclarations = root.DescendantNodes()
                        .OfType<PropertyDeclarationSyntax>()
                        .Where(p => p.Identifier.Text == symbolName);

                    foreach (var propDecl in propertyDeclarations)
                    {
                        var symbol = semanticModel.GetDeclaredSymbol(propDecl);
                        if (symbol != null) return symbol;
                    }

                    // Look for field declarations
                    var fieldDeclarations = root.DescendantNodes()
                        .OfType<FieldDeclarationSyntax>();

                    foreach (var fieldDecl in fieldDeclarations)
                    {
                        foreach (var variable in fieldDecl.Declaration.Variables)
                        {
                            if (variable.Identifier.Text == symbolName)
                            {
                                var symbol = semanticModel.GetDeclaredSymbol(variable);
                                if (symbol != null) return symbol;
                            }
                        }
                    }
                }
            }

            return null;
        }

        private async Task<List<string>> DetectNameConflictsAsync(
            Solution solution, ISymbol targetSymbol, string newName)
        {
            var conflicts = new List<string>();

            // Get the containing type or namespace
            var containingSymbol = targetSymbol.ContainingType ?? (INamespaceOrTypeSymbol?)targetSymbol.ContainingNamespace;
            if (containingSymbol == null) return conflicts;

            // Check if new name conflicts with existing members in the same scope
            var members = containingSymbol.GetMembers(newName);
            if (members.Any())
            {
                foreach (var member in members)
                {
                    conflicts.Add($"Symbol '{newName}' already exists in {containingSymbol.ToDisplayString()}: {member.Kind} {member.ToDisplayString()}");
                }
            }

            return conflicts;
        }

        private void AddRenameLocation(
            Dictionary<string, FileRenameChange> fileChanges,
            Location location,
            bool isDefinition,
            string oldName,
            string newName)
        {
            var filePath = location.SourceTree?.FilePath ?? string.Empty;
            if (string.IsNullOrEmpty(filePath)) return;

            if (!fileChanges.ContainsKey(filePath))
            {
                fileChanges[filePath] = new FileRenameChange
                {
                    FilePath = filePath,
                    FileName = Path.GetFileName(filePath),
                    Locations = new List<RenameLocation>()
                };
            }

            var lineSpan = location.GetLineSpan();
            var lineText = GetLineText(location.SourceTree!, lineSpan.StartLinePosition.Line);

            fileChanges[filePath].Locations.Add(new RenameLocation
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                LineNumber = lineSpan.StartLinePosition.Line + 1,
                ColumnNumber = lineSpan.StartLinePosition.Character + 1,
                OldText = oldName,
                NewText = newName,
                LineContext = lineText,
                IsDefinition = isDefinition
            });

            fileChanges[filePath].ChangeCount = fileChanges[filePath].Locations.Count;
        }

        private void AssessRenameRisk(RenameSymbolResults results, ISymbol symbol)
        {
            var riskScore = 0;
            var reasons = new List<string>();

            // Accessibility risk
            if (symbol.DeclaredAccessibility == Accessibility.Public)
            {
                riskScore += 3;
                reasons.Add("public API");
            }
            else if (symbol.DeclaredAccessibility == Accessibility.Internal)
            {
                riskScore += 2;
                reasons.Add("internal API");
            }
            else if (symbol.DeclaredAccessibility == Accessibility.Protected)
            {
                riskScore += 2;
                reasons.Add("protected member");
            }
            else
            {
                riskScore += 1;
            }

            // Usage count risk
            if (results.TotalLocations > 100)
            {
                riskScore += 3;
                reasons.Add($"{results.TotalLocations} references");
            }
            else if (results.TotalLocations > 50)
            {
                riskScore += 2;
                reasons.Add($"{results.TotalLocations} references");
            }
            else if (results.TotalLocations > 20)
            {
                riskScore += 1;
                reasons.Add($"{results.TotalLocations} references");
            }

            // Project scope risk
            if (results.ProjectsAffected > 5)
            {
                riskScore += 2;
                reasons.Add($"{results.ProjectsAffected} projects");
            }
            else if (results.ProjectsAffected > 2)
            {
                riskScore += 1;
            }

            // Symbol kind risk
            if (symbol.Kind == SymbolKind.NamedType)
            {
                riskScore += 1;
                reasons.Add("type rename");
            }

            // Determine risk level
            if (riskScore >= 7)
            {
                results.RiskLevel = "Critical";
            }
            else if (riskScore >= 5)
            {
                results.RiskLevel = "High";
            }
            else if (riskScore >= 3)
            {
                results.RiskLevel = "Medium";
            }
            else
            {
                results.RiskLevel = "Low";
            }

            results.RiskReason = string.Join(", ", reasons);
        }

        #endregion
    }
}
