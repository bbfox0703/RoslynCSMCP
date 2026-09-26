using System.Text.Json;
using FluentAssertions;
using Microsoft.Build.Locator;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Core.Services;
using RoslynMcpServer.Tests.Helpers;
using RoslynMcpServer.Tools;
using AdvancedModule = RoslynMcpServer.Advanced.Tools.AdvancedTools;
using MetricsModule = RoslynMcpServer.Metrics.Tools.MetricsTools;
using QualityModule = RoslynMcpServer.Quality.Tools.QualityTools;
using TestingModule = RoslynMcpServer.Testing.Tools.TestingTools;

namespace RoslynMcpServer.Tests.Integration.Tools;

/// <summary>
/// One restored sample solution, plus variants that fail to load, shared by the output-contract tests.
/// </summary>
public sealed class ToolOutputContractFixture : IDisposable
{
    private readonly IntegrationTestHelper _helper = new(nameof(ToolOutputContractTests));

    public string SolutionPath { get; }
    public string SlnxPath { get; }
    public string BrokenSolutionPath { get; }
    public string MissingProjectSolutionPath { get; }

    public ToolOutputContractFixture()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }

        var project = new ProjectDefinition("Sample")
            .AddSourceFile("Shapes.cs", """
                namespace Sample.Core;

                // TODO(alice): replace with the real storage layer before release
                public interface IShape { double Area(); }

                public abstract class ShapeBase : IShape
                {
                    public abstract double Area();
                }

                public class Circle : ShapeBase
                {
                    public double Radius { get; set; }
                    public override double Area() => Math.PI * Radius * Radius;
                }

                public class Ring : Circle
                {
                    public double Inner { get; set; }
                }
                """)
            .AddSourceFile("MathUtil.cs", """
                namespace Sample.Util;

                // FIXME: handle negative values
                public static class MathUtil
                {
                    [Obsolete("Use ClampPositive instead")]
                    public static double Clamp(double value) => value < 0 ? 0 : value;

                    public static double ClampPositive(double value) => Math.Max(0, value);

                #pragma warning disable CS0618
                    public static double Legacy() => Clamp(-1);
                #pragma warning restore CS0618
                }
                """);

        SolutionPath = _helper.CreateSolution("Sample", project);
        var root = _helper.TestRootPath;

        SlnxPath = Path.Combine(root, "Sample.slnx");
        File.WriteAllText(SlnxPath, """
            <Solution>
              <Project Path="Sample/Sample.csproj" />
            </Solution>
            """);

        BrokenSolutionPath = Path.Combine(root, "Broken.sln");
        File.WriteAllText(BrokenSolutionPath, "this is not a solution file");

        // Same real project plus one whose project file does not exist
        MissingProjectSolutionPath = Path.Combine(root, "MissingProject.sln");
        var lines = File.ReadAllLines(SolutionPath).ToList();
        lines.InsertRange(lines.IndexOf("Global"), new[]
        {
            "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"Ghost\", \"Ghost\\Ghost.csproj\", \"{11111111-2222-3333-4444-555555555555}\"",
            "EndProject"
        });
        File.WriteAllLines(MissingProjectSolutionPath, lines);
    }

    public void Dispose() => _helper.Dispose();
}

/// <summary>
/// Tool output contracts: load failures surface as warnings, and format/groupBy values change the output.
/// </summary>
public class ToolOutputContractTests : IClassFixture<ToolOutputContractFixture>, IDisposable
{
    private readonly ToolOutputContractFixture _fixture;
    private readonly ServiceProvider _services;
    private readonly SecurityValidator _validator = new(NullLogger<SecurityValidator>.Instance);
    private readonly McpErrorHandler _errorHandler = new(NullLogger<McpErrorHandler>.Instance);

