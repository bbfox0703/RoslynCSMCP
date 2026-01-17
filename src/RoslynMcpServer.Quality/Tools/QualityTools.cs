using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcpServer.Core.Models;
using RoslynMcpServer.Core.Services;
using System.ComponentModel;
using System.Text;

namespace RoslynMcpServer.Quality.Tools;

/// <summary>
/// MCP Tools for C# code quality analysis.
/// This module provides 6 tools (~1,050 tokens) for analyzing code quality.
/// </summary>
[McpServerToolType]
public class QualityTools
{
    #region Tool Methods

    [McpServerTool, Description("Analyze code complexity and identify high-complexity methods")]
    public static async Task<string> AnalyzeCodeComplexity(
        [Description("Path to solution file")] string solutionPath,
        [Description("Complexity threshold (1-10)")] int threshold = 5,
        IServiceProvider? serviceProvider = null)
    {
        try
        {
            var validator = serviceProvider?.GetService<SecurityValidator>();
            if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
            {
                return "Error: Invalid solution path provided.";
            }

            var analysisService = serviceProvider?.GetService<CodeAnalysisService>();
            if (analysisService == null)
            {
                return "Error: Code analysis service not available.";
            }

            var solution = await analysisService.GetSolutionAsync(solutionPath);
            var complexityResults = new List<ComplexityResult>();

            foreach (var project in solution.Projects.Where(p => p.SupportsCompilation))
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation == null) continue;

                foreach (var tree in compilation.SyntaxTrees)
                {
                    var root = await tree.GetRootAsync();
                    var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();

                    foreach (var method in methods)
                    {
                        var complexity = CalculateCyclomaticComplexity(method);
                        if (complexity >= threshold)
                        {
                            var lineSpan = method.GetLocation().GetLineSpan();
                            complexityResults.Add(new ComplexityResult
                            {
                                MethodName = method.Identifier.ValueText,
                                FileName = Path.GetFileName(tree.FilePath),
                                LineNumber = lineSpan.StartLinePosition.Line + 1,
                                Complexity = complexity,
                                ClassName = GetContainingClassName(method),
                                Namespace = GetContainingNamespace(method)
                            });
                        }
                    }
                }
            }

