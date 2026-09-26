using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcpServer.Core.Services;

namespace RoslynMcpServer.Tests.Unit.Services;

/// <summary>
/// Syntax-level checks for the FindReferencesFiltered writesOnly / publicOnly classification
/// and for symbol name normalization. No workspace is loaded.
/// </summary>
public class ReferenceSyntaxClassifierTests
{
    private const string Marker = "/*T*/";

    #region IsWrittenTo

    [Theory]
    [InlineData("/*T*/f = 1;")]
    [InlineData("this./*T*/f = 1;")]
    [InlineData("(/*T*/f) = 1;")]
    [InlineData("/*T*/f += 1;")]
    [InlineData("/*T*/f ??= 1;")]
    [InlineData("/*T*/f++;")]
    [InlineData("++/*T*/f;")]
    [InlineData("/*T*/f--;")]
    [InlineData("--/*T*/f;")]
    [InlineData("Take(out /*T*/f);")]
    [InlineData("TakeRef(ref /*T*/f);")]
    [InlineData("TakeRef(ref this./*T*/f);")]
    [InlineData("(/*T*/f, g) = (1, 2);")]
    [InlineData("((/*T*/f, g), g) = ((1, 2), 3);")]
    [InlineData("foreach ((/*T*/f, g) in pairs) { }")]
    [InlineData("var c = new C { /*T*/f = 1 };")]
    [InlineData("other?./*T*/f = 1;")]
    public void IsWrittenTo_WriteForms_ReturnsTrue(string statement)
    {
        ReferenceSyntaxClassifier.IsWrittenTo(FindMarkedName(statement)).Should().BeTrue();
    }

    [Theory]
    [InlineData("g = /*T*/f;")]
    [InlineData("g = /*T*/f + 1;")]
    [InlineData("g += /*T*/f;")]
    [InlineData("g = this./*T*/f;")]
    [InlineData("/*T*/f.ToString();")]
    [InlineData("/*T*/other.f = 1;")]
    [InlineData("arr[/*T*/f] = 1;")]
    [InlineData("(g, h) = (/*T*/f, 1);")]
    [InlineData("TakeIn(in /*T*/f);")]
    [InlineData("Console.WriteLine(/*T*/f);")]
    [InlineData("var n = nameof(/*T*/f);")]
    [InlineData("if (/*T*/f == 0) { }")]
    [InlineData("var x = -/*T*/f;")]
    public void IsWrittenTo_ReadForms_ReturnsFalse(string statement)
    {
        ReferenceSyntaxClassifier.IsWrittenTo(FindMarkedName(statement)).Should().BeFalse();
    }

