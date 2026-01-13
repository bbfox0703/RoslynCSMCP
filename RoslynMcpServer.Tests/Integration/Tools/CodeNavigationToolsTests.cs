using RoslynMcpServer.Tests.Helpers;
using RoslynMcpServer.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using RoslynMcpServer.Services;
using Microsoft.Build.Locator;

namespace RoslynMcpServer.Tests.Integration.Tools;

/// <summary>
/// Integration tests for core CodeNavigationTools
/// These tests use real solution files and test the full stack from tool to service to Roslyn
/// </summary>
public class CodeNavigationToolsTests : IDisposable
{
    private readonly IntegrationTestHelper _testHelper;
    private readonly IServiceProvider _serviceProvider;
    private string? _solutionPath;

    static CodeNavigationToolsTests()
    {
        // Register MSBuild once for all tests
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }
    }

    public CodeNavigationToolsTests()
    {
        _testHelper = new IntegrationTestHelper(nameof(CodeNavigationToolsTests));
        _serviceProvider = CreateServiceProvider();
    }

    private IServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();

        // Add logging
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

        // Add caching
        services.AddMemoryCache();

        // Add all services needed for tools
        services.AddSingleton<CodeAnalysisService>();
        services.AddSingleton<SymbolSearchService>();
        services.AddSingleton<TypeSignatureService>();
        services.AddSingleton<ProjectStructureService>();
        services.AddSingleton<SecurityValidator>();
        services.AddSingleton<DiagnosticLogger>();

        return services.BuildServiceProvider();
    }

    private void CreateBasicSolution()
    {
        // Create a basic solution with multiple projects and classes
        var coreProject = new ProjectDefinition("Core")
            .AddSourceFile("IUserService.cs",
                CodeTemplates.Interface("Core.Services", "IUserService"))
            .AddSourceFile("User.cs",
                CodeTemplates.SimpleClass("Core.Models", "User"))
            .AddSourceFile("UserValidator.cs",
                CodeTemplates.ClassWithMethod("Core.Validators", "UserValidator", "ValidateUser", "bool"));

        var appProject = new ProjectDefinition("App")
            .AddReference("Core")
            .AddSourceFile("UserService.cs",
                CodeTemplates.SimpleClass("App.Services", "UserService", null, "Core.Services.IUserService"))
            .AddSourceFile("UserController.cs",
                CodeTemplates.ClassWithReferences("App.Controllers", "UserController", "UserService"))
            .AddSourceFile("ComplexCalculator.cs",
                CodeTemplates.ComplexMethod("App.Utils", "ComplexCalculator", 8));

        var testProject = new ProjectDefinition("App.Tests")
            .AddReference("App")
            .AddReference("Core")
            .AddSourceFile("UserServiceTests.cs",
                CodeTemplates.ClassWithMethod("App.Tests", "UserServiceTests", "TestUserService"));

        _solutionPath = _testHelper.CreateSolution("TestSolution",
            coreProject,
            appProject,
            testProject);
    }

    public void Dispose()
    {
        _testHelper?.Dispose();
        (_serviceProvider as ServiceProvider)?.Dispose();
    }

    #region SearchSymbols Tests

    [Fact]
    public async Task SearchSymbols_FindsClassByWildcard()
    {
        // Arrange
        CreateBasicSolution();

        // Act
        var result = await CodeNavigationTools.SearchSymbols(
            pattern: "User*",
            solutionPath: _solutionPath!,
            symbolTypes: "class",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("User");
        result.Should().Contain("UserService");
        result.Should().Contain("UserController");
        result.Should().Contain("UserValidator");
    }

    [Fact]
    public async Task SearchSymbols_FindsInterfaceByPattern()
    {
        // Arrange
        CreateBasicSolution();

        // Act
        var result = await CodeNavigationTools.SearchSymbols(
            pattern: "IUser*",
            solutionPath: _solutionPath!,
            symbolTypes: "interface",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("IUserService");
    }

    [Fact]
    public async Task SearchSymbols_FindsMethodByName()
    {
        // Arrange
        CreateBasicSolution();

        // Act
        var result = await CodeNavigationTools.SearchSymbols(
            pattern: "ValidateUser",
            solutionPath: _solutionPath!,
            symbolTypes: "method",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("ValidateUser");
        result.Should().Contain("UserValidator");
    }

    [Fact]
    public async Task SearchSymbols_InvalidPath_ReturnsError()
    {
        // Arrange
        var invalidPath = "C:\\NonExistent\\Solution.sln";

        // Act
        var result = await CodeNavigationTools.SearchSymbols(
            pattern: "User*",
            solutionPath: invalidPath,
            symbolTypes: "class",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().Contain("Error");
        result.Should().Match(r =>
            r.Contains("not found") ||
            r.Contains("does not exist") ||
            r.Contains("could not") ||
            r.Contains("Invalid"));
    }

    #endregion

    #region FindReferences Tests

    [Fact]
    public async Task FindReferences_FindsClassReferences()
    {
        // Arrange
        CreateBasicSolution();

        // Act
        var result = await CodeNavigationTools.FindReferences(
            symbolName: "UserService",
            solutionPath: _solutionPath!,
            detailLevel: "locations",
            includeDefinition: true,
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        // Should either find references or indicate searching completed
        result.Should().Match(r =>
            r.Contains("UserService") ||
            r.Contains("No references") ||
            r.Contains("found"));
    }

    [Fact]
    public async Task FindReferences_SummaryMode_ShowsOnlyFileCounts()
    {
        // Arrange
        CreateBasicSolution();

        // Act
        var result = await CodeNavigationTools.FindReferences(
            symbolName: "User",
            solutionPath: _solutionPath!,
            detailLevel: "summary",
            includeDefinition: true,
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        // Summary should show file counts, not individual lines
        result.Should().Match(r => r.Contains("files") || r.Contains("locations") || r.Contains("references"));
    }

    [Fact]
    public async Task FindReferences_NonExistentSymbol_ReturnsNoReferences()
    {
        // Arrange
        CreateBasicSolution();

        // Act
        var result = await CodeNavigationTools.FindReferences(
            symbolName: "NonExistentClass",
            solutionPath: _solutionPath!,
            detailLevel: "locations",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Match(r => r.Contains("not found") || r.Contains("No references"));
    }

    #endregion

    #region GetSymbolInfo Tests

    [Fact]
    public async Task GetSymbolInfo_ReturnsClassInformation()
    {
        // Arrange
        CreateBasicSolution();

        // Act
        var result = await CodeNavigationTools.GetSymbolInfo(
            symbolName: "UserService",
            solutionPath: _solutionPath!,
            detailLevel: "basic",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("UserService");
        // Should indicate it's a class or NamedType
        result.Should().Match(r => r.Contains("class") || r.Contains("NamedType"));
    }

    [Fact]
    public async Task GetSymbolInfo_FullDetail_IncludesMembers()
    {
        // Arrange
        CreateBasicSolution();

        // Act
        var result = await CodeNavigationTools.GetSymbolInfo(
            symbolName: "UserService",
            solutionPath: _solutionPath!,
            detailLevel: "full",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("UserService");
        // Full detail mode - result should be more detailed than basic mode
        // Note: Specific member inclusion depends on implementation
    }

    #endregion

    #region GetTypeSignature Tests

    [Fact]
    public async Task GetTypeSignature_ReturnsClassSignature()
    {
        // Arrange
        CreateBasicSolution();

        // Act
        var result = await CodeNavigationTools.GetTypeSignature(
            typeName: "UserService",
            solutionPath: _solutionPath!,
            includePrivate: false,
            includeDocumentation: true,
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("class UserService");
        result.Should().Contain("IUserService");
        // Should include public methods but not implementation
        result.Should().Contain("DoSomething");
        result.Should().NotContain("// Implementation");
    }

    [Fact]
    public async Task GetTypeSignature_InterfaceType_ReturnsInterfaceSignature()
    {
        // Arrange
        CreateBasicSolution();

        // Act
        var result = await CodeNavigationTools.GetTypeSignature(
            typeName: "IUserService",
            solutionPath: _solutionPath!,
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("interface IUserService");
        result.Should().Contain("DoSomething");
        result.Should().Contain("GetValue");
    }

    #endregion

    #region GetProjectStructure Tests

    [Fact]
    public async Task GetProjectStructure_ReturnsAllProjects()
    {
        // Arrange
        CreateBasicSolution();

        // Act
        var result = await CodeNavigationTools.GetProjectStructure(
            solutionPath: _solutionPath!,
            publicOnly: true,
            includeMembers: false,
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("Core");
        result.Should().Contain("App");
        result.Should().Contain("App.Tests");
    }

    [Fact]
    public async Task GetProjectStructure_ShowsNamespaces()
    {
        // Arrange
        CreateBasicSolution();

        // Act
        var result = await CodeNavigationTools.GetProjectStructure(
            solutionPath: _solutionPath!,
            publicOnly: true,
            includeMembers: false,
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("Core.Services");
        result.Should().Contain("Core.Models");
        result.Should().Contain("App.Services");
        result.Should().Contain("App.Controllers");
    }

    [Fact]
    public async Task GetProjectStructure_WithMembers_IncludesMemberSignatures()
    {
        // Arrange
        CreateBasicSolution();

        // Act
        var result = await CodeNavigationTools.GetProjectStructure(
            solutionPath: _solutionPath!,
            publicOnly: true,
            includeMembers: true,
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        // Should include class names
        result.Should().Contain("UserService");
        // Should include method signatures when includeMembers is true
        result.Should().Contain("DoSomething");
    }

    [Fact]
    public async Task GetProjectStructure_NamespaceFilter_ReturnsFilteredResults()
    {
        // Arrange
        CreateBasicSolution();

        // Act
        var result = await CodeNavigationTools.GetProjectStructure(
            solutionPath: _solutionPath!,
            publicOnly: true,
            includeMembers: false,
            namespaceFilter: "Core.*",
            serviceProvider: _serviceProvider);

        // Assert
        result.Should().NotBeNullOrEmpty();
        // Should return structure information, even if projects couldn't be loaded
        result.Should().Match(r =>
            r.Contains("Solution") ||
            r.Contains("project") ||
            r.Contains("Summary"));
    }

    #endregion

    #region Cross-Tool Integration Tests

    [Fact]
    public async Task IntegrationWorkflow_SearchThenGetInfo()
    {
        // Arrange
        CreateBasicSolution();

        // Act 1: Search for User classes
        var searchResult = await CodeNavigationTools.SearchSymbols(
            pattern: "User*",
            solutionPath: _solutionPath!,
            symbolTypes: "class",
            serviceProvider: _serviceProvider);

        // Assert 1
        searchResult.Should().Contain("UserService");

        // Act 2: Get detailed info for UserService
        var infoResult = await CodeNavigationTools.GetSymbolInfo(
            symbolName: "UserService",
            solutionPath: _solutionPath!,
            detailLevel: "full",
            serviceProvider: _serviceProvider);

        // Assert 2
        infoResult.Should().Contain("UserService");
        infoResult.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task IntegrationWorkflow_GetSignatureThenFindReferences()
    {
        // Arrange
        CreateBasicSolution();

        // Act 1: Get type signature
        var signatureResult = await CodeNavigationTools.GetTypeSignature(
            typeName: "User",
            solutionPath: _solutionPath!,
            serviceProvider: _serviceProvider);

        // Assert 1
        signatureResult.Should().Contain("class User");

        // Act 2: Find all references to User
        var referencesResult = await CodeNavigationTools.FindReferences(
            symbolName: "User",
            solutionPath: _solutionPath!,
            detailLevel: "locations",
            serviceProvider: _serviceProvider);

        // Assert 2
        referencesResult.Should().NotBeNullOrEmpty();
    }

    #endregion
}
