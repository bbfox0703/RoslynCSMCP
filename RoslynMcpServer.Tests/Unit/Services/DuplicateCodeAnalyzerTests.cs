using FluentAssertions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcpServer.Core.Models;
using RoslynMcpServer.Core.Services;

namespace RoslynMcpServer.Tests.Unit.Services;

/// <summary>
/// Unit tests for FindDuplicateCode's normalization and similarity grouping (no MSBuild needed).
/// </summary>
public class DuplicateCodeAnalyzerTests
{
    private static List<DuplicateCodeBlock> Find(string source, int similarity = 90, int minLines = 3)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: "Dup.cs");
        var blocks = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Select(m => DuplicateCodeAnalyzer.CreateCodeBlock(m, "P"))
            .Where(b => b.LineCount >= minLines)
            .ToList();
        return DuplicateCodeAnalyzer.FindDuplicateBlocks(blocks, similarity);
    }

    private const string RenamedCopies = @"
public class Class1
{
    public int Calculate(int x)
    {
        // running total
        int result = 0;
        result += x * 2;
        result += x + 1;
        return result;
    }
}
public class Class2
{
    private static long Compute(int value, bool unused)
    {
        int total = 0;          /* same logic, other names */
        total += value * 2;
        total += value + 1;
        return total;
    }
}";

    [Fact]
    public void RenamedCopy_WithOtherSignatureLocalsAndComments_FoundAt100Percent()
    {
        // The old hash covered the whole declaration, including name, signature, and comments.
        var group = Find(RenamedCopies, similarity: 100).Should().ContainSingle().Subject;

        group.SimilarityPercentage.Should().Be(100);
        group.Instances.Select(i => i.MethodName).Should().BeEquivalentTo(new[] { "Calculate", "Compute" });
    }

    [Fact]
    public void NearDuplicate_FoundBelowThresholdAndNotAt100()
    {
        const string source = @"
class C
{
    int First(int x)
    {
        int a = x + 1;
        int b = a * 2;
        int c = b - 3;
        System.Console.WriteLine(c);
        return c;
    }

    int Second(int y)
    {
        int a = y + 1;
        int b = a * 2;
        int c = b - 3;
        System.Console.WriteLine(c);
        System.Console.WriteLine(b);
        return c;
    }
}";

        Find(source, similarity: 100).Should().BeEmpty();

        var group = Find(source, similarity: 80).Should().ContainSingle().Subject;
        group.SimilarityPercentage.Should().BeInRange(80, 99);
        group.Instances.Should().HaveCount(2);
    }

    [Fact]
    public void SimilarityThreshold_IsHonored()
    {
        const string source = @"
class C
{
    int First(int x)
    {
        int a = x + 1;
        int b = a * 2;
        return b;
    }

    int Second(int y)
    {
        int a = y + 1;
        System.Console.WriteLine(a);
        int b = a * 2;
        return b;
    }
}";

        // 19 vs 28 tokens with all 19 in common: 2 * 19 / 47 = 80%.
        Find(source, similarity: 70).Should().ContainSingle().Which.SimilarityPercentage.Should().Be(80);

        Find(source, similarity: 90).Should().BeEmpty();
    }

    [Fact]
    public void DifferentCalledMembers_NotDuplicates()
    {
        // Only names declared inside the method are normalized; calls must match.
        const string source = @"
class C
{
    void First(string path)
    {
        var text = System.IO.File.ReadAllText(path);
        System.Console.WriteLine(text);
        System.Console.WriteLine(text.Length);
    }

    void Second(string path)
    {
        var text = System.IO.Path.GetFileName(path);
        System.Diagnostics.Debug.WriteLine(text);
        System.Diagnostics.Trace.WriteLine(text.Trim());
    }
}";

        Find(source, similarity: 100).Should().BeEmpty();
    }

    [Fact]
    public void ComputeSimilarity_IsDiceOverLongestCommonSubsequence()
    {
        // LCS("a b c d", "a b x d") = 3, so 2 * 3 / 8 = 75%.
        DuplicateCodeAnalyzer.ComputeSimilarity(new[] { "a", "b", "c", "d" }, new[] { "a", "b", "x", "d" })
            .Should().Be(75);
        DuplicateCodeAnalyzer.ComputeSimilarity(new[] { "a", "b" }, new[] { "a", "b" })
            .Should().Be(100);
    }
}
