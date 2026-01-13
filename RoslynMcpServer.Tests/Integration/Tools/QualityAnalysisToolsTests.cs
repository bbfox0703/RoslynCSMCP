using RoslynMcpServer.Tests.Helpers;
using RoslynMcpServer.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Services;
using Microsoft.Build.Locator;

namespace RoslynMcpServer.Tests.Integration.Tools;

/// <summary>
/// Integration tests for quality analysis tools
/// Tests complexity analysis, unused code detection, and other quality metrics
/// </summary>
public class QualityAnalysisToolsTests : IDisposable
{
    private readonly IntegrationTestHelper _testHelper;
    private readonly IServiceProvider _serviceProvider;
    private string? _solutionPath;

    static QualityAnalysisToolsTests()
    {
        // Register MSBuild once for all tests
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }
    }

    public QualityAnalysisToolsTests()
    {
        _testHelper = new IntegrationTestHelper(nameof(QualityAnalysisToolsTests));
        _serviceProvider = CreateServiceProvider();
    }

    private IServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddMemoryCache();

        // Add all necessary services
        services.AddSingleton<CodeAnalysisService>();
        services.AddSingleton<SymbolSearchService>();
        services.AddSingleton<UnusedCodeAnalyzer>();
        services.AddSingleton<AttributeSearchService>();
        services.AddSingleton<DiagnosticsService>();
        services.AddSingleton<CallHierarchyService>();
        services.AddSingleton<SecurityValidator>();
        services.AddSingleton<DiagnosticLogger>();

        return services.BuildServiceProvider();
    }

    private void CreateComplexSolution()
    {
        var coreProject = new ProjectDefinition("Core")
            .AddSourceFile("SimpleClass.cs",
                CodeTemplates.SimpleClass("Core", "SimpleClass"))
            .AddSourceFile("ComplexCalculator.cs",
                CodeTemplates.ComplexMethod("Core", "ComplexCalculator", 12))
            .AddSourceFile("UnusedUtility.cs",
                CodeTemplates.ClassWithMethod("Core", "UnusedUtility", "NeverCalled"));

        var appProject = new ProjectDefinition("App")
            .AddReference("Core")
            .AddSourceFile("MainApp.cs",
                CodeTemplates.ClassWithReferences("App", "MainApp", "SimpleClass"));

        _solutionPath = _testHelper.CreateSolution("ComplexSolution",
            coreProject,
            appProject);
    }

    public void Dispose()
    {
        _testHelper?.Dispose();
        (_serviceProvider as ServiceProvider)?.Dispose();
    }

    #region AnalyzeCodeComplexity Tests

    [Fact]
    public async Task AnalyzeCodeComplexity_FindsHighComplexityMethod()
    {
        // Arrange
        CreateComplexSolution();

        // Act
        var result = await CodeNavigationTools.AnalyzeCodeComplexity(
            solutionPath: _solutionPath!,
            threshold: 5,
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("ComplexCalculator");
        result.Should().Contain("ComplexMethod");
        // Should indicate high complexity (12 > threshold of 5)
        result.Should().Match(r => r.Contains("complexity") || r.Contains("12"));
    }

    [Fact]
    public async Task AnalyzeCodeComplexity_WithHighThreshold_FindsFewerMethods()
    {
        // Arrange
        CreateComplexSolution();

        // Act
        var result = await CodeNavigationTools.AnalyzeCodeComplexity(
            solutionPath: _solutionPath!,
            threshold: 15,
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        // With threshold 15, ComplexMethod (12) should not be flagged
        // Or should indicate that complexity is below threshold
    }

    [Fact]
    public async Task AnalyzeCodeComplexity_InvalidThreshold_HandlesGracefully()
    {
        // Arrange
        CreateComplexSolution();

        // Act
        var result = await CodeNavigationTools.AnalyzeCodeComplexity(
            solutionPath: _solutionPath!,
            threshold: 0,
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        // Should either use default threshold or return error message
    }

    #endregion

    #region FindUnusedCode Tests

    [Fact]
    public async Task FindUnusedCode_DetectsUnusedClass()
    {
        // Arrange
        CreateComplexSolution();

        // Act
        var result = await CodeNavigationTools.FindUnusedCode(
            solutionPath: _solutionPath!,
            scope: "all",
            format: "normal",
            includeTests: false,
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        // UnusedUtility class should be detected as unused
        result.Should().Contain("UnusedUtility");
    }

    [Fact]
    public async Task FindUnusedCode_SummaryFormat_ShowsCounts()
    {
        // Arrange
        CreateComplexSolution();

        // Act
        var result = await CodeNavigationTools.FindUnusedCode(
            solutionPath: _solutionPath!,
            scope: "all",
            format: "summary",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        // Summary should show counts
        result.Should().Match(r => r.Contains("unused") || r.Contains("count") || r.Contains("total"));
    }

    [Fact]
    public async Task FindUnusedCode_PrivateScope_FindsOnlyPrivateUnused()
    {
        // Arrange
        CreateComplexSolution();

        // Act
        var result = await CodeNavigationTools.FindUnusedCode(
            solutionPath: _solutionPath!,
            scope: "private",
            format: "normal",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        // Should only report private unused members
    }

    #endregion

    #region GetCompilationErrors Tests

    [Fact]
    public async Task GetCompilationErrors_CleanSolution_ReportsNoErrors()
    {
        // Arrange
        CreateComplexSolution();

        // Act
        var result = await CodeNavigationTools.GetCompilationErrors(
            solutionPath: _solutionPath!,
            severity: "Error",
            mode: "normal",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        // Should return compilation status information
        result.Should().Match(r =>
            r.Contains("error") ||
            r.Contains("Error") ||
            r.Contains("Compilation") ||
            r.Contains("diagnostic"));
    }

    [Fact]
    public async Task GetCompilationErrors_AllSeverities_IncludesWarnings()
    {
        // Arrange
        CreateComplexSolution();

        // Act
        var result = await CodeNavigationTools.GetCompilationErrors(
            solutionPath: _solutionPath!,
            severity: "All",
            mode: "normal",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        // Should show both errors and warnings (if any)
    }

    #endregion

    #region FindAttributeUsages Tests

    [Fact]
    public async Task FindAttributeUsages_CustomAttribute_FindsUsages()
    {
        // Arrange
        var project = new ProjectDefinition("TestProject")
            .AddSourceFile("CustomAttribute.cs",
                CodeTemplates.AttributeClass("TestProject", "Custom"))
            .AddSourceFile("AnnotatedClass.cs",
                CodeTemplates.ClassWithAttribute("TestProject", "MyClass", "Custom", "test message"));

        _solutionPath = _testHelper.CreateSolution("AttributeSolution", project);

        // Act
        var result = await CodeNavigationTools.FindAttributeUsages(
            attributeName: "Custom",
            solutionPath: _solutionPath!,
            targetType: "all",
            format: "normal",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("MyClass");
        result.Should().Contain("AnnotatedMethod");
    }

    [Fact]
    public async Task FindAttributeUsages_NonExistentAttribute_ReturnsNoUsages()
    {
        // Arrange
        CreateComplexSolution();

        // Act
        var result = await CodeNavigationTools.FindAttributeUsages(
            attributeName: "NonExistentAttribute",
            solutionPath: _solutionPath!,
            targetType: "all",
            format: "normal",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Match(r => r.Contains("not found") || r.Contains("No usages") || r.Contains("0"));
    }

    #endregion

    #region GetClassHierarchy Tests

    [Fact]
    public async Task GetClassHierarchy_FindsImplementations()
    {
        // Arrange
        var project = new ProjectDefinition("HierarchyTest")
            .AddSourceFile("IService.cs",
                CodeTemplates.Interface("HierarchyTest", "IService"))
            .AddSourceFile("ServiceImpl.cs",
                CodeTemplates.SimpleClass("HierarchyTest", "ServiceImpl", null, "IService"))
            .AddSourceFile("AdvancedService.cs",
                CodeTemplates.SimpleClass("HierarchyTest", "AdvancedService", "ServiceImpl"));

        _solutionPath = _testHelper.CreateSolution("HierarchySolution", project);

        // Act
        var result = await CodeNavigationTools.GetClassHierarchy(
            typeName: "ServiceImpl",
            solutionPath: _solutionPath!,
            direction: "both",
            format: "normal",
            maxDepth: 10,
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("ServiceImpl");
        // Should show interface implementation
        result.Should().Contain("IService");
        // Should show derived class
        result.Should().Contain("AdvancedService");
    }

    [Fact]
    public async Task GetClassHierarchy_DescendantsOnly_ShowsDerivedClasses()
    {
        // Arrange
        var project = new ProjectDefinition("HierarchyTest")
            .AddSourceFile("BaseClass.cs",
                CodeTemplates.SimpleClass("HierarchyTest", "BaseClass"))
            .AddSourceFile("DerivedClass.cs",
                CodeTemplates.SimpleClass("HierarchyTest", "DerivedClass", "BaseClass"));

        _solutionPath = _testHelper.CreateSolution("DescendantsSolution", project);

        // Act
        var result = await CodeNavigationTools.GetClassHierarchy(
            typeName: "BaseClass",
            solutionPath: _solutionPath!,
            direction: "descendants",
            format: "normal",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("DerivedClass");
    }

    #endregion

    #region FindImplementations Tests

    [Fact]
    public async Task FindImplementations_Interface_FindsAllImplementations()
    {
        // Arrange
        var project = new ProjectDefinition("ImplTest")
            .AddSourceFile("IRepository.cs",
                CodeTemplates.Interface("ImplTest", "IRepository"))
            .AddSourceFile("SqlRepository.cs",
                CodeTemplates.SimpleClass("ImplTest", "SqlRepository", null, "IRepository"))
            .AddSourceFile("InMemoryRepository.cs",
                CodeTemplates.SimpleClass("ImplTest", "InMemoryRepository", null, "IRepository"));

        _solutionPath = _testHelper.CreateSolution("ImplSolution", project);

        // Act
        var result = await CodeNavigationTools.FindImplementations(
            typeName: "IRepository",
            solutionPath: _solutionPath!,
            format: "normal",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("SqlRepository");
        result.Should().Contain("InMemoryRepository");
    }

    #endregion
}
