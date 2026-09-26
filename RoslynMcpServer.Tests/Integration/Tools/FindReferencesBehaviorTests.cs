using Microsoft.Build.Locator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Core.Models;
using RoslynMcpServer.Core.Services;
using RoslynMcpServer.Tests.Helpers;
using RoslynMcpServer.Tools;
using AdvancedModuleTools = RoslynMcpServer.Advanced.Tools.AdvancedTools;
using NavigationModuleTools = RoslynMcpServer.Navigation.Tools.NavigationTools;

namespace RoslynMcpServer.Tests.Integration.Tools;

/// <summary>
/// Two real solutions shared by every test in <see cref="FindReferencesBehaviorTests"/>
/// (creating and restoring them is the slow part).
/// </summary>
public sealed class FindReferencesSolutionFixture : IDisposable
{
    public const string CounterSource = """
        namespace Lib.Models;

        public class Counter
        {
            private int _count;
            private int total;

            public int Total => total;

            public int Value { get; set; }

            public void Increment()
            {
                _count++;
            }

            public void Load(string text)
            {
                int.TryParse(text, out _count);
            }

            public void Assign(int v)
            {
                _count = v;
            }

            public void Add(int v)
            {
                _count += v;
            }

            public void Reset(int other)
            {
                (_count, total) = (other, 0);
            }

            public int Snapshot()
            {
                var copy = 0;
                copy = _count + 1;
                return copy;
            }

            public bool IsZero()
            {
                return _count == 0;
            }

            public void SetTotal(int t)
            {
                total = t;
            }
        }

        public class Unused
        {
        }
        """;

    public const string HolderSource = """
        namespace Lib.Models;

        public class Holder
        {
            public int Value;

            public int ReadBoth(Counter counter, int? maybe)
            {
                var fromHolder = Value;
                var fromCounter = counter.Value;
                var fromNullable = maybe.Value;
                return fromHolder + fromCounter + fromNullable;
            }
        }
        """;

    public const string UsersSource = """
        namespace Lib.Models;

        public class PublicApi
        {
            public Counter CreatePublic() => new Counter();

            private Counter CreatePrivate() => new Counter();
        }

        internal class InternalUser
        {
            public Counter CreateInternal() => new Counter();
        }
        """;

    public const string ConsumerSource = """
        using Lib.Models;

        namespace App;

        public class Consumer
        {
            public int Run()
            {
                var counter = new Counter();
                counter.Value = 5;
                counter.Increment();
                var sum = counter.Total;
                return counter.Value + sum;
            }
        }
        """;

    public const string CounterTestsSource = """
        using Lib.Models;

        namespace App.Tests;

        public class CounterTests
        {
            public void Increments()
            {
                var counter = new Counter();
                counter.Increment();
            }
        }
        """;

    public const string StandaloneSource = """
        namespace Standalone;

        public class Counter
        {
            public void Increment() { }
        }

        public class Runner
        {
            public void Go() => new Counter().Increment();
        }
        """;

    private readonly IntegrationTestHelper _helper;

    public FindReferencesSolutionFixture()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }

        _helper = new IntegrationTestHelper(nameof(FindReferencesBehaviorTests));

        var lib = new ProjectDefinition("Lib")
            .AddSourceFile("Counter.cs", CounterSource)
            .AddSourceFile("Holder.cs", HolderSource)
            .AddSourceFile("Users.cs", UsersSource);
        var app = new ProjectDefinition("App")
            .AddReference("Lib")
            .AddSourceFile("Consumer.cs", ConsumerSource);
        var tests = new ProjectDefinition("App.Tests")
            .AddReference("Lib")
            .AddSourceFile("CounterTests.cs", CounterTestsSource);
        SolutionA = _helper.CreateSolution("SolA", lib, app, tests);

        var standalone = new ProjectDefinition("Standalone")
            .AddSourceFile("Standalone.cs", StandaloneSource);
        SolutionB = _helper.CreateSolution("SolB", standalone);

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

    public string SolutionA { get; }
    public string SolutionB { get; }
    public ServiceProvider Services { get; }

    public SymbolSearchService SearchService => Services.GetRequiredService<SymbolSearchService>();

    public void Dispose()
    {
        Services.Dispose();
        _helper.Dispose();
    }
}

