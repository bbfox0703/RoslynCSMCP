using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcpServer.Core.Models;
using RoslynMcpServer.Core.Services;
using System.ComponentModel;
using System.Text;

namespace RoslynMcpServer.Testing.Tools;

/// <summary>
/// MCP Tools for C# test analysis.
/// This module provides 2 tools (~350 tokens) for testing analysis.
/// </summary>
[McpServerToolType]
public class TestingTools
{
    [McpServerTool, Description("Find test classes and methods for a given type")]
    public static async Task<string> FindTestsForType(
        [Description("Type name to find tests for")] string typeName,
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary (counts only), normal (grouped list), detailed (full information). Default: normal")]
        string format = "normal",
        IServiceProvider? serviceProvider = null)
    {
        try
        {
            var validator = serviceProvider?.GetService<SecurityValidator>();
            if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
            {
                return "Error: Invalid solution path provided.";
            }

            var discoveryService = serviceProvider?.GetService<TestDiscoveryService>();
            if (discoveryService == null)
            {
                return "Error: Test discovery service not available.";
            }

            var results = await discoveryService.FindTestsForTypeAsync(solutionPath, typeName);

            return format.ToLowerInvariant() switch
            {
                "summary" => FormatTestResultsSummary(results, typeName),
                "detailed" => FormatTestResultsDetailed(results, typeName),
                _ => FormatTestResultsNormal(results, typeName)
            };
        }
        catch (Exception ex)
        {
            var logger = serviceProvider?.GetService<ILogger<TestingTools>>();
            logger?.LogError(ex, "Error finding tests for type");
            return $"Error: An unexpected error occurred while finding tests: {ex.Message}";
        }
    }

    [McpServerTool, Description("Analyze test coverage for all types in solution - identify untested code, coverage percentages, and high-risk areas")]
    public static async Task<string> GetTestCoverage(
        [Description("Path to solution file (.sln)")] string solutionPath,
        [Description("Output format: summary (key metrics), normal (balanced), detailed (comprehensive). Default: normal")]
        string format = "normal",
        [Description("Scope filter: public (public only), all (all types). Default: public")] string scope = "public",
        [Description("Group by: project, namespace. Default: project")] string groupBy = "project",
        IServiceProvider? serviceProvider = null)
    {
        try
        {
            var validator = serviceProvider?.GetService<SecurityValidator>();
            if (!validator?.ValidateSolutionPath(solutionPath) ?? false)
            {
                return "Error: Invalid solution path provided.";
            }

            var coverageAnalyzer = serviceProvider?.GetService<TestCoverageAnalyzer>();
            if (coverageAnalyzer == null)
            {
                return "Error: Test coverage analyzer service not available.";
            }

            var results = await coverageAnalyzer.AnalyzeTestCoverageAsync(solutionPath, scope, groupBy);

            return format.ToLowerInvariant() switch
            {
                "summary" => FormatTestCoverageSummary(results),
                "detailed" => FormatTestCoverageDetailed(results),
                _ => FormatTestCoverageNormal(results)
            };
        }
        catch (Exception ex)
        {
            var logger = serviceProvider?.GetService<ILogger<TestingTools>>();
            logger?.LogError(ex, "Error analyzing test coverage");
            return $"Error: An unexpected error occurred while analyzing test coverage: {ex.Message}";
        }
    }

    #region Formatting Methods

    private static string FormatTestResultsSummary(List<TestClassResult> results, string typeName)
    {
        if (!results.Any())
            return $"No tests found for '{typeName}'.";

        var totalTests = results.Sum(r => r.TestCount);
        return $"Found {results.Count} test classes with {totalTests} tests for '{typeName}'.";
    }

    private static string FormatTestResultsNormal(List<TestClassResult> results, string typeName)
    {
        if (!results.Any())
            return $"No tests found for '{typeName}'.";

        var output = new StringBuilder();
        output.AppendLine($"# Tests for '{typeName}'");
        output.AppendLine($"Found {results.Count} test classes:\n");

        foreach (var testClass in results)
        {
            output.AppendLine($"## {testClass.TestClassName}");
            output.AppendLine($"  Framework: {testClass.TestFramework}");
            output.AppendLine($"  File: {testClass.FileName}:{testClass.LineNumber}");
            output.AppendLine($"  Tests: {testClass.TestCount}");
            foreach (var method in testClass.TestMethods.Take(10))
            {
                output.AppendLine($"    - {method.MethodName}");
            }
            if (testClass.TestMethods.Count > 10)
                output.AppendLine($"    ... and {testClass.TestMethods.Count - 10} more");
            output.AppendLine();
        }

        return output.ToString();
    }

