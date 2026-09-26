using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Core.Services;
using RoslynMcpServer.Tests.Helpers;
using FullTools = RoslynMcpServer.Tools.CodeNavigationTools;
using RefactoringModuleTools = RoslynMcpServer.Refactoring.Tools.RefactoringTools;

namespace RoslynMcpServer.Tests.Unit.Services;

public class ChangeImpactAnalyzerTests : IDisposable
{
    private const string SolutionPath = "InMemory/Impact.sln";

    // Format <- Report.Title <- Report.Header <- Page.Render <- Page.Twice (Render is called twice)
    private const string CoreCode = """
        namespace Core
        {
            public class Formatter
            {
                public string Format(string value) => value.Trim();
            }

            public class Report
            {
                private readonly Formatter _formatter = new Formatter();

                public string Title(string text) => _formatter.Format(text);

                public string Header(string text) => "# " + Title(text);

                public class Section
                {
                    public class Paragraph
                    {
                        public string Body { get; set; } = "";
                    }
                }
            }
        }
        """;

    private const string AppCode = """
        namespace App
        {
            public class Page
            {
                public string Render(string text) => new Core.Report().Header(text);

                public string Twice(string text) => Render(text) + Render(text);

                public string Paragraph() => new Core.Report.Section.Paragraph().Body;
            }
        }
        """;

    private readonly InMemoryCodeAnalysisService _codeAnalysis;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly ChangeImpactAnalyzer _analyzer;

    public ChangeImpactAnalyzerTests()
    {
        var solution = new InMemorySolution()
            .AddProject("Core").AddDocument("Core", "Core.cs", CoreCode)
            .AddProject("App", "Core").AddDocument("App", "Page.cs", AppCode);

        solution.AssertCompilesAsync().GetAwaiter().GetResult();

        _codeAnalysis = solution.CreateCodeAnalysisService(SolutionPath);
        var symbolSearch = new SymbolSearchService(_codeAnalysis, NullLogger<SymbolSearchService>.Instance, _cache);
        _analyzer = new ChangeImpactAnalyzer(NullLogger<ChangeImpactAnalyzer>.Instance, _codeAnalysis, symbolSearch);
    }

    public void Dispose()
    {
        _codeAnalysis.Dispose();
        _cache.Dispose();
    }

    [Fact]
    public async Task IndirectReferences_FollowEnclosingMembersUpToMaxDepth()
    {
        var results = await _analyzer.AnalyzeChangeImpactAsync("Format", SolutionPath, maxDepth: 3);

        results.Warnings.Should().BeEmpty();
        results.DirectReferences.Should().Be(1);
        results.IndirectReferences.Should().Be(2);

        results.ImpactedSymbols.Select(s => (s.SymbolName, s.ReferenceKind, s.Distance, s.ProjectName))
            .Should().BeEquivalentTo(new[]
            {
                ("Title", "Direct", 0, "Core"),
                ("Header", "Indirect", 1, "Core"),
                ("Render", "Indirect", 2, "App")
            });
        results.ImpactedProjectNames.Should().BeEquivalentTo("Core", "App");
    }

    [Fact]
    public async Task IndirectReferences_DeeperMaxDepth_ReachesMoreLevels()
    {
        var results = await _analyzer.AnalyzeChangeImpactAsync("Format", SolutionPath, maxDepth: 4);

        // Twice calls Render twice: two more locations at distance 3
        results.IndirectReferences.Should().Be(4);
        results.ImpactedSymbols.Where(s => s.Distance == 3).Should().HaveCount(2)
            .And.OnlyContain(s => s.SymbolName == "Twice" && s.ReferencedSymbol == "App.Page.Render(string)");
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(3, false)]
    public async Task IndirectReferences_DisabledOrDepthOne_ReportsDirectOnly(int maxDepth, bool includeIndirect)
    {
        var results = await _analyzer.AnalyzeChangeImpactAsync("Format", SolutionPath, maxDepth, includeIndirect);

        results.DirectReferences.Should().Be(1);
        results.IndirectReferences.Should().Be(0);
        results.ImpactedSymbols.Should().ContainSingle();
    }

    [Fact]
    public async Task DependencyChains_FollowRealReferences()
    {
        var results = await _analyzer.AnalyzeChangeImpactAsync("Format", SolutionPath, maxDepth: 4);

        var chain = results.DependencyChains.Should().ContainSingle().Subject;
        chain.Chain.Should().Equal("Format", "Title", "Header", "Render", "Twice");
        chain.CrossesProjectBoundary.Should().BeTrue();
    }