    public ToolOutputContractTests(ToolOutputContractFixture fixture)
    {
        _fixture = fixture;

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddMemoryCache();
        services.AddSingleton<CodeAnalysisService>();
        services.AddSingleton<SymbolSearchService>();
        services.AddSingleton<CodeMetricsService>();
        services.AddSingleton<TestDiscoveryService>();
        services.AddSingleton<TestCoverageAnalyzer>();
        services.AddSingleton<DiagnosticsService>();
        services.AddSingleton<AttributeSearchService>();
        services.AddSingleton<SecurityValidator>();
        _services = services.BuildServiceProvider();
    }

    public void Dispose() => _services.Dispose();

    private T Get<T>() where T : notnull => _services.GetRequiredService<T>();

    // ── Load failures are not clean results ───────────────────────────────────

    [Fact]
    public async Task AdvancedFindDeprecatedAPIs_UnloadableSolution_ShowsWarnings()
    {
        var result = await AdvancedModule.FindDeprecatedAPIs(
            _fixture.BrokenSolutionPath,
            analyzer: new DeprecatedAPIAnalyzer(NullLogger<DeprecatedAPIAnalyzer>.Instance),
            validator: _validator,
            errorHandler: _errorHandler);

        result.Should().Contain("⚠️ Warnings");
    }

    [Theory]
    [InlineData("summary")]
    [InlineData("normal")]
    [InlineData("detailed")]
    public async Task QualityFindUnusedCode_UnloadableSolution_ShowsWarningsInEveryFormat(string format)
    {
        var result = await QualityModule.FindUnusedCode(
            _fixture.BrokenSolutionPath,
            format: format,
            analyzer: new UnusedCodeAnalyzer(NullLogger<UnusedCodeAnalyzer>.Instance, _validator),
            validator: _validator,
            errorHandler: _errorHandler);

        result.Should().Contain("⚠️ Warnings");
    }

    [Fact]
    public async Task FullGetCompilationErrors_MissingProject_DoesNotClaimTheSolutionBuilds()
    {
        var result = await CodeNavigationTools.GetCompilationErrors(
            _fixture.MissingProjectSolutionPath,
            severity: "Error",
            serviceProvider: _services);

        result.Should().NotContain("Solution builds successfully");
        result.Should().Contain("Workspace load");
    }

    // ── Advanced format values ────────────────────────────────────────────────

    [Fact]
    public async Task AdvancedFindTODOComments_FormatsDiffer()
    {
        async Task<string> Run(string format) => await AdvancedModule.FindTODOComments(
            _fixture.SolutionPath,
            format: format,
            analyzer: new TODOCommentAnalyzer(NullLogger<TODOCommentAnalyzer>.Instance),
            validator: _validator,
            errorHandler: _errorHandler);

        var summary = await Run("summary");
        var normal = await Run("Normal");
        var detailed = await Run("DETAILED");

        summary.Should().Contain("By project:").And.NotContain("MathUtil.cs");
        normal.Should().Contain("MathUtil.cs:");
        detailed.Should().Contain("(author: alice)").And.Contain(Path.Combine("Sample", "Shapes.cs"));
    }

