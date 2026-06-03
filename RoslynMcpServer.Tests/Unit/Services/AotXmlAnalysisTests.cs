using FluentAssertions;
using RoslynMcpServer.Core.Services;

namespace RoslynMcpServer.Tests.Unit.Services;

/// <summary>
/// Unit tests for the XML-based (formerly regex) AOT XAML/.csproj analysis.
/// Exercises AotCompatibilityAnalyzer.AnalyzeXaml / AnalyzeBuildConfig directly so no
/// MSBuild workspace is required.
/// </summary>
public class AotXmlAnalysisTests
{
    // ── XAML ────────────────────────────────────────────────────────────────

    [Fact]
    public void Xaml_RunWithBoundText_FlaggedCritical()
    {
        var xaml = @"<UserControl xmlns='https://github.com/avaloniaui'
             xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
             x:DataType='vm:Foo'>
  <TextBlock><Run Text='{Binding Name}'/></TextBlock>
</UserControl>";

        var issues = AotCompatibilityAnalyzer.AnalyzeXaml(xaml, "View.axaml", "P");

        var run = issues.Should().ContainSingle(i => i.Title.Contains("Run Text")).Subject;
        run.Severity.Should().Be("Critical");
        run.LineNumber.Should().Be(4); // the <Run> line
    }

    [Fact]
    public void Xaml_RootWithBindingButNoDataType_FlaggedMedium()
    {
        var xaml = @"<Window xmlns='https://github.com/avaloniaui'>
  <TextBlock Text='{Binding Title}'/>
</Window>";

        var issues = AotCompatibilityAnalyzer.AnalyzeXaml(xaml, "Main.axaml", "P");

        issues.Should().ContainSingle(i => i.Title.Contains("Root element has bindings"))
              .Which.Severity.Should().Be("Medium");
    }

    [Fact]
    public void Xaml_RootWithDataType_NotFlagged()
    {
        var xaml = @"<Window xmlns='https://github.com/avaloniaui'
        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        x:DataType='vm:Main'>
  <TextBlock Text='{Binding Title}'/>
</Window>";

        var issues = AotCompatibilityAnalyzer.AnalyzeXaml(xaml, "Main.axaml", "P");

        issues.Should().NotContain(i => i.Title.Contains("Root element has bindings"));
    }

    [Fact]
    public void Xaml_DataTemplateWithoutDataType_FlaggedButWithDataTypeIsNot()
    {
        var xaml = @"<UserControl xmlns='https://github.com/avaloniaui'
             xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <DataTemplate><TextBlock/></DataTemplate>
  <DataTemplate x:DataType='m:Row'><TextBlock/></DataTemplate>
</UserControl>";

        var issues = AotCompatibilityAnalyzer.AnalyzeXaml(xaml, "V.axaml", "P");

        // Only the first DataTemplate (no x:DataType) is flagged.
        issues.Should().ContainSingle(i => i.Title.Contains("DataTemplate"))
              .Which.LineNumber.Should().Be(3);
    }

    [Fact]
    public void Xaml_DataGridTemplateColumnWithoutSortMemberPath_Flagged()
    {
        var xaml = @"<UserControl xmlns='https://github.com/avaloniaui'>
  <DataGrid>
    <DataGridTemplateColumn Header='A'/>
    <DataGridTemplateColumn Header='B' SortMemberPath='B'/>
  </DataGrid>
</UserControl>";

        var issues = AotCompatibilityAnalyzer.AnalyzeXaml(xaml, "Grid.axaml", "P");

        issues.Should().ContainSingle(i => i.Title.Contains("DataGridTemplateColumn"))
              .Which.LineNumber.Should().Be(3);
    }

    [Fact]
    public void Xaml_Malformed_FallsBackToRegexAndStillDetects()
    {
        // Missing closing tag → not well-formed XML → regex fallback path.
        var xaml = "<TextBlock><Run Text=\"{Binding Name}\">";

        var issues = AotCompatibilityAnalyzer.AnalyzeXaml(xaml, "Broken.axaml", "P");

        issues.Should().Contain(i => i.Title.Contains("Run Text") && i.Severity == "Critical");
    }

    // ── .csproj ─────────────────────────────────────────────────────────────

    private const string AvaloniaMissingEverything = @"<Project Sdk='Microsoft.NET.Sdk'>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include='Avalonia' Version='12.0.0' />
  </ItemGroup>
</Project>";

    [Fact]
    public void Csproj_AvaloniaMissingSettings_FlagsComTrimmerAndBindings()
    {
        var issues = AotCompatibilityAnalyzer.AnalyzeBuildConfig(AvaloniaMissingEverything, "App.csproj", "P");

        issues.Should().Contain(i => i.Title.Contains("BuiltInComInteropSupport") && i.Severity == "High");
        issues.Should().Contain(i => i.Title.Contains("TrimmerRootAssembly") && i.Severity == "Medium");
        issues.Should().Contain(i => i.Title.Contains("AvaloniaUseCompiledBindingsByDefault") && i.Severity == "Low");
    }

    [Fact]
    public void Csproj_NonAvalonia_ReturnsNoIssues()
    {
        var csproj = @"<Project Sdk='Microsoft.NET.Sdk'>
  <ItemGroup>
    <PackageReference Include='Newtonsoft.Json' Version='13.0.4' />
  </ItemGroup>
</Project>";

        AotCompatibilityAnalyzer.AnalyzeBuildConfig(csproj, "App.csproj", "P").Should().BeEmpty();
    }

    [Fact]
    public void Csproj_FullyConfigured_ReturnsNoIssues()
    {
        var csproj = @"<Project Sdk='Microsoft.NET.Sdk'>
  <PropertyGroup>
    <BuiltInComInteropSupport>false</BuiltInComInteropSupport>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include='Avalonia' Version='12.0.0' />
    <TrimmerRootAssembly Include='Avalonia' />
  </ItemGroup>
</Project>";

        AotCompatibilityAnalyzer.AnalyzeBuildConfig(csproj, "App.csproj", "P").Should().BeEmpty();
    }

    [Fact]
    public void Csproj_AvaloniaDesktopOnWinX64_Flagged()
    {
        var csproj = @"<Project Sdk='Microsoft.NET.Sdk'>
  <PropertyGroup>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <BuiltInComInteropSupport>false</BuiltInComInteropSupport>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include='Avalonia.Desktop' Version='12.0.0' />
    <TrimmerRootAssembly Include='Avalonia' />
  </ItemGroup>
</Project>";

        AotCompatibilityAnalyzer.AnalyzeBuildConfig(csproj, "App.csproj", "P")
            .Should().ContainSingle(i => i.Title.Contains("Avalonia.Desktop"))
            .Which.Severity.Should().Be("Medium");
    }

    [Fact]
    public void Csproj_SingleQuotedAndWhitespacedValues_HandledByXmlParser()
    {
        // Single-quoted Include + whitespace around the element value: the old `""`-anchored
        // regex would miss both; the XML parser handles them.
        var csproj = @"<Project Sdk='Microsoft.NET.Sdk'>
  <PropertyGroup>
    <BuiltInComInteropSupport>  false  </BuiltInComInteropSupport>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include='Avalonia' Version='12.0.0' />
    <TrimmerRootAssembly Include='Avalonia' />
  </ItemGroup>
</Project>";

        // Recognized as Avalonia (so not skipped) and BuiltInComInteropSupport reads as false.
        AotCompatibilityAnalyzer.AnalyzeBuildConfig(csproj, "App.csproj", "P")
            .Should().NotContain(i => i.Title.Contains("BuiltInComInteropSupport"));
    }
}
