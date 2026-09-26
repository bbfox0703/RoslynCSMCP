using Microsoft.Build.Locator;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Core.Services;
using RoslynMcpServer.Dependencies.Tools;
using RoslynMcpServer.Tests.Helpers;

namespace RoslynMcpServer.Tests.Integration.Tools;

/// <summary>
/// Integration tests for the split Dependencies module's AnalyzePackages and FindUnusedDependencies.
/// </summary>
public class SplitPackageToolsTests : IDisposable
{
    private readonly IntegrationTestHelper _testHelper;
    private readonly PackageAnalysisService _packageAnalyzer;
    private readonly UnusedDependencyAnalyzer _unusedAnalyzer;
    private readonly SecurityValidator _validator = new(NullLogger<SecurityValidator>.Instance);
    private readonly McpErrorHandler _errorHandler = new(NullLogger<McpErrorHandler>.Instance);

    static SplitPackageToolsTests()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }
    }

    public SplitPackageToolsTests()
    {
        _testHelper = new IntegrationTestHelper(nameof(SplitPackageToolsTests));
        _unusedAnalyzer = new UnusedDependencyAnalyzer(NullLogger<UnusedDependencyAnalyzer>.Instance);
        _packageAnalyzer = new PackageAnalysisService(NullLogger<PackageAnalysisService>.Instance, _unusedAnalyzer);
    }

    public void Dispose() => _testHelper.Dispose();

    /// <summary>
    /// Two projects on different Newtonsoft.Json versions (a conflict). ProjA imports it only through
    /// a global using with the global:: alias, ProjB through a sub-namespace; ProjA's Serilog is unused.
    /// Versions are ones this repository already restores, so no network access is needed.
    /// </summary>
    private string CreateSolution()
    {
        var projA = new ProjectDefinition("ProjA")
            .AddPackageReference("Newtonsoft.Json", "13.0.4")
            .AddPackageReference("Serilog", "4.3.1")
            .AddSourceFile("Usings.cs", "global using global::Newtonsoft.Json;")
            .AddSourceFile("A.cs", "namespace ProjA; public class A { public string S() => JsonConvert.SerializeObject(1); }");

        var projB = new ProjectDefinition("ProjB")
            .AddPackageReference("Newtonsoft.Json", "13.0.3")
            .AddSourceFile("B.cs", "using Newtonsoft.Json.Linq;\nnamespace ProjB; public class B { public JObject O() => new JObject(); }");

        return _testHelper.CreateSolution("PackagesSolution", projA, projB);
    }

    [Fact]
    public async Task AnalyzePackages_ShowsConflictsAndUnusedPackages_WithoutVulnerabilityCheck()
    {
        var solutionPath = CreateSolution();

        var result = await DependencyTools.AnalyzePackages(
            solutionPath,
            format: "normal",
            checkUpdates: false,
            checkConflicts: true,
            checkVulnerabilities: false,
            analyzeUsage: true,
            analyzer: _packageAnalyzer,
            validator: _validator,
            errorHandler: _errorHandler);

        result.Should().Contain("## Version Conflicts (1)");
        result.Should().Contain("Newtonsoft.Json: ");
        result.Should().Contain("## Possibly Unused Packages (1)");
        result.Should().Contain("Serilog v4.3.1 (ProjA)");
        // checkConflicts used to be passed positionally into checkVulnerabilities.
        result.Should().NotContain("Vulnerability Check");
    }

    [Fact]
    public async Task AnalyzePackages_CheckConflictsFalse_OmitsConflicts()
    {
        var solutionPath = CreateSolution();

        var result = await DependencyTools.AnalyzePackages(
            solutionPath,
            format: "summary",
            checkUpdates: false,
            checkConflicts: false,
            checkVulnerabilities: false,
            analyzeUsage: true,
            analyzer: _packageAnalyzer,
            validator: _validator,
            errorHandler: _errorHandler);

        result.Should().Contain("Version conflicts: 0");
        result.Should().Contain("Unused packages: 1");
    }

    [Fact]
    public async Task FindUnusedDependencies_AgreesWithAnalyzePackages()
    {
        var solutionPath = CreateSolution();

        var result = await DependencyTools.FindUnusedDependencies(
            solutionPath,
            format: "normal",
            includeNuGetPackages: true,
            includeProjectReferences: false,
            analyzer: _unusedAnalyzer,
            validator: _validator,
            errorHandler: _errorHandler);

        result.Should().Contain("Serilog (NuGetPackage)");
        result.Should().NotContain("Newtonsoft.Json");
    }
}