/// <summary>
/// End-to-end checks for the FindReferences family: declaration sites, source-only and qualified
/// name resolution, per-reference filters, and the Navigation / Advanced / Full tool wrappers.
/// </summary>
public class FindReferencesBehaviorTests : IClassFixture<FindReferencesSolutionFixture>
{
    private readonly FindReferencesSolutionFixture _fixture;

    public FindReferencesBehaviorTests(FindReferencesSolutionFixture fixture)
    {
        _fixture = fixture;
    }

    private static List<string> Lines(IEnumerable<ReferenceResult> results) =>
        results.Select(r => r.LineText.Trim()).ToList();

    #region Service: declaration sites and name resolution

    [Fact]
    public async Task FindReferences_IncludeDefinition_ReturnsDeclarationSites()
    {
        var withDefinition = (await _fixture.SearchService.FindReferencesAsync("Counter", _fixture.SolutionA, includeDefinition: true)).ToList();
        var withoutDefinition = (await _fixture.SearchService.FindReferencesAsync("Counter", _fixture.SolutionA, includeDefinition: false)).ToList();

        var definition = withDefinition.Should().ContainSingle(r => r.IsDefinition).Which;
        definition.LineText.Trim().Should().Be("public class Counter");
        definition.ProjectName.Should().Be("Lib");
        definition.ReferenceKind.Should().Be("Definition");

        withoutDefinition.Should().NotContain(r => r.IsDefinition);
        Lines(withoutDefinition).Should().NotContain("public class Counter");
        withoutDefinition.Should().HaveCount(withDefinition.Count - 1);
    }

    [Fact]
    public async Task FindReferences_DoesNotMatchFrameworkMembers()
    {
        var results = await _fixture.SearchService.FindReferencesAsync("Value", _fixture.SolutionA, includeDefinition: false);

        var lines = Lines(results);
        lines.Should().Contain("var fromHolder = Value;");
        lines.Should().Contain("var fromCounter = counter.Value;");
        lines.Should().Contain("counter.Value = 5;");
        // Nullable<T>.Value lives in the framework and must not be merged in
        lines.Should().NotContain("var fromNullable = maybe.Value;");
    }

    [Fact]
    public async Task FindReferences_QualifiedName_NarrowsToContainingType()
    {
        var holderValue = Lines(await _fixture.SearchService.FindReferencesAsync("Holder.Value", _fixture.SolutionA, includeDefinition: false));
        var counterValue = Lines(await _fixture.SearchService.FindReferencesAsync("Lib.Models.Counter.Value", _fixture.SolutionA, includeDefinition: false));

        holderValue.Should().Equal("var fromHolder = Value;");
        counterValue.Should().Contain("counter.Value = 5;")
            .And.Contain("var fromCounter = counter.Value;")
            .And.NotContain("var fromHolder = Value;");
    }

    [Fact]
    public async Task SearchReferences_ReportsWhetherTheSymbolExists()
    {
        var unused = await _fixture.SearchService.SearchReferencesAsync("Unused", _fixture.SolutionA, includeDefinition: false);
        var missing = await _fixture.SearchService.SearchReferencesAsync("DoesNotExist", _fixture.SolutionA, includeDefinition: true);

        unused.MatchedSymbolCount.Should().Be(1);
        unused.References.Should().BeEmpty();
        missing.MatchedSymbolCount.Should().Be(0);
        missing.References.Should().BeEmpty();
    }

    [Fact]
    public async Task FindReferences_PrefersExactCase_FallsBackToIgnoringCase()
    {
        var exact = Lines(await _fixture.SearchService.FindReferencesAsync("Total", _fixture.SolutionA, includeDefinition: false));
        var anyCase = Lines(await _fixture.SearchService.FindReferencesAsync("TOTAL", _fixture.SolutionA, includeDefinition: false));

        // 'Total' is the property only; the private field 'total' is not merged in
        exact.Should().Equal("var sum = counter.Total;");
        // No exact match for 'TOTAL', so both the property and the field match
        anyCase.Should().Contain("var sum = counter.Total;").And.Contain("total = t;");
    }

    #endregion

