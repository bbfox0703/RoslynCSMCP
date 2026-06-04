using System.Runtime.CompilerServices;
using FluentAssertions;
using RoslynMcpServer.Core.Models;
using RoslynMcpServer.Core.Services;

namespace RoslynMcpServer.Tests.Unit.Services;

/// <summary>
/// Real-world validation of the AOT XAML/.csproj analyzers against fixtures derived from shipped
/// Avalonia + .NET 10 Native AOT apps (UE5CEDumper, ZoltDump, discrete, AchievoLab, AOBMaker,
/// CrimsonAtomtic). These pin two things:
///   1. The analyzer does NOT false-positive on correctly-configured AOT projects.
///   2. The one finding it does surface on real apps — Avalonia.Desktop on win-x64 — is the
///      single, deliberately-accepted finding (removing it on a shipped app needs a full re-test),
///      and the analyzer reports only that, nothing spurious alongside it.
/// See docs/aot-validation-corpus.md.
/// </summary>
public class AotValidationCorpusTests
{
    private static string ReadFixture(string name, [CallerFilePath] string thisFile = "")
    {
        // thisFile = ...\RoslynMcpServer.Tests\Unit\Services\AotValidationCorpusTests.cs
        var dir = Path.GetDirectoryName(thisFile)!;
        var path = Path.Combine(dir, "..", "..", "Fixtures", "Aot", name);
        return File.ReadAllText(path);
    }

    [Fact]
    public void AotCorrectApp_WithoutAvaloniaDesktop_ProducesNoBuildConfigIssues()
    {
        var issues = AotCompatibilityAnalyzer.AnalyzeBuildConfig(
            ReadFixture("AotCorrect.NoDesktop.csproj.xml"), "App.csproj", "ZoltDump-like");

        issues.Should().BeEmpty("a correctly-configured AOT Avalonia project should not be flagged");
    }

    [Fact]
    public void AppKeepingAvaloniaDesktop_ReportsOnlyTheAcceptedDesktopFinding()
    {
        var issues = AotCompatibilityAnalyzer.AnalyzeBuildConfig(
            ReadFixture("AotWithAvaloniaDesktop.csproj.xml"), "App.csproj", "UE5-like");

        // Avalonia.Desktop on win-x64 is a real, deliberately-accepted finding (kept because
        // removing it on a shipped app is a behavioral change that needs a full re-test).
        // Everything else (ComInterop=false, compiled bindings, TrimmerRootAssembly) is correct,
        // so the analyzer must surface exactly this one issue and nothing else.
        var only = issues.Should().ContainSingle().Subject;
        only.Category.Should().Be("BuildConfig");
        only.Title.Should().Contain("Avalonia.Desktop");
        only.Severity.Should().Be("Medium");
    }

    [Fact]
    public void CompiledBindingView_ProducesNoXamlIssues()
    {
        var issues = AotCompatibilityAnalyzer.AnalyzeXaml(
            ReadFixture("CompiledBindingView.axaml.xml"), "View.axaml", "ZoltDump-like");

        issues.Should().BeEmpty("a compiled-binding view with x:DataType and no bound <Run> is AOT-clean");
    }
}
