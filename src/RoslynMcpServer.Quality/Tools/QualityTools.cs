using Microsoft.CodeAnalysis.CSharp.Syntax;
using ModelContextProtocol.Server;
using RoslynMcpServer.Core.Models;
using RoslynMcpServer.Core.Services;
using System.ComponentModel;
using System.Text;

namespace RoslynMcpServer.Quality.Tools;

/// <summary>
/// MCP Tools for C# code quality analysis.
/// This module provides 7 tools (~1,225 tokens) for analyzing code quality and concurrency.
/// </summary>
[McpServerToolType]
public class QualityTools
{
    #region Tool Methods

    [McpServerTool, Description("""
        Report ordinary methods (not constructors, accessors, or operators) whose cyclomatic complexity is at or
        above threshold: 1 plus one per branch, loop, catch, case label, switch-expression arm, when guard, ?:, ??,
        ?., &&, ||, and pattern and/or, with lambdas and local functions counted toward their enclosing method.
        Returns every match, grouped by file name and sorted by complexity, with class, method, and line. For method
        length and parameter counts, use FindCodeSmells.
        """)]
    public static async Task<string> AnalyzeCodeComplexity(
        [Description("Path to solution file")] string solutionPath,
        [Description("Minimum cyclomatic complexity for a method to be reported, inclusive; any positive integer (default: 5)")] int threshold = 5,
        CodeAnalysisService analysisService = null!,
        SecurityValidator validator = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            var pathError = validator.ValidateSolutionPath(solutionPath, errorHandler);
            if (pathError != null) return pathError;

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
            return errorHandler.HandleException(ex, "AnalyzeCodeComplexity");
        }
    }

    [McpServerTool, Description("""
        Run syntactic heuristics for ten code smells across all projects, including tests and generated code.
        Method-level smells examine ordinary methods only; DataClumps compares methods within one file, and
        SpeculativeGenerality flags every abstract class with abstract methods and every single-member interface
        without counting implementations. FeatureEnvy and MiddleMan are noisy. Returns findings grouped by smell
        type with severity, symbol, file, and line, up to 10 per type (100 in detailed).
        """)]
    public static async Task<string> FindCodeSmells(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary (severity counts only), normal (up to 10 findings per smell type), detailed (up to 100 per type, adding description and recommendation). Default: normal")]
        string format = "normal",
        [Description("Comma-separated, case-insensitive: LongMethod (20+ lines), LargeClass (300+ lines or 20+ members), LongParameterList (4+ parameters), FeatureEnvy, DataClumps (same 3+ parameter types on 2+ methods in a file), PrimitiveObsession (3+ parameters of one primitive type), SwitchStatements (5+ sections), SpeculativeGenerality, MessageChains (3+ dots), MiddleMan. Default: all")]
        string smellTypes = "all",
        [Description("Return only findings of exactly this severity (case-insensitive): High, Medium, Low, or All (default: All)")] string severity = "All",
        Phase1AnalysisService phase1Service = null!,
        SecurityValidator validator = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            var pathError = validator.ValidateSolutionPath(solutionPath, errorHandler);
            if (pathError != null) return pathError;

            var smellTypeArray = smellTypes.Equals("all", StringComparison.OrdinalIgnoreCase)
                ? new[] { "LongMethod", "LargeClass", "LongParameterList", "FeatureEnvy", "DataClumps", "PrimitiveObsession", "SwitchStatements", "SpeculativeGenerality", "MessageChains", "MiddleMan" }
                : smellTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var results = await phase1Service.FindCodeSmellsAsync(solutionPath, smellTypeArray, severity);

            return FormatCodeSmellResults(results, format.ToLowerInvariant());
        }
        catch (Exception ex)
        {
            return errorHandler.HandleException(ex, "FindCodeSmells");
        }
    }

    [McpServerTool, Description("""
        Find types, methods, properties, fields, and events with no reference in the solution other than their own
        declaration; references from test projects count as usage. Skips static Main, test methods, overrides,
        explicit interface implementations, instance constructors, Program and Startup types, and
        serialization-attributed symbols. Code used only through reflection, framework conventions (controller
        actions, deserialized properties), implicit calls (foreach GetEnumerator, Deconstruct), or callers outside
        the solution is still reported. Returns items grouped by kind, up to 20 per kind.
        """)]
    public static async Task<string> FindUnusedCode(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary (counts only), normal (grouped list), detailed (full information). Default: normal")]
        string format = "normal",
        [Description("Declared accessibility to analyze: private, internal, public, or all (those three together); protected members are never analyzed. Default: all")]
        string scope = "all",
        [Description("Also report unused symbols declared in test projects (name contains Test or Spec); references from test projects count as usage either way (default: false)")] bool includeTests = false,
        UnusedCodeAnalyzer analyzer = null!,
        SecurityValidator validator = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            var pathError = validator.ValidateSolutionPath(solutionPath, errorHandler);
            if (pathError != null) return pathError;

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
            return errorHandler.HandleException(ex, "FindUnusedCode");
        }
    }

    [McpServerTool, Description("""
        Find methods whose bodies are copies or near-copies. Bodies are compared as token sequences with comments,
        whitespace, literal values, and names declared inside the method (parameters, locals, loop, catch, and lambda
        variables) normalized away, so a copy with a different method name, signature, or local names still matches;
        called methods, members, and types must agree. Similarity is 2 x LCS / (tokens of both bodies), and each
        group collects the methods at or above the threshold around its largest member. Duplicated fragments inside
        otherwise different methods are not detected. Returns up to 20 groups, most similar first, with file, line
        range, and method name per instance.
        """)]
    public static async Task<string> FindDuplicateCode(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary (counts only), normal (grouped list), detailed (full information). Default: normal")]
        string format = "normal",
        [Description("Minimum line span of a method's whole declaration for it to be compared; values below 3 are replaced with 5 (default: 5)")] int minLines = 5,
        [Description("Minimum body similarity in percent, 70-100; 100 finds only exact copies after normalization, and values outside the range are replaced with 90 (default: 90)")] int similarity = 90,
        DuplicateCodeAnalyzer analyzer = null!,
        SecurityValidator validator = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            var pathError = validator.ValidateSolutionPath(solutionPath, errorHandler);
            if (pathError != null) return pathError;

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
            return errorHandler.HandleException(ex, "FindDuplicateCode");
        }
    }

    [McpServerTool, Description("Inventory numeric and string literals that could be extracted as named constants, skipping common values such as 0 and 1. Broad by design: every remaining literal is reported, including strings of at least minStringLength characters. For fewer, higher-signal results on numeric literals only, classified by how each is used and with severities, use AnalyzeMagicNumbers.")]
    public static async Task<string> FindMagicNumbers(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary (counts only), normal (grouped list), detailed (with suggestions). Default: normal")]
        string format = "normal",
        [Description("Include string literals (default: true)")] bool includeStrings = true,
        [Description("Include numeric literals (default: true)")] bool includeNumbers = true,
        [Description("Minimum string length to consider (default: 3)")] int minStringLength = 3,
        Phase1AnalysisService phase1Service = null!,
        SecurityValidator validator = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            var pathError = validator.ValidateSolutionPath(solutionPath, errorHandler);
            if (pathError != null) return pathError;

            var results = await phase1Service.FindMagicNumbersAsync(
                solutionPath,
                includeStrings,
                includeNumbers,
                minStringLength);

            return FormatMagicNumberResults(results, format.ToLowerInvariant());
        }
        catch (Exception ex)
        {
            return errorHandler.HandleException(ex, "FindMagicNumbers");
        }
    }

    [McpServerTool, Description("""
        Check declared names against fixed rules (.editorconfig is ignored): PascalCase without underscores for
        types, methods, properties, and public or internal fields; an I or T prefix for interfaces and type
        parameters (a bare T is flagged); _camelCase for private and protected fields; camelCase for parameters.
        Test and generated code are included, so underscore-style test names are reported. Returns a compliance
        score and violations grouped by type, up to 10 per type, each with a suggested name and location.
        """)]
    public static async Task<string> AnalyzeNamingConventions(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary (key metrics), normal (balanced), detailed (comprehensive). Default: normal")]
        string format = "normal",
        [Description("Comma-separated, case-sensitive: InterfaceNaming, TypeNaming, MethodNaming, PropertyNaming, FieldNaming, ParameterNaming, TypeParameterNaming, or all. FieldNaming results are reported as ConstantNaming, PrivateFieldNaming, or PublicFieldNaming. Default: all")]
        string? violationTypes = null,
        [Description("Declared accessibility to check: all; public (public only); internal (internal and public). public and internal skip parameters, type parameters, and private or protected members. Default: all")]
        string scope = "all",
        NamingConventionAnalyzer analyzer = null!,
        SecurityValidator validator = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            var pathError = validator.ValidateSolutionPath(solutionPath, errorHandler);
            if (pathError != null) return pathError;

            string[]? violationTypesArray = null;
            if (!string.IsNullOrWhiteSpace(violationTypes) && !violationTypes.Equals("all", StringComparison.OrdinalIgnoreCase))
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
            return errorHandler.HandleException(ex, "AnalyzeNamingConventions");
        }
    }

    #endregion

    #region Helper Methods

    private static int CalculateCyclomaticComplexity(MethodDeclarationSyntax method)
        => ComplexityCalculator.CalculateCyclomatic(method);

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
            return "No code smells detected.".WithWarnings(results.Warnings);

        var output = new StringBuilder();

        if (format == "summary")
        {
            output.AppendLine($"Code Smell Analysis Summary:");
            output.AppendLine($"  Total smells: {results.TotalSmells}");
            output.AppendLine($"  High severity: {results.HighSeverity}");
            output.AppendLine($"  Medium severity: {results.MediumSeverity}");
            output.AppendLine($"  Low severity: {results.LowSeverity}");
            return output.AppendWarnings(results.Warnings).ToString();
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

        return output.AppendWarnings(results.Warnings).ToString();
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
        return output.AppendWarnings(results.Warnings).ToString();
    }

    private static string FormatUnusedCodeNormal(UnusedCodeResults results)
    {
        if (!results.UnusedItems.Any())
            return "No unused code found.".WithWarnings(results.Warnings);

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

        return output.AppendWarnings(results.Warnings).ToString();
    }

    private static string FormatUnusedCodeDetailed(UnusedCodeResults results)
    {
        if (!results.UnusedItems.Any())
            return "No unused code found.".WithWarnings(results.Warnings);

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

        return output.AppendWarnings(results.Warnings).ToString();
    }

    private static string FormatDuplicateCodeSummary(DuplicateCodeResults results)
    {
        var output = new StringBuilder();
        output.AppendLine("Duplicate Code Analysis Summary:");
        output.AppendLine($"  Duplicate blocks: {results.TotalDuplicateBlocks}");
        output.AppendLine($"  Total instances: {results.TotalDuplicateInstances}");
        output.AppendLine($"  High similarity (95%+): {results.HighSimilarityCount}");
        output.AppendLine($"  Medium similarity (85-94%): {results.MediumSimilarityCount}");
        return output.AppendWarnings(results.Warnings).ToString();
    }

    private static string FormatDuplicateCodeNormal(DuplicateCodeResults results)
    {
        if (!results.DuplicateBlocks.Any())
            return "No duplicate code found.".WithWarnings(results.Warnings);

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

        return output.AppendWarnings(results.Warnings).ToString();
    }

    private static string FormatDuplicateCodeDetailed(DuplicateCodeResults results)
    {
        if (!results.DuplicateBlocks.Any())
            return "No duplicate code found.".WithWarnings(results.Warnings);

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

        return output.AppendWarnings(results.Warnings).ToString();
    }

    private static string FormatMagicNumberResults(MagicNumberResults results, string format)
    {
        if (!results.MagicNumbers.Any())
            return "No magic numbers found.".WithWarnings(results.Warnings);

        var output = new StringBuilder();

        if (format == "summary")
        {
            output.AppendLine("Magic Number Analysis Summary:");
            output.AppendLine($"  Total: {results.TotalMagicNumbers}");
            output.AppendLine($"  Numeric literals: {results.NumericLiterals}");
            output.AppendLine($"  String literals: {results.StringLiterals}");
            return output.AppendWarnings(results.Warnings).ToString();
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

        return output.AppendWarnings(results.Warnings).ToString();
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
        return output.AppendWarnings(results.Warnings).ToString();
    }

    private static string FormatNamingConventionsNormal(NamingConventionResults results)
    {
        if (!results.Violations.Any())
            return $"No naming convention violations found. Compliance: {results.ComplianceScore:F1}%".WithWarnings(results.Warnings);

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

        return output.AppendWarnings(results.Warnings).ToString();
    }

    private static string FormatNamingConventionsDetailed(NamingConventionResults results)
    {
        if (!results.Violations.Any())
            return $"No naming convention violations found. Compliance: {results.ComplianceScore:F1}%".WithWarnings(results.Warnings);

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

        return output.AppendWarnings(results.Warnings).ToString();
    }

    [McpServerTool, Description("""
        Analyze concurrency anti-patterns in C# code for correctness and performance.
        Detects: await missing .ConfigureAwait(false) (library deadlock risk),
        System.Threading.Timer not stopped in Dispose (callback-after-dispose),
        Task.WhenAll with unbounded .Select() without SemaphoreSlim throttling,
        public async methods missing CancellationToken parameter,
        CancellationToken received but not propagated to inner async calls,
        static List<T>/Dictionary<K,V> used without thread-safe alternatives,
        mutable static primitive fields without volatile/Interlocked/ThreadStatic,
        and await expressions inside lock() blocks (use SemaphoreSlim instead).
        Does not analyze general shared-state data races beyond static collections and fields.
        """)]
    public static async Task<string> AnalyzeConcurrencyPatterns(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary, normal, detailed. Default: normal")] string format = "normal",
        [Description("Issue types (comma-separated): ConfigureAwait, TimerDispose, UnboundedConcurrency, MissingCancellationToken, CancellationNotPropagated, NonThreadSafeCollection, StaticFieldLock, AwaitInLock, all. Default: all")] string issueTypes = "all",
        [Description("Minimum severity to report: Critical, High, Medium, Low, all. Default: all")] string severity = "all",
        ConcurrencyPatternAnalyzer analyzer = null!,
        SecurityValidator validator = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            var pathError = validator.ValidateSolutionPath(solutionPath, errorHandler);
            if (pathError != null) return pathError;

            var issueTypeArray = issueTypes.Equals("all", StringComparison.OrdinalIgnoreCase)
                ? new[] { "ConfigureAwait", "TimerDispose", "UnboundedConcurrency", "MissingCancellationToken",
                          "CancellationNotPropagated", "NonThreadSafeCollection", "StaticFieldLock", "AwaitInLock" }
                : issueTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var results = await analyzer.AnalyzeAsync(solutionPath, issueTypeArray);

            // Apply severity filter
            if (!severity.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                var severityOrder = new[] { "Critical", "High", "Medium", "Low" };
                var minIdx = Array.FindIndex(severityOrder, s => s.Equals(severity, StringComparison.OrdinalIgnoreCase));
                if (minIdx >= 0)
                {
                    results.Issues = results.Issues
                        .Where(i => Array.FindIndex(severityOrder, s => s == i.Severity) <= minIdx)
                        .ToList();
                }
            }

            return format.ToLowerInvariant() switch
            {
                "summary" => FormatConcurrencySummary(results),
                "detailed" => FormatConcurrencyDetailed(results),
                _ => FormatConcurrencyNormal(results)
            };
        }
        catch (Exception ex)
        {
            return errorHandler.HandleException(ex, "AnalyzeConcurrencyPatterns");
        }
    }

    private static int SeverityOrder(string severity) => severity switch
    {
        "Critical" => 0,
        "High" => 1,
        "Medium" => 2,
        "Low" => 3,
        _ => 99
    };

    private static string FormatConcurrencySummary(ConcurrencyAnalysisResults results)
    {
        var output = new StringBuilder();
        output.AppendLine("Concurrency Pattern Analysis — Summary");
        output.AppendLine($"  Analyzed projects : {results.AnalyzedProjects}");
        output.AppendLine($"  Analyzed files    : {results.AnalyzedFiles}");
        if (results.FailedProjects > 0)
            output.AppendLine($"  Failed projects   : {results.FailedProjects}");
        output.AppendLine();
        output.AppendLine("Issues by severity:");
        output.AppendLine($"  Critical : {results.CriticalIssues}");
        output.AppendLine($"  High     : {results.HighIssues}");
        output.AppendLine($"  Medium   : {results.MediumIssues}");
        output.AppendLine($"  Low      : {results.LowIssues}");
        output.AppendLine($"  Total    : {results.TotalIssues}");
        output.AppendLine();

        if (results.IssuesByType.Count > 0)
        {
            output.AppendLine("Issues by type:");
            foreach (var (type, count) in results.IssuesByType.OrderByDescending(kv => kv.Value))
                output.AppendLine($"  {type,-26}: {count}");
            output.AppendLine();
        }

        var score = Math.Max(0, 100 - results.CriticalIssues * 10 - results.HighIssues * 5
                                     - results.MediumIssues * 2 - results.LowIssues);
        output.AppendLine($"Concurrency Health Score: {score}/100");
        if (score == 100) output.AppendLine("  Excellent — no concurrency issues detected.");
        else if (score >= 75) output.AppendLine("  Good — minor concurrency improvements possible.");
        else if (score >= 50) output.AppendLine("  Fair — several concurrency issues to address.");
        else output.AppendLine("  Poor — significant concurrency risks requiring attention.");

        output.AppendWarnings(results.Warnings);

        return output.ToString();
    }

    private static string FormatConcurrencyNormal(ConcurrencyAnalysisResults results)
    {
        if (results.TotalIssues == 0)
        {
            var ok = new StringBuilder();
            ok.AppendLine("No concurrency issues found.");
            ok.AppendLine($"Analyzed {results.AnalyzedProjects} project(s), {results.AnalyzedFiles} file(s).");
            return ok.AppendWarnings(results.Warnings).ToString();
        }

        var output = new StringBuilder();
        output.AppendLine($"Found {results.TotalIssues} concurrency issue(s) " +
                          $"(Critical:{results.CriticalIssues} High:{results.HighIssues} " +
                          $"Medium:{results.MediumIssues} Low:{results.LowIssues})");
        output.AppendLine();

        var grouped = results.Issues.GroupBy(i => i.IssueType);
        foreach (var group in grouped.OrderBy(g => g.Key))
        {
            output.AppendLine($"**{group.Key}** ({group.Count()}):");
            foreach (var issue in group.OrderBy(i => SeverityOrder(i.Severity)).Take(10))
            {
                output.AppendLine($"  - [{issue.Severity}] {issue.Title}");
                output.AppendLine($"    @ {issue.FileName}:{issue.LineNumber}  ({issue.ProjectName})");
                if (!string.IsNullOrEmpty(issue.Recommendation))
                    output.AppendLine($"    → {issue.Recommendation}");
            }
            if (group.Count() > 10)
                output.AppendLine($"  ... and {group.Count() - 10} more (use format=detailed to see all)");
            output.AppendLine();
        }

        output.AppendWarnings(results.Warnings);

        return output.ToString();
    }

    private static string FormatConcurrencyDetailed(ConcurrencyAnalysisResults results)
    {
        if (results.TotalIssues == 0)
            return $"No concurrency issues found. Analyzed {results.AnalyzedProjects} project(s).".WithWarnings(results.Warnings);

        var output = new StringBuilder();
        output.AppendLine("# Concurrency Pattern Analysis — Detailed Report");
        output.AppendLine();
        output.AppendLine($"Projects analyzed : {results.AnalyzedProjects}");
        output.AppendLine($"Files with issues : {results.AnalyzedFiles}");
        output.AppendLine($"Total issues      : {results.TotalIssues} " +
                          $"(C:{results.CriticalIssues} H:{results.HighIssues} " +
                          $"M:{results.MediumIssues} L:{results.LowIssues})");
        output.AppendLine();

        if (results.IssuesByProject.Count > 1)
        {
            output.AppendLine("## Issues by Project");
            foreach (var (proj, count) in results.IssuesByProject.OrderByDescending(kv => kv.Value))
                output.AppendLine($"  {proj}: {count}");
            output.AppendLine();
        }

        var grouped = results.Issues.GroupBy(i => i.IssueType).OrderBy(g => g.Key);
        foreach (var group in grouped)
        {
            output.AppendLine($"## {group.Key} ({group.Count()} issue(s))");
            output.AppendLine();

            foreach (var issue in group.OrderBy(i => SeverityOrder(i.Severity)))
            {
                output.AppendLine($"### [{issue.Severity}] {issue.Title}");
                output.AppendLine($"- **File**: `{issue.FilePath}:{issue.LineNumber}`");
                output.AppendLine($"- **Project**: {issue.ProjectName}");
                output.AppendLine($"- **Description**: {issue.Description}");
                if (!string.IsNullOrEmpty(issue.CodeSnippet))
                    output.AppendLine($"- **Code**: `{issue.CodeSnippet}`");
                output.AppendLine($"- **Recommendation**: {issue.Recommendation}");
                if (!string.IsNullOrEmpty(issue.FixExample))
                {
                    output.AppendLine("- **Fix Example**:");
                    output.AppendLine("  ```csharp");
                    foreach (var line in issue.FixExample.Split('\n'))
                        output.AppendLine($"  {line}");
                    output.AppendLine("  ```");
                }
                output.AppendLine();
            }
        }

        output.AppendWarnings(results.Warnings);

        return output.ToString();
    }

    [McpServerTool, Description("""
        Detect magic numbers (unexplained numeric literals) in C# code.
        Detects: array/indexer access with literal index > 1 outside loops (ArrayIndexLiteral),
        arithmetic expressions with literal offset > 3 (ArithmeticLiteral, e.g. base + 16),
        new T[N] or new List<T>(N) with hard-coded size > 4 (HardcodedCapacity),
        comparison operators with literal threshold > 10 (ComparisonLiteral, e.g. count > 42),
        and return statements that return a literal integer > 1 as an exit/error code (ReturnLiteral).
        Complements PInvoke's MagicOffset (unsafe pointer arithmetic) with general non-unsafe coverage.
        Numeric literals only; for a broad inventory that also covers string literals, use FindMagicNumbers.
        """)]
    public static async Task<string> AnalyzeMagicNumbers(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary, normal, detailed. Default: normal")] string format = "normal",
        [Description("Issue types (comma-separated): ArrayIndexLiteral, ArithmeticLiteral, HardcodedCapacity, ComparisonLiteral, ReturnLiteral, all. Default: all")] string issueTypes = "all",
        [Description("Minimum severity to report: Critical, High, Medium, Low, all. Default: all")] string severity = "all",
        MagicNumberAnalyzer analyzer = null!,
        SecurityValidator validator = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            var pathError = validator.ValidateSolutionPath(solutionPath, errorHandler);
            if (pathError != null) return pathError;

            var issueTypeArray = issueTypes.Equals("all", StringComparison.OrdinalIgnoreCase)
                ? new[] { "ArrayIndexLiteral", "ArithmeticLiteral", "HardcodedCapacity", "ComparisonLiteral", "ReturnLiteral" }
                : issueTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var results = await analyzer.AnalyzeAsync(solutionPath, issueTypeArray);

            if (!severity.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                var severityOrder = new[] { "Critical", "High", "Medium", "Low" };
                var minIdx = Array.FindIndex(severityOrder, s => s.Equals(severity, StringComparison.OrdinalIgnoreCase));
                if (minIdx >= 0)
                {
                    results.Issues = results.Issues
                        .Where(i => Array.FindIndex(severityOrder, s => s == i.Severity) <= minIdx)
                        .ToList();
                }
            }

            return format.ToLowerInvariant() switch
            {
                "summary" => FormatMagicSummary(results),
                "detailed" => FormatMagicDetailed(results),
                _ => FormatMagicNormal(results)
            };
        }
        catch (Exception ex)
        {
            return errorHandler.HandleException(ex, "AnalyzeMagicNumbers");
        }
    }

    #region Magic Number Formatting

    private static int MagicSeverityOrder(string severity) => severity switch
    {
        "Critical" => 0,
        "High" => 1,
        "Medium" => 2,
        "Low" => 3,
        _ => 99
    };

    private static string FormatMagicSummary(MagicNumberAnalysisResults results)
    {
        var output = new StringBuilder();
        output.AppendLine("Magic Number Analysis — Summary");
        output.AppendLine($"  Analyzed projects : {results.AnalyzedProjects}");
        output.AppendLine($"  Analyzed files    : {results.AnalyzedFiles}");
        if (results.FailedProjects > 0)
            output.AppendLine($"  Failed projects   : {results.FailedProjects}");
        output.AppendLine();
        output.AppendLine("Issues by severity:");
        output.AppendLine($"  Critical : {results.CriticalIssues}");
        output.AppendLine($"  High     : {results.HighIssues}");
        output.AppendLine($"  Medium   : {results.MediumIssues}");
        output.AppendLine($"  Low      : {results.LowIssues}");
        output.AppendLine($"  Total    : {results.TotalIssues}");
        output.AppendLine();

        if (results.IssuesByType.Count > 0)
        {
            output.AppendLine("Issues by type:");
            foreach (var (type, count) in results.IssuesByType.OrderByDescending(kv => kv.Value))
                output.AppendLine($"  {type,-22}: {count}");
            output.AppendLine();
        }

        var score = Math.Max(0, 100 - results.HighIssues * 5 - results.MediumIssues * 2 - results.LowIssues);
        output.AppendLine($"Maintainability Score: {score}/100");
        if (score == 100) output.AppendLine("  Excellent — no magic numbers detected.");
        else if (score >= 75) output.AppendLine("  Good — minor naming improvements possible.");
        else if (score >= 50) output.AppendLine("  Fair — several magic numbers to address.");
        else output.AppendLine("  Poor — many magic numbers reduce maintainability.");

        output.AppendWarnings(results.Warnings);

        return output.ToString();
    }

    private static string FormatMagicNormal(MagicNumberAnalysisResults results)
    {
        if (results.TotalIssues == 0)
        {
            var ok = new StringBuilder();
            ok.AppendLine("No magic numbers found.");
            ok.AppendLine($"Analyzed {results.AnalyzedProjects} project(s), {results.AnalyzedFiles} file(s).");
            return ok.AppendWarnings(results.Warnings).ToString();
        }

        var output = new StringBuilder();
        output.AppendLine($"Found {results.TotalIssues} magic number(s) " +
                          $"(High:{results.HighIssues} Medium:{results.MediumIssues} Low:{results.LowIssues})");
        output.AppendLine();

        var grouped = results.Issues.GroupBy(i => i.IssueType);
        foreach (var group in grouped.OrderBy(g => g.Key))
        {
            output.AppendLine($"**{group.Key}** ({group.Count()}):");
            foreach (var issue in group.OrderBy(i => MagicSeverityOrder(i.Severity)).Take(10))
            {
                output.AppendLine($"  - [{issue.Severity}] {issue.Title}");
                output.AppendLine($"    @ {issue.FileName}:{issue.LineNumber}  ({issue.ProjectName})");
                if (!string.IsNullOrEmpty(issue.Recommendation))
                    output.AppendLine($"    → {issue.Recommendation}");
            }
            if (group.Count() > 10)
                output.AppendLine($"  ... and {group.Count() - 10} more (use format=detailed to see all)");
            output.AppendLine();
        }

        output.AppendWarnings(results.Warnings);

        return output.ToString();
    }

    private static string FormatMagicDetailed(MagicNumberAnalysisResults results)
    {
        if (results.TotalIssues == 0)
            return $"No magic numbers found. Analyzed {results.AnalyzedProjects} project(s).".WithWarnings(results.Warnings);

        var output = new StringBuilder();
        output.AppendLine("# Magic Number Analysis — Detailed Report");
        output.AppendLine();
        output.AppendLine($"Projects analyzed : {results.AnalyzedProjects}");
        output.AppendLine($"Files with issues : {results.AnalyzedFiles}");
        output.AppendLine($"Total issues      : {results.TotalIssues} " +
                          $"(H:{results.HighIssues} M:{results.MediumIssues} L:{results.LowIssues})");
        output.AppendLine();

        if (results.IssuesByProject.Count > 1)
        {
            output.AppendLine("## Issues by Project");
            foreach (var (proj, count) in results.IssuesByProject.OrderByDescending(kv => kv.Value))
                output.AppendLine($"  {proj}: {count}");
            output.AppendLine();
        }

        var grouped = results.Issues.GroupBy(i => i.IssueType).OrderBy(g => g.Key);
        foreach (var group in grouped)
        {
            output.AppendLine($"## {group.Key} ({group.Count()} issue(s))");
            output.AppendLine();

            foreach (var issue in group.OrderBy(i => MagicSeverityOrder(i.Severity)))
            {
                output.AppendLine($"### [{issue.Severity}] {issue.Title}");
                output.AppendLine($"- **File**: `{issue.FilePath}:{issue.LineNumber}`");
                output.AppendLine($"- **Project**: {issue.ProjectName}");
                output.AppendLine($"- **Value**: `{issue.LiteralValue}`");
                output.AppendLine($"- **Description**: {issue.Description}");
                if (!string.IsNullOrEmpty(issue.CodeSnippet))
                    output.AppendLine($"- **Code**: `{issue.CodeSnippet}`");
                output.AppendLine($"- **Recommendation**: {issue.Recommendation}");
                output.AppendLine();
            }
        }

        output.AppendWarnings(results.Warnings);

        return output.ToString();
    }

    #endregion

    #endregion
}
