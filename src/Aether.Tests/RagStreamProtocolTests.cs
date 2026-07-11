using Aether.Rag;
using Aether.Rag.Models;
using Xunit;

namespace Aether.Tests;

/// <summary>
/// Covers <see cref="RagStreamEvent"/>/<see cref="RagTraceSummary"/>, the
/// typed replacement for the old "__RAG_SOURCES__"/"__RAG_TRACE__" sentinel
/// strings this file used to test the parser for
/// (docs/review/03-next-level-roadmap.md Phase 1).
/// </summary>
public sealed class RagStreamProtocolTests
{
    [Fact]
    public void ForSources_carries_every_chunk_field()
    {
        var chunk = new RagTraceChunk { Rank = 1, Title = "Doc A", File = "a.md", Path = "docs/a.md", Score = 0.87f, Content = "hello" };

        var evt = RagStreamEvent.ForSources([chunk]);

        Assert.Equal(RagStreamEventKind.Sources, evt.Kind);
        var single = Assert.Single(evt.Sources!);
        Assert.Equal(1, single.Rank);
        Assert.Equal("Doc A", single.Title);
        Assert.Equal("a.md", single.File);
        Assert.Equal("docs/a.md", single.Path);
        Assert.Equal(0.87f, single.Score, precision: 2);
        Assert.Equal("hello", single.Content);
    }

    [Fact]
    public void ForTrace_leaves_optional_fields_null_when_not_supplied()
    {
        var evt = RagStreamEvent.ForTrace(new RagTraceSummary("t1", 12, 34, 0.5f, "TokenOverlap"));

        Assert.Equal(RagStreamEventKind.Trace, evt.Kind);
        var trace = evt.Trace!;
        Assert.Equal("t1", trace.Id);
        Assert.Equal(12, trace.RetrievalLatencyMs);
        Assert.Equal(34, trace.TotalLatencyMs);
        Assert.Equal(0.5f, trace.GroundingScore);
        Assert.Null(trace.ExpandedQuery);
        Assert.Null(trace.PlannerNotes);
        Assert.Null(trace.Refused);
        Assert.Null(trace.RefusalReason);
    }

    [Fact]
    public void ForTrace_reads_optional_fields_when_present()
    {
        var evt = RagStreamEvent.ForTrace(new RagTraceSummary(
            "t2", 1, 2, 0.9f, "TokenOverlap",
            ExpandedQuery: "expanded",
            PlannerNotes: "notes",
            Refused: true,
            RefusalReason: "no context"));

        var trace = evt.Trace!;
        Assert.Equal("expanded", trace.ExpandedQuery);
        Assert.Equal("notes", trace.PlannerNotes);
        Assert.True(trace.Refused);
        Assert.Equal("no context", trace.RefusalReason);
    }

    [Fact]
    public void ForToken_carries_text_and_no_payload()
    {
        var evt = RagStreamEvent.ForToken("hello");

        Assert.Equal(RagStreamEventKind.Token, evt.Kind);
        Assert.Equal("hello", evt.Text);
        Assert.Null(evt.Sources);
        Assert.Null(evt.Trace);
    }
}
