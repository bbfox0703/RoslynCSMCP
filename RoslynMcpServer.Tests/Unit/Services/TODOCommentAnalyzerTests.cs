using FluentAssertions;
using RoslynMcpServer.Core.Services;

namespace RoslynMcpServer.Tests.Unit.Services;

/// <summary>
/// Unit tests for FindTODOComments marker recognition.
/// </summary>
public class TODOCommentAnalyzerTests
{
    private static readonly HashSet<string> AllTypes =
        new(TODOCommentAnalyzer.DefaultCommentTypes, StringComparer.OrdinalIgnoreCase);

    private static List<TODOCommentAnalyzer.CommentMarker> Markers(string comment, params string[] types) =>
        TODOCommentAnalyzer.FindMarkers(
            comment,
            types.Length == 0 ? AllTypes : new HashSet<string>(types, StringComparer.OrdinalIgnoreCase)).ToList();

    [Theory]
    [InlineData("// Enables debug output")]            // 'debug' is not BUG
    [InlineData("// See the release notes")]           // 'notes' is not NOTE
    [InlineData("// TODOs are tracked in the backlog")] // 'TODOs' is not TODO
    [InlineData("// works around a bug in the parser")] // lower case mid-line is prose
    [InlineData("/// <note>Remarks</note>")]            // XML tag, not a marker
    public void NonMarkers_NotReported(string comment)
    {
        Markers(comment).Should().BeEmpty();
    }

    [Fact]
    public void Todo_WithColon_ParsesMessage()
    {
        Markers("// TODO: fix the retry loop")
            .Should().ContainSingle()
            .Which.Should().Be(new TODOCommentAnalyzer.CommentMarker("TODO", "", "fix the retry loop", 0));
    }

    [Fact]
    public void Todo_WithoutSpaceAfterSlashes_Found()
    {
        Markers("//TODO(alice): tighten validation")
            .Should().ContainSingle(m => m.Type == "TODO" && m.Author == "alice" && m.Message == "tighten validation");
    }

    [Fact]
    public void LowerCaseMarker_AtStartOfCommentLine_Found()
    {
        Markers("// todo: lower-case marker").Should().ContainSingle(m => m.Type == "TODO");
    }

    [Fact]
    public void UpperCaseMarker_MidLine_Found()
    {
        Markers("// Known BUG in the tokenizer").Should().ContainSingle(m => m.Type == "BUG");
    }

    [Fact]
    public void TypeFilter_AppliedBeforeChoosingTheMatch()
    {
        // Previously the first marker (NOTE) was taken and then filtered out, losing the TODO.
        Markers("// NOTE: keep this; TODO: remove the fallback", "TODO")
            .Should().ContainSingle(m => m.Type == "TODO" && m.Message == "remove the fallback");
    }

    [Fact]
    public void MultiLineComment_OneMarkerPerLine()
    {
        var markers = Markers("/* TODO: first\n * FIXME: second */");

        markers.Should().HaveCount(2);
        markers[0].Should().Be(new TODOCommentAnalyzer.CommentMarker("TODO", "", "first", 0));
        markers[1].Should().Be(new TODOCommentAnalyzer.CommentMarker("FIXME", "", "second", 1));
    }

    [Fact]
    public void BareMarker_WithoutMessage_Found()
    {
        Markers("// HACK").Should().ContainSingle(m => m.Type == "HACK" && m.Message == "");
    }
}