    #region Service: filters

    [Fact]
    public async Task FindReferencesFiltered_WritesOnly_ClassifiesEachReference()
    {
        var results = await _fixture.SearchService.FindReferencesFilteredAsync(
            "_count", _fixture.SolutionA, includeDefinition: true, writesOnly: true);

        Lines(results).Should().BeEquivalentTo(
            "_count++;",
            "int.TryParse(text, out _count);",
            "_count = v;",
            "_count += v;",
            "(_count, total) = (other, 0);");
        results.Should().NotContain(r => r.IsDefinition);
    }

    [Fact]
    public async Task FindReferencesFiltered_WritesOnly_KeepsPropertyWrites()
    {
        var results = await _fixture.SearchService.FindReferencesFilteredAsync(
            "Counter.Value", _fixture.SolutionA, includeDefinition: true, writesOnly: true);

        Lines(results).Should().Equal("counter.Value = 5;");
    }

    [Fact]
    public async Task FindReferencesFiltered_CrossProjectOnly_DropsDeclaringProject()
    {
        var all = (await _fixture.SearchService.FindReferencesAsync("Counter", _fixture.SolutionA, includeDefinition: true)).ToList();
        var cross = (await _fixture.SearchService.FindReferencesFilteredAsync(
            "Counter", _fixture.SolutionA, includeDefinition: true, crossProjectOnly: true)).ToList();

        all.Should().Contain(r => r.ProjectName == "Lib");
        cross.Should().NotBeEmpty();
        cross.Select(r => r.ProjectName).Distinct().Should().BeEquivalentTo("App", "App.Tests");
        cross.Should().NotContain(r => r.IsDefinition);
    }

    [Fact]
    public async Task FindReferencesFiltered_PublicOnly_FiltersEachReferenceByContext()
    {
        var results = await _fixture.SearchService.FindReferencesFilteredAsync(
            "Counter", _fixture.SolutionA, includeDefinition: true, publicOnly: true);

        var lines = Lines(results);
        lines.Should().Contain("public class Counter");
        lines.Should().Contain("public Counter CreatePublic() => new Counter();");
        lines.Should().Contain("public int ReadBoth(Counter counter, int? maybe)");
        lines.Should().Contain("var counter = new Counter();");
        lines.Should().NotContain("private Counter CreatePrivate() => new Counter();");
        lines.Should().NotContain("public Counter CreateInternal() => new Counter();");
    }

    [Fact]
    public async Task FindReferencesFiltered_ProjectFilters_ApplyToEachReference()
    {
        var noTests = await _fixture.SearchService.FindReferencesFilteredAsync(
            "Counter", _fixture.SolutionA, includeDefinition: true, excludeTests: true);
        var appOnly = await _fixture.SearchService.FindReferencesFilteredAsync(
            "Counter", _fixture.SolutionA, includeDefinition: true, projectFilter: "App*");

        noTests.Select(r => r.ProjectName).Distinct().Should().BeEquivalentTo("Lib", "App");
        appOnly.Select(r => r.ProjectName).Distinct().Should().BeEquivalentTo("App", "App.Tests");
    }

    [Fact]
    public async Task FindReferencesAcrossSolutions_RecordsTheSolutionOfEachReference()
    {
        var results = (await _fixture.SearchService.FindReferencesAcrossSolutionsAsync(
            "Counter", new[] { _fixture.SolutionA, _fixture.SolutionB }, includeDefinition: true)).ToList();

        results.Where(r => r.ProjectName == "Standalone").Should().OnlyContain(r => r.SolutionPath == _fixture.SolutionB);
        results.Where(r => r.ProjectName != "Standalone").Should().OnlyContain(r => r.SolutionPath == _fixture.SolutionA);
        results.Where(r => r.IsDefinition).Should().HaveCount(2);
    }

    #endregion

    #region Tool wrappers

    [Fact]
    public async Task NavigationFindReferences_DeclaredButUnreferenced_IsNotAnError()
    {
        var result = await CallNavigationFindReferences("Unused", includeDefinition: false);

        result.Should().NotStartWith("Error");
        result.Should().Contain("No references found for 'Unused'");
    }

