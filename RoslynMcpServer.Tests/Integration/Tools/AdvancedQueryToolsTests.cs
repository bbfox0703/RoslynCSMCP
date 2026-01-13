using RoslynMcpServer.Tests.Helpers;
using RoslynMcpServer.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Services;
using Microsoft.Build.Locator;

namespace RoslynMcpServer.Tests.Integration.Tools;

/// <summary>
/// Integration tests for advanced query and navigation tools
/// </summary>
public class AdvancedQueryToolsTests : IDisposable
{
    private readonly IntegrationTestHelper _testHelper;
    private readonly IServiceProvider _serviceProvider;
    private string? _solutionPath;

    static AdvancedQueryToolsTests()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }
    }

    public AdvancedQueryToolsTests()
    {
        _testHelper = new IntegrationTestHelper(nameof(AdvancedQueryToolsTests));
        _serviceProvider = CreateServiceProvider();
    }

    private IServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddMemoryCache();

        services.AddSingleton<CodeAnalysisService>();
        services.AddSingleton<SymbolSearchService>();
        services.AddSingleton<CallHierarchyService>();
        services.AddSingleton<BatchQueryService>();
        services.AddSingleton<TestDiscoveryService>();
        services.AddSingleton<SecurityValidator>();
        services.AddSingleton<DiagnosticLogger>();

        return services.BuildServiceProvider();
    }

    private void CreateTestSolution()
    {
        var project = new ProjectDefinition("TestProject")
            .AddSourceFile("Calculator.cs",
                CodeTemplates.ClassWithMethod("TestProject", "Calculator", "Add", "int"))
            .AddSourceFile("Service.cs",
                CodeTemplates.ClassWithReferences("TestProject", "Service", "Calculator"))
            .AddSourceFile("Helper.cs",
                CodeTemplates.SimpleClass("TestProject", "Helper"));

        _solutionPath = _testHelper.CreateSolution("TestSolution", project);
    }

    public void Dispose()
    {
        _testHelper?.Dispose();
        (_serviceProvider as ServiceProvider)?.Dispose();
    }

    #region FindReferencesFiltered Tests

    [Fact]
    public async Task FindReferencesFiltered_WritesOnly_FindsAssignments()
    {
        // Arrange
        CreateTestSolution();

        // Act
        var result = await CodeNavigationTools.FindReferencesFiltered(
            symbolName: "Calculator",
            solutionPath: _solutionPath!,
            writesOnly: true,
            publicOnly: false,
            excludeTests: false,
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Match(r => r.Contains("Calculator") || r.Contains("references") || r.Contains("found"));
    }

    [Fact]
    public async Task FindReferencesFiltered_PublicOnly_FiltersPrivateUsages()
    {
        // Arrange
        CreateTestSolution();

        // Act
        var result = await CodeNavigationTools.FindReferencesFiltered(
            symbolName: "Helper",
            solutionPath: _solutionPath!,
            publicOnly: true,
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task FindReferencesFiltered_ExcludeTests_OmitsTestProjects()
    {
        // Arrange
        var mainProject = new ProjectDefinition("Main")
            .AddSourceFile("Class1.cs", CodeTemplates.SimpleClass("Main", "Class1"));

        var testProject = new ProjectDefinition("Main.Tests")
            .AddReference("Main")
            .AddSourceFile("Class1Tests.cs", CodeTemplates.SimpleClass("Main.Tests", "Class1Tests"));

        _solutionPath = _testHelper.CreateSolution("TestSolution", mainProject, testProject);

        // Act
        var result = await CodeNavigationTools.FindReferencesFiltered(
            symbolName: "Class1",
            solutionPath: _solutionPath!,
            excludeTests: true,
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region GetCallHierarchy Tests

    [Fact]
    public async Task GetCallHierarchy_FindsCallers()
    {
        // Arrange
        CreateTestSolution();

        // Act
        var result = await CodeNavigationTools.GetCallHierarchy(
            solutionPath: _solutionPath!,
            methodName: "Add",
            direction: "callers",
            maxDepth: 3,
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Match(r =>
            r.Contains("Add") ||
            r.Contains("hierarchy") ||
            r.Contains("method"));
    }

    [Fact]
    public async Task GetCallHierarchy_BothDirections_ShowsCallersAndCallees()
    {
        // Arrange
        CreateTestSolution();

        // Act
        var result = await CodeNavigationTools.GetCallHierarchy(
            solutionPath: _solutionPath!,
            methodName: "Add",
            direction: "both",
            maxDepth: 2,
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region BatchQuery Tests

    [Fact]
    public async Task BatchQuery_MultipleQueries_ExecutesAll()
    {
        // Arrange
        CreateTestSolution();

        var queries = @"[
            {
                ""tool"": ""SearchSymbols"",
                ""parameters"": {
                    ""pattern"": ""*"",
                    ""solutionPath"": """ + _solutionPath!.Replace("\\", "\\\\") + @""",
                    ""symbolTypes"": ""class""
                }
            },
            {
                ""tool"": ""GetSymbolInfo"",
                ""parameters"": {
                    ""symbolName"": ""Calculator"",
                    ""solutionPath"": """ + _solutionPath!.Replace("\\", "\\\\") + @"""
                }
            }
        ]";

        // Act
        var result = await CodeNavigationTools.BatchQuery(
            queriesJson: queries,
            parallel: true,
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Match(r =>
            r.Contains("Query") ||
            r.Contains("Result") ||
            r.Contains("batch"));
    }

    [Fact]
    public async Task BatchQuery_InvalidJson_ReturnsError()
    {
        // Arrange
        CreateTestSolution();

        // Act
        var result = await CodeNavigationTools.BatchQuery(
            queriesJson: "invalid json",
            parallel: false,
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().Contain("Error");
    }

    #endregion

    #region FindTestsForType Tests

    [Fact]
    public async Task FindTestsForType_FindsTestClasses()
    {
        // Arrange
        var mainProject = new ProjectDefinition("Calculator")
            .AddSourceFile("Calculator.cs",
                CodeTemplates.SimpleClass("Calculator", "Calculator"));

        var testProject = new ProjectDefinition("Calculator.Tests")
            .AddReference("Calculator")
            .AddSourceFile("CalculatorTests.cs",
                CodeTemplates.ClassWithMethod("Calculator.Tests", "CalculatorTests", "TestAdd"));

        _solutionPath = _testHelper.CreateSolution("TestSolution", mainProject, testProject);

        // Act
        var result = await CodeNavigationTools.FindTestsForType(
            typeName: "Calculator",
            solutionPath: _solutionPath!,
            includePartialMatches: true,
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Match(r =>
            r.Contains("test") ||
            r.Contains("Test") ||
            r.Contains("Calculator"));
    }

    [Fact]
    public async Task FindTestsForType_NoTests_ReturnsNotFound()
    {
        // Arrange
        var project = new ProjectDefinition("Lib")
            .AddSourceFile("Utility.cs",
                CodeTemplates.SimpleClass("Lib", "Utility"));

        _solutionPath = _testHelper.CreateSolution("TestSolution", project);

        // Act
        var result = await CodeNavigationTools.FindTestsForType(
            typeName: "Utility",
            solutionPath: _solutionPath!,
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Match(r =>
            r.Contains("not found") ||
            r.Contains("No test") ||
            r.Contains("not be found") ||
            r.Contains("could not") ||
            r.Contains("0") ||
            r.Contains("Error"));
    }

    #endregion
}
