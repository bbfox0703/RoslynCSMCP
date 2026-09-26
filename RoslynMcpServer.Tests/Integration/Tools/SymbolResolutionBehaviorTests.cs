using Microsoft.Build.Locator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Core.Models;
using RoslynMcpServer.Core.Services;
using RoslynMcpServer.Tests.Helpers;
using RoslynMcpServer.Tools;
using AdvancedModuleTools = RoslynMcpServer.Advanced.Tools.AdvancedTools;
using NavigationModuleTools = RoslynMcpServer.Navigation.Tools.NavigationTools;

namespace RoslynMcpServer.Tests.Integration.Tools;

/// <summary>
/// A real two-project solution shared by every test in <see cref="SymbolResolutionBehaviorTests"/>
/// (creating and restoring it is the slow part).
/// </summary>
public sealed class SymbolResolutionSolutionFixture : IDisposable
{
    public const string ShapesSource = """
        namespace Lib.Geometry;

        public abstract class Shape
        {
            public abstract double Area();
        }

        public class Circle : Shape
        {
            public override double Area() => 3.14;
        }

        public sealed class Square : Shape
        {
            public override double Area() => 1;
        }
        """;

    public const string LegacySource = """
        namespace Lib.Legacy;

        public class Shape
        {
        }

        public class Polygon : Shape
        {
        }
        """;

    public const string DataSource = """
        namespace Lib.Data;

        public class Item
        {
        }

        public interface IRepository
        {
            void Save(Item item);
        }

        public class ItemRepository : IRepository
        {
            public void Save(Item item) { }
        }
        """;

    public const string ServicesSource = """
        using Lib.Data;

        namespace Lib.Services;

        public class UserService
        {
            public void Save(Item item) { }

            public void Save(Item item, bool overwrite) { }

            public void Load(string? path) { }

            public void Load(int? id) { }

            public class Options
            {
                public int Retries { get; set; }
            }
        }

        public class OrderService
        {
            public void Save() { }
        }

        public class Counter
        {
            private int total;

            public int Total => total;
        }

        public class Result
        {
        }

        public class Result<T>
        {
        }

        public class Timer
        {
        }
        """;

    public const string AppSource = """
        using Lib.Geometry;

        namespace App.Geometry;

        public class Triangle : Shape
        {
            public override double Area() => 0.5;
        }

        public class Circle
        {
        }
        """;

    private readonly IntegrationTestHelper _helper;

    public SymbolResolutionSolutionFixture()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }

        _helper = new IntegrationTestHelper(nameof(SymbolResolutionBehaviorTests));

        var lib = new ProjectDefinition("Lib")
            .AddSourceFile("Shapes.cs", ShapesSource)
            .AddSourceFile("Legacy.cs", LegacySource)
            .AddSourceFile("Data.cs", DataSource)
            .AddSourceFile("Services.cs", ServicesSource);
        var app = new ProjectDefinition("App")
            .AddReference("Lib")
            .AddSourceFile("App.cs", AppSource);
        SolutionPath = _helper.CreateSolution("Resolution", lib, app);

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddMemoryCache();
        services.AddSingleton<CodeAnalysisService>();
        services.AddSingleton<SymbolSearchService>();
        services.AddSingleton<SecurityValidator>();
        services.AddSingleton<DiagnosticLogger>();
        services.AddSingleton<McpErrorHandler>();
        Services = services.BuildServiceProvider();
    }

    public string SolutionPath { get; }
    public ServiceProvider Services { get; }

    public SymbolSearchService SearchService => Services.GetRequiredService<SymbolSearchService>();

    public void Dispose()
    {
        Services.Dispose();
        _helper.Dispose();
    }
}

/// <summary>
/// End-to-end checks for how GetSymbolInfo, FindImplementations and GetClassHierarchy resolve a name:
/// source declarations only, qualified and nested names, type arguments and parameter lists, exact-case
/// preference, ambiguity reports, and the Navigation / Advanced / Full tool wrappers.
/// </summary>
public class SymbolResolutionBehaviorTests : IClassFixture<SymbolResolutionSolutionFixture>
{
    private readonly SymbolResolutionSolutionFixture _fixture;

