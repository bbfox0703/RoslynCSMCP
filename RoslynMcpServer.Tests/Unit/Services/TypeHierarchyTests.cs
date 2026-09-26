using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Core.Models;
using RoslynMcpServer.Core.Services;
using RoslynMcpServer.Tests.Helpers;
using AdvancedModuleTools = RoslynMcpServer.Advanced.Tools.AdvancedTools;
using FullTools = RoslynMcpServer.Tools.CodeNavigationTools;

namespace RoslynMcpServer.Tests.Unit.Services;

/// <summary>
/// FindImplementations, GetClassHierarchy, and type-name lookup against a three-project solution in which
/// App references Core and Tests references both, so referenced source types are visible to several compilations
/// </summary>
public class TypeHierarchyTests : IDisposable
{
    private const string SolutionPath = "InMemory/TypeHierarchy.sln";

    private const string CoreCode = """
        namespace Core
        {
            public interface IRepository<T> { T Get(int id); }

            public abstract class RepositoryBase<T> : IRepository<T>
            {
                public abstract T Get(int id);
            }

            public abstract class Shape { }

            public interface IAuditable { }

            public class Timer { }

            public class Outer
            {
                public class Middle
                {
                    public class Inner : IAuditable
                    {
                        public void DeepMethod() { }
                    }
                }
            }
        }
        """;

    private const string AppCode = """
        using System;
        using Core;

        namespace App
        {
            public class User { }

            public class UserRepository : RepositoryBase<User>
            {
                public override User Get(int id) => new User();
            }

            public class CachedUserRepository : UserRepository { }

            public class OrderRepository : IRepository<int>
            {
                public int Get(int id) => id;
            }

            public class Circle : Shape { }

            public class Resource : IDisposable
            {
                public void Dispose() { }
            }
        }
        """;

    private const string TestsCode = """
        namespace Tests
        {
            public class FakeRepository : Core.IRepository<string>
            {
                public string Get(int id) => "";
            }
        }
        """;

    private readonly InMemoryCodeAnalysisService _codeAnalysis;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly SymbolSearchService _service;

    public TypeHierarchyTests()
    {
        var solution = new InMemorySolution()
            .AddProject("Core").AddDocument("Core", "Core.cs", CoreCode)
            .AddProject("App", "Core").AddDocument("App", "App.cs", AppCode)
            .AddProject("Tests", "App", "Core").AddDocument("Tests", "Tests.cs", TestsCode);

        solution.AssertCompilesAsync().GetAwaiter().GetResult();

        _codeAnalysis = solution.CreateCodeAnalysisService(SolutionPath);
        _service = new SymbolSearchService(_codeAnalysis, NullLogger<SymbolSearchService>.Instance, _cache);
    }

    public void Dispose()
    {
        _codeAnalysis.Dispose();
        _cache.Dispose();
    }

    #region FindImplementations

    [Fact]
    public async Task FindImplementations_ListsEachTypeOnceUnderItsDeclaringProject()
    {
        // Circle (declared in App) is also visible to the Tests compilation; the old walk listed it twice per compilation
        var results = await _service.FindImplementationsAsync("Shape", SolutionPath);

        results.Should().ContainSingle();
        results[0].ImplementingTypeName.Should().Be("Circle");
        results[0].ProjectName.Should().Be("App");
    }

    [Fact]
    public async Task FindImplementations_FindsImplementersOfGenericInterfaceThroughConstructedTypes()
    {
        var results = await _service.FindImplementationsAsync("IRepository", SolutionPath);

        results.Select(r => (r.ImplementingTypeName, r.ProjectName)).Should().BeEquivalentTo(new[]
        {
            ("CachedUserRepository", "App"),
            ("FakeRepository", "Tests"),
            ("OrderRepository", "App"),
            ("UserRepository", "App")
        });
    }

    [Fact]
    public async Task FindImplementations_IncludeAbstract_AddsAbstractGenericBase()
    {
        var results = await _service.FindImplementationsAsync("IRepository", SolutionPath, includeAbstractImplementations: true);

        results.Should().ContainSingle(r => r.ImplementingTypeName == "RepositoryBase")
            .Which.ProjectName.Should().Be("Core");
        results.Should().HaveCount(5);
    }