    [Fact]
    public async Task NavigationFindReferences_UnknownSymbol_ReturnsSymbolNotFound()
    {
        var result = await CallNavigationFindReferences("DoesNotExist", includeDefinition: true);

        result.Should().StartWith($"Error [{McpErrorCodes.SymbolNotFound}]");
    }

    [Fact]
    public async Task NavigationFindReferences_IncludeDefinition_MarksDeclaration()
    {
        var result = await CallNavigationFindReferences("Unused", includeDefinition: true);

        result.Should().Contain("public class Unused").And.Contain("[DEF]");
    }

    [Fact]
    public async Task NavigationFindReferencesFiltered_UsesServiceFilters()
    {
        var writes = await CallNavigationFindReferencesFiltered("_count", writesOnly: true);
        var cross = await CallNavigationFindReferencesFiltered("Counter", crossProjectOnly: true);
        var publicOnly = await CallNavigationFindReferencesFiltered("Counter", publicOnly: true);

        // writesOnly used to substring-match ReferenceKind and dropped every member reference
        writes.Should().Contain("_count++;").And.Contain("out _count").And.NotContain("copy = _count + 1;");
        // crossProjectOnly used to depend on a definition entry that was never produced
        cross.Should().Contain("Consumer.cs").And.Contain("CounterTests.cs").And.NotContain("Users.cs");
        // publicOnly used to be ignored
        publicOnly.Should().Contain("CreatePublic").And.NotContain("CreatePrivate").And.NotContain("CreateInternal");
    }

    [Fact]
    public async Task AdvancedFindReferencesAcrossSolutions_GroupsBySolution()
    {
        var result = await AdvancedModuleTools.FindReferencesAcrossSolutions(
            "Counter",
            $"{_fixture.SolutionA},{_fixture.SolutionB}",
            includeDefinition: true,
            searchService: _fixture.SearchService,
            validator: _fixture.Services.GetRequiredService<SecurityValidator>(),
            errorHandler: _fixture.Services.GetRequiredService<McpErrorHandler>());

        result.Should().Contain("in 2 of 2 solutions");
        result.Should().Contain("## SolA.sln").And.Contain("## SolB.sln");
        result.Should().NotContain("## Lib").And.NotContain("## App").And.NotContain("## Standalone");
        result.Should().Contain("[definition]");
    }

    [Fact]
    public async Task FullFindReferences_IncludeDefinitionAndUnknownSymbol()
    {
        var withDefinition = await CodeNavigationTools.FindReferences(
            symbolName: "Unused",
            solutionPath: _fixture.SolutionA,
            detailLevel: "locations",
            includeDefinition: true,
            serviceProvider: _fixture.Services);
        var unknown = await CodeNavigationTools.FindReferences(
            symbolName: "DoesNotExist",
            solutionPath: _fixture.SolutionA,
            serviceProvider: _fixture.Services);

        withDefinition.Should().Contain("Definition").And.Contain("public class Unused");
        unknown.Should().Contain("Symbol 'DoesNotExist' not found");
    }

    private Task<string> CallNavigationFindReferences(string symbolName, bool includeDefinition) =>
        NavigationModuleTools.FindReferences(
            symbolName,
            _fixture.SolutionA,
            detailLevel: "locations",
            includeDefinition: includeDefinition,
            pageSize: 100,
            searchService: _fixture.SearchService,
            validator: _fixture.Services.GetRequiredService<SecurityValidator>(),
            errorHandler: _fixture.Services.GetRequiredService<McpErrorHandler>());

    private Task<string> CallNavigationFindReferencesFiltered(
        string symbolName, bool writesOnly = false, bool crossProjectOnly = false, bool publicOnly = false) =>
        NavigationModuleTools.FindReferencesFiltered(
            symbolName,
            _fixture.SolutionA,
            detailLevel: "locations",
            includeDefinition: true,
            crossProjectOnly: crossProjectOnly,
            writesOnly: writesOnly,
            publicOnly: publicOnly,
            pageSize: 100,
            searchService: _fixture.SearchService,
            validator: _fixture.Services.GetRequiredService<SecurityValidator>(),
            errorHandler: _fixture.Services.GetRequiredService<McpErrorHandler>());

    #endregion
}
