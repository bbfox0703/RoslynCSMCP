using FluentAssertions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcpServer.Core.Services;

namespace RoslynMcpServer.Tests.Unit.Services;

public class ComplexityCalculatorTests
{
    // Parses a single method declaration (the snippet is the full method) and returns
    // its cyclomatic complexity via the shared calculator.
    private static int Cyclomatic(string methodCode)
    {
        var source = $"using System;\nusing System.Collections.Generic;\nclass C {{\n{methodCode}\n}}";
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        return ComplexityCalculator.CalculateCyclomatic(method);
    }

    private static int Cognitive(string methodCode)
    {
        var source = $"using System;\nclass C {{\n{methodCode}\n}}";
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        return ComplexityCalculator.CalculateCognitive(method);
    }

    [Fact]
    public void NoDecisionPoints_ReturnsOne()
    {
        Cyclomatic("int M() => 42;").Should().Be(1);
    }

    [Fact]
    public void If_AddsOne()
    {
        Cyclomatic("int M(int x){ if (x > 0) return 1; return 0; }").Should().Be(2);
    }

    [Theory]
    [InlineData("int M(int x) => x > 0 ? 1 : 2;", 2)]                 // ternary ?:
    [InlineData("string M(string s) => s ?? \"\";", 2)]              // null-coalescing ??
    [InlineData("int? M(string s) => s?.Length;", 2)]                // null-conditional ?.
    [InlineData("bool M(bool a, bool b, bool c) => a && b && c;", 3)] // two && operators
    public void ExpressionLevelDecisionPoints_AreCounted(string method, int expected)
    {
        Cyclomatic(method).Should().Be(expected);
    }

    [Fact]
    public void SwitchExpression_CountsArmsExceptDiscard()
    {
        // arms: 1 => , 2 => , _ =>  (discard not counted)  →  1 + 2 = 3
        var code = "string M(int x) => x switch { 1 => \"a\", 2 => \"b\", _ => \"c\" };";
        Cyclomatic(code).Should().Be(3);
    }

    [Fact]
    public void SwitchStatement_CountsCaseLabelsExceptDefault()
    {
        var code = @"int M(int x){ switch(x){ case 1: return 1; case 2: return 2; default: return 0; } }";
        Cyclomatic(code).Should().Be(3);
    }

    [Theory]
    [InlineData("bool M(int x) => x is > 0 and < 10;", 2)]   // one 'and' pattern combinator
    [InlineData("bool M(int x) => x is 1 or 2 or 3;", 3)]    // two 'or' pattern combinators
    public void PatternCombinators_AreCounted(string method, int expected)
    {
        Cyclomatic(method).Should().Be(expected);
    }

    [Fact]
    public void ForeachAndCatch_AreCounted()
    {
        var code = @"void M(System.Collections.Generic.List<int> xs){ try { foreach (var x in xs) {} } catch { } }";
        Cyclomatic(code).Should().Be(3); // foreach +1, catch +1
    }

    [Fact]
    public void CaseWhenGuard_AddsExtraDecision()
    {
        // case label (+1) plus its 'when' guard (+1) → base 1 + 2 = 3
        var code = @"int M(object o){ switch(o){ case int i when i > 0: return i; default: return 0; } }";
        Cyclomatic(code).Should().Be(3);
    }

    [Fact]
    public void Cognitive_PenalizesNestingOverFlatControlFlow()
    {
        var flat = Cognitive("void M(int x){ if (x>0){} if (x>1){} }");
        var nested = Cognitive("void M(int x){ if (x>0){ if (x>1){} } }");

        flat.Should().Be(2);       // 1 + 1, no nesting penalty
        nested.Should().Be(3);     // outer 1 + inner (1 + nesting 1)
        nested.Should().BeGreaterThan(flat);
    }
}
