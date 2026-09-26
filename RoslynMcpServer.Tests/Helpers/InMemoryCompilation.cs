using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace RoslynMcpServer.Tests.Helpers;

/// <summary>
/// Compiles C# snippets in memory against the running framework's assemblies, so analyzers can be
/// exercised with a real semantic model without MSBuild or a solution on disk.
/// </summary>
public static class InMemoryCompilation
{
    private static readonly Lazy<List<MetadataReference>> References = new(() =>
        ((AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string) ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList());

    /// <summary>
    /// Compiles the sources as one library; each source gets the matching path (or Source{i}.cs).
    /// </summary>
    public static CSharpCompilation Compile(IEnumerable<string> sources, IReadOnlyList<string>? paths = null)
    {
        var trees = sources
            .Select((source, i) => CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.Preview),
                path: paths != null && i < paths.Count ? paths[i] : $"Source{i}.cs"))
            .ToList();

        return CSharpCompilation.Create(
            "InMemoryTest",
            trees,
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    /// <summary>
    /// Compiles one source file and returns its tree and semantic model.
    /// </summary>
    public static (SyntaxTree Tree, SemanticModel Model) CompileSingle(string source, string path = "Test.cs")
    {
        var compilation = Compile(new[] { source }, new[] { path });
        var tree = compilation.SyntaxTrees.Single();
        return (tree, compilation.GetSemanticModel(tree));
    }
}