    [Fact]
    public async Task AdvancedGetClassHierarchy_MermaidAndJson()
    {
        async Task<string> Run(string format) => await AdvancedModule.GetClassHierarchy(
            "Circle",
            _fixture.SolutionPath,
            format: format,
            searchService: Get<SymbolSearchService>(),
            validator: _validator,
            errorHandler: _errorHandler);

        var mermaid = await Run("mermaid");
        mermaid.Should().StartWith("classDiagram");
        mermaid.Should().Contain("Sample_Core_ShapeBase <|-- Sample_Core_Circle");
        mermaid.Should().Contain("Sample_Core_IShape <|.. Sample_Core_ShapeBase");
        mermaid.Should().Contain("Sample_Core_Circle <|-- Sample_Core_Ring");

        using var json = JsonDocument.Parse(await Run("json"));
        json.RootElement.GetProperty("typeName").GetString().Should().Be("Circle");
        json.RootElement.GetProperty("descendants").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AdvancedFindAttributeUsages_DetailedShowsArguments()
    {
        var detailed = await AdvancedModule.FindAttributeUsages(
            "Obsolete",
            _fixture.SolutionPath,
            format: "detailed",
            searchService: Get<AttributeSearchService>(),
            validator: _validator,
            errorHandler: _errorHandler);

        detailed.Should().Contain("Arguments:").And.Contain("Use ClampPositive instead");
    }

    [Fact]
    public async Task AdvancedFindDeprecatedAPIs_SummaryOmitsLocations()
    {
        async Task<string> Run(string format) => await AdvancedModule.FindDeprecatedAPIs(
            _fixture.SolutionPath,
            format: format,
            analyzer: new DeprecatedAPIAnalyzer(NullLogger<DeprecatedAPIAnalyzer>.Instance),
            validator: _validator,
            errorHandler: _errorHandler);

        var summary = await Run("Summary");
        var normal = await Run("normal");

        summary.Should().Contain("Clamp").And.NotContain("MathUtil.cs:");
        normal.Should().Contain("MathUtil.cs:");
    }

    // ── Metrics format and groupBy ────────────────────────────────────────────

    [Fact]
    public async Task MetricsGetCodeMetrics_FormatsDiffer()
    {
        async Task<string> Run(string format) => await MetricsModule.GetCodeMetrics(
            _fixture.SolutionPath,
            format: format,
            metricsService: Get<CodeMetricsService>(),
            validator: _validator,
            errorHandler: _errorHandler);

        var summary = await Run("summary");
        var normal = await Run("normal");
        var detailed = await Run("detailed");

        summary.Should().Contain("Overall Statistics").And.NotContain("Largest Types").And.NotContain("Project Breakdown");
        normal.Should().Contain("Largest Types").And.Contain("Project Breakdown").And.NotContain("Namespace Breakdown");
        detailed.Should().Contain("Project Breakdown").And.Contain("Namespace Breakdown").And.Contain("Type Breakdown");
        detailed.Should().Contain("Sample.Util:").And.Contain("Sample.Core.Circle (Shapes.cs):");
    }

    [Fact]
    public async Task FullGetCodeMetrics_GroupByNamespaceAndType()
    {
        var result = await CodeNavigationTools.GetCodeMetrics(
            _fixture.SolutionPath, groupBy: "namespace,type", serviceProvider: _services);

        result.Should().Contain("Namespace Breakdown").And.Contain("Type Breakdown").And.NotContain("Project Breakdown");
        result.Should().Contain("Sample.Core:");
    }

    [Fact]
    public async Task FullGetCodeMetrics_UnknownGroupBy_ReturnsError()
    {
        var result = await CodeNavigationTools.GetCodeMetrics(
            _fixture.SolutionPath, groupBy: "assembly", serviceProvider: _services);

        result.Should().StartWith("Error:").And.Contain("groupBy must be");
    }

    [Fact]
    public async Task TestingGetTestCoverage_GroupByNamespace_PrintsNamespaceStatistics()
    {
        var result = await TestingModule.GetTestCoverage(
            _fixture.SolutionPath,
            groupBy: "namespace",
            coverageAnalyzer: Get<TestCoverageAnalyzer>(),
            validator: _validator,
            errorHandler: _errorHandler);

        result.Should().Contain("## Namespace Statistics:").And.Contain("Sample.Core:");
    }

    // ── .slnx solutions load ──────────────────────────────────────────────────

    [Fact]
    public async Task MetricsGetCodeMetrics_SlnxSolution_Loads()
    {
        var result = await MetricsModule.GetCodeMetrics(
            _fixture.SlnxPath,
            format: "summary",
            metricsService: Get<CodeMetricsService>(),
            validator: _validator,
            errorHandler: _errorHandler);

        result.Should().Contain("Total Projects: 1").And.Contain("Total Files: ");
        result.Should().NotContain("Total Files: 0");
    }
}
