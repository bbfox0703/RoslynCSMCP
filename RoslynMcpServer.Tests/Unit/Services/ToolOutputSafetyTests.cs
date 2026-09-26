using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Core.Models;
using RoslynMcpServer.Core.Services;
using AdvancedModule = RoslynMcpServer.Advanced.Tools.AdvancedTools;

namespace RoslynMcpServer.Tests.Unit.Services;

/// <summary>
/// Guards against failures that read as clean results: warnings must reach tool output,
/// and only solution files (.sln, .slnx) are accepted as solution paths.
/// </summary>
public class ToolOutputSafetyTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SecurityValidator _validator = new(NullLogger<SecurityValidator>.Instance);
    private readonly McpErrorHandler _errorHandler = new(NullLogger<McpErrorHandler>.Instance);

    public ToolOutputSafetyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "RoslynMcpTests", nameof(ToolOutputSafetyTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    private string CreateFile(string name, string content = "")
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static List<OperationWarning> Warnings(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new OperationWarning { Context = $"Project{i}", Message = $"failed {i}" })
            .ToList();

    // ── OperationWarningExtensions ────────────────────────────────────────────

    [Fact]
    public void WithWarnings_NoWarnings_ReturnsTextUnchanged()
    {
        "No issues found.".WithWarnings(new List<OperationWarning>()).Should().Be("No issues found.");
    }

    [Fact]
    public void WithWarnings_AppendsContextAndMessageAfterBlankLine()
    {
        var text = "No issues found.".WithWarnings(Warnings(1));

        text.Should().StartWith("No issues found." + Environment.NewLine + Environment.NewLine);
        text.Should().Contain("⚠️ Warnings (1); results may be incomplete:");
        text.Should().Contain("  - Project1: failed 1");
    }

    [Fact]
    public void AppendWarnings_CapsListAndCountsTheRest()
    {
        var text = new StringBuilder("Report").AppendWarnings(Warnings(12), maxShown: 10).ToString();

        text.Should().Contain("Project10: failed 10");
        text.Should().NotContain("Project11");
        text.Should().Contain("... and 2 more");
    }

    [Fact]
    public void AppendWarnings_DoesNotAddExtraBlankLineAfterExistingOne()
    {
        var output = new StringBuilder();
        output.AppendLine("Report");
        output.AppendLine();

        var text = output.AppendWarnings(Warnings(1)).ToString();

        text.Should().StartWith("Report" + Environment.NewLine + Environment.NewLine + "⚠️");
    }

    // ── Solution path validation ──────────────────────────────────────────────

    [Theory]
    [InlineData("App.sln")]
    [InlineData("App.slnx")]
    [InlineData("App.SLNX")]
    public void ValidateSolutionPath_AcceptsExistingSolutionFiles(string fileName)
    {
        _validator.ValidateSolutionPath(CreateFile(fileName)).Should().BeTrue();
    }

    [Fact]
    public void ValidateSolutionPath_RejectsProjectFiles()
    {
        _validator.ValidateSolutionPath(CreateFile("App.csproj", "<Project />")).Should().BeFalse();
    }

    [Fact]
    public void ValidateSolutionPathExtension_ProjectFile_ExplainsExpectedExtensions()
    {
        var error = _validator.ValidateSolutionPath(CreateFile("App.csproj", "<Project />"), _errorHandler);

        error.Should().NotBeNull();
        error.Should().Contain("Expected a solution file (.sln or .slnx)");
    }

    [Fact]
    public void ValidateSolutionPathExtension_MissingSolution_ReportsNotFound()
    {
        var error = _validator.ValidateSolutionPath(Path.Combine(_tempDir, "Missing.sln"), _errorHandler);

        error.Should().Contain("Solution not found");
    }

    // ── CodeMetricsService.TryParseGroupBy ────────────────────────────────────

    [Theory]
    [InlineData("project", MetricsBreakdown.Project)]
    [InlineData("Namespace", MetricsBreakdown.Namespace)]
    [InlineData("project, type", MetricsBreakdown.Project | MetricsBreakdown.Type)]
    [InlineData("none", MetricsBreakdown.None)]
    [InlineData("", MetricsBreakdown.None)]
    public void TryParseGroupBy_AcceptsKnownValues(string groupBy, MetricsBreakdown expected)
    {
        CodeMetricsService.TryParseGroupBy(groupBy, out var breakdown).Should().BeTrue();
        breakdown.Should().Be(expected);
    }

    [Theory]
    [InlineData("assembly")]
    [InlineData("project,file")]
    public void TryParseGroupBy_RejectsUnknownValues(string groupBy)
    {
        CodeMetricsService.TryParseGroupBy(groupBy, out _).Should().BeFalse();
    }

    // ── Advanced module validates paths before analyzing ──────────────────────

    [Fact]
    public async Task AdvancedFindTODOComments_MissingSolution_ReturnsErrorNotCleanResult()
    {
        var result = await AdvancedModule.FindTODOComments(
            solutionPath: Path.Combine(_tempDir, "Missing.sln"),
            analyzer: new TODOCommentAnalyzer(NullLogger<TODOCommentAnalyzer>.Instance),
            validator: _validator,
            errorHandler: _errorHandler);

        result.Should().Contain("Solution not found");
        result.Should().NotContain("No TODO comments found");
    }

    [Fact]
    public async Task AdvancedFindLargeFiles_ProjectFile_ReturnsInvalidPathError()
    {
        var result = await AdvancedModule.FindLargeFiles(
            solutionPath: CreateFile("App.csproj", "<Project />"),
            analyzer: new LargeFileAnalyzer(NullLogger<LargeFileAnalyzer>.Instance),
            validator: _validator,
            errorHandler: _errorHandler);

        result.Should().Contain("Expected a solution file (.sln or .slnx)");
    }

    [Fact]
    public async Task AdvancedAnalyzeAPIChanges_ValidatesBothPaths()
    {
        var oldSolution = CreateFile("Old.sln");

        var result = await AdvancedModule.AnalyzeAPIChanges(
            oldSolutionPath: oldSolution,
            newSolutionPath: Path.Combine(_tempDir, "New.sln"),
            analyzer: null!,
            validator: _validator,
            errorHandler: _errorHandler);

        result.Should().Contain("Solution not found").And.Contain("New.sln");
    }
}
