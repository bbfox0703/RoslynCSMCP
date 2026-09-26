using FluentAssertions;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcpServer.Core.Models;
using RoslynMcpServer.Core.Services;

namespace RoslynMcpServer.Tests.Unit.Services;

/// <summary>
/// Unit tests for the package-usage heuristic shared by AnalyzePackages and FindUnusedDependencies,
/// and for parsing 'dotnet list package --vulnerable --format json'.
/// </summary>
public class PackageAnalysisTests
{
    private static HashSet<string> Imports(params string[] sources) =>
        PackageUsageHeuristics.CollectImportedNamespaces(sources.Select(s => CSharpSyntaxTree.ParseText(s)));

    private static bool IsUsed(string packageId, HashSet<string> imported, bool hasCompileAssets = true) =>
        PackageUsageHeuristics.Evaluate(new PackageUsageHeuristics.PackageReferenceInfo(packageId, "1.0.0", hasCompileAssets), imported).IsUsed;

    // ── Imported namespaces ──────────────────────────────────────────────────

    [Fact]
    public void Imports_GlobalUsingWithGlobalAlias_Normalized()
    {
        // The form the SDK generates for csproj <Using> items.
        Imports("global using global::Newtonsoft.Json;")
            .Should().Contain("Newtonsoft.Json");
    }

    [Fact]
    public void Imports_StaticAliasAndNamespaceScopedUsings_Collected()
    {
        Imports(
            "using static global::System.Math;\nusing J = Newtonsoft.Json.Linq;\nnamespace N { using Serilog.Events; class C { } }")
            .Should().BeEquivalentTo(new[] { "System.Math", "Newtonsoft.Json.Linq", "Serilog.Events" });
    }

    // ── Usage heuristic ──────────────────────────────────────────────────────

    [Fact]
    public void Package_ImportedOnlyThroughGlobalUsing_IsUsed()
    {
        IsUsed("Newtonsoft.Json", Imports("global using global::Newtonsoft.Json;")).Should().BeTrue();
    }

    [Fact]
    public void Package_ImportedThroughSubNamespace_IsUsed()
    {
        // AnalyzePackages used an exact match here, FindUnusedDependencies a prefix match.
        IsUsed("Newtonsoft.Json", Imports("using Newtonsoft.Json.Linq;")).Should().BeTrue();
    }

    [Fact]
    public void Package_PrefixWithoutNamespaceBoundary_IsNotUsed()
    {
        IsUsed("Serilog", Imports("using SerilogTimings;")).Should().BeFalse();
    }

    [Fact]
    public void Package_ThreeSegmentId_MatchesParentNamespace()
    {
        IsUsed("Microsoft.Extensions.Logging.Abstractions", Imports("using Microsoft.Extensions.Logging;"))
            .Should().BeTrue();
    }

    [Fact]
    public void Package_WithoutCompileAssets_IsUsed()
    {
        IsUsed("Some.Build.Tool", Imports("using System;"), hasCompileAssets: false).Should().BeTrue();
    }

    [Theory]
    [InlineData("Microsoft.NET.Test.Sdk")]
    [InlineData("xunit.runner.visualstudio")]
    [InlineData("coverlet.collector")]
    [InlineData("Contoso.Analyzers")]
    public void KnownBuildTestAndAnalyzerPackages_AreUsed(string packageId)
    {
        IsUsed(packageId, Imports("using System;")).Should().BeTrue();
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData("runtime; build; native; contentfiles; analyzers; buildtransitive", null, false)]
    [InlineData("compile; build", null, true)]
    [InlineData("all", "compile", false)]
    [InlineData(null, "runtime", true)]
    public void HasCompileAssets_FollowsIncludeAndExcludeAssets(string? include, string? exclude, bool expected)
    {
        PackageUsageHeuristics.HasCompileAssets(include, exclude).Should().Be(expected);
    }

