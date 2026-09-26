using FluentAssertions;
using RoslynMcpServer.Core.Models;
using RoslynMcpServer.Core.Services;
using RoslynMcpServer.Tests.Helpers;

namespace RoslynMcpServer.Tests.Unit.Services;

/// <summary>
/// Unit tests for the FindPerformanceIssues detectors, run against in-memory compilations.
/// </summary>
public class PerformanceIssueAnalyzerTests
{
    private const string Usings = "using System; using System.IO; using System.Collections.Generic; using System.Threading.Tasks;\n";

    private static List<PerformanceIssue> Disposables(string source)
    {
        var (tree, model) = InMemoryCompilation.CompileSingle(Usings + source);
        return PerformanceIssueAnalyzer.AnalyzeDisposableNotDisposed(tree.GetRoot(), model, tree, "P");
    }

    private static List<PerformanceIssue> DisposableLocals(string body) =>
        Disposables("class C { object M(Stream input, List<IDisposable> bag) {\n" + body + "\nreturn null!; } }");

    private static List<PerformanceIssue> Concatenation(string body)
    {
        var (tree, model) = InMemoryCompilation.CompileSingle(
            Usings + "class C { string _log = \"\"; void M(string[] items, int n) {\n" + body + "\n} }");
        return PerformanceIssueAnalyzer.AnalyzeStringConcatenation(tree.GetRoot(), model, tree, "P");
    }

    // ── DisposableNotDisposed: locals ────────────────────────────────────────

    [Fact]
    public void Disposable_LocalCreatedAndNeverDisposed_Flagged()
    {
        // The old check compared a Nullable<SyntaxToken> with null, so locals were never reported.
        DisposableLocals("var s = new MemoryStream(); s.WriteByte(1);")
            .Should().ContainSingle(i => i.IssueType == "DisposableNotDisposed" && i.Description.Contains("'s'") && i.MethodName == "M");
    }

    [Fact]
    public void Disposable_UsingDeclaration_NotFlagged()
    {
        DisposableLocals("using var s = new MemoryStream(); s.WriteByte(1);").Should().BeEmpty();
    }

    [Fact]
    public void Disposable_UsingStatement_NotFlagged()
    {
        DisposableLocals("using (var s = new MemoryStream()) { s.WriteByte(1); }").Should().BeEmpty();
    }

    [Fact]
    public void Disposable_LocalInsideUsingBlockBody_Flagged()
    {
        // Only the using statement's own declaration is disposed, not locals declared in its body.
        DisposableLocals("using (var a = new MemoryStream()) { var b = new MemoryStream(); b.WriteByte(1); }")
            .Should().ContainSingle(i => i.Description.Contains("'b'"));
    }

    [Theory]
    [InlineData("var s = new MemoryStream(); try { s.WriteByte(1); } finally { s.Dispose(); }")]
    [InlineData("var s = new MemoryStream(); s?.Dispose();")]
    [InlineData("var s = new MemoryStream(); return s;")]
    [InlineData("var s = new MemoryStream(); using var reader = new StreamReader(s);")]
    [InlineData("var s = new MemoryStream(); bag.Add(s);")]
    [InlineData("var s = new MemoryStream(); using (s) { }")]
    [InlineData("var s = input; s.WriteByte(1);")]
    [InlineData("var t = Task.Run(() => { }); t.Wait();")]
    public void Disposable_DisposedHandedOffOrNotOwned_NotFlagged(string body)
    {
        DisposableLocals(body).Should().BeEmpty();
    }

    [Fact]
    public void Disposable_StaticFactoryResultNeverDisposed_Flagged()
    {
        DisposableLocals("var f = File.OpenRead(\"x\"); f.ReadByte();")
            .Should().ContainSingle(i => i.Description.Contains("'f'"));
    }

    // ── DisposableNotDisposed: fields ────────────────────────────────────────

    [Fact]
    public void Disposable_InjectedField_NotFlagged()
    {
        // The old check reported every IDisposable-typed field, including injected ones.
        Disposables("class D { private readonly Stream _s; public D(Stream s) { _s = s; } }")
            .Should().BeEmpty();
    }

    [Fact]
    public void Disposable_OwnedFieldInNonDisposableType_Flagged()
    {
        Disposables("class D { private readonly MemoryStream _s = new MemoryStream(); }")
            .Should().ContainSingle(i => i.Title == "IDisposable field not disposed" && i.Description.Contains("does not implement IDisposable"));
    }

    [Fact]
    public void Disposable_OwnedFieldDisposedInDispose_NotFlagged()
    {
        Disposables("class D : IDisposable { private readonly MemoryStream _s = new(); public void Dispose() => _s.Dispose(); }")
            .Should().BeEmpty();
    }

    [Fact]
    public void Disposable_OwnedFieldMissingFromDispose_Flagged()
    {
        Disposables("class D : IDisposable { private MemoryStream _s; public D() { _s = new MemoryStream(); } public void Dispose() { } }")
            .Should().ContainSingle(i => i.Description.Contains("never disposed"));
    }

    [Fact]
    public void Disposable_StaticField_NotFlagged()
    {
        Disposables("class D { private static readonly MemoryStream Shared = new MemoryStream(); }")
            .Should().BeEmpty();
    }

    // ── StringConcatenation ──────────────────────────────────────────────────

    [Fact]
    public void Concatenation_InNestedLoops_ReportedOnce()
    {
        // The old check ran per loop, so an assignment inside two loops was reported twice.
        Concatenation("string s = \"\"; for (int i = 0; i < n; i++) { foreach (var x in items) { s += x; } }")
            .Should().ContainSingle();
    }

    [Fact]
    public void Concatenation_StringTargetWithAnyName_Flagged()
    {
        // 'result' contains neither 'str' nor 'text'; the old name-based check missed it.
        Concatenation("string result = \"\"; foreach (var x in items) result += x;")
            .Should().ContainSingle(i => i.IssueType == "StringConcatenation");
    }

    [Fact]
    public void Concatenation_NonStringTargetNamedLikeString_NotFlagged()
    {
        Concatenation("int strCount = 0; foreach (var x in items) strCount += x.Length;")
            .Should().BeEmpty();
    }

    [Fact]
    public void Concatenation_SelfAssignmentWithPlus_Flagged()
    {
        Concatenation("string s = \"\"; while (n-- > 0) { s = s + \",\" + n; }")
            .Should().ContainSingle();
    }

    [Fact]
    public void Concatenation_FieldInLoop_Flagged()
    {
        Concatenation("foreach (var x in items) _log += x;")
            .Should().ContainSingle();
    }

    [Fact]
    public void Concatenation_StringDeclaredInsideLoop_NotFlagged()
    {
        Concatenation("foreach (var x in items) { var line = \"\"; line += x; line += \";\"; }")
            .Should().BeEmpty();
    }

    [Fact]
    public void Concatenation_OutsideLoop_NotFlagged()
    {
        Concatenation("string s = \"\"; s += items[0];").Should().BeEmpty();
    }

    // ── ExceptionHandling ────────────────────────────────────────────────────

    [Fact]
    public void ExceptionHandling_EmptyCatchOfException_ReportedOnce()
    {
        // Previously reported both as "Empty catch block" and "Catching Exception without handling".
        var (tree, model) = InMemoryCompilation.CompileSingle(
            "using System; class C { void M() { try { } catch (Exception) { } } }");
        PerformanceIssueAnalyzer.AnalyzeExceptionHandling(tree.GetRoot(), model, tree, "P")
            .Should().ContainSingle(i => i.Title == "Empty catch block");
    }
}
