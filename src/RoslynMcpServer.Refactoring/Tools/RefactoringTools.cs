using ModelContextProtocol.Server;
using RoslynMcpServer.Core.Models;
using RoslynMcpServer.Core.Services;
using System.ComponentModel;
using System.Text;

namespace RoslynMcpServer.Refactoring.Tools;

/// <summary>
/// MCP Tools for C# refactoring.
/// This module provides 5 tools (~875 tokens) for refactoring and source generator migration.
/// </summary>
[McpServerToolType]
public class RefactoringTools
{
    [McpServerTool, Description("Safely rename a symbol with preview and conflict detection")]
    public static async Task<string> RenameSymbolSafely(
        [Description("Current symbol name to rename")] string symbolName,
        [Description("New name for the symbol")] string newName,
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Preview only (true) or execute rename (false). Default: true")] bool previewOnly = true,
        Phase1AnalysisService phase1Service = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            var results = await phase1Service.RenameSymbolAsync(solutionPath, symbolName, newName, previewOnly);
            return FormatRenameResults(results);
        }
        catch (Exception ex)
        {
            return errorHandler.HandleException(ex, "RenameSymbolSafely");
        }
    }

    [McpServerTool, Description("Extract interface from a class for better testability and SOLID principles")]
    public static async Task<string> ExtractInterface(
        [Description("Class name to extract interface from")] string className,
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Interface name to generate (default: I{ClassName})")] string? interfaceName = null,
        [Description("Target namespace for the interface (default: same as class)")] string? targetNamespace = null,
        Phase2AnalysisService phase2Service = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            var results = await phase2Service.ExtractInterfaceAsync(solutionPath, className, interfaceName, targetNamespace);
            return FormatInterfaceExtractionResults(results);
        }
        catch (Exception ex)
        {
            return errorHandler.HandleException(ex, "ExtractInterface");
        }
    }

    [McpServerTool, Description("Analyze impact of changing a symbol - identify all dependent code, assess risk, and get recommendations before refactoring")]
    public static async Task<string> GetChangeImpact(
        [Description("Symbol name to analyze impact for")] string symbolName,
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary (key metrics), normal (balanced), detailed (comprehensive). Default: normal")]
        string format = "normal",
        [Description("Maximum depth for indirect reference analysis (default: 3)")] int maxDepth = 3,
        ChangeImpactAnalyzer analyzer = null!,
        SecurityValidator validator = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            var pathError = validator.ValidateSolutionPath(solutionPath, errorHandler);
            if (pathError != null) return pathError;

            var results = await analyzer.AnalyzeChangeImpactAsync(symbolName, solutionPath, maxDepth);

            return format.ToLowerInvariant() switch
            {
                "summary" => FormatChangeImpactSummary(results),
                "detailed" => FormatChangeImpactDetailed(results),
                _ => FormatChangeImpactNormal(results)
            };
        }
        catch (Exception ex)
        {
            return errorHandler.HandleException(ex, "GetChangeImpact");
        }
    }

    [McpServerTool, Description("Analyze architecture layer violations based on defined rules (Clean Architecture, DDD, etc.)")]
    public static async Task<string> AnalyzeLayerViolations(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("JSON string defining layers and rules")] string layerDefinitionsJson,
        [Description("Output format: summary (counts only), normal (grouped list), detailed (with recommendations). Default: normal")]
        string format = "normal",
        Phase1AnalysisService phase1Service = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            var results = await phase1Service.AnalyzeLayerViolationsAsync(solutionPath, layerDefinitionsJson);
            return FormatLayerViolationResults(results, format);
        }
        catch (Exception ex)
        {
            return errorHandler.HandleException(ex, "AnalyzeLayerViolations");
        }
    }

    [McpServerTool, Description("""
        Find source generator migration opportunities to reduce boilerplate and improve performance/AOT compatibility.
        Detects: runtime Regex that can use [GeneratedRegex] (compile-time, AOT-safe),
        JsonSerializer calls without a JsonSerializerContext (add [JsonSerializable] for AOT/performance),
        manual INotifyPropertyChanged that can use [ObservableProperty] (CommunityToolkit.Mvvm),
        manual ICommand classes that can use [RelayCommand] (CommunityToolkit.Mvvm),
        and logger calls with string interpolation that should use [LoggerMessage] (zero-allocation logging).
        """)]
    public static async Task<string> FindSourceGeneratorOpportunities(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary, normal, detailed. Default: normal")] string format = "normal",
        [Description("Categories (comma-separated): GeneratedRegex, JsonSerializable, ObservableProperty, RelayCommand, LoggerMessage, all. Default: all")] string categories = "all",
        [Description("Minimum severity to report: High, Medium, Low, all. Default: all")] string severity = "all",
        SourceGeneratorOpportunityAnalyzer analyzer = null!,
        SecurityValidator validator = null!,
        McpErrorHandler errorHandler = null!)
    {
        try
        {
            var pathError = validator.ValidateSolutionPath(solutionPath, errorHandler);
            if (pathError != null) return pathError;

            var categoryArray = categories.Equals("all", StringComparison.OrdinalIgnoreCase)
                ? new[] { "GeneratedRegex", "JsonSerializable", "ObservableProperty", "RelayCommand", "LoggerMessage" }
                : categories.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var results = await analyzer.AnalyzeAsync(solutionPath, categoryArray);

            // Apply severity filter
            if (!severity.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                var severityOrder = new[] { "High", "Medium", "Low" };
                var minIdx = Array.FindIndex(severityOrder, s => s.Equals(severity, StringComparison.OrdinalIgnoreCase));
                if (minIdx >= 0)
                {
                    results.Opportunities = results.Opportunities
                        .Where(o => Array.FindIndex(severityOrder, s => s == o.Severity) <= minIdx)
                        .ToList();
                }
            }

            return format.ToLowerInvariant() switch
            {
                "summary" => FormatSrcGenSummary(results),
                "detailed" => FormatSrcGenDetailed(results),
                _ => FormatSrcGenNormal(results)
            };
        }
        catch (Exception ex)
        {
            return errorHandler.HandleException(ex, "FindSourceGeneratorOpportunities");
        }
    }

    #region Formatting Methods

    private static string FormatRenameResults(RenameSymbolResults results)
    {
        var output = new StringBuilder();

        if (!results.Success)
        {
            output.AppendLine($"Rename failed: {results.ErrorMessage}");
            return output.ToString();
        }

        output.AppendLine($"# Rename: {results.Target.CurrentName} -> {results.Target.NewName}");
        output.AppendLine($"Mode: {(results.IsPreview ? "Preview" : "Executed")}");
        output.AppendLine($"Risk Level: {results.RiskLevel}");
        output.AppendLine($"Files affected: {results.FilesAffected}");
        output.AppendLine($"Total locations: {results.TotalLocations}\n");

        if (results.HasConflicts)
        {
            output.AppendLine("## Conflicts:");
            foreach (var conflict in results.Conflicts)
                output.AppendLine($"  - {conflict}");
            output.AppendLine();
        }

        output.AppendLine("## File Changes:");
        foreach (var fileChange in results.FileChanges.Take(20))
        {
            output.AppendLine($"  {fileChange.FileName} ({fileChange.ChangeCount} changes)");
        }

        return output.ToString();
    }

    private static string FormatInterfaceExtractionResults(InterfaceExtractionResult results)
    {
        var output = new StringBuilder();

        if (!results.Success)
        {
            output.AppendLine($"Interface extraction failed: {results.ErrorMessage}");
            return output.ToString();
        }

        output.AppendLine($"# Extract Interface: {results.InterfaceName}");
        output.AppendLine($"From class: {results.ClassName}");
        output.AppendLine($"Namespace: {results.Namespace}");
        output.AppendLine($"Members: {results.SelectedMembers}/{results.TotalMembers}\n");

        output.AppendLine("## Generated Interface:");
        output.AppendLine("```csharp");
        output.AppendLine(results.InterfaceCode);
        output.AppendLine("```");

        if (!string.IsNullOrEmpty(results.SuggestedFilePath))
            output.AppendLine($"\nSuggested file: {results.SuggestedFilePath}");

        return output.ToString();
    }

    private static string FormatChangeImpactSummary(ChangeImpactResults results)
    {
        var output = new StringBuilder();
        output.AppendLine($"Change Impact Summary for '{results.TargetSymbol}':");
        output.AppendLine($"  Risk Level: {results.RiskLevel}");
        output.AppendLine($"  Direct references: {results.DirectReferences}");
        output.AppendLine($"  Indirect references: {results.IndirectReferences}");
        output.AppendLine($"  Impacted projects: {results.ImpactedProjects}");
        output.AppendLine($"  Impacted files: {results.ImpactedFiles}");
        return output.ToString();
    }

    private static string FormatChangeImpactNormal(ChangeImpactResults results)
    {
        var output = new StringBuilder();
        output.AppendLine($"# Change Impact: {results.TargetSymbol}");
        output.AppendLine($"Risk: {results.RiskLevel} | Direct: {results.DirectReferences} | Indirect: {results.IndirectReferences}\n");

        if (results.ImpactedProjectNames.Any())
        {
            output.AppendLine("## Impacted Projects:");
            foreach (var project in results.ImpactedProjectNames)
                output.AppendLine($"  - {project}");
            output.AppendLine();
        }

        if (results.Recommendations.Any())
        {
            output.AppendLine("## Recommendations:");
            foreach (var rec in results.Recommendations)
                output.AppendLine($"  - {rec}");
        }

        return output.ToString();
    }

    private static string FormatChangeImpactDetailed(ChangeImpactResults results)
    {
        var output = new StringBuilder();
        output.AppendLine($"# Change Impact Analysis: {results.TargetSymbol}");
        output.AppendLine($"Full name: {results.TargetSymbolFullName}");
        output.AppendLine($"Kind: {results.SymbolKind}");
        output.AppendLine($"Accessibility: {results.Accessibility}");
        output.AppendLine($"Risk Level: {results.RiskLevel}");
        output.AppendLine($"Public API: {results.IsPublicAPI}");
        output.AppendLine($"Breaking Change: {results.IsBreakingChange}\n");

        output.AppendLine($"## Impact Statistics:");
        output.AppendLine($"  Total impacted: {results.TotalImpactedSymbols}");
        output.AppendLine($"  Direct: {results.DirectReferences}");
        output.AppendLine($"  Indirect: {results.IndirectReferences}");
        output.AppendLine($"  Projects: {results.ImpactedProjects}");
        output.AppendLine($"  Files: {results.ImpactedFiles}\n");

        if (results.ImpactedSymbols.Any())
        {
            output.AppendLine("## Impacted Symbols:");
            foreach (var symbol in results.ImpactedSymbols.Take(50))
            {
                output.AppendLine($"  - {symbol.SymbolName} ({symbol.SymbolKind}) @ {symbol.FileName}:{symbol.LineNumber}");
            }
        }

        return output.ToString();
    }

    private static string FormatLayerViolationResults(LayerViolationResults results, string format)
    {
        if (!results.Violations.Any())
            return $"No layer violations found. Compliance: {results.ComplianceScore:F1}%";

        var output = new StringBuilder();

        if (format == "summary")
        {
            output.AppendLine("Layer Violation Summary:");
            output.AppendLine($"  Total violations: {results.TotalViolations}");
            output.AppendLine($"  Critical: {results.CriticalViolations}");
            output.AppendLine($"  Compliance: {results.ComplianceScore:F1}%");
            return output.ToString();
        }

        output.AppendLine($"# Layer Violation Analysis");
        output.AppendLine($"Total: {results.TotalViolations} violations (Compliance: {results.ComplianceScore:F1}%)\n");

        var grouped = results.Violations.GroupBy(v => v.ViolationType);
        foreach (var group in grouped)
        {
            output.AppendLine($"## {group.Key} ({group.Count()}):");
            foreach (var violation in group.Take(format == "detailed" ? 50 : 10))
            {
                output.AppendLine($"  - [{violation.Severity}] {violation.FromProject} -> {violation.ToProject}");
                if (format == "detailed")
                {
                    output.AppendLine($"    {violation.Description}");
                    output.AppendLine($"    Recommendation: {violation.Recommendation}");
                }
            }
            output.AppendLine();
        }

        return output.ToString();
    }

    private static int SrcGenSeverityOrder(string severity) => severity switch
    {
        "High" => 0,
        "Medium" => 1,
        "Low" => 2,
        _ => 99
    };

    private static string FormatSrcGenSummary(SourceGeneratorAnalysisResults results)
    {
        var output = new StringBuilder();
        output.AppendLine("Source Generator Opportunities — Summary");
        output.AppendLine($"  Analyzed projects : {results.AnalyzedProjects}");
        output.AppendLine($"  Analyzed files    : {results.AnalyzedFiles}");
        if (results.FailedProjects > 0)
            output.AppendLine($"  Failed projects   : {results.FailedProjects}");
        output.AppendLine();
        output.AppendLine("Opportunities by severity:");
        output.AppendLine($"  High   : {results.HighOpportunities}");
        output.AppendLine($"  Medium : {results.MediumOpportunities}");
        output.AppendLine($"  Low    : {results.LowOpportunities}");
        output.AppendLine($"  Total  : {results.TotalOpportunities}");
        output.AppendLine();

        if (results.OpportunitiesByCategory.Count > 0)
        {
            output.AppendLine("Opportunities by category:");
            foreach (var (cat, count) in results.OpportunitiesByCategory.OrderByDescending(kv => kv.Value))
                output.AppendLine($"  {cat,-22}: {count}");
            output.AppendLine();
        }

        // Modernization score: 100 - (opportunities × 2), minimum 0
        var score = Math.Max(0, 100 - results.TotalOpportunities * 2);
        output.AppendLine($"Modernization Score: {score}/100");
        if (score == 100) output.AppendLine("  Excellent — already using source generators where applicable.");
        else if (score >= 75) output.AppendLine("  Good — a few source generator opportunities remain.");
        else if (score >= 50) output.AppendLine("  Fair — meaningful boilerplate reduction possible.");
        else output.AppendLine("  Poor — significant source generator adoption opportunities.");

        if (results.Warnings.Count > 0)
        {
            output.AppendLine();
            foreach (var w in results.Warnings)
                output.AppendLine($"Warning: {w.Message}");
        }

        return output.ToString();
    }

    private static string FormatSrcGenNormal(SourceGeneratorAnalysisResults results)
    {
        if (results.TotalOpportunities == 0)
        {
            var ok = new StringBuilder();
            ok.AppendLine("No source generator opportunities found.");
            ok.AppendLine($"Analyzed {results.AnalyzedProjects} project(s), {results.AnalyzedFiles} file(s).");
            return ok.ToString();
        }

        var output = new StringBuilder();
        output.AppendLine($"Found {results.TotalOpportunities} source generator opportunity/ies " +
                          $"(High:{results.HighOpportunities} Medium:{results.MediumOpportunities} Low:{results.LowOpportunities})");
        output.AppendLine();

        var grouped = results.Opportunities.GroupBy(o => o.Category);
        foreach (var group in grouped.OrderBy(g => g.Key))
        {
            output.AppendLine($"**{group.Key}** ({group.Count()}):");
            foreach (var opp in group.OrderBy(o => SrcGenSeverityOrder(o.Severity)).Take(10))
            {
                output.AppendLine($"  - [{opp.Severity}] {opp.Title}");
                output.AppendLine($"    @ {opp.FileName}:{opp.LineNumber}  ({opp.ProjectName})");
                if (!string.IsNullOrEmpty(opp.Recommendation))
                    output.AppendLine($"    → {opp.Recommendation}");
            }
            if (group.Count() > 10)
                output.AppendLine($"  ... and {group.Count() - 10} more (use format=detailed to see all)");
            output.AppendLine();
        }

        if (results.Warnings.Count > 0)
            foreach (var w in results.Warnings)
                output.AppendLine($"Warning: {w.Message}");

        return output.ToString();
    }

    private static string FormatSrcGenDetailed(SourceGeneratorAnalysisResults results)
    {
        if (results.TotalOpportunities == 0)
            return $"No source generator opportunities found. Analyzed {results.AnalyzedProjects} project(s).";

        var output = new StringBuilder();
        output.AppendLine("# Source Generator Opportunities — Detailed Report");
        output.AppendLine();
        output.AppendLine($"Projects analyzed    : {results.AnalyzedProjects}");
        output.AppendLine($"Files with findings  : {results.AnalyzedFiles}");
        output.AppendLine($"Total opportunities  : {results.TotalOpportunities} " +
                          $"(H:{results.HighOpportunities} M:{results.MediumOpportunities} L:{results.LowOpportunities})");
        output.AppendLine();

        if (results.OpportunitiesByProject.Count > 1)
        {
            output.AppendLine("## Opportunities by Project");
            foreach (var (proj, count) in results.OpportunitiesByProject.OrderByDescending(kv => kv.Value))
                output.AppendLine($"  {proj}: {count}");
            output.AppendLine();
        }

        var grouped = results.Opportunities.GroupBy(o => o.Category).OrderBy(g => g.Key);
        foreach (var group in grouped)
        {
            output.AppendLine($"## {group.Key} ({group.Count()} opportunity/ies)");
            output.AppendLine();

            foreach (var opp in group.OrderBy(o => SrcGenSeverityOrder(o.Severity)))
            {
                output.AppendLine($"### [{opp.Severity}] {opp.Title}");
                output.AppendLine($"- **File**: `{opp.FilePath}:{opp.LineNumber}`");
                output.AppendLine($"- **Project**: {opp.ProjectName}");
                output.AppendLine($"- **Description**: {opp.Description}");
                if (!string.IsNullOrEmpty(opp.CodeSnippet))
                    output.AppendLine($"- **Code**: `{opp.CodeSnippet}`");
                output.AppendLine($"- **Recommendation**: {opp.Recommendation}");
                if (!string.IsNullOrEmpty(opp.FixExample))
                {
                    output.AppendLine("- **Fix Example**:");
                    output.AppendLine("  ```csharp");
                    foreach (var line in opp.FixExample.Split('\n'))
                        output.AppendLine($"  {line}");
                    output.AppendLine("  ```");
                }
                output.AppendLine();
            }
        }

        if (results.Warnings.Count > 0)
        {
            output.AppendLine("## Warnings");
            foreach (var w in results.Warnings)
                output.AppendLine($"- {w.Message}");
        }

        return output.ToString();
    }

    #endregion
}
