using FluentAssertions;
using RoslynMcpServer.Core.Models;
using RoslynMcpServer.Core.Services;
using RoslynMcpServer.Tests.Helpers;

namespace RoslynMcpServer.Tests.Unit.Services;

/// <summary>
/// Unit tests for AnalyzeExceptionHandling (Phase2AnalysisService), run against in-memory compilations.
/// </summary>
public class ExceptionHandlingAnalysisTests
{
    private static ExceptionHandlingResults Analyze(
        string source,
        bool checkEmptyCatch = true,
        bool checkSwallowedExceptions = true,
        bool checkMissingUsing = true,
        bool checkGenericCatch = true)
    {
        var (tree, model) = InMemoryCompilation.CompileSingle(
            "using System; using System.IO;\n" + source,
            Path.Combine("src", "Handlers.cs"));

        var results = new ExceptionHandlingResults();
        Phase2AnalysisService.AnalyzeExceptionHandlingInTree(
            tree,
            model,
            "Proj",
            new Phase2AnalysisService.ExceptionHandlingOptions(checkEmptyCatch, checkSwallowedExceptions, checkMissingUsing, checkGenericCatch),
            results);
        Phase2AnalysisService.FinalizeExceptionHandlingCounts(results);
        return results;
    }

    // ── MissingUsing ─────────────────────────────────────────────────────────

    [Fact]
    public void MissingUsing_UsingVarDeclaration_NotFlagged()
    {
        // The old check looked for a UsingStatementSyntax parent, which a using declaration never has.
        Analyze("class C { void M() { using var s = new MemoryStream(); s.WriteByte(1); } }")
            .Issues.Should().NotContain(i => i.IssueType == "MissingUsing");
    }

    [Fact]
    public void MissingUsing_ReturnedLocal_NotFlagged()
    {
        Analyze("class C { Stream M() { var s = new MemoryStream(); return s; } }")
            .Issues.Should().NotContain(i => i.IssueType == "MissingUsing");
    }

    [Fact]
    public void MissingUsing_UndisposedLocal_FlaggedAndCounted()
    {
        var results = Analyze("class C { void M() { var s = new MemoryStream(); s.WriteByte(1); } }");

        results.Issues.Should().ContainSingle(i => i.IssueType == "MissingUsing");
        results.MissingUsingCount.Should().Be(1);
        results.MediumCount.Should().Be(1);
    }

    // ── Issue metadata ───────────────────────────────────────────────────────

    [Fact]
    public void Issues_CarryMethodFileProjectExceptionTypeAndCatchLine()
    {
        var results = Analyze(
            "class C {\n" +
            "  void Load() {\n" +
            "    try {\n" +
            "      Console.WriteLine();\n" +
            "    }\n" +
            "    catch (IOException) { }\n" +
            "  }\n" +
            "}");

        var issue = results.Issues.Should().ContainSingle(i => i.IssueType == "EmptyCatch").Subject;
        issue.MethodName.Should().Be("Load");
        issue.FileName.Should().Be("Handlers.cs");
        issue.ProjectName.Should().Be("Proj");
        issue.ExceptionType.Should().Be("System.IO.IOException");
        // Line of the catch clause (line 7 after the usings line), not of the try statement.
        issue.LineNumber.Should().Be(7);
    }

    // ── GenericException ─────────────────────────────────────────────────────

    [Fact]
    public void GenericCatch_CountedInGenericCatchCount()
    {
        // The split summary prints GenericCatchCount, which was never set.
        var results = Analyze("class C { void M() { try { } catch (Exception ex) { Console.Error.WriteLine(ex); } } }");

        results.GenericExceptionCount.Should().Be(1);
        results.GenericCatchCount.Should().Be(1);
        results.LowCount.Should().Be(1);
    }

    [Fact]
    public void GenericCatch_CanBeTurnedOff()
    {
        Analyze("class C { void M() { try { } catch (Exception ex) { Console.Error.WriteLine(ex); } } }", checkGenericCatch: false)
            .Issues.Should().NotContain(i => i.IssueType == "GenericException");
    }

    [Fact]
    public void GenericCatch_WithExceptionFilter_NotFlagged()
    {
        Analyze("class C { void M() { try { } catch (Exception ex) when (ex is not OutOfMemoryException) { Console.Error.WriteLine(ex); } } }")
            .Issues.Should().NotContain(i => i.IssueType == "GenericException");
    }

    [Fact]
    public void GenericCatch_BareCatch_Flagged()
    {
        Analyze("class C { void M() { try { } catch { Console.Error.WriteLine(); } } }")
            .Issues.Should().ContainSingle(i => i.IssueType == "GenericException" && i.ExceptionType == "(all exceptions)");
    }

    [Fact]
    public void SwallowedException_Disabled_DoesNotDisableGenericCatch()
    {
        // The split tool used to map GenericCatch onto checkSwallowedExceptions.
        var results = Analyze("class C { int M() { try { return 1; } catch (Exception) { return 0; } } }", checkSwallowedExceptions: false);

        results.Issues.Should().ContainSingle(i => i.IssueType == "GenericException");
        results.SwallowedExceptionCount.Should().Be(0);
    }
}