    [Fact]
    public void ReadPackageReferences_ReadsAttributesElementsAndAssets()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pkgtest-{Guid.NewGuid():N}.csproj");
        File.WriteAllText(path, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.4" />
                <PackageReference Include="Serilog"><Version>4.3.1</Version></PackageReference>
                <PackageReference Include="Pinned" VersionOverride="2.0.0" />
                <PackageReference Include="coverlet.collector" Version="8.0.0">
                  <IncludeAssets>runtime; build</IncludeAssets>
                </PackageReference>
                <PackageReference Update="IgnoredUpdateItem" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);
        try
        {
            var packages = PackageUsageHeuristics.ReadPackageReferences(path);

            packages.Should().BeEquivalentTo(new[]
            {
                new PackageUsageHeuristics.PackageReferenceInfo("Newtonsoft.Json", "13.0.4", true),
                new PackageUsageHeuristics.PackageReferenceInfo("Serilog", "4.3.1", true),
                new PackageUsageHeuristics.PackageReferenceInfo("Pinned", "2.0.0", true),
                new PackageUsageHeuristics.PackageReferenceInfo("coverlet.collector", "8.0.0", false),
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── Vulnerability report parsing ─────────────────────────────────────────

    // Shape captured from 'dotnet list <sln> package --vulnerable --include-transitive --format json' (SDK 10).
    private const string VulnerableReport = """
        {
          "version": 1,
          "parameters": "--vulnerable --include-transitive",
          "sources": [ "https://api.nuget.org/v3/index.json" ],
          "projects": [
            {
              "path": "C:/repo/A/A.csproj",
              "frameworks": [
                {
                  "framework": "net10.0",
                  "topLevelPackages": [
                    {
                      "id": "Newtonsoft.Json", "requestedVersion": "12.0.1", "resolvedVersion": "12.0.1",
                      "vulnerabilities": [ { "severity": "High", "advisoryurl": "https://github.com/advisories/GHSA-5crp-9r3c-p9vr" } ]
                    },
                    {
                      "id": "System.Text.Json", "requestedVersion": "8.0.0", "resolvedVersion": "8.0.0",
                      "vulnerabilities": [
                        { "severity": "High", "advisoryurl": "https://github.com/advisories/GHSA-hh2w-p6rv-4g7w" },
                        { "severity": "High", "advisoryurl": "https://github.com/advisories/GHSA-8g4q-xg66-9fp4" }
                      ]
                    }
                  ],
                  "transitivePackages": [
                    {
                      "id": "System.Net.Http", "resolvedVersion": "4.3.0",
                      "vulnerabilities": [ { "severity": "Moderate", "advisoryurl": "https://github.com/advisories/GHSA-7jgj-8wvc-jh57" } ]
                    }
                  ]
                }
              ]
            },
            {
              "path": "C:/repo/B/B.csproj",
              "frameworks": [
                {
                  "framework": "net10.0",
                  "topLevelPackages": [
                    {
                      "id": "Newtonsoft.Json", "requestedVersion": "12.0.1", "resolvedVersion": "12.0.1",
                      "vulnerabilities": [ { "severity": "High", "advisoryurl": "https://github.com/advisories/GHSA-5crp-9r3c-p9vr" } ]
                    }
                  ]
                }
              ]
            },
            { "path": "C:/repo/C/C.csproj" }
          ]
        }
        """;

    [Fact]
    public void ParseVulnerabilityReport_AggregatesAdvisoriesAcrossProjects()
    {
        var warnings = new List<OperationWarning>();
        var vulnerabilities = PackageAnalysisService.ParseVulnerabilityReport(VulnerableReport, warnings);

        warnings.Should().BeEmpty();
        vulnerabilities.Should().HaveCount(4);

        var newtonsoft = vulnerabilities.Single(v => v.PackageName == "Newtonsoft.Json");
        newtonsoft.AffectedVersion.Should().Be("12.0.1");
        newtonsoft.Severity.Should().Be("High");
        newtonsoft.VulnerabilityId.Should().Be("GHSA-5crp-9r3c-p9vr");
        newtonsoft.AdvisoryUrl.Should().Be("https://github.com/advisories/GHSA-5crp-9r3c-p9vr");
        newtonsoft.AffectedProjects.Should().BeEquivalentTo(new[] { "A", "B" });
        newtonsoft.IsTransitive.Should().BeFalse();

        vulnerabilities.Count(v => v.PackageName == "System.Text.Json").Should().Be(2);

        var transitive = vulnerabilities.Single(v => v.PackageName == "System.Net.Http");
        transitive.IsTransitive.Should().BeTrue();
        transitive.Severity.Should().Be("Medium");

        // Most severe first
        vulnerabilities.Last().Should().BeSameAs(transitive);
    }

    [Fact]
    public void ParseVulnerabilityReport_ProblemsBecomeWarnings()
    {
        var warnings = new List<OperationWarning>();
        var vulnerabilities = PackageAnalysisService.ParseVulnerabilityReport("""
            { "version": 1, "problems": [ { "text": "Restore failed. Run `dotnet restore` for more details on the issue.", "level": "error" } ] }
            """, warnings);

        vulnerabilities.Should().BeEmpty();
        warnings.Should().ContainSingle(w => w.Context == "Vulnerability Check" && w.Message.Contains("Restore failed"));
    }

    [Fact]
    public void ParseVulnerabilityReport_NoVulnerabilities_Empty()
    {
        var warnings = new List<OperationWarning>();
        PackageAnalysisService.ParseVulnerabilityReport("""
            { "version": 1, "projects": [ { "path": "C:/repo/A/A.csproj" } ] }
            """, warnings).Should().BeEmpty();
        warnings.Should().BeEmpty();
    }

    // ── Version conflicts ────────────────────────────────────────────────────

    [Fact]
    public void DetectVersionConflicts_RecommendsHighestVersion()
    {
        var conflicts = PackageAnalysisService.DetectVersionConflicts(new List<PackageInfo>
        {
            new() { Name = "Newtonsoft.Json", Version = "13.0.3", ProjectName = "A" },
            new() { Name = "newtonsoft.json", Version = "13.0.4", ProjectName = "B" },
            new() { Name = "Serilog", Version = "4.3.1", ProjectName = "A" },
        });

        conflicts.Should().ContainSingle()
            .Which.RecommendedVersion.Should().Be("13.0.4");
    }
}
