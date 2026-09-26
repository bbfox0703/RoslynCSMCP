using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Core.Models;
using RoslynMcpServer.Core.Services;
using RoslynMcpServer.Tests.Helpers;
using AdvancedModuleTools = RoslynMcpServer.Advanced.Tools.AdvancedTools;

namespace RoslynMcpServer.Tests.Unit.Services;

public class APIChangeAnalyzerTests : IDisposable
{
    private const string OldPath = "InMemory/Old.sln";
    private const string NewPath = "InMemory/New.sln";

    private const string OldCode = """
        namespace Lib
        {
            public class Service
            {
                public void Save(int id) { }
                public void Save(string name) { }
                public int Count() => 0;
                protected void OnSaved() { }
                protected internal void Hook() { }
                internal void InternalHelper() { }
                public string Name { get; set; } = "";
            }

            public class Other
            {
                public void Save(int id) { }
            }

            internal class Hidden
            {
                public void Run() { }
            }
        }
        """;

    private const string NewCode = """
        namespace Lib
        {
            public class Service
            {
                public void Save(int id) { }
                public void Save(long id) { }
                public long Count() => 0;
                protected void Hook() { }
                internal void InternalHelper(int value) { }
                public string Name { get; set; } = "";
            }

            public class Other
            {
                public void Save(int id) { }
            }

            internal class Hidden
            {
                public void Run(int times) { }
            }
        }
        """;

    private readonly InMemoryCodeAnalysisService _codeAnalysis = new();
    private readonly APIChangeAnalyzer _analyzer;

    public APIChangeAnalyzerTests()
    {
        _codeAnalysis
            .Register(OldPath, CreateSolution(OldCode))
            .Register(NewPath, CreateSolution(NewCode));
        _analyzer = new APIChangeAnalyzer(NullLogger<APIChangeAnalyzer>.Instance, _codeAnalysis);
    }

    public void Dispose() => _codeAnalysis.Dispose();

    [Fact]
    public async Task Overloads_AreComparedSeparately()
    {
        var results = await _analyzer.AnalyzeAPIChangesAsync(OldPath, NewPath);

        results.Warnings.Should().BeEmpty();
        Single(results, "Removed", "Lib.Service.Save(string)").ImpactLevel.Should().Be("Breaking");
        Single(results, "Added", "Lib.Service.Save(long)").ImpactLevel.Should().Be("NonBreaking");
        results.Changes.Should().NotContain(c => c.FullSymbolName == "Lib.Service.Save(int)");
    }

    [Fact]
    public async Task SameNamedMembersOfDifferentTypes_DoNotCollapse()
    {
        var results = await _analyzer.AnalyzeAPIChangesAsync(OldPath, NewPath);

        // Other.Save(int) is unchanged; with simple-name keys it collided with Service.Save overloads
        results.Changes.Should().NotContain(c => c.DeclaringType == "Other");
        results.AnalyzedOldSymbols.Should().Be(results.AnalyzedNewSymbols + 1,
            "old has Save(string) and OnSaved, new has Save(long) instead; every overload is its own symbol");
    }

    [Fact]
    public async Task ReturnTypeChange_IsASignatureChange()
    {
        var results = await _analyzer.AnalyzeAPIChangesAsync(OldPath, NewPath);

        var change = Single(results, "SignatureChanged", "Lib.Service.Count()");
        change.ImpactLevel.Should().Be("Breaking");
        change.OldSignature.Should().Contain("int");
        change.NewSignature.Should().Contain("long");
    }

    [Fact]
    public async Task ProtectedMembers_ArePartOfThePublicApi()
    {
        var results = await _analyzer.AnalyzeAPIChangesAsync(OldPath, NewPath);

        Single(results, "Removed", "Lib.Service.OnSaved()").ImpactLevel.Should().Be("Breaking");
    }

    [Fact]
    public async Task AccessibilityChangeInvisibleOutsideAssembly_IsInternal()
    {
        // protected internal -> protected: code outside the assembly can use it only from derived types either way
        var results = await _analyzer.AnalyzeAPIChangesAsync(OldPath, NewPath);

        var change = Single(results, "AccessibilityChanged", "Lib.Service.Hook()");
        change.ImpactLevel.Should().Be("Internal");
        results.InternalChanges.Should().Be(1);
    }

