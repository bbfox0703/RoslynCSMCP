using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcpServer.Core.Models;
using RoslynMcpServer.Core.Services;
using System.ComponentModel;
using System.Text;

namespace RoslynMcpServer.Refactoring.Tools;

/// <summary>
/// MCP Tools for C# refactoring.
/// This module provides 4 tools (~700 tokens) for refactoring operations.
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
        IServiceProvider? serviceProvider = null)
    {
        try
        {
            var phase1Service = serviceProvider?.GetService<Phase1AnalysisService>();
            if (phase1Service == null)
            {
                return "Error: Phase1AnalysisService not available.";
            }

            var results = await phase1Service.RenameSymbolAsync(solutionPath, symbolName, newName, previewOnly);
            return FormatRenameResults(results);
        }
        catch (Exception ex)
        {
            var logger = serviceProvider?.GetService<ILogger<RefactoringTools>>();
            logger?.LogError(ex, "Error renaming symbol");
            return $"Error: An unexpected error occurred while renaming symbol: {ex.Message}";
        }
    }

    [McpServerTool, Description("Extract interface from a class for better testability and SOLID principles")]
    public static async Task<string> ExtractInterface(
        [Description("Class name to extract interface from")] string className,
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Interface name to generate (default: I{ClassName})")] string? interfaceName = null,
        [Description("Target namespace for the interface (default: same as class)")] string? targetNamespace = null,
        IServiceProvider? serviceProvider = null)
    {
        try
        {
            var phase2Service = serviceProvider?.GetService<Phase2AnalysisService>();
            if (phase2Service == null)
            {
                return "Error: Phase2AnalysisService not available.";
            }

            var results = await phase2Service.ExtractInterfaceAsync(solutionPath, className, interfaceName, targetNamespace);
            return FormatInterfaceExtractionResults(results);
        }
        catch (Exception ex)
        {
            var logger = serviceProvider?.GetService<ILogger<RefactoringTools>>();
            logger?.LogError(ex, "Error extracting interface");
            return $"Error: An unexpected error occurred while extracting interface: {ex.Message}";
        }
    }

    [McpServerTool, Description("Analyze impact of changing a symbol - identify all dependent code, assess risk, and get recommendations before refactoring")]
    public static async Task<string> GetChangeImpact(
        [Description("Symbol name to analyze impact for")] string symbolName,
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary (key metrics), normal (balanced), detailed (comprehensive). Default: normal")]
        string format = "normal",
        [Description("Maximum depth for indirect reference analysis (default: 3)")] int maxDepth = 3,
        IServiceProvider? serviceProvider = null)
    {
        try
        {
            var validator = serviceProvider?.GetService<SecurityValidator>();
            if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
            {
                return "Error: Invalid solution path provided.";
            }

            var analyzer = serviceProvider?.GetService<ChangeImpactAnalyzer>();
            if (analyzer == null)
            {
                return "Error: Change impact analyzer service not available.";
            }

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
            var logger = serviceProvider?.GetService<ILogger<RefactoringTools>>();
            logger?.LogError(ex, "Error analyzing change impact");
            return $"Error: An unexpected error occurred while analyzing change impact: {ex.Message}";
        }
    }

    [McpServerTool, Description("Analyze architecture layer violations based on defined rules (Clean Architecture, DDD, etc.)")]
    public static async Task<string> AnalyzeLayerViolations(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("JSON string defining layers and rules")] string layerDefinitionsJson,
        [Description("Output format: summary (counts only), normal (grouped list), detailed (with recommendations). Default: normal")]
        string format = "normal",
        IServiceProvider? serviceProvider = null)
    {
        try
        {
            var phase1Service = serviceProvider?.GetService<Phase1AnalysisService>();
            if (phase1Service == null)
            {
                return "Error: Phase1AnalysisService not available.";
            }

            var results = await phase1Service.AnalyzeLayerViolationsAsync(solutionPath, layerDefinitionsJson);
            return FormatLayerViolationResults(results, format);
        }
        catch (Exception ex)
        {
            var logger = serviceProvider?.GetService<ILogger<RefactoringTools>>();
            logger?.LogError(ex, "Error analyzing layer violations");
            return $"Error: An unexpected error occurred while analyzing layer violations: {ex.Message}";
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

    #endregion
}
