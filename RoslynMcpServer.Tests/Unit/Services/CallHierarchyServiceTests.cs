using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Core.Services;
using RoslynMcpServer.Tests.Helpers;
using AdvancedModuleTools = RoslynMcpServer.Advanced.Tools.AdvancedTools;

namespace RoslynMcpServer.Tests.Unit.Services;

public class CallHierarchyServiceTests : IDisposable
{
    private const string SolutionPath = "InMemory/Calls.sln";

    // Root -> Top -> Middle -> Leaf (twice); Recursive -> Leaf and itself; Other.UseTop -> Top (second project)
    private const string ServiceCode = """
        namespace Calls
        {
            public class Service
            {
                public int Leaf(int x) => x + 1;
                public int Leaf(string s) => s.Length;
                public int Middle(int x) => Leaf(x) + Leaf(x) + Leaf("a");
                public int Top(int x) => Middle(x);
                public int Root(int x) => Top(x);
                public int Recursive(int n) => n <= 0 ? Leaf(n) : Recursive(n - 1);
            }
        }
        """;

    private const string ConsumerCode = """
        namespace Consumer
        {
            public class Other
            {
                public int UseTop() => new Calls.Service().Top(1);
            }
        }
        """;

    private readonly InMemoryCodeAnalysisService _codeAnalysis;
    private readonly CallHierarchyService _service;

    public CallHierarchyServiceTests()
    {
        var solution = new InMemorySolution()
            .AddProject("Calls").AddDocument("Calls", "Service.cs", ServiceCode)
            .AddProject("Consumer", "Calls").AddDocument("Consumer", "Other.cs", ConsumerCode);

        solution.AssertCompilesAsync().GetAwaiter().GetResult();

        _codeAnalysis = solution.CreateCodeAnalysisService(SolutionPath);
        _service = new CallHierarchyService(_codeAnalysis, NullLogger<CallHierarchyService>.Instance);
    }

    public void Dispose() => _codeAnalysis.Dispose();

    [Fact]
    public async Task Callers_MaxDepthOne_ListsOnlyDirectCallers()
    {
        var output = await _service.GetCallHierarchyAsync(SolutionPath, "Leaf", "callers", maxDepth: 1);

        output.Should().Contain("Service.Middle").And.Contain("Service.Recursive");
        output.Should().NotContain("Service.Top").And.NotContain("Service.Root");
    }

    [Fact]
    public async Task Callers_MaxDepthThree_FollowsCallersOfCallers()
    {
        var output = await _service.GetCallHierarchyAsync(SolutionPath, "Leaf", "callers", maxDepth: 3);

        var lines = Lines(output);
        var middle = lines.Single(l => l.Contains("├─> Service.Middle"));
        var top = lines.Single(l => l.Contains("├─> Service.Top"));
        var root = lines.Single(l => l.Contains("├─> Service.Root"));
        var useTop = lines.Single(l => l.Contains("├─> Other.UseTop"));

        Indentation(top).Should().BeGreaterThan(Indentation(middle));
        Indentation(root).Should().BeGreaterThan(Indentation(top));
        Indentation(useTop).Should().Be(Indentation(root), "callers in another project are found at the same level");
        middle.Should().Contain("(2 calls)", "Middle calls Leaf(int) twice");
    }

    [Fact]
    public async Task Callers_MaxDepthTwo_StopsBeforeThirdLevel()
    {
        var output = await _service.GetCallHierarchyAsync(SolutionPath, "Leaf", "callers", maxDepth: 2);

        output.Should().Contain("Service.Top");
        output.Should().NotContain("Service.Root").And.NotContain("Other.UseTop");
    }

    [Fact]
    public async Task Callers_RecursiveCaller_IsMarkedAndNotExpanded()
    {
        var output = await _service.GetCallHierarchyAsync(SolutionPath, "Leaf", "callers", maxDepth: 5);

        var recursiveLines = Lines(output).Where(l => l.Contains("├─> Service.Recursive")).ToList();
        recursiveLines.Should().HaveCount(2, "Recursive calls Leaf, and Recursive's own caller is itself");
        recursiveLines[1].Should().Contain("↺ recursive");
    }

    [Fact]
    public async Task Callees_MaxDepth_FollowsCalleesOfCallees()
    {
        var shallow = await _service.GetCallHierarchyAsync(SolutionPath, "Root", "callees", maxDepth: 2);
        var deep = await _service.GetCallHierarchyAsync(SolutionPath, "Root", "callees", maxDepth: 3);

        shallow.Should().Contain("Service.Top").And.Contain("Service.Middle");
        shallow.Should().NotContain("Service.Leaf");
        deep.Should().Contain("Service.Leaf");
    }

    [Fact]
    public async Task Callees_OverloadsAreListedSeparately()
    {
        var output = await _service.GetCallHierarchyAsync(SolutionPath, "Middle", "callees", maxDepth: 1);

        var leafLines = Lines(output).Where(l => l.Contains("├─> Service.Leaf")).ToList();
        leafLines.Should().HaveCount(2);
        leafLines.Should().ContainSingle(l => l.Contains("(2 calls)"));
    }

    [Theory]
    [InlineData("CALLERS")]
    [InlineData(" Callers ")]
    public async Task Direction_IsCaseInsensitive(string direction)
    {
        var output = await _service.GetCallHierarchyAsync(SolutionPath, "Leaf", direction, maxDepth: 1);

        output.Should().Contain("Callers (").And.Contain("Service.Middle");
        output.Should().NotContain("Callees (");
    }

    [Fact]
    public async Task Direction_Invalid_ReturnsError()
    {
        var output = await _service.GetCallHierarchyAsync(SolutionPath, "Leaf", "up", maxDepth: 1);

        output.Should().StartWith("Error: Invalid direction 'up'");
    }

    [Fact]
    public async Task MaxDepth_OutOfRange_IsClamped()
    {
        var zero = await _service.GetCallHierarchyAsync(SolutionPath, "Leaf", "callers", maxDepth: 0);
        var huge = await _service.GetCallHierarchyAsync(SolutionPath, "Leaf", "callers", maxDepth: 1000);

        zero.Should().Contain("Max depth: 1").And.NotContain("Service.Top");
        huge.Should().Contain($"Max depth: {CallHierarchyService.MaxAllowedDepth}").And.Contain("Service.Root");
    }

    [Fact]
    public async Task AdvancedTool_PassesMaxDepthThrough()
    {
        var output = await AdvancedModuleTools.GetCallHierarchy(
            "Leaf", SolutionPath, "callers", 3, _service, new McpErrorHandler(NullLogger<McpErrorHandler>.Instance));

        output.Should().Contain("Service.Root");
    }

    private static List<string> Lines(string output) => output.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

    private static int Indentation(string line) => line.Length - line.TrimStart().Length;
}