    [Fact]
    public async Task IncludeInternal_ClassifiesInternalChangesAsInternal()
    {
        var withoutInternal = await _analyzer.AnalyzeAPIChangesAsync(OldPath, NewPath, includeInternal: false);
        var withInternal = await _analyzer.AnalyzeAPIChangesAsync(OldPath, NewPath, includeInternal: true);

        Single(withInternal, "Removed", "Lib.Service.InternalHelper()").ImpactLevel.Should().Be("Internal");
        Single(withInternal, "Added", "Lib.Service.InternalHelper(int)").ImpactLevel.Should().Be("Internal");

        // A public member of an internal type is not visible outside the assembly either
        Single(withInternal, "Removed", "Lib.Hidden.Run()").ImpactLevel.Should().Be("Internal");
        Single(withInternal, "Added", "Lib.Hidden.Run(int)").ImpactLevel.Should().Be("Internal");

        withInternal.BreakingChanges.Should().Be(withoutInternal.BreakingChanges);
        withInternal.NonBreakingChanges.Should().Be(withoutInternal.NonBreakingChanges);
        withInternal.InternalChanges.Should().Be(withoutInternal.InternalChanges + 4);
        withInternal.RecommendedVersionBump.Should().Be("Major");
    }

    [Fact]
    public async Task OnlyInternalChanges_RecommendPatch()
    {
        var codeAnalysis = new InMemoryCodeAnalysisService()
            .Register(OldPath, CreateSolution("namespace Lib { public class A { internal void Helper() { } } }"))
            .Register(NewPath, CreateSolution("namespace Lib { public class A { internal void Helper(int x) { } } }"));
        var analyzer = new APIChangeAnalyzer(NullLogger<APIChangeAnalyzer>.Instance, codeAnalysis);

        var results = await analyzer.AnalyzeAPIChangesAsync(OldPath, NewPath, includeInternal: true);

        results.TotalChanges.Should().Be(2);
        results.InternalChanges.Should().Be(2);
        results.RecommendedVersionBump.Should().Be("Patch");
    }

    [Fact]
    public async Task PublicAdditionOnly_RecommendsMinor()
    {
        var codeAnalysis = new InMemoryCodeAnalysisService()
            .Register(OldPath, CreateSolution("namespace Lib { public class A { public void Run() { } } }"))
            .Register(NewPath, CreateSolution("namespace Lib { public class A { public void Run() { } public void Run(int times) { } } }"));
        var analyzer = new APIChangeAnalyzer(NullLogger<APIChangeAnalyzer>.Instance, codeAnalysis);

        var results = await analyzer.AnalyzeAPIChangesAsync(OldPath, NewPath);

        Single(results, "Added", "Lib.A.Run(int)").ImpactLevel.Should().Be("NonBreaking");
        results.TotalChanges.Should().Be(1);
        results.RecommendedVersionBump.Should().Be("Minor");
    }

    [Fact]
    public async Task AdvancedTool_LoadFailure_IsReportedAsWarning()
    {
        // Both files exist so path validation passes; only the old one has a solution, so loading the new one fails
        using var oldFile = new PlaceholderSolutionFile();
        using var missing = new PlaceholderSolutionFile();
        _codeAnalysis.Register(oldFile.FilePath, await _codeAnalysis.GetSolutionAsync(OldPath));

        var output = await AdvancedModuleTools.AnalyzeAPIChanges(
            oldFile.FilePath, missing.FilePath, "normal", _analyzer,
            new SecurityValidator(NullLogger<SecurityValidator>.Instance),
            new McpErrorHandler(NullLogger<McpErrorHandler>.Instance));

        output.Should().Contain("Warnings (");
        output.Should().Contain(missing.FilePath);
    }

    private static APIChange Single(APIChangeResults results, string changeType, string fullSymbolName) =>
        results.Changes.Should().ContainSingle(c => c.ChangeType == changeType && c.FullSymbolName == fullSymbolName,
            $"expected one {changeType} change for {fullSymbolName}").Subject;

    private static Microsoft.CodeAnalysis.Solution CreateSolution(string code)
    {
        var solution = new InMemorySolution().AddProject("Lib").AddDocument("Lib", "Lib.cs", code);
        solution.AssertCompilesAsync().GetAwaiter().GetResult();
        return solution.Solution;
    }
}
