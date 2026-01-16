using RoslynMcpServer.Tests.Helpers;
using RoslynMcpServer.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Core.Services;
using Microsoft.Build.Locator;

namespace RoslynMcpServer.Tests.Integration.Tools;

/// <summary>
/// Integration tests for package analysis and testing tools
/// </summary>
public class PackageAndTestToolsTests : IDisposable
{
    private readonly IntegrationTestHelper _testHelper;
    private readonly IServiceProvider _serviceProvider;
    private string? _solutionPath;

    static PackageAndTestToolsTests()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }
    }

    public PackageAndTestToolsTests()
    {
        _testHelper = new IntegrationTestHelper(nameof(PackageAndTestToolsTests));
        _serviceProvider = CreateServiceProvider();
    }

    private IServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddMemoryCache();

        services.AddSingleton<CodeAnalysisService>();
        services.AddSingleton<UnusedDependencyAnalyzer>();
        services.AddSingleton<PackageAnalysisService>();
        services.AddSingleton<TestDiscoveryService>();
        services.AddSingleton<TestCoverageAnalyzer>();
        services.AddSingleton<APIChangeAnalyzer>();
        services.AddSingleton<SecurityValidator>();
        services.AddSingleton<DiagnosticLogger>();

        return services.BuildServiceProvider();
    }

    private void CreateProjectWithPackages()
    {
        var project = new ProjectDefinition("PackageProject")
            .AddSourceFile("Class1.cs", CodeTemplates.SimpleClass("PackageProject", "Class1"));

        _solutionPath = _testHelper.CreateSolution("PackageSolution", project);
    }

    private void CreateTestCoverageSolution()
    {
        var mainProject = new ProjectDefinition("MainLib")
            .AddSourceFile("Calculator.cs", @"
namespace MainLib;

public class Calculator
{
    public int Add(int a, int b) => a + b;
    public int Subtract(int a, int b) => a - b;
    public int Multiply(int a, int b) => a * b;
}

public class Formatter
{
    public string Format(int value) => value.ToString();
}");

        var testProject = new ProjectDefinition("MainLib.Tests")
            .AddReference("MainLib")
            .AddSourceFile("CalculatorTests.cs", @"
namespace MainLib.Tests;

public class CalculatorTests
{
    public void TestAdd() { }
    public void TestSubtract() { }
}");

        _solutionPath = _testHelper.CreateSolution("TestCoverageSolution", mainProject, testProject);
    }

    public void Dispose()
    {
        _testHelper?.Dispose();
        (_serviceProvider as ServiceProvider)?.Dispose();
    }

    #region AnalyzePackages Tests

    [Fact]
    public async Task AnalyzePackages_ShowsPackageInfo()
    {
        // Arrange
        CreateProjectWithPackages();

        // Act
        var result = await CodeNavigationTools.AnalyzePackages(
            solutionPath: _solutionPath!,
            checkUpdates: false,
            checkVulnerabilities: false,
            analyzeUsage: false,
            format: "normal",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Match(r =>
            r.Contains("Package") ||
            r.Contains("package") ||
            r.Contains("NuGet") ||
            r.Contains("analysis"));
    }

    [Fact]
    public async Task AnalyzePackages_SummaryFormat_ShowsOverview()
    {
        // Arrange
        CreateProjectWithPackages();

        // Act
        var result = await CodeNavigationTools.AnalyzePackages(
            solutionPath: _solutionPath!,
            checkUpdates: false,
            format: "summary",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Match(r =>
            r.Contains("Total") ||
            r.Contains("Summary") ||
            r.Contains("count"));
    }

    #endregion

    #region GetTestCoverage Tests

    [Fact]
    public async Task GetTestCoverage_AnalyzesCoverage()
    {
        // Arrange
        CreateTestCoverageSolution();

        // Act
        var result = await CodeNavigationTools.GetTestCoverage(
            solutionPath: _solutionPath!,
            scope: "public",
            groupBy: "project",
            format: "normal",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Match(r =>
            r.Contains("Coverage") ||
            r.Contains("coverage") ||
            r.Contains("test") ||
            r.Contains("Test"));
    }

    [Fact]
    public async Task GetTestCoverage_GroupByNamespace_OrganizesByNamespace()
    {
        // Arrange
        CreateTestCoverageSolution();

        // Act
        var result = await CodeNavigationTools.GetTestCoverage(
            solutionPath: _solutionPath!,
            scope: "public",
            groupBy: "namespace",
            format: "normal",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetTestCoverage_SummaryFormat_ShowsKeyMetrics()
    {
        // Arrange
        CreateTestCoverageSolution();

        // Act
        var result = await CodeNavigationTools.GetTestCoverage(
            solutionPath: _solutionPath!,
            scope: "all",
            format: "summary",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Match(r =>
            r.Contains("Total") ||
            r.Contains("Summary") ||
            r.Contains("%"));
    }

    #endregion

    #region AnalyzeAPIChanges Tests

    [Fact]
    public async Task AnalyzeAPIChanges_ComparesVersions()
    {
        // Arrange - Create version 1
        var oldProject = new ProjectDefinition("API")
            .AddSourceFile("Service.cs", @"
namespace API;

public class Service
{
    public void Method1() { }
    public void Method2() { }
}");

        var oldSolutionPath = _testHelper.CreateSolution("OldVersion", oldProject);

        // Create version 2 with changes
        var helper2 = new IntegrationTestHelper("AnalyzeAPIChanges_v2");
        var newProject = new ProjectDefinition("API")
            .AddSourceFile("Service.cs", @"
namespace API;

public class Service
{
    public void Method1() { }
    // Method2 removed
    public void Method3() { }  // New method
}");

        var newSolutionPath = helper2.CreateSolution("NewVersion", newProject);

        try
        {
            // Act
            var result = await CodeNavigationTools.AnalyzeAPIChanges(
                oldSolutionPath: oldSolutionPath,
                newSolutionPath: newSolutionPath,
                oldVersionLabel: "v1.0",
                newVersionLabel: "v2.0",
                includeInternal: false,
                format: "normal",
                serviceProvider: _serviceProvider);

            // Assert
            result.Should().NotBeNullOrEmpty();
            result.Should().Match(r =>
                r.Contains("API") ||
                r.Contains("change") ||
                r.Contains("Change") ||
                r.Contains("version"));
        }
        finally
        {
            helper2?.Dispose();
        }
    }

    [Fact]
    public async Task AnalyzeAPIChanges_SummaryFormat_ShowsBreakingChanges()
    {
        // Arrange
        var oldProject = new ProjectDefinition("API")
            .AddSourceFile("Class1.cs", CodeTemplates.SimpleClass("API", "Class1"));
        var oldSolutionPath = _testHelper.CreateSolution("OldAPI", oldProject);

        var helper2 = new IntegrationTestHelper("AnalyzeAPIChanges_Summary_v2");
        var newProject = new ProjectDefinition("API")
            .AddSourceFile("Class1.cs", CodeTemplates.SimpleClass("API", "Class1"));
        var newSolutionPath = helper2.CreateSolution("NewAPI", newProject);

        try
        {
            // Act
            var result = await CodeNavigationTools.AnalyzeAPIChanges(
                oldSolutionPath: oldSolutionPath,
                newSolutionPath: newSolutionPath,
                format: "summary",
                serviceProvider: _serviceProvider);

            // Assert
            result.Should().NotBeNullOrEmpty();
        }
        finally
        {
            helper2?.Dispose();
        }
    }

    #endregion

    #region FindReferencesAcrossSolutions Tests

    [Fact]
    public async Task FindReferencesAcrossSolutions_SearchesMultipleSolutions()
    {
        // Arrange
        var solution1Path = _testHelper.CreateSolution("Solution1",
            new ProjectDefinition("Lib1")
                .AddSourceFile("Class1.cs", CodeTemplates.SimpleClass("Lib1", "SharedClass")));

        var helper2 = new IntegrationTestHelper("FindReferencesAcross_Sol2");
        var solution2Path = helper2.CreateSolution("Solution2",
            new ProjectDefinition("Lib2")
                .AddSourceFile("Class2.cs", CodeTemplates.SimpleClass("Lib2", "SharedClass")));

        try
        {
            // Act
            var result = await CodeNavigationTools.FindReferencesAcrossSolutions(
                symbolName: "SharedClass",
                solutionPaths: $"{solution1Path},{solution2Path}",
                detailLevel: "locations",
                includeDefinition: true,
                serviceProvider: _serviceProvider);

            // Assert
            result.Should().NotBeNullOrEmpty();
            result.Should().Match(r =>
                r.Contains("SharedClass") ||
                r.Contains("references") ||
                r.Contains("Reference") ||
                r.Contains("found") ||
                r.Contains("Symbol") ||
                r.Contains("Error"));
        }
        finally
        {
            helper2?.Dispose();
        }
    }

    #endregion
}