    [Fact]
    public async Task SymbolLookup_DoesNotResolveFrameworkSymbols()
    {
        // Console.WriteLine exists only in the framework
        var results = await _analyzer.AnalyzeChangeImpactAsync("WriteLine", SolutionPath);

        results.TargetSymbolFullName.Should().BeEmpty();
        results.Warnings.Should().ContainSingle().Which.Message.Should().Contain("'WriteLine' not found");
    }

    [Fact]
    public async Task SymbolLookup_PrefersSourceSymbolSharingAFrameworkName()
    {
        // string.Format also exists; the source method must be chosen
        var results = await _analyzer.AnalyzeChangeImpactAsync("Format", SolutionPath, maxDepth: 1);

        results.TargetSymbolFullName.Should().Be("Core.Formatter.Format(string)");
        results.ProjectName.Should().Be("Core");
    }

    [Fact]
    public async Task SymbolLookup_FindsMembersOfTypesNestedTwoLevelsDeep()
    {
        var results = await _analyzer.AnalyzeChangeImpactAsync("Core.Report.Section.Paragraph.Body", SolutionPath, maxDepth: 1);

        results.Warnings.Should().BeEmpty();
        results.DirectReferences.Should().Be(1);
        results.ImpactedSymbols.Should().ContainSingle().Which.SymbolName.Should().Be("Paragraph");
    }

    [Fact]
    public async Task FullTool_ShowsIndirectReferences()
    {
        using var provider = CreateServiceProvider();

        var output = await FullTools.GetChangeImpact("Format", SolutionPath, "normal", maxDepth: 3, serviceProvider: provider);

        output.Should().Contain("Indirect References: 2");
        output.Should().Contain("Format → Title → Header → Render");
    }

    [Fact]
    public async Task FullTool_UnknownSymbol_ShowsNotFoundWarning()
    {
        using var provider = CreateServiceProvider();

        var output = await FullTools.GetChangeImpact("NoSuchSymbol", SolutionPath, "summary", serviceProvider: provider);

        output.Should().Contain("'NoSuchSymbol' not found");
        output.Should().NotContain("Risk Level");
    }

    [Theory]
    [InlineData("summary")]
    [InlineData("normal")]
    [InlineData("detailed")]
    public async Task RefactoringTool_UnknownSymbol_ShowsNotFoundWarningInsteadOfZeroReport(string format)
    {
        using var placeholder = new PlaceholderSolutionFile();
        var analyzer = CreateAnalyzerFor(placeholder.FilePath);

        var output = await RefactoringModuleTools.GetChangeImpact(
            "NoSuchSymbol", placeholder.FilePath, format, 3, analyzer,
            new SecurityValidator(NullLogger<SecurityValidator>.Instance),
            new McpErrorHandler(NullLogger<McpErrorHandler>.Instance));

        output.Should().Contain("'NoSuchSymbol' not found");
        output.Should().NotContain("Risk");
        output.Should().NotContain("Direct");
    }

    [Fact]
    public async Task RefactoringTool_Detailed_ListsIndirectLocations()
    {
        using var placeholder = new PlaceholderSolutionFile();
        var analyzer = CreateAnalyzerFor(placeholder.FilePath);

        var output = await RefactoringModuleTools.GetChangeImpact(
            "Format", placeholder.FilePath, "detailed", 3, analyzer,
            new SecurityValidator(NullLogger<SecurityValidator>.Instance),
            new McpErrorHandler(NullLogger<McpErrorHandler>.Instance));

        output.Should().Contain("Indirect: 2");
        output.Should().Contain("Render (Method) @ Page.cs:5 [indirect, via Core.Report.Header(string)]");
    }

    private ChangeImpactAnalyzer CreateAnalyzerFor(string solutionPath)
    {
        var codeAnalysis = new InMemoryCodeAnalysisService(solutionPath, _codeAnalysis.GetSolutionAsync(SolutionPath).Result);
        var symbolSearch = new SymbolSearchService(codeAnalysis, NullLogger<SymbolSearchService>.Instance, _cache);
        return new ChangeImpactAnalyzer(NullLogger<ChangeImpactAnalyzer>.Instance, codeAnalysis, symbolSearch);
    }

    private ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_analyzer);
        return services.BuildServiceProvider();
    }
}