    public SymbolResolutionBehaviorTests(SymbolResolutionSolutionFixture fixture)
    {
        _fixture = fixture;
    }

    private string SolutionPath => _fixture.SolutionPath;

    private Task<SymbolInfo?> GetSymbolInfoAsync(string name) =>
        _fixture.SearchService.GetSymbolInfoAsync(name, SolutionPath);

    private async Task<SymbolResolutionException> ResolutionFailureAsync(Func<Task> action) =>
        (await action.Should().ThrowAsync<SymbolResolutionException>()).Which;

    #region GetSymbolInfo: which declarations can match

    [Fact]
    public async Task GetSymbolInfo_FrameworkOnlyName_IsNotFound()
    {
        // Previously matched System.Exception from the referenced framework assemblies
        (await GetSymbolInfoAsync("Exception")).Should().BeNull();
        (await GetSymbolInfoAsync("IDisposable")).Should().BeNull();
    }

    [Fact]
    public async Task GetSymbolInfo_SourceTypeSharingAFrameworkName_ResolvesToTheSourceType()
    {
        var info = await GetSymbolInfoAsync("Timer");

        info.Should().NotBeNull();
        info!.FullName.Should().Be("Lib.Services.Timer");
        info.Assembly.Should().Be("Lib");
    }

    [Fact]
    public async Task GetSymbolInfo_MemberOfNestedType_IsFound()
    {
        var bySimpleName = await GetSymbolInfoAsync("Retries");
        var byQualifiedName = await GetSymbolInfoAsync("UserService.Options.Retries");
        var nestedType = await GetSymbolInfoAsync("Lib.Services.UserService+Options");

        bySimpleName!.FullName.Should().Be("Lib.Services.UserService.Options.Retries");
        bySimpleName.DeclaringType.Should().Be("Options");
        byQualifiedName!.FullName.Should().Be(bySimpleName.FullName);
        nestedType!.FullName.Should().Be("Lib.Services.UserService.Options");
        nestedType.Kind.Should().Be("NamedType");
    }

    [Fact]
    public async Task GetSymbolInfo_UnknownName_IsNotFound()
    {
        (await GetSymbolInfoAsync("DoesNotExist")).Should().BeNull();
        (await GetSymbolInfoAsync("NoSuchType.Save")).Should().BeNull();
    }

    #endregion

    #region GetSymbolInfo: picking among several matches

    [Fact]
    public async Task GetSymbolInfo_SameNameInSeveralTypes_ReportsAmbiguity()
    {
        var failure = await ResolutionFailureAsync(() => GetSymbolInfoAsync("Save"));

        failure.IsAmbiguous.Should().BeTrue();
        failure.Candidates.Select(c => c.DisplayName).Should().Contain(new[]
        {
            "Lib.Data.IRepository.Save(Lib.Data.Item)",
            "Lib.Data.ItemRepository.Save(Lib.Data.Item)",
            "Lib.Services.OrderService.Save()",
            "Lib.Services.UserService.Save(Lib.Data.Item)",
            "Lib.Services.UserService.Save(Lib.Data.Item, bool)",
        });
        failure.Candidates.Should().OnlyContain(c => c.Kind == "method" && c.ProjectName == "Lib" && c.LineNumber > 0);
        failure.Message.Should().StartWith("'Save' matches 5 declarations in the solution's source.")
            .And.Contain("Lib.Services.OrderService.Save() (method, Services.cs:");
    }

    [Fact]
    public async Task GetSymbolInfo_QualifiedName_NarrowsToContainingType()
    {
        var info = await GetSymbolInfoAsync("OrderService.Save");

        info!.FullName.Should().Be("Lib.Services.OrderService.Save()");
    }

    [Fact]
    public async Task GetSymbolInfo_QualifierMatchesWholeSegmentsOnly()
    {
        // 'Service.Save' must not match UserService.Save or OrderService.Save
        (await GetSymbolInfoAsync("Service.Save")).Should().BeNull();
    }

