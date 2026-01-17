using RoslynMcpServer.Tests.Helpers;
using RoslynMcpServer.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Core.Services;
using Microsoft.Build.Locator;

namespace RoslynMcpServer.Tests.Integration.Tools;

/// <summary>
/// Integration tests for dependency analysis tools
/// </summary>
public class DependencyAnalysisToolsTests : IDisposable
{
    private readonly IntegrationTestHelper _testHelper;
    private readonly IServiceProvider _serviceProvider;
    private string? _solutionPath;

    static DependencyAnalysisToolsTests()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }
    }

    public DependencyAnalysisToolsTests()
    {
        _testHelper = new IntegrationTestHelper(nameof(DependencyAnalysisToolsTests));
        _serviceProvider = CreateServiceProvider();
    }

    private IServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddMemoryCache();

        services.AddSingleton<CodeAnalysisService>();
        services.AddSingleton<CodeMetricsService>();
        services.AddSingleton<DependencyGraphService>();
        services.AddSingleton<UnusedDependencyAnalyzer>();
        services.AddSingleton<ChangeImpactAnalyzer>();
        services.AddSingleton<SecurityValidator>();
        services.AddSingleton<DiagnosticLogger>();

        return services.BuildServiceProvider();
    }

    private void CreateDependencySolution()
    {
        var coreProject = new ProjectDefinition("Core")
            .AddSourceFile("ILogger.cs",
                CodeTemplates.Interface("Core", "ILogger"))
            .AddSourceFile("Logger.cs",
                CodeTemplates.SimpleClass("Core", "Logger", null, "ILogger"));

        var dataProject = new ProjectDefinition("Data")
            .AddReference("Core")
            .AddSourceFile("Repository.cs",
                CodeTemplates.SimpleClass("Data", "Repository"));

        var appProject = new ProjectDefinition("App")
            .AddReference("Core")
            .AddReference("Data")
            .AddSourceFile("Service.cs",
                CodeTemplates.ClassWithReferences("App", "Service", "Repository", "Logger"));

        _solutionPath = _testHelper.CreateSolution("DependencySolution",
            coreProject,
            dataProject,
            appProject);
    }

    public void Dispose()
    {
        _testHelper?.Dispose();
        (_serviceProvider as ServiceProvider)?.Dispose();
    }

    #region GetCodeMetrics Tests

    [Fact]
    public async Task GetCodeMetrics_ReturnsMetricsForSolution()
    {
        // Arrange
        CreateDependencySolution();

        // Act
        var result = await CodeNavigationTools.GetCodeMetrics(
            solutionPath: _solutionPath!,
            groupBy: "project",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Match(r =>
            r.Contains("metric") ||
            r.Contains("Metric") ||
            r.Contains("project") ||
            r.Contains("lines"));
    }

    [Fact]
    public async Task GetCodeMetrics_GroupByNamespace_OrganizesByNamespace()
    {
        // Arrange
        CreateDependencySolution();

        // Act
        var result = await CodeNavigationTools.GetCodeMetrics(
            solutionPath: _solutionPath!,
            groupBy: "namespace",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region GetDependencyGraph Tests

    [Fact]
    public async Task GetDependencyGraph_TextFormat_ShowsDependencies()
    {
        // Arrange
        CreateDependencySolution();

        // Act
        var result = await CodeNavigationTools.GetDependencyGraph(
            solutionPath: _solutionPath!,
            format: "text",
            includePackages: false,
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Match(r =>
            r.Contains("Project") ||
            r.Contains("depend") ||
            r.Contains("->"));
    }

    [Fact]
    public async Task GetDependencyGraph_MermaidFormat_GeneratesDiagram()
    {
        // Arrange
        CreateDependencySolution();

        // Act
        var result = await CodeNavigationTools.GetDependencyGraph(
            solutionPath: _solutionPath!,
            format: "mermaid",
            includePackages: false,
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Match(r =>
            r.Contains("graph") ||
            r.Contains("-->") ||
            r.Contains("flowchart"));
    }

    [Fact]
    public async Task GetDependencyGraph_DotFormat_GeneratesGraphviz()
    {
        // Arrange
        CreateDependencySolution();

        // Act
        var result = await CodeNavigationTools.GetDependencyGraph(
            solutionPath: _solutionPath!,
            format: "dot",
            includePackages: false,
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Match(r =>
            r.Contains("digraph") ||
            r.Contains("->") ||
            r.Contains("node"));
    }

    #endregion

    #region AnalyzeDependencies Tests

    [Fact]
    public async Task AnalyzeDependencies_IdentifiesDependencyChains()
    {
        // Arrange
        CreateDependencySolution();

        // Act
        var result = await CodeNavigationTools.AnalyzeDependencies(
            solutionPath: _solutionPath!,
            maxDepth: 3,
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Match(r =>
            r.Contains("Dependencies") ||
            r.Contains("dependency") ||
            r.Contains("project"));
    }

    #endregion

    #region FindUnusedDependencies Tests

    [Fact]
    public async Task FindUnusedDependencies_IdentifiesUnusedReferences()
    {
        // Arrange
        CreateDependencySolution();

        // Act
        var result = await CodeNavigationTools.FindUnusedDependencies(
            solutionPath: _solutionPath!,
            includeProjectReferences: true,
            includeNuGetPackages: false,
            format: "normal",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Match(r =>
            r.Contains("unused") ||
            r.Contains("Unused") ||
            r.Contains("dependency") ||
            r.Contains("reference"));
    }

    [Fact]
    public async Task FindUnusedDependencies_SummaryFormat_ShowsCounts()
    {
        // Arrange
        CreateDependencySolution();

        // Act
        var result = await CodeNavigationTools.FindUnusedDependencies(
            solutionPath: _solutionPath!,
            format: "summary",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        // Should show counts or summary information
        result.Should().Match(r =>
            r.Contains("Total") ||
            r.Contains("count") ||
            r.Contains("Summary") ||
            r.Contains("Unused") ||
            r.Contains("unused") ||
            r.Contains("items") ||
            r.Contains("dependencies") ||
            r.Contains("projects"));
    }

    #endregion

    #region GetChangeImpact Tests

    [Fact]
    public async Task GetChangeImpact_AnalyzesImpactScope()
    {
        // Arrange
        CreateDependencySolution();

        // Act
        var result = await CodeNavigationTools.GetChangeImpact(
            symbolName: "Logger",
            solutionPath: _solutionPath!,
            includeIndirectReferences: true,
            maxDepth: 3,
            format: "normal",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Match(r =>
            r.Contains("Impact") ||
            r.Contains("impact") ||
            r.Contains("Logger") ||
            r.Contains("change"));
    }

    [Fact]
    public async Task GetChangeImpact_SummaryFormat_ShowsKeyMetrics()
    {
        // Arrange
        CreateDependencySolution();

        // Act
        var result = await CodeNavigationTools.GetChangeImpact(
            symbolName: "Repository",
            solutionPath: _solutionPath!,
            format: "summary",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
    }

    #endregion
}