    private static string FormatTestResultsDetailed(List<TestClassResult> results, string typeName)
    {
        if (!results.Any())
            return $"No tests found for '{typeName}'.";

        var output = new StringBuilder();
        output.AppendLine($"# Test Analysis for '{typeName}'");
        output.AppendLine($"Total test classes: {results.Count}");
        output.AppendLine($"Total test methods: {results.Sum(r => r.TestCount)}\n");

        foreach (var testClass in results)
        {
            output.AppendLine($"## {testClass.TestClassFullName}");
            output.AppendLine($"  Framework: {testClass.TestFramework}");
            output.AppendLine($"  Project: {testClass.ProjectName}");
            output.AppendLine($"  File: {testClass.FilePath}:{testClass.LineNumber}");
            if (!string.IsNullOrEmpty(testClass.Documentation))
                output.AppendLine($"  Documentation: {testClass.Documentation}");
            output.AppendLine($"  Test Methods ({testClass.TestCount}):");
            foreach (var method in testClass.TestMethods)
            {
                output.AppendLine($"    - {method.MethodName} @ line {method.LineNumber}");
                output.AppendLine($"      Attributes: {string.Join(", ", method.TestAttributes)}");
            }
            output.AppendLine();
        }

        return output.ToString();
    }

    private static string FormatTestCoverageSummary(TestCoverageResults results)
    {
        var output = new StringBuilder();
        output.AppendLine("Test Coverage Summary:");
        output.AppendLine($"  Type coverage: {results.OverallTypeCoverage:F1}% ({results.TestedTypes}/{results.TotalTypes})");
        output.AppendLine($"  Member coverage: {results.OverallMemberCoverage:F1}% ({results.TestedPublicMembers}/{results.TotalPublicMembers})");
        output.AppendLine($"  Critical risk: {results.CriticalRiskTypes}");
        output.AppendLine($"  High risk: {results.HighRiskTypes}");
        return output.ToString();
    }

    private static string FormatTestCoverageNormal(TestCoverageResults results)
    {
        var output = new StringBuilder();
        output.AppendLine($"# Test Coverage Analysis");
        output.AppendLine($"Type coverage: {results.OverallTypeCoverage:F1}%");
        output.AppendLine($"Member coverage: {results.OverallMemberCoverage:F1}%\n");

        if (results.TypeCoverages.Any(t => t.RiskLevel == "Critical" || t.RiskLevel == "High"))
        {
            output.AppendLine("## High Risk (Untested):");
            foreach (var type in results.TypeCoverages
                .Where(t => t.RiskLevel == "Critical" || t.RiskLevel == "High")
                .Take(20))
            {
                output.AppendLine($"  - [{type.RiskLevel}] {type.TypeName} ({type.CoveragePercentage:F0}% coverage)");
            }
            output.AppendLine();
        }

        output.AppendLine("## Project Statistics:");
        foreach (var (project, stats) in results.ProjectStatistics)
        {
            output.AppendLine($"  {project}: {stats.TypeCoveragePercentage:F1}% types, {stats.MemberCoveragePercentage:F1}% members");
        }

        return output.ToString();
    }

    private static string FormatTestCoverageDetailed(TestCoverageResults results)
    {
        var output = new StringBuilder();
        output.AppendLine($"# Test Coverage Analysis");
        output.AppendLine($"Total types: {results.TotalTypes}");
        output.AppendLine($"Tested types: {results.TestedTypes}");
        output.AppendLine($"Type coverage: {results.OverallTypeCoverage:F1}%");
        output.AppendLine($"Member coverage: {results.OverallMemberCoverage:F1}%\n");

        output.AppendLine("## Risk Summary:");
        output.AppendLine($"  Critical: {results.CriticalRiskTypes}");
        output.AppendLine($"  High: {results.HighRiskTypes}");
        output.AppendLine($"  Medium: {results.MediumRiskTypes}");
        output.AppendLine($"  Low: {results.LowRiskTypes}\n");

        output.AppendLine("## Uncovered Types:");
        foreach (var type in results.TypeCoverages.Where(t => !t.HasTests).Take(50))
        {
            output.AppendLine($"### {type.TypeName}");
            output.AppendLine($"  File: {type.FilePath}:{type.LineNumber}");
            output.AppendLine($"  Complexity: {type.CyclomaticComplexity}");
            output.AppendLine($"  Risk: {type.RiskLevel}");
            output.AppendLine($"  Public members: {type.TotalPublicMembers}");
            output.AppendLine();
        }

        return output.ToString();
    }

    #endregion
}