    [Fact]
    public async Task FindImplementations_FindsSubclassesOfGenericAbstractClass()
    {
        var results = await _service.FindImplementationsAsync("RepositoryBase", SolutionPath);

        results.Select(r => r.ImplementingTypeName).Should().BeEquivalentTo("CachedUserRepository", "UserRepository");
    }

    [Fact]
    public async Task FindImplementations_TargetNestedTwoLevelsDeep_IsFound()
    {
        var results = await _service.FindImplementationsAsync("IAuditable", SolutionPath);

        results.Should().ContainSingle();
        results[0].ImplementingTypeFullName.Should().Be("Core.Outer.Middle.Inner");
        results[0].ProjectName.Should().Be("Core");
    }

    #endregion

    #region GetClassHierarchy

    [Fact]
    public async Task GetClassHierarchy_FindsDescendantsOfGenericBase_AtEveryLevel()
    {
        var result = await _service.GetClassHierarchyAsync("RepositoryBase", SolutionPath, "descendants");

        result.Should().NotBeNull();
        var userRepository = result!.Descendants.Should().ContainSingle().Subject;
        userRepository.Name.Should().Be("UserRepository");
        userRepository.ProjectName.Should().Be("App");
        userRepository.Depth.Should().Be(1);

        var cached = userRepository.Children.Should().ContainSingle().Subject;
        cached.Name.Should().Be("CachedUserRepository");
        cached.Depth.Should().Be(2);
    }

    [Fact]
    public async Task GetClassHierarchy_GenericInterface_ListsDirectImplementersOnce()
    {
        var result = await _service.GetClassHierarchyAsync("IRepository", SolutionPath, "descendants");

        result!.Descendants.Select(d => (d.Name, d.ProjectName)).Should().BeEquivalentTo(new[]
        {
            ("FakeRepository", "Tests"),
            ("OrderRepository", "App"),
            ("RepositoryBase", "Core")
        });
        result.Descendants.Single(d => d.Name == "RepositoryBase").Children
            .Should().ContainSingle(c => c.Name == "UserRepository");
    }

    [Fact]
    public async Task GetClassHierarchy_DescendantInReferencingProject_IsListedOnceWithDeclaringProject()
    {
        var result = await _service.GetClassHierarchyAsync("Shape", SolutionPath, "descendants");

        var circle = result!.Descendants.Should().ContainSingle().Subject;
        circle.Name.Should().Be("Circle");
        circle.ProjectName.Should().Be("App");
    }

    [Fact]
    public async Task GetClassHierarchy_AncestorsOfSourceTypes_ReportDeclaringProject()
    {
        var result = await _service.GetClassHierarchyAsync("CachedUserRepository", SolutionPath, "ancestors");

        var userRepository = result!.Ancestors.Should().ContainSingle().Subject;
        userRepository.ProjectName.Should().Be("App");
        var repositoryBase = userRepository.Children.Should().ContainSingle(n => n.Name == "RepositoryBase").Subject;
        repositoryBase.FullName.Should().Be("Core.RepositoryBase<App.User>");
        repositoryBase.ProjectName.Should().Be("Core");
    }

    [Fact]
    public async Task GetClassHierarchy_TypeNestedTwoLevelsDeep_IsFound()
    {
        var result = await _service.GetClassHierarchyAsync("inner", SolutionPath);

        result.Should().NotBeNull();
        result!.TypeFullName.Should().Be("Core.Outer.Middle.Inner");
        result.Ancestors.Should().ContainSingle(a => a.Name == "IAuditable");
    }

    [Fact]
    public async Task GetClassHierarchy_SourceTypePreferredOverSameNamedFrameworkType()
    {
        var result = await _service.GetClassHierarchyAsync("Timer", SolutionPath);

        result!.TypeFullName.Should().Be("Core.Timer");
    }

    [Fact]
    public async Task GetClassHierarchy_DirectionIsCaseInsensitive()
    {
        var result = await _service.GetClassHierarchyAsync("Shape", SolutionPath, "DESCENDANTS");

        result!.Descendants.Should().ContainSingle(d => d.Name == "Circle");
        result.Ancestors.Should().BeEmpty();
    }

