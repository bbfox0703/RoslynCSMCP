using Microsoft.Extensions.Logging;
using Moq;
using RoslynMcpServer.Services;
using RoslynMcpServer.Tests.Helpers;

namespace RoslynMcpServer.Tests.Unit.Services;

public class IncrementalAnalyzerTests
{
    private readonly Mock<ILogger<IncrementalAnalyzer>> _mockLogger;
    private readonly IncrementalAnalyzer _analyzer;

    public IncrementalAnalyzerTests()
    {
        _mockLogger = MockServiceProvider.CreateMockLogger<IncrementalAnalyzer>();
        _analyzer = new IncrementalAnalyzer(_mockLogger.Object);
    }

    #region Cognitive Complexity Tests

    [Fact]
    public async Task CognitiveComplexity_ComplexMethod_ReportsCorrectly()
    {
        // Arrange - Create method with complexity >= 5 (threshold)
        var code = @"
namespace TestNamespace
{
    public class TestClass
    {
        public void ComplexMethod(int value)
        {
            if (value > 0)         // +1
            {
                value++;
            }
            if (value > 10)        // +1
            {
                value++;
            }
            if (value > 20)        // +1
            {
                value++;
            }
            if (value > 30)        // +1
            {
                value++;
            }
            if (value > 40)        // +1
            {
                value++;
            }
            // Total: 5 cognitive complexity
        }
    }
}";
        var solution = new TestSolutionBuilder()
            .AddProject("TestProject")
            .AddDocument("TestProject", "TestClass.cs", code)
            .Solution;

        // Act
        var analysisResult = await _analyzer.AnalyzeIncrementallyAsync(solution);

        // Assert
        analysisResult.ComplexityIssues.Should().NotBeEmpty();
        var result = analysisResult.ComplexityIssues.First(r => r.MethodName.Contains("ComplexMethod"));
        result.CognitiveComplexity.Should().BeGreaterOrEqualTo(5);
    }

    [Fact]
    public async Task CognitiveComplexity_NestedIfStatements_AppliesNestingPenalty()
    {
        // Arrange - Deep nesting increases cognitive complexity significantly
        var code = @"
namespace TestNamespace
{
    public class TestClass
    {
        public void TestMethod(int value)
        {
            if (value > 0)           // +1 (level 0)
            {
                if (value > 10)      // +2 (1 + nesting level 1)
                {
                    if (value > 20)  // +3 (1 + nesting level 2)
                    {
                        value++;
                    }
                }
            }
            // Total: 6 cognitive complexity (exceeds threshold of 5)
        }
    }
}";
        var solution = new TestSolutionBuilder()
            .AddProject("TestProject")
            .AddDocument("TestProject", "TestClass.cs", code)
            .Solution;

        // Act
        var analysisResult = await _analyzer.AnalyzeIncrementallyAsync(solution);

        // Assert
        analysisResult.ComplexityIssues.Should().NotBeEmpty();
        var result = analysisResult.ComplexityIssues.First(r => r.MethodName.Contains("TestMethod"));
        result.CognitiveComplexity.Should().Be(6); // 1 + 2 + 3
        result.MaxNestingDepth.Should().Be(3);
    }

    [Fact]
    public async Task ComplexityMetrics_NestedStructure_ShowsDifference()
    {
        // Arrange - Nested structure has higher cognitive vs cyclomatic
        var code = @"
namespace TestNamespace
{
    public class TestClass
    {
        public int NestedMethod(int value)
        {
            int result = 0;
            if (value > 0)           // Cyclomatic +1, Cognitive +1
            {
                if (value > 10)      // Cyclomatic +1, Cognitive +2 (nesting penalty)
                {
                    if (value > 20)  // Cyclomatic +1, Cognitive +3 (nesting penalty)
                    {
                        result = value;
                    }
                }
            }
            return result;
            // Cyclomatic: 4 (1 base + 3 ifs)
            // Cognitive: 6 (1 + 2 + 3 with nesting penalties)
        }
    }
}";
        var solution = new TestSolutionBuilder()
            .AddProject("TestProject")
            .AddDocument("TestProject", "TestClass.cs", code)
            .Solution;

        // Act
        var analysisResult = await _analyzer.AnalyzeIncrementallyAsync(solution);

        // Assert
        analysisResult.ComplexityIssues.Should().NotBeEmpty();
        var result = analysisResult.ComplexityIssues.First(r => r.MethodName.Contains("NestedMethod"));
        result.Complexity.Should().Be(4); // 1 base + 3 if statements
        result.CognitiveComplexity.Should().Be(6); // 1 + 2 + 3 (with nesting penalties)
        result.MaxNestingDepth.Should().Be(3);

        // Cognitive complexity is higher than cyclomatic due to nesting
        result.CognitiveComplexity.Should().BeGreaterThan(result.Complexity);
    }

