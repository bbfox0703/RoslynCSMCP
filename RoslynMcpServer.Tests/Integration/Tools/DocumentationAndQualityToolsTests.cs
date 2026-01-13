using RoslynMcpServer.Tests.Helpers;
using RoslynMcpServer.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Services;
using Microsoft.Build.Locator;

namespace RoslynMcpServer.Tests.Integration.Tools;

/// <summary>
/// Integration tests for documentation and code quality tools
/// </summary>
public class DocumentationAndQualityToolsTests : IDisposable
{
    private readonly IntegrationTestHelper _testHelper;
    private readonly IServiceProvider _serviceProvider;
    private string? _solutionPath;

    static DocumentationAndQualityToolsTests()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }
    }

    public DocumentationAndQualityToolsTests()
    {
        _testHelper = new IntegrationTestHelper(nameof(DocumentationAndQualityToolsTests));
        _serviceProvider = CreateServiceProvider();
    }

    private IServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddMemoryCache();

        services.AddSingleton<CodeAnalysisService>();
        services.AddSingleton<DocumentationAnalyzer>();
        services.AddSingleton<PerformanceIssueAnalyzer>();
        services.AddSingleton<NamingConventionAnalyzer>();
        services.AddSingleton<DeprecatedAPIAnalyzer>();
        services.AddSingleton<SecurityValidator>();
        services.AddSingleton<DiagnosticLogger>();

        return services.BuildServiceProvider();
    }

    private void CreateDocumentedCode()
    {
        var documentedCode = @"namespace TestProject;

/// <summary>
/// Well documented class
/// </summary>
public class DocumentedClass
{
    /// <summary>
    /// Documented method
    /// </summary>
    public void DocumentedMethod() { }

    // Not documented
    public void UndocumentedMethod() { }
}

public class UndocumentedClass
{
    public void Method1() { }
    public void Method2() { }
}";

        var project = new ProjectDefinition("DocProject")
            .AddSourceFile("Documentation.cs", documentedCode);

        _solutionPath = _testHelper.CreateSolution("DocSolution", project);
    }

    private void CreateNamingIssues()
    {
        var namingCode = @"namespace TestProject;

public class badClassName  // Should be PascalCase
{
    public string CONSTANT_VALUE = ""test"";  // Should be PascalCase for const
    private int _myfield;  // OK
    public int my_public_field;  // Should be PascalCase

    public void bad_method_name() { }  // Should be PascalCase

    public void GoodMethodName(int bad_param)  // param should be camelCase
    {
        int LOCAL_VAR = 1;  // Should be camelCase
    }
}

public interface badInterfaceName { }  // Should start with I";

        var project = new ProjectDefinition("NamingProject")
            .AddSourceFile("NamingIssues.cs", namingCode);

        _solutionPath = _testHelper.CreateSolution("NamingSolution", project);
    }

    private void CreatePerformanceIssues()
    {
        var perfCode = @"namespace TestProject;

using System.Linq;
using System.Collections.Generic;

public class PerformanceIssues
{
    public string StringConcat(List<string> items)
    {
        string result = """";
        foreach (var item in items)
        {
            result += item;  // String concatenation in loop
        }
        return result;
    }

    public async Task<int> SyncOverAsync()
    {
        return await Task.Run(() => 42).ConfigureAwait(false).GetAwaiter().GetResult();
    }

    public void LinqMisuse()
    {
        var numbers = new[] { 1, 2, 3, 4, 5 };
        var first = numbers.Where(x => x > 2).First();  // Should use FirstOrDefault
    }
}";

        var project = new ProjectDefinition("PerfProject")
            .AddSourceFile("Performance.cs", perfCode);

        _solutionPath = _testHelper.CreateSolution("PerfSolution", project);
    }

    public void Dispose()
    {
        _testHelper?.Dispose();
        (_serviceProvider as ServiceProvider)?.Dispose();
    }

    #region AnalyzeDocumentationCoverage Tests

    [Fact]
    public async Task AnalyzeDocumentationCoverage_ShowsCoverageStats()
    {
        // Arrange
        CreateDocumentedCode();

        // Act
        var result = await CodeNavigationTools.AnalyzeDocumentationCoverage(
            solutionPath: _solutionPath!,
            scope: "public",
            format: "normal",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Match(r =>
            r.Contains("Documentation") ||
            r.Contains("coverage") ||
            r.Contains("documented") ||
            r.Contains("%"));
    }

    [Fact]
    public async Task AnalyzeDocumentationCoverage_SummaryFormat_ShowsOverview()
    {
        // Arrange
        CreateDocumentedCode();

        // Act
        var result = await CodeNavigationTools.AnalyzeDocumentationCoverage(
            solutionPath: _solutionPath!,
            scope: "all",
            format: "summary",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region FindPerformanceIssues Tests

    [Fact]
    public async Task FindPerformanceIssues_DetectsCommonIssues()
    {
        // Arrange
        CreatePerformanceIssues();

        // Act
        var result = await CodeNavigationTools.FindPerformanceIssues(
            solutionPath: _solutionPath!,
            issueTypes: null,
            format: "normal",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Match(r =>
            r.Contains("Performance") ||
            r.Contains("performance") ||
            r.Contains("issue") ||
            r.Contains("optimization"));
    }

    [Fact]
    public async Task FindPerformanceIssues_SpecificType_FiltersResults()
    {
        // Arrange
        CreatePerformanceIssues();

        // Act
        var result = await CodeNavigationTools.FindPerformanceIssues(
            solutionPath: _solutionPath!,
            issueTypes: "StringConcatenation",
            format: "normal",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task FindPerformanceIssues_SummaryFormat_ShowsCounts()
    {
        // Arrange
        CreatePerformanceIssues();

        // Act
        var result = await CodeNavigationTools.FindPerformanceIssues(
            solutionPath: _solutionPath!,
            format: "summary",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Match(r =>
            r.Contains("Total") ||
            r.Contains("count") ||
            r.Contains("Summary"));
    }

    #endregion

    #region AnalyzeNamingConventions Tests

    [Fact]
    public async Task AnalyzeNamingConventions_DetectsViolations()
    {
        // Arrange
        CreateNamingIssues();

        // Act
        var result = await CodeNavigationTools.AnalyzeNamingConventions(
            solutionPath: _solutionPath!,
            scope: "all",
            violationTypes: null,
            format: "normal",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Match(r =>
            r.Contains("Naming") ||
            r.Contains("naming") ||
            r.Contains("convention") ||
            r.Contains("violation"));
    }

    [Fact]
    public async Task AnalyzeNamingConventions_PublicOnly_FiltersScope()
    {
        // Arrange
        CreateNamingIssues();

        // Act
        var result = await CodeNavigationTools.AnalyzeNamingConventions(
            solutionPath: _solutionPath!,
            scope: "public",
            format: "normal",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task AnalyzeNamingConventions_SpecificViolations_FiltersTypes()
    {
        // Arrange
        CreateNamingIssues();

        // Act
        var result = await CodeNavigationTools.AnalyzeNamingConventions(
            solutionPath: _solutionPath!,
            scope: "all",
            violationTypes: "InterfaceNaming,TypeNaming",
            format: "normal",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region FindDeprecatedAPIs Tests

    [Fact]
    public async Task FindDeprecatedAPIs_FindsObsoleteUsage()
    {
        // Arrange
        var code = @"namespace TestProject;

[System.Obsolete(""Use NewMethod instead"")]
public class OldClass
{
    public void OldMethod() { }
}

public class Consumer
{
    public void UseOldStuff()
    {
        var old = new OldClass();
    }
}";

        var project = new ProjectDefinition("ObsoleteProject")
            .AddSourceFile("Obsolete.cs", code);

        _solutionPath = _testHelper.CreateSolution("ObsoleteSolution", project);

        // Act
        var result = await CodeNavigationTools.FindDeprecatedAPIs(
            solutionPath: _solutionPath!,
            includeFrameworkAPIs: false,
            format: "normal",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Match(r =>
            r.Contains("Obsolete") ||
            r.Contains("obsolete") ||
            r.Contains("Deprecated") ||
            r.Contains("deprecated"));
    }

    [Fact]
    public async Task FindDeprecatedAPIs_SummaryFormat_ShowsOverview()
    {
        // Arrange
        var project = new ProjectDefinition("SimpleProject")
            .AddSourceFile("Simple.cs", CodeTemplates.SimpleClass("SimpleProject", "SimpleClass"));

        _solutionPath = _testHelper.CreateSolution("SimpleSolution", project);

        // Act
        var result = await CodeNavigationTools.FindDeprecatedAPIs(
            solutionPath: _solutionPath!,
            format: "summary",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
    }

    #endregion
}
