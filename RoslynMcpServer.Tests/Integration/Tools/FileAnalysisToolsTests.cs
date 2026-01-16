using RoslynMcpServer.Tests.Helpers;
using RoslynMcpServer.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Core.Services;
using Microsoft.Build.Locator;

namespace RoslynMcpServer.Tests.Integration.Tools;

/// <summary>
/// Integration tests for file analysis tools
/// </summary>
public class FileAnalysisToolsTests : IDisposable
{
    private readonly IntegrationTestHelper _testHelper;
    private readonly IServiceProvider _serviceProvider;
    private string? _solutionPath;
    private string? _testFilePath;

    static FileAnalysisToolsTests()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }
    }

    public FileAnalysisToolsTests()
    {
        _testHelper = new IntegrationTestHelper(nameof(FileAnalysisToolsTests));
        _serviceProvider = CreateServiceProvider();
    }

    private IServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddMemoryCache();

        services.AddSingleton<CodeAnalysisService>();
        services.AddSingleton<FileAnalysisService>();
        services.AddSingleton<FileStatisticsAnalyzer>();
        services.AddSingleton<LargeFileAnalyzer>();
        services.AddSingleton<DuplicateCodeAnalyzer>();
        services.AddSingleton<TODOCommentAnalyzer>();
        services.AddSingleton<SecurityValidator>();
        services.AddSingleton<DiagnosticLogger>();

        return services.BuildServiceProvider();
    }

    private void CreateFilesForAnalysis()
    {
        var largeClassCode = @"namespace TestNamespace;

public class LargeClass
{
" + string.Join("\n", Enumerable.Range(1, 200).Select(i =>
    $"    public void Method{i}() {{ /* TODO: Implement method {i} */ }}")) + @"
}";

        var project = new ProjectDefinition("FileTestProject")
            .AddSourceFile("SmallFile.cs",
                CodeTemplates.SimpleClass("FileTestProject", "SmallClass"))
            .AddSourceFile("LargeFile.cs", largeClassCode)
            .AddSourceFile("WithComments.cs", @"
namespace FileTestProject;

// TODO: Refactor this class
public class WithComments
{
    // FIXME: Bug in this method
    public void DoWork()
    {
        // HACK: Temporary workaround
        var x = 1;
    }

    // NOTE: Important business logic
    public void Process() { }
}");

        _solutionPath = _testHelper.CreateSolution("FileTestSolution", project);
        _testFilePath = Path.Combine(_testHelper.TestRootPath, "FileTestProject", "SmallFile.cs");
    }

    public void Dispose()
    {
        _testHelper?.Dispose();
        (_serviceProvider as ServiceProvider)?.Dispose();
    }

    #region GetFileOutline Tests

    [Fact]
    public async Task GetFileOutline_ShowsTypeStructure()
    {
        // Arrange
        CreateFilesForAnalysis();

        // Act
        var result = await CodeNavigationTools.GetFileOutline(
            filePath: _testFilePath!,
            includeMembers: true,
            includeDocumentation: true,
            maxMembers: 10,
            mode: "normal",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Match(r =>
            r.Contains("SmallClass") ||
            r.Contains("class") ||
            r.Contains("Type") ||
            r.Contains("Outline"));
    }

    [Fact]
    public async Task GetFileOutline_CompactMode_MinimalInfo()
    {
        // Arrange
        CreateFilesForAnalysis();

        // Act
        var result = await CodeNavigationTools.GetFileOutline(
            filePath: _testFilePath!,
            mode: "compact",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetFileOutline_InvalidFile_ReturnsError()
    {
        // Arrange
        var invalidPath = "C:\\NonExistent\\File.cs";

        // Act
        var result = await CodeNavigationTools.GetFileOutline(
            filePath: invalidPath,
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().Contain("Error");
    }

    #endregion

    #region GetFileStatistics Tests

    [Fact]
    public async Task GetFileStatistics_ReturnsMetrics()
    {
        // Arrange
        CreateFilesForAnalysis();

        // Act
        var result = await CodeNavigationTools.GetFileStatistics(
            filePath: _testFilePath!,
            format: "normal",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Match(r =>
            r.Contains("Statistics") ||
            r.Contains("lines") ||
            r.Contains("LOC") ||
            r.Contains("complexity"));
    }

    [Fact]
    public async Task GetFileStatistics_SummaryFormat_KeyMetrics()
    {
        // Arrange
        CreateFilesForAnalysis();

        // Act
        var result = await CodeNavigationTools.GetFileStatistics(
            filePath: _testFilePath!,
            format: "summary",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region FindLargeFiles Tests

    [Fact]
    public async Task FindLargeFiles_IdentifiesLargeFiles()
    {
        // Arrange
        CreateFilesForAnalysis();

        // Act
        var result = await CodeNavigationTools.FindLargeFiles(
            solutionPath: _solutionPath!,
            threshold: 50,
            format: "normal",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Match(r =>
            r.Contains("Large") ||
            r.Contains("large") ||
            r.Contains("file") ||
            r.Contains("lines"));
    }

    [Fact]
    public async Task FindLargeFiles_SummaryFormat_ShowsTopFiles()
    {
        // Arrange
        CreateFilesForAnalysis();

        // Act
        var result = await CodeNavigationTools.FindLargeFiles(
            solutionPath: _solutionPath!,
            threshold: 10,
            format: "summary",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region FindDuplicateCode Tests

    [Fact]
    public async Task FindDuplicateCode_DetectsDuplicates()
    {
        // Arrange
        var project = new ProjectDefinition("DuplicateTest")
            .AddSourceFile("File1.cs", @"
namespace DuplicateTest;
public class Class1
{
    public int Calculate(int x)
    {
        int result = 0;
        result += x * 2;
        result += x + 1;
        return result;
    }
}")
            .AddSourceFile("File2.cs", @"
namespace DuplicateTest;
public class Class2
{
    public int Compute(int y)
    {
        int result = 0;
        result += y * 2;
        result += y + 1;
        return result;
    }
}");

        _solutionPath = _testHelper.CreateSolution("DuplicateSolution", project);

        // Act
        var result = await CodeNavigationTools.FindDuplicateCode(
            solutionPath: _solutionPath!,
            minLines: 3,
            similarity: 80,
            format: "normal",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Match(r =>
            r.Contains("Duplicate") ||
            r.Contains("duplicate") ||
            r.Contains("similar") ||
            r.Contains("code"));
    }

    #endregion

    #region FindTODOComments Tests

    [Fact]
    public async Task FindTODOComments_FindsAllCommentTypes()
    {
        // Arrange
        CreateFilesForAnalysis();

        // Act
        var result = await CodeNavigationTools.FindTODOComments(
            solutionPath: _solutionPath!,
            types: "all",
            format: "normal",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Match(r =>
            r.Contains("TODO") ||
            r.Contains("FIXME") ||
            r.Contains("HACK") ||
            r.Contains("comment"));
    }

    [Fact]
    public async Task FindTODOComments_SpecificType_FiltersResults()
    {
        // Arrange
        CreateFilesForAnalysis();

        // Act
        var result = await CodeNavigationTools.FindTODOComments(
            solutionPath: _solutionPath!,
            types: "TODO,FIXME",
            format: "normal",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task FindTODOComments_SummaryFormat_ShowsCounts()
    {
        // Arrange
        CreateFilesForAnalysis();

        // Act
        var result = await CodeNavigationTools.FindTODOComments(
            solutionPath: _solutionPath!,
            types: "all",
            format: "summary",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Match(r =>
            r.Contains("Total") ||
            r.Contains("count") ||
            r.Contains("Summary") ||
            r.Contains("Found") ||
            r.Contains("Comments"));
    }

    #endregion
}
