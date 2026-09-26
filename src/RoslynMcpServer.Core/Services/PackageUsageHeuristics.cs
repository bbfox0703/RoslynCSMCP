using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Xml.Linq;

namespace RoslynMcpServer.Core.Services
{
    /// <summary>
    /// Shared "is this PackageReference used?" heuristic for AnalyzePackages (PackageAnalysisService)
    /// and FindUnusedDependencies (UnusedDependencyAnalyzer), so both tools give the same answer.
    ///
    /// A package counts as used when a using directive in the project (including global usings,
    /// such as the ones the SDK generates from csproj &lt;Using&gt; items) imports one of its expected
    /// namespaces or a sub-namespace of one. Packages that contribute no compile-time assets
    /// (IncludeAssets without compile, or ExcludeAssets with compile) and well-known build, test,
    /// and analyzer packages are always treated as used.
    /// </summary>
    internal static class PackageUsageHeuristics
    {
        internal sealed record PackageReferenceInfo(string Name, string Version, bool HasCompileAssets);

        // Packages whose namespace differs from their ID
        private static readonly Dictionary<string, string[]> KnownNamespaces = new(StringComparer.OrdinalIgnoreCase)
        {
            ["NUnit"] = new[] { "NUnit.Framework" },
            ["xunit"] = new[] { "Xunit" },
            ["xunit.v3"] = new[] { "Xunit" },
            ["xunit.assert"] = new[] { "Xunit" },
            ["xunit.core"] = new[] { "Xunit" },
            ["MSTest.TestFramework"] = new[] { "Microsoft.VisualStudio.TestTools.UnitTesting" },
            ["Microsoft.CodeAnalysis.Workspaces.MSBuild"] = new[] { "Microsoft.CodeAnalysis.MSBuild" },
        };

        // Packages that are consumed by the build, the test host, or analyzers rather than by code
        private static readonly string[] AlwaysUsedPackages =
        {
            "Microsoft.NET.Test.Sdk",
            "coverlet.collector",
            "coverlet.msbuild",
            "xunit.runner.visualstudio",
            "xunit.runner.console",
            "NUnit3TestAdapter",
            "MSTest.TestAdapter",
            "Microsoft.SourceLink.GitHub",
            "Microsoft.SourceLink.AzureRepos.Git",
            "Microsoft.SourceLink.GitLab",
            "Microsoft.SourceLink.Bitbucket.Git",
            "Microsoft.CodeAnalysis.NetAnalyzers",
            "StyleCop.Analyzers",
            "SonarAnalyzer.CSharp",
            "Roslynator.Analyzers",
            "Meziantou.Analyzer",
            "Microsoft.VisualStudio.Threading.Analyzers",
            "Microsoft.CodeAnalysis.PublicApiAnalyzers",
            "Microsoft.CodeAnalysis.BannedApiAnalyzers",
            "Nerdbank.GitVersioning",
            "MinVer",
            "GitVersion.MsBuild",
        };

        /// <summary>
        /// Reads the PackageReference items written directly in a project file. Versions come from
        /// the Version (or VersionOverride) attribute or element; versions defined centrally in
        /// Directory.Packages.props are not resolved.
        /// </summary>
        internal static List<PackageReferenceInfo> ReadPackageReferences(string projectFilePath)
        {
            var doc = XDocument.Load(projectFilePath);

            return doc.Descendants()
                .Where(e => e.Name.LocalName == "PackageReference")
                .Select(e => new PackageReferenceInfo(
                    GetValue(e, "Include") ?? string.Empty,
                    GetValue(e, "Version") ?? GetValue(e, "VersionOverride") ?? string.Empty,
                    HasCompileAssets(GetValue(e, "IncludeAssets"), GetValue(e, "ExcludeAssets"))))
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .ToList();
        }

        private static string? GetValue(XElement element, string name)
        {
            var value = element.Attribute(name)?.Value
                ?? element.Elements().FirstOrDefault(c => c.Name.LocalName == name)?.Value;
            return value?.Trim();
        }

        internal static bool HasCompileAssets(string? includeAssets, string? excludeAssets)
        {
            static HashSet<string> Parse(string? value) =>
                (value ?? string.Empty)
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var include = Parse(includeAssets);
            var exclude = Parse(excludeAssets);

            var included = include.Count == 0 || include.Contains("all") || include.Contains("compile");
            var excluded = exclude.Contains("all") || exclude.Contains("compile");
            return included && !excluded;
        }

        /// <summary>
        /// Namespaces imported by the using directives of the given trees, with any global:: alias
        /// removed: "global using global::Newtonsoft.Json;" yields "Newtonsoft.Json".
        /// Alias and static usings contribute their target namespace or type name.
        /// </summary>
        internal static HashSet<string> CollectImportedNamespaces(IEnumerable<SyntaxTree> trees)
        {
            var imported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var tree in trees)
            {
                var root = tree.GetRoot();
                var usings = root
                    .DescendantNodes(n => n is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax)
                    .OfType<UsingDirectiveSyntax>();

                foreach (var usingDirective in usings)
                {
                    var name = NormalizeUsingTarget(usingDirective.NamespaceOrType);
                    if (name.Length > 0)
                        imported.Add(name);
                }
            }

            return imported;
        }

        internal static string NormalizeUsingTarget(TypeSyntax? target)
        {
            if (target == null)
                return string.Empty;

            var text = string.Concat(target.ToString().Where(c => !char.IsWhiteSpace(c)));
            const string globalPrefix = "global::";
            return text.StartsWith(globalPrefix, StringComparison.Ordinal) ? text.Substring(globalPrefix.Length) : text;
        }

        /// <summary>
        /// Namespaces a package is expected to provide: a known mapping, otherwise the package ID and,
        /// for IDs with three or more segments, the ID without its last segment
        /// (Microsoft.Extensions.Logging.Abstractions → Microsoft.Extensions.Logging).
        /// </summary>
        internal static List<string> GetExpectedNamespaces(string packageId)
        {
            if (KnownNamespaces.TryGetValue(packageId, out var known))
                return known.ToList();

            var expected = new List<string> { packageId };
            var parts = packageId.Split('.');
            if (parts.Length > 2)
                expected.Add(string.Join(".", parts.Take(parts.Length - 1)));

            return expected;
        }

        /// <summary>
        /// Expected namespaces that some using directive imports, directly or through a sub-namespace.
        /// </summary>
        internal static List<string> GetUsedNamespaces(IEnumerable<string> expectedNamespaces, ISet<string> importedNamespaces)
        {
            return expectedNamespaces
                .Where(ns => importedNamespaces.Any(u =>
                    u.Equals(ns, StringComparison.OrdinalIgnoreCase) ||
                    u.StartsWith(ns + ".", StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        internal static bool IsAlwaysUsedPackage(string packageId)
        {
            return AlwaysUsedPackages.Contains(packageId, StringComparer.OrdinalIgnoreCase) ||
                   packageId.EndsWith(".Analyzers", StringComparison.OrdinalIgnoreCase) ||
                   packageId.StartsWith("Microsoft.SourceLink.", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Applies the heuristic to one package reference.
        /// </summary>
        internal static (bool IsUsed, List<string> ExpectedNamespaces, List<string> UsedNamespaces) Evaluate(
            PackageReferenceInfo package,
            ISet<string> importedNamespaces)
        {
            var expected = GetExpectedNamespaces(package.Name);
            var used = GetUsedNamespaces(expected, importedNamespaces);
            var isUsed = used.Count > 0 || !package.HasCompileAssets || IsAlwaysUsedPackage(package.Name);
            return (isUsed, expected, used);
        }
    }
}