    private static SyntaxNode FindMarkedName(string statement)
    {
        var code = $$"""
            class C
            {
                int f, g, h;
                int[] arr;
                C other;
                (int, int)[] pairs;
                void M()
                {
                    {{statement}}
                }
            }
            """;
        var root = CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(LanguageVersion.Preview)).GetRoot();
        return NameAfterMarker(root, code, Marker);
    }

    /// <summary>
    /// The name token right after the marker comment. Located by position because a comment that
    /// follows another token on the same line is that token's trailing trivia.
    /// </summary>
    private static SyntaxNode NameAfterMarker(SyntaxNode root, string code, string marker)
    {
        var position = code.IndexOf(marker, StringComparison.Ordinal);
        position.Should().BeGreaterThanOrEqualTo(0, $"marker {marker} must be present");
        var node = root.FindToken(position + marker.Length).Parent!;
        node.Should().BeAssignableTo<SimpleNameSyntax>();
        return node;
    }

    #endregion

    #region IsInPublicApiContext

    private const string VisibilityCode = """
        public class Api
        {
            public int PublicMethod() => /*T1*/Target.Value;
            private int PrivateMethod() => /*T2*/Target.Value;
            internal int InternalMethod() => /*T3*/Target.Value;
            protected int ProtectedMethod() => /*T4*/Target.Value;
            public int Prop
            {
                get => /*T5*/Target.Value;
                private set { var x = /*T6*/Target.Value; }
            }
            public int Field = /*T7*/Target.Value;
            private class Nested
            {
                public int M() => /*T8*/Target.Value;
            }
            public int Create(/*T9*/Target t) => 0;
            protected internal int ProtectedInternal() => /*T11*/Target.Value;
            private protected int PrivateProtected() => /*T12*/Target.Value;
        }

        internal class Hidden
        {
            public int M() => /*T10*/Target.Value;
        }

        public class Target
        {
            public static int Value;
        }
        """;

    [Theory]
    [InlineData("T1", true)]
    [InlineData("T2", false)]
    [InlineData("T3", false)]
    [InlineData("T4", true)]
    [InlineData("T5", true)]
    [InlineData("T6", false)]
    [InlineData("T7", true)]
    [InlineData("T8", false)]
    [InlineData("T9", true)]
    [InlineData("T10", false)]
    [InlineData("T11", true)]
    [InlineData("T12", false)]
    public void IsInPublicApiContext_UsesEnclosingMemberVisibility(string marker, bool expected)
    {
        var (node, model) = FindMarkedNameWithModel(VisibilityCode, $"/*{marker}*/");

        ReferenceSyntaxClassifier.IsInPublicApiContext(node, model).Should().Be(expected);
    }

    [Fact]
    public void IsInPublicApiContext_TopLevelStatement_ReturnsFalse()
    {
        var code = """
            var x = /*T*/Target.Value;

            public class Target
            {
                public static int Value;
            }
            """;
        var (node, model) = FindMarkedNameWithModel(code, Marker, OutputKind.ConsoleApplication);

        ReferenceSyntaxClassifier.IsInPublicApiContext(node, model).Should().BeFalse();
    }

    [Fact]
    public void IsInPublicApiContext_Declarations_UseTheirOwnVisibility()
    {
        var tree = CSharpSyntaxTree.ParseText(VisibilityCode);
        var model = CreateCompilation(tree, OutputKind.DynamicallyLinkedLibrary).GetSemanticModel(tree);
        var root = tree.GetRoot();

        // Declaration sites are classified from the node at the declared identifier
        var publicMethod = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.Text == "PublicMethod");
        var privateMethod = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.Text == "PrivateMethod");
        var publicField = root.DescendantNodes().OfType<VariableDeclaratorSyntax>().Single(v => v.Identifier.Text == "Field");

        ReferenceSyntaxClassifier.IsInPublicApiContext(root.FindNode(publicMethod.Identifier.Span), model).Should().BeTrue();
        ReferenceSyntaxClassifier.IsInPublicApiContext(root.FindNode(privateMethod.Identifier.Span), model).Should().BeFalse();
        ReferenceSyntaxClassifier.IsInPublicApiContext(root.FindNode(publicField.Identifier.Span), model).Should().BeTrue();
    }

    private static (SyntaxNode Node, SemanticModel Model) FindMarkedNameWithModel(
        string code, string marker, OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary)
    {
        var tree = CSharpSyntaxTree.ParseText(code);
        var model = CreateCompilation(tree, outputKind).GetSemanticModel(tree);
        return (NameAfterMarker(tree.GetRoot(), code, marker), model);
    }

    private static CSharpCompilation CreateCompilation(SyntaxTree tree, OutputKind outputKind) =>
        CSharpCompilation.Create(
            "VisibilityTest",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(outputKind));

    #endregion

    #region NormalizeRequestedName

    [Theory]
    [InlineData("Save", "Save")]
    [InlineData("  UserService.Save  ", "UserService.Save")]
    [InlineData("global::MyApp.Services.UserService.Save", "MyApp.Services.UserService.Save")]
    [InlineData("Repository<T>.Save(int, string)", "Repository.Save")]
    [InlineData("Dictionary<string, List<int>>", "Dictionary")]
    [InlineData("List`1", "List")]
    [InlineData("MyApp.Outer+Inner", "MyApp.Outer.Inner")]
    [InlineData("Save()", "Save")]
    public void NormalizeRequestedName_StripsDecorations(string input, string expected)
    {
        SymbolSearchService.NormalizeRequestedName(input).Should().Be(expected);
    }

    #endregion
}