    [Fact]
    public async Task GetSymbolInfo_SameNameInTwoProjects_ReportsBothProjects()
    {
        var failure = await ResolutionFailureAsync(() => GetSymbolInfoAsync("Geometry.Circle"));

        failure.IsAmbiguous.Should().BeTrue();
        failure.Candidates.Select(c => (c.DisplayName, c.ProjectName)).Should().BeEquivalentTo(new[]
        {
            ("App.Geometry.Circle", "App"),
            ("Lib.Geometry.Circle", "Lib"),
        });
        (await GetSymbolInfoAsync("Lib.Geometry.Circle"))!.Assembly.Should().Be("Lib");
    }

    [Fact]
    public async Task GetSymbolInfo_ParameterList_SelectsAnOverload()
    {
        var one = await GetSymbolInfoAsync("UserService.Save(Item)");
        var two = await GetSymbolInfoAsync("Lib.Services.UserService.Save(Lib.Data.Item item, Boolean overwrite)");

        one!.FullName.Should().Be("Lib.Services.UserService.Save(Lib.Data.Item)");
        two!.FullName.Should().Be("Lib.Services.UserService.Save(Lib.Data.Item, bool)");
        two.Parameters.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetSymbolInfo_ParameterList_DistinguishesNullableValueTypes()
    {
        var byString = await GetSymbolInfoAsync("UserService.Load(string)");
        var byAnnotatedString = await GetSymbolInfoAsync("UserService.Load(string?)");
        var byNullableInt = await GetSymbolInfoAsync("UserService.Load(int?)");

        byString!.FullName.Should().Be("Lib.Services.UserService.Load(string?)");
        byAnnotatedString!.FullName.Should().Be(byString.FullName);
        byNullableInt!.FullName.Should().Be("Lib.Services.UserService.Load(int?)");
    }

    [Fact]
    public async Task GetSymbolInfo_ParameterListMatchingNoOverload_ListsTheOverloads()
    {
        var failure = await ResolutionFailureAsync(() => GetSymbolInfoAsync("UserService.Load(int)"));

        failure.IsAmbiguous.Should().BeFalse();
        failure.Candidates.Select(c => c.DisplayName).Should().BeEquivalentTo(
            "Lib.Services.UserService.Load(string?)",
            "Lib.Services.UserService.Load(int?)");
        failure.Message.Should().StartWith("No type or member in the solution's source matches 'UserService.Load(int)'.");
    }

    [Fact]
    public async Task GetSymbolInfo_EveryListedCandidateName_ResolvesToThatCandidate()
    {
        var failure = await ResolutionFailureAsync(() => GetSymbolInfoAsync("Save"));

        foreach (var candidate in failure.Candidates)
        {
            var info = await GetSymbolInfoAsync(candidate.DisplayName);
            info.Should().NotBeNull(because: $"'{candidate.DisplayName}' was offered as a name to call again with");
            info!.FullName.Should().Be(candidate.DisplayName);
        }
    }

    [Fact]
    public async Task GetSymbolInfo_PrefersExactCase_ThenReportsAmbiguity()
    {
        var exact = await GetSymbolInfoAsync("Total");
        var lowerExact = await GetSymbolInfoAsync("total");
        var failure = await ResolutionFailureAsync(() => GetSymbolInfoAsync("TOTAL"));

        exact!.Kind.Should().Be("Property");
        lowerExact!.Kind.Should().Be("Field");
        // No exact-case match, so the property and the field tie
        failure.IsAmbiguous.Should().BeTrue();
        failure.Candidates.Select(c => c.Kind).Should().BeEquivalentTo("property", "field");
    }

    [Fact]
    public async Task GetSymbolInfo_CaseInsensitiveFallback_ResolvesAUniqueMatch()
    {
        var info = await GetSymbolInfoAsync("orderservice");

        info!.FullName.Should().Be("Lib.Services.OrderService");
    }

    [Fact]
    public async Task GetSymbolInfo_GenericArity_SelectsBetweenGenericAndNonGenericTypes()
    {
        var plain = await GetSymbolInfoAsync("Result");
        var angle = await GetSymbolInfoAsync("Result<T>");
        var backtick = await GetSymbolInfoAsync("Lib.Services.Result`1");

        plain!.FullName.Should().Be("Lib.Services.Result");
        angle!.FullName.Should().Be("Lib.Services.Result<T>");
        backtick!.FullName.Should().Be("Lib.Services.Result<T>");
    }

    #endregion

    #region FindImplementations

    [Fact]
    public async Task FindImplementations_NameSharedWithConcreteClass_UsesTheAbstractClass()
    {
        // Lib.Legacy.Shape is concrete, so only Lib.Geometry.Shape can be the target
        var results = await _fixture.SearchService.FindImplementationsAsync("Shape", SolutionPath);

        var names = results.Select(r => r.ImplementingTypeFullName).ToList();
        names.Should().Contain("Lib.Geometry.Circle")
            .And.Contain("Lib.Geometry.Square")
            .And.Contain("App.Geometry.Triangle")
            .And.NotContain("Lib.Legacy.Polygon");
        results.Should().OnlyContain(r => r.InterfaceOrBaseTypeName == "Shape");
    }

    [Fact]
    public async Task FindImplementations_ConcreteClass_ListsTheRejectedDeclaration()
    {
        var failure = await ResolutionFailureAsync(() =>
            _fixture.SearchService.FindImplementationsAsync("UserService", SolutionPath));

        failure.IsAmbiguous.Should().BeFalse();
        var candidate = failure.Candidates.Should().ContainSingle().Which;
        candidate.DisplayName.Should().Be("Lib.Services.UserService");
        candidate.Kind.Should().Be("class");
        failure.Message.Should().StartWith("No interface or abstract class in the solution's source matches 'UserService'.");
    }

    [Fact]
    public async Task FindImplementations_FrameworkOrUnknownType_IsReportedAsNotFound()
    {
        var framework = await ResolutionFailureAsync(() =>
            _fixture.SearchService.FindImplementationsAsync("IDisposable", SolutionPath));
        var unknown = await ResolutionFailureAsync(() =>
            _fixture.SearchService.FindImplementationsAsync("IDoesNotExist", SolutionPath));

        framework.Candidates.Should().BeEmpty();
        framework.Message.Should().StartWith("No interface or abstract class named 'IDisposable' is declared in the solution's source.");
        unknown.Candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task FindImplementations_QualifiedInterfaceName_Resolves()
    {
        var results = await _fixture.SearchService.FindImplementationsAsync("Lib.Data.IRepository", SolutionPath);

        results.Select(r => r.ImplementingTypeFullName).Should().Contain("Lib.Data.ItemRepository");
    }

    #endregion

    #region GetClassHierarchy

    [Fact]
    public async Task GetClassHierarchy_SameNameInTwoNamespaces_ReportsAmbiguity()
    {
        var failure = await ResolutionFailureAsync(() =>
            _fixture.SearchService.GetClassHierarchyAsync("Shape", SolutionPath));

        failure.IsAmbiguous.Should().BeTrue();
        failure.Candidates.Select(c => (c.DisplayName, c.Kind)).Should().BeEquivalentTo(new[]
        {
            ("Lib.Geometry.Shape", "abstract class"),
            ("Lib.Legacy.Shape", "class"),
        });
    }

    [Fact]
    public async Task GetClassHierarchy_QualifiedName_UsesThatType()
    {
        var legacy = await _fixture.SearchService.GetClassHierarchyAsync("Legacy.Shape", SolutionPath);

        legacy!.TypeFullName.Should().Be("Lib.Legacy.Shape");
        legacy.Descendants.Select(d => d.FullName).Should().Contain("Lib.Legacy.Polygon")
            .And.NotContain("Lib.Geometry.Circle");
    }

    [Fact]
    public async Task GetClassHierarchy_FrameworkOnlyName_IsNotFound()
    {
        (await _fixture.SearchService.GetClassHierarchyAsync("Exception", SolutionPath)).Should().BeNull();
    }

    [Fact]
    public async Task GetClassHierarchy_MemberName_ListsTheRejectedDeclarations()
    {
        var failure = await ResolutionFailureAsync(() =>
            _fixture.SearchService.GetClassHierarchyAsync("OrderService.Save", SolutionPath));

        failure.IsAmbiguous.Should().BeFalse();
        failure.Candidates.Should().ContainSingle().Which.Kind.Should().Be("method");
    }

    [Fact]
    public async Task GetClassHierarchy_NestedAndGenericTypes_Resolve()
    {
        var nested = await _fixture.SearchService.GetClassHierarchyAsync("UserService.Options", SolutionPath);
        var generic = await _fixture.SearchService.GetClassHierarchyAsync("Result<T>", SolutionPath);

        nested!.TypeFullName.Should().Be("Lib.Services.UserService.Options");
        generic!.TypeFullName.Should().Be("Lib.Services.Result<T>");
    }

    #endregion

    #region Tool wrappers

    [Fact]
    public async Task FullGetSymbolInfo_AmbiguousName_ListsCandidatesInsteadOfAnError()
    {
        var result = await CodeNavigationTools.GetSymbolInfo(
            symbolName: "Save",
            solutionPath: SolutionPath,
            serviceProvider: _fixture.Services);

        result.Should().StartWith("'Save' matches 5 declarations")
            .And.Contain("Lib.Services.UserService.Save(Lib.Data.Item, bool)");
    }

    [Fact]
    public async Task FullGetSymbolInfo_UnknownName_ReturnsSymbolNotFound()
    {
        var result = await CodeNavigationTools.GetSymbolInfo(
            symbolName: "Exception",
            solutionPath: SolutionPath,
            serviceProvider: _fixture.Services);

        result.Should().Be("Symbol not found.");
    }

    [Fact]
    public async Task NavigationGetSymbolInfo_Overload_DescribesTheSelectedOverload()
    {
        var result = await NavigationModuleTools.GetSymbolInfo(
            "UserService.Save(Item, bool)",
            SolutionPath,
            detailLevel: "full",
            searchService: _fixture.SearchService,
            validator: _fixture.Services.GetRequiredService<SecurityValidator>(),
            errorHandler: _fixture.Services.GetRequiredService<McpErrorHandler>(),
            logger: NullLogger<NavigationModuleTools>.Instance);

        result.Should().Contain("# Save").And.Contain("overwrite");
    }

    [Fact]
    public async Task NavigationFindImplementations_ReportsWhyNothingWasSearched()
    {
        var concrete = await CallNavigationFindImplementations("UserService");
        var framework = await CallNavigationFindImplementations("IDisposable");
        var found = await CallNavigationFindImplementations("IRepository");

        concrete.Should().Contain("No interface or abstract class in the solution's source matches 'UserService'")
            .And.Contain("Lib.Services.UserService (class");
        framework.Should().Contain("No interface or abstract class named 'IDisposable'");
        found.Should().Contain("ItemRepository");
    }

    [Fact]
    public async Task FullFindImplementations_ConcreteClass_IsNotReportedAsNoImplementations()
    {
        var result = await CodeNavigationTools.FindImplementations(
            typeName: "UserService",
            solutionPath: SolutionPath,
            serviceProvider: _fixture.Services);

        result.Should().NotContain("No implementations found")
            .And.Contain("Lib.Services.UserService (class");
    }

    [Fact]
    public async Task AdvancedGetClassHierarchy_AmbiguousName_ListsCandidates()
    {
        var result = await AdvancedModuleTools.GetClassHierarchy(
            "Shape",
            SolutionPath,
            searchService: _fixture.SearchService,
            errorHandler: _fixture.Services.GetRequiredService<McpErrorHandler>());

        result.Should().StartWith("'Shape' matches 2 declarations")
            .And.Contain("Lib.Geometry.Shape (abstract class")
            .And.Contain("Lib.Legacy.Shape (class");
    }

    [Fact]
    public async Task FullGetClassHierarchy_QualifiedName_ShowsDescendants()
    {
        var result = await CodeNavigationTools.GetClassHierarchy(
            typeName: "Lib.Legacy.Shape",
            solutionPath: SolutionPath,
            direction: "descendants",
            serviceProvider: _fixture.Services);

        result.Should().Contain("Polygon").And.NotContain("Circle");
    }

    private Task<string> CallNavigationFindImplementations(string typeName) =>
        NavigationModuleTools.FindImplementations(
            typeName,
            SolutionPath,
            searchService: _fixture.SearchService,
            validator: _fixture.Services.GetRequiredService<SecurityValidator>(),
            errorHandler: _fixture.Services.GetRequiredService<McpErrorHandler>(),
            logger: NullLogger<NavigationModuleTools>.Instance);

    #endregion
}