            return FormatComplexityResults(complexityResults);
        }
        catch (Exception ex)
        {
            var logger = serviceProvider?.GetService<ILogger<QualityTools>>();
            logger?.LogError(ex, "Error analyzing code complexity");
            return "Error: An unexpected error occurred during complexity analysis.";
        }
    }

    [McpServerTool, Description("Find code smells and anti-patterns in the solution")]
    public static async Task<string> FindCodeSmells(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary (counts only), normal (grouped list), detailed (with metrics). Default: normal")]
        string format = "normal",
        [Description("Comma-separated smell types: LongMethod, LargeClass, LongParameterList, FeatureEnvy, DataClumps, PrimitiveObsession, SwitchStatements, SpeculativeGenerality, MessageChains, MiddleMan. Default: all")]
        string smellTypes = "all",
        [Description("Severity filter: High, Medium, Low, All (default: All)")] string severity = "All",
        IServiceProvider? serviceProvider = null)
    {
        try
        {
            var phase1Service = serviceProvider?.GetService<Phase1AnalysisService>();
            if (phase1Service == null)
            {
                return "Error: Phase1AnalysisService not available.";
            }

            var smellTypeArray = smellTypes.Equals("all", StringComparison.OrdinalIgnoreCase)
                ? new[] { "LongMethod", "LargeClass", "LongParameterList", "FeatureEnvy", "DataClumps", "PrimitiveObsession", "SwitchStatements", "SpeculativeGenerality", "MessageChains", "MiddleMan" }
                : smellTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var results = await phase1Service.FindCodeSmellsAsync(solutionPath, smellTypeArray, severity);

            return FormatCodeSmellResults(results, format);
        }
        catch (Exception ex)
        {
            var logger = serviceProvider?.GetService<ILogger<QualityTools>>();
            logger?.LogError(ex, "Error finding code smells");
            return $"Error: An unexpected error occurred while finding code smells: {ex.Message}";
        }
    }

    [McpServerTool, Description("Find unused code (dead code) in the solution - types, methods, properties, and fields with no references")]
    public static async Task<string> FindUnusedCode(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary (counts only), normal (grouped list), detailed (full information). Default: normal")]
        string format = "normal",
        [Description("Scope: private (private members only), internal (internal members only), public (public members only), all (all members). Default: all")]
        string scope = "all",
        [Description("Include test projects in analysis (default: false)")] bool includeTests = false,
        IServiceProvider? serviceProvider = null)
    {
        try
        {
            var validator = serviceProvider?.GetService<SecurityValidator>();
            if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
            {
                return "Error: Invalid solution path provided.";
            }

            var analyzer = serviceProvider?.GetService<UnusedCodeAnalyzer>();
            if (analyzer == null)
            {
                return "Error: Unused code analyzer service not available.";
            }

            var results = await analyzer.AnalyzeUnusedCodeAsync(solutionPath, scope, includeTests);

            return format.ToLowerInvariant() switch
            {
                "summary" => FormatUnusedCodeSummary(results),
                "detailed" => FormatUnusedCodeDetailed(results),
                _ => FormatUnusedCodeNormal(results)
            };
        }
        catch (Exception ex)
        {
            var logger = serviceProvider?.GetService<ILogger<QualityTools>>();
            logger?.LogError(ex, "Error finding unused code");
            return $"Error: An unexpected error occurred while finding unused code: {ex.Message}";
        }
    }

    [McpServerTool, Description("Find duplicate code blocks across the solution")]
    public static async Task<string> FindDuplicateCode(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary (counts only), normal (grouped list), detailed (full information). Default: normal")]
        string format = "normal",
        [Description("Minimum lines to consider duplicate (default: 5)")] int minLines = 5,
        [Description("Similarity threshold percentage 70-100 (default: 90)")] int similarity = 90,
        IServiceProvider? serviceProvider = null)
    {
        try
        {
            var validator = serviceProvider?.GetService<SecurityValidator>();
            if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
            {
                return "Error: Invalid solution path provided.";
            }

            var analyzer = serviceProvider?.GetService<DuplicateCodeAnalyzer>();
            if (analyzer == null)
            {
                return "Error: Duplicate code analyzer service not available.";
            }

            var results = await analyzer.AnalyzeDuplicateCodeAsync(solutionPath, minLines, similarity);

            return format.ToLowerInvariant() switch
            {
                "summary" => FormatDuplicateCodeSummary(results),
                "detailed" => FormatDuplicateCodeDetailed(results),
                _ => FormatDuplicateCodeNormal(results)
            };
        }
        catch (Exception ex)
        {
            var logger = serviceProvider?.GetService<ILogger<QualityTools>>();
            logger?.LogError(ex, "Error finding duplicate code");
            return $"Error: An unexpected error occurred while finding duplicate code: {ex.Message}";
        }
    }

    [McpServerTool, Description("Find magic numbers and hardcoded literals that should be extracted as constants")]
    public static async Task<string> FindMagicNumbers(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary (counts only), normal (grouped list), detailed (with suggestions). Default: normal")]
        string format = "normal",
        [Description("Include string literals (default: true)")] bool includeStrings = true,
        [Description("Include numeric literals (default: true)")] bool includeNumbers = true,
        [Description("Minimum string length to consider (default: 3)")] int minStringLength = 3,
        IServiceProvider? serviceProvider = null)
    {
        try
        {
            var phase1Service = serviceProvider?.GetService<Phase1AnalysisService>();
            if (phase1Service == null)
            {
                return "Error: Phase1AnalysisService not available.";
            }

            var results = await phase1Service.FindMagicNumbersAsync(
                solutionPath,
                includeStrings,
                includeNumbers,
                minStringLength);

            return FormatMagicNumberResults(results, format);
        }
        catch (Exception ex)
        {
            var logger = serviceProvider?.GetService<ILogger<QualityTools>>();
            logger?.LogError(ex, "Error finding magic numbers");
            return $"Error: An unexpected error occurred while finding magic numbers: {ex.Message}";
        }
    }

    [McpServerTool, Description("Analyze C# naming convention compliance and detect violations")]
    public static async Task<string> AnalyzeNamingConventions(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary (key metrics), normal (balanced), detailed (comprehensive). Default: normal")]
        string format = "normal",
        [Description("Comma-separated violation types to check: InterfaceNaming, TypeNaming, MethodNaming, PropertyNaming, FieldNaming, ParameterNaming, TypeParameterNaming. Default: all")]
        string? violationTypes = null,
        [Description("Analysis scope: all, public, internal. Default: all")]
        string scope = "all",
        IServiceProvider? serviceProvider = null)
    {
        try
        {
            var validator = serviceProvider?.GetService<SecurityValidator>();
            if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
            {
                return "Error: Invalid solution path provided.";
            }

            var analyzer = serviceProvider?.GetService<NamingConventionAnalyzer>();
            if (analyzer == null)
            {
                return "Error: Naming convention analyzer service not available.";
            }

            string[]? violationTypesArray = null;
            if (!string.IsNullOrWhiteSpace(violationTypes))
            {
                violationTypesArray = violationTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }

            var results = await analyzer.AnalyzeNamingConventionsAsync(solutionPath, violationTypesArray, scope);

            return format.ToLowerInvariant() switch
            {
                "summary" => FormatNamingConventionsSummary(results),
                "detailed" => FormatNamingConventionsDetailed(results),
                _ => FormatNamingConventionsNormal(results)
            };
        }
        catch (Exception ex)
        {
            var logger = serviceProvider?.GetService<ILogger<QualityTools>>();
            logger?.LogError(ex, "Error analyzing naming conventions");
            return $"Error: An unexpected error occurred while analyzing naming conventions: {ex.Message}";
        }
    }

    #endregion

    #region Helper Methods

    private static int CalculateCyclomaticComplexity(MethodDeclarationSyntax method)
    {
        int complexity = 1;
        foreach (var node in method.DescendantNodes())
        {
            complexity += node switch
            {
                IfStatementSyntax => 1,
                WhileStatementSyntax => 1,
                ForStatementSyntax => 1,
                ForEachStatementSyntax => 1,
                CaseSwitchLabelSyntax => 1,
                CasePatternSwitchLabelSyntax => 1,
                CatchClauseSyntax => 1,
                ConditionalExpressionSyntax => 1,
                _ => 0
            };
        }
        return complexity;
    }

    private static string GetContainingClassName(MethodDeclarationSyntax method)
    {
        var classDecl = method.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        return classDecl?.Identifier.ValueText ?? "Unknown";
    }

    private static string GetContainingNamespace(MethodDeclarationSyntax method)
    {
        var namespaceDecl = method.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
        return namespaceDecl?.Name.ToString() ?? "Global";
    }

    #endregion

    #region Formatting Methods

    private static string FormatComplexityResults(List<ComplexityResult> results)
    {
        if (!results.Any())
            return "No high-complexity methods found.";

        var output = new StringBuilder();
        output.AppendLine($"Found {results.Count} high-complexity methods:\n");

        var grouped = results.GroupBy(r => r.FileName).OrderByDescending(g => g.Max(r => r.Complexity));

        foreach (var fileGroup in grouped)
        {
            output.AppendLine($"**{fileGroup.Key}**");
            foreach (var result in fileGroup.OrderByDescending(r => r.Complexity))
            {
                output.AppendLine($"  - {result.ClassName}.{result.MethodName} (complexity: {result.Complexity}, line: {result.LineNumber})");
            }
            output.AppendLine();
        }

        return output.ToString();
    }

    private static string FormatCodeSmellResults(CodeSmellResults results, string format)
    {
        if (!results.Smells.Any())
            return "No code smells detected.";

        var output = new StringBuilder();

        if (format == "summary")
        {
            output.AppendLine($"Code Smell Analysis Summary:");
            output.AppendLine($"  Total smells: {results.TotalSmells}");
            output.AppendLine($"  High severity: {results.HighSeverity}");
            output.AppendLine($"  Medium severity: {results.MediumSeverity}");
            output.AppendLine($"  Low severity: {results.LowSeverity}");
            return output.ToString();
        }

        output.AppendLine($"Found {results.TotalSmells} code smells:\n");

        var grouped = results.Smells.GroupBy(s => s.SmellType);
        foreach (var group in grouped)
        {
            output.AppendLine($"**{group.Key}** ({group.Count()}):");
            foreach (var smell in group.Take(format == "detailed" ? 100 : 10))
            {
                output.AppendLine($"  - [{smell.Severity}] {smell.SymbolName} @ {smell.FileName}:{smell.LineNumber}");
                if (format == "detailed")
                {
                    output.AppendLine($"    {smell.Description}");
                    output.AppendLine($"    Recommendation: {smell.Recommendation}");
                }
            }
            if (group.Count() > (format == "detailed" ? 100 : 10))
                output.AppendLine($"    ... and {group.Count() - (format == "detailed" ? 100 : 10)} more");
            output.AppendLine();
        }

        return output.ToString();
    }

    private static string FormatUnusedCodeSummary(UnusedCodeResults results)
    {
        var output = new StringBuilder();
        output.AppendLine("Unused Code Analysis Summary:");
        output.AppendLine($"  Total unused items: {results.UnusedItems.Count}");
        output.AppendLine($"  Classes: {results.ClassCount}");
        output.AppendLine($"  Methods: {results.MethodCount}");
        output.AppendLine($"  Properties: {results.PropertyCount}");
        output.AppendLine($"  Fields: {results.FieldCount}");
        return output.ToString();
    }

    private static string FormatUnusedCodeNormal(UnusedCodeResults results)
    {
        if (!results.UnusedItems.Any())
            return "No unused code found.";

        var output = new StringBuilder();
        output.AppendLine($"Found {results.UnusedItems.Count} unused code items:\n");

        var grouped = results.UnusedItems.GroupBy(u => u.Kind);
        foreach (var group in grouped)
        {
            output.AppendLine($"**{group.Key}** ({group.Count()}):");
            foreach (var item in group.Take(20))
            {
                output.AppendLine($"  - {item.Name} @ {item.FileName}:{item.LineNumber}");
            }
            if (group.Count() > 20)
                output.AppendLine($"    ... and {group.Count() - 20} more");
            output.AppendLine();
        }

        return output.ToString();
    }

    private static string FormatUnusedCodeDetailed(UnusedCodeResults results)
    {
        if (!results.UnusedItems.Any())
            return "No unused code found.";

        var output = new StringBuilder();
        output.AppendLine($"# Unused Code Analysis");
        output.AppendLine($"Total: {results.UnusedItems.Count} items\n");

        foreach (var item in results.UnusedItems)
        {
            output.AppendLine($"## {item.Kind}: {item.FullName}");
            output.AppendLine($"  File: {item.FilePath}:{item.LineNumber}");
            output.AppendLine($"  Accessibility: {item.Accessibility}");
            output.AppendLine($"  Reason: {item.Reason}");
            output.AppendLine();
        }

        return output.ToString();
    }

    private static string FormatDuplicateCodeSummary(DuplicateCodeResults results)
    {
        var output = new StringBuilder();
        output.AppendLine("Duplicate Code Analysis Summary:");
        output.AppendLine($"  Duplicate blocks: {results.TotalDuplicateBlocks}");
        output.AppendLine($"  Total instances: {results.TotalDuplicateInstances}");
        output.AppendLine($"  High similarity (95%+): {results.HighSimilarityCount}");
        output.AppendLine($"  Medium similarity (85-94%): {results.MediumSimilarityCount}");
        return output.ToString();
    }

    private static string FormatDuplicateCodeNormal(DuplicateCodeResults results)
    {
        if (!results.DuplicateBlocks.Any())
            return "No duplicate code found.";

        var output = new StringBuilder();
        output.AppendLine($"Found {results.TotalDuplicateBlocks} duplicate code blocks:\n");

        foreach (var block in results.DuplicateBlocks.OrderByDescending(b => b.SimilarityPercentage).Take(20))
        {
            output.AppendLine($"**Group {block.GroupId}** ({block.SimilarityPercentage}% similarity, {block.LineCount} lines):");
            foreach (var instance in block.Instances)
            {
                output.AppendLine($"  - {instance.FileName}:{instance.StartLine}-{instance.EndLine} ({instance.MethodName})");
            }
            output.AppendLine();
        }

        return output.ToString();
    }

    private static string FormatDuplicateCodeDetailed(DuplicateCodeResults results)
    {
        if (!results.DuplicateBlocks.Any())
            return "No duplicate code found.";

        var output = new StringBuilder();
        output.AppendLine($"# Duplicate Code Analysis");
        output.AppendLine($"Total blocks: {results.TotalDuplicateBlocks}\n");

        foreach (var block in results.DuplicateBlocks.OrderByDescending(b => b.SimilarityPercentage))
        {
            output.AppendLine($"## Group {block.GroupId}");
            output.AppendLine($"Similarity: {block.SimilarityPercentage}% | Lines: {block.LineCount}");
            output.AppendLine("Instances:");
            foreach (var instance in block.Instances)
            {
                output.AppendLine($"  - {instance.FilePath}:{instance.StartLine}-{instance.EndLine}");
                output.AppendLine($"    Method: {instance.MethodName}");
                if (!string.IsNullOrEmpty(instance.CodeSnippet))
                    output.AppendLine($"    Preview: {instance.CodeSnippet.Substring(0, Math.Min(100, instance.CodeSnippet.Length))}...");
            }
            output.AppendLine();
        }

        return output.ToString();
    }

    private static string FormatMagicNumberResults(MagicNumberResults results, string format)
    {
        if (!results.MagicNumbers.Any())
            return "No magic numbers found.";

        var output = new StringBuilder();

        if (format == "summary")
        {
            output.AppendLine("Magic Number Analysis Summary:");
            output.AppendLine($"  Total: {results.TotalMagicNumbers}");
            output.AppendLine($"  Numeric literals: {results.NumericLiterals}");
            output.AppendLine($"  String literals: {results.StringLiterals}");
            return output.ToString();
        }

        output.AppendLine($"Found {results.TotalMagicNumbers} magic numbers:\n");

        var grouped = results.MagicNumbers.GroupBy(m => m.Type);
        foreach (var group in grouped)
        {
            output.AppendLine($"**{group.Key}** ({group.Count()}):");
            foreach (var magic in group.Take(format == "detailed" ? 50 : 10))
            {
                output.AppendLine($"  - `{magic.Value}` @ {magic.FileName}:{magic.LineNumber}");
                if (format == "detailed" && !string.IsNullOrEmpty(magic.SuggestedConstantName))
                    output.AppendLine($"    Suggested: {magic.SuggestedConstantName}");
            }
            if (group.Count() > (format == "detailed" ? 50 : 10))
                output.AppendLine($"    ... and {group.Count() - (format == "detailed" ? 50 : 10)} more");
            output.AppendLine();
        }

        return output.ToString();
    }

    private static string FormatNamingConventionsSummary(NamingConventionResults results)
    {
        var output = new StringBuilder();
        output.AppendLine("Naming Convention Analysis Summary:");
        output.AppendLine($"  Analyzed symbols: {results.AnalyzedSymbols}");
        output.AppendLine($"  Total violations: {results.TotalViolations}");
        output.AppendLine($"  Compliance score: {results.ComplianceScore:F1}%");
        output.AppendLine($"  High severity: {results.HighSeverityViolations}");
        output.AppendLine($"  Medium severity: {results.MediumSeverityViolations}");
        output.AppendLine($"  Low severity: {results.LowSeverityViolations}");
        return output.ToString();
    }

    private static string FormatNamingConventionsNormal(NamingConventionResults results)
    {
        if (!results.Violations.Any())
            return $"No naming convention violations found. Compliance: {results.ComplianceScore:F1}%";

        var output = new StringBuilder();
        output.AppendLine($"Found {results.TotalViolations} naming violations (Compliance: {results.ComplianceScore:F1}%):\n");

        var grouped = results.Violations.GroupBy(v => v.ViolationType);
        foreach (var group in grouped)
        {
            output.AppendLine($"**{group.Key}** ({group.Count()}):");
            foreach (var violation in group.Take(10))
            {
                output.AppendLine($"  - [{violation.Severity}] {violation.CurrentName} → {violation.SuggestedName}");
                output.AppendLine($"    @ {violation.FileName}:{violation.LineNumber}");
            }
            if (group.Count() > 10)
                output.AppendLine($"    ... and {group.Count() - 10} more");
            output.AppendLine();
        }

        return output.ToString();
    }

    private static string FormatNamingConventionsDetailed(NamingConventionResults results)
    {
        if (!results.Violations.Any())
            return $"No naming convention violations found. Compliance: {results.ComplianceScore:F1}%";

        var output = new StringBuilder();
        output.AppendLine($"# Naming Convention Analysis");
        output.AppendLine($"Compliance: {results.ComplianceScore:F1}%");
        output.AppendLine($"Total violations: {results.TotalViolations}\n");

        foreach (var violation in results.Violations.OrderBy(v => v.Severity).ThenBy(v => v.ViolationType))
        {
            output.AppendLine($"## {violation.ViolationType}: {violation.CurrentName}");
            output.AppendLine($"  Severity: {violation.Severity}");
            output.AppendLine($"  Suggested: {violation.SuggestedName}");
            output.AppendLine($"  Expected: {violation.ExpectedConvention}");
            output.AppendLine($"  File: {violation.FilePath}:{violation.LineNumber}");
            output.AppendLine($"  Reason: {violation.Reason}");
            output.AppendLine();
        }

        return output.ToString();
    }

    #endregion
}
