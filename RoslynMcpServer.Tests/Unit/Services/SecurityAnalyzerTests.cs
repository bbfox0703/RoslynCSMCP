using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcpServer.Core.Models;
using RoslynMcpServer.Core.Services;

namespace RoslynMcpServer.Tests.Unit.Services;

/// <summary>
/// Unit tests for the semantic (formerly keyword/substring) security detectors.
/// Snippets are compiled in-memory so each detector runs against a real semantic model.
/// </summary>
public class SecurityAnalyzerTests
{
    private static readonly Lazy<List<MetadataReference>> References = new(() =>
        ((AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string) ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList());

    private static (SyntaxNode Root, SemanticModel Model) Compile(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "SecTest", new[] { tree }, References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return (tree.GetRoot(), compilation.GetSemanticModel(tree));
    }

    private static List<SecurityIssue> Sql(string body)
    {
        var (root, model) = Compile(
            "class C { void M(string name, int id, int count) {\n" + body + "\n} }");
        return SecurityIssueAnalyzer.AnalyzeSqlInjection(root, model, "f.cs", "f.cs", "P");
    }

    // ── SQL injection ─────────────────────────────────────────────────────────

    [Fact]
    public void Sql_ConcatWithRuntimeValueIntoQuery_Flagged()
    {
        Sql("string q = \"SELECT * FROM Users WHERE name = '\" + name + \"'\";")
            .Should().ContainSingle(i => i.Category == "sql-injection");
    }

    [Fact]
    public void Sql_InterpolationWithRuntimeValue_Flagged()
    {
        Sql("string q = $\"SELECT * FROM t WHERE id = {id}\";")
            .Should().ContainSingle(i => i.Category == "sql-injection");
    }

    [Fact]
    public void Sql_ProseContainingWordUpdated_NotFlagged()
    {
        // Old keyword scan flagged this because "updated" contains "update".
        Sql("string msg = \"User \" + name + \" was updated\";")
            .Should().BeEmpty();
    }

    [Fact]
    public void Sql_ConstantOnlyQuery_NotFlagged()
    {
        // No runtime value spliced in → not injectable.
        Sql("string q = \"SELECT * FROM Users WHERE id = 1\";")
            .Should().BeEmpty();
    }

    [Fact]
    public void Sql_InterpolationWithConstantHole_NotFlagged()
    {
        Sql("string q = $\"SELECT * FROM t WHERE id = {5}\";")
            .Should().BeEmpty();
    }

    [Fact]
    public void Sql_NonSqlInterpolation_NotFlagged()
    {
        Sql("string s = $\"deleted {count} items from cache\";")
            .Should().BeEmpty();
    }

    // ── Weak cryptography ─────────────────────────────────────────────────────

    [Fact]
    public void Crypto_Md5Usage_Flagged()
    {
        var (root, model) = Compile(
            "class C { void M() { var h = System.Security.Cryptography.MD5.Create(); } }");
        SecurityIssueAnalyzer.AnalyzeWeakCryptography(root, model, "f.cs", "f.cs", "P")
            .Should().ContainSingle(i => i.Title.Contains("MD5"));
    }

    [Fact]
    public void Crypto_TypeNameContainingDesSubstring_NotFlagged()
    {
        // "ResultDescriptor" contains "Des" — the old substring match produced a false positive.
        var (root, model) = Compile(
            "class ResultDescriptor { } class C { void M() { var d = new ResultDescriptor(); } }");
        SecurityIssueAnalyzer.AnalyzeWeakCryptography(root, model, "f.cs", "f.cs", "P")
            .Should().BeEmpty();
    }

    [Fact]
    public void Crypto_Sha256_NotFlagged()
    {
        var (root, model) = Compile(
            "class C { void M() { var h = System.Security.Cryptography.SHA256.Create(); } }");
        SecurityIssueAnalyzer.AnalyzeWeakCryptography(root, model, "f.cs", "f.cs", "P")
            .Should().BeEmpty();
    }

    [Fact]
    public void Crypto_SameTypeTwiceOnOneLine_ReportedOnce()
    {
        var (root, model) = Compile(
            "class C { void M() { var h = System.Security.Cryptography.MD5.Create() ?? System.Security.Cryptography.MD5.Create(); } }");
        SecurityIssueAnalyzer.AnalyzeWeakCryptography(root, model, "f.cs", "f.cs", "P")
            .Should().HaveCount(1);
    }

    // ── Insecure deserialization (self-contained stub types) ──────────────────

    [Fact]
    public void Deser_BinaryFormatter_Flagged()
    {
        var (root, model) = Compile(@"
namespace System.Runtime.Serialization.Formatters.Binary { public class BinaryFormatter { } }
class C { void M() { var f = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter(); } }");
        SecurityIssueAnalyzer.AnalyzeDeserialization(root, model, "f.cs", "f.cs", "P")
            .Should().ContainSingle(i => i.Title.Contains("BinaryFormatter") && i.Severity == "Critical");
    }
}