    [Fact]
    public async Task GetClassHierarchy_InvalidDirection_Throws()
    {
        var act = () => _service.GetClassHierarchyAsync("Shape", SolutionPath, "sideways");

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*sideways*");
    }

    [Fact]
    public async Task GetClassHierarchy_MaxDepth_LimitsDescendantLevels()
    {
        var result = await _service.GetClassHierarchyAsync("RepositoryBase", SolutionPath, "descendants", maxDepth: 1);

        result!.Descendants.Should().ContainSingle().Which.Children.Should().BeEmpty();
    }

    #endregion

    #region Tool output

    [Fact]
    public async Task FullGetClassHierarchy_MixedCaseDirection_ShowsDescendants()
    {
        using var provider = CreateServiceProvider();

        var output = await FullTools.GetClassHierarchy("RepositoryBase", SolutionPath, direction: "Descendants", serviceProvider: provider);

        output.Should().Contain("DESCENDANTS");
        output.Should().Contain("CachedUserRepository");
        output.Should().NotContain("ANCESTORS");
    }

    [Fact]
    public async Task FullGetClassHierarchy_InvalidDirection_ReturnsError()
    {
        using var provider = CreateServiceProvider();

        var output = await FullTools.GetClassHierarchy("Shape", SolutionPath, direction: "sideways", serviceProvider: provider);

        output.Should().StartWith("Error: Invalid direction 'sideways'");
    }

    [Theory]
    [InlineData("compact")]
    [InlineData("normal")]
    [InlineData("text")]
    [InlineData("detailed")]
    public async Task AdvancedGetClassHierarchy_PrintsNestedLevels(string format)
    {
        var output = await AdvancedClassHierarchy("RepositoryBase", format);

        var lines = output.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var userRepositoryLine = lines.Single(l => l.Contains("UserRepository") && !l.Contains("Cached"));
        var cachedLine = lines.Single(l => l.Contains("CachedUserRepository"));

        // The second-level descendant is printed, indented below its parent
        Indentation(cachedLine).Should().BeGreaterThan(Indentation(userRepositoryLine));
        lines.IndexOf(cachedLine).Should().BeGreaterThan(lines.IndexOf(userRepositoryLine));
    }

    [Fact]
    public async Task AdvancedGetClassHierarchy_FormatsDiffer()
    {
        var compact = await AdvancedClassHierarchy("RepositoryBase", "compact");
        var normal = await AdvancedClassHierarchy("RepositoryBase", "normal");
        var detailed = await AdvancedClassHierarchy("RepositoryBase", "detailed");

        compact.Should().Contain("- UserRepository").And.NotContain("App.UserRepository");
        normal.Should().Contain("App.UserRepository (Class) @ App.cs:");
        detailed.Should().Contain("Project: App | Namespace: App");
        detailed.Should().Contain("Accessibility: Public, abstract");
    }

    [Fact]
    public async Task SearchSymbols_FindsMembersOfTypesNestedTwoLevelsDeep()
    {
        var results = await _service.SearchSymbolsAsync("DeepMethod", SolutionPath, "method", ignoreCase: false);

        results.Should().Contain(r => r.FullName == "Core.Outer.Middle.Inner.DeepMethod()");
    }

    #endregion

    private ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<CodeAnalysisService>(_codeAnalysis);
        services.AddSingleton(_service);
        return services.BuildServiceProvider();
    }

    // The tool validates that the solution file exists, so serve the in-memory solution from a real path
    private async Task<string> AdvancedClassHierarchy(string typeName, string format)
    {
        using var placeholder = new PlaceholderSolutionFile();
        _codeAnalysis.Register(placeholder.FilePath, await _codeAnalysis.GetSolutionAsync(SolutionPath));

        return await AdvancedModuleTools.GetClassHierarchy(
            typeName, placeholder.FilePath, format, _service,
            new SecurityValidator(NullLogger<SecurityValidator>.Instance),
            new McpErrorHandler(NullLogger<McpErrorHandler>.Instance));
    }

    private static int Indentation(string line) => line.Length - line.TrimStart().Length;
}