    [Fact]
    public async Task MaxNestingDepth_DeepNesting_CalculatesCorrectly()
    {
        // Arrange - Use the helper to create deep nesting
        var code = TestSolutionBuilder.CreateNestedMethod("TestNamespace", "TestClass", nestingDepth: 5);
        var solution = new TestSolutionBuilder()
            .AddProject("TestProject")
            .AddDocument("TestProject", "TestClass.cs", code)
            .Solution;

        // Act
        var analysisResult = await _analyzer.AnalyzeIncrementallyAsync(solution);

        // Assert
        analysisResult.ComplexityIssues.Should().NotBeEmpty();
        var result = analysisResult.ComplexityIssues.First(r => r.MethodName.Contains("NestedMethod"));
        result.MaxNestingDepth.Should().Be(5);
        result.CognitiveComplexity.Should().BeGreaterThan(5); // Should exceed threshold
    }

    [Fact]
    public async Task ComplexityAnalysis_HighCyclomaticComplexity_Reports()
    {
        // Arrange - Method with high cyclomatic complexity (many branches)
        var code = TestSolutionBuilder.CreateComplexMethod("TestNamespace", "TestClass", decisionPoints: 10);
        var solution = new TestSolutionBuilder()
            .AddProject("TestProject")
            .AddDocument("TestProject", "TestClass.cs", code)
            .Solution;

        // Act
        var analysisResult = await _analyzer.AnalyzeIncrementallyAsync(solution);

        // Assert
        analysisResult.ComplexityIssues.Should().NotBeEmpty();
        var result = analysisResult.ComplexityIssues.First(r => r.MethodName.Contains("ComplexMethod"));
        result.Complexity.Should().BeGreaterOrEqualTo(10); // Base 1 + 10 ifs
    }

    #endregion

    #region Incremental Analysis Tests

    [Fact]
    public async Task AnalyzeIncrementally_ProcessesDocuments()
    {
        // Arrange
        var code = TestSolutionBuilder.CreateSimpleClass("TestNamespace", "TestClass");
        var solution = new TestSolutionBuilder()
            .AddProject("TestProject")
            .AddDocument("TestProject", "TestClass.cs", code)
            .Solution;

        // Act
        var result = await _analyzer.AnalyzeIncrementallyAsync(solution);

        // Assert
        result.Should().NotBeNull();
        result.ProcessedDocuments.Should().BeGreaterThan(0);
        result.AnalysisEndTime.Should().BeAfter(result.AnalysisStartTime);
    }

    [Fact]
    public async Task AnalyzeIncrementally_ExtractsSymbols()
    {
        // Arrange
        var code = @"
namespace TestNamespace
{
    public class TestClass
    {
        public void TestMethod() { }
        public int TestProperty { get; set; }
    }
}";
        var solution = new TestSolutionBuilder()
            .AddProject("TestProject")
            .AddDocument("TestProject", "TestClass.cs", code)
            .Solution;

        // Act
        var result = await _analyzer.AnalyzeIncrementallyAsync(solution);

        // Assert
        result.Symbols.Should().NotBeEmpty();
        result.Symbols.Should().Contain(s => s.Name == "TestClass");
        result.Symbols.Should().Contain(s => s.Name == "TestMethod");
    }

    [Fact]
    public async Task AnalyzeIncrementally_MultipleDocuments_ProcessesAll()
    {
        // Arrange
        var solution = new TestSolutionBuilder()
            .AddProject("TestProject")
            .AddDocument("TestProject", "Class1.cs",
                TestSolutionBuilder.CreateSimpleClass("TestNamespace", "Class1"))
            .AddDocument("TestProject", "Class2.cs",
                TestSolutionBuilder.CreateSimpleClass("TestNamespace", "Class2"))
            .AddDocument("TestProject", "Class3.cs",
                TestSolutionBuilder.CreateSimpleClass("TestNamespace", "Class3"))
            .Solution;

        // Act
        var result = await _analyzer.AnalyzeIncrementallyAsync(solution);

        // Assert
        result.ProcessedDocuments.Should().Be(3);
        result.Symbols.Should().Contain(s => s.Name == "Class1");
        result.Symbols.Should().Contain(s => s.Name == "Class2");
        result.Symbols.Should().Contain(s => s.Name == "Class3");
    }

    #endregion
}
