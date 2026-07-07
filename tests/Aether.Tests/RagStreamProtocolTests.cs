using Aether.Rag;
using Xunit;

namespace Aether.Tests;

public sealed class RagStreamProtocolTests
{
    [Fact]
    public void ParseSources_extracts_every_chunk_field()
    {
        var header = "__RAG_SOURCES__[{\"rank\":1,\"title\":\"Doc A\",\"file\":\"a.md\",\"path\":\"docs/a.md\",\"score\":0.87,\"content\":\"hello\"}]__END_SOURCES__";

        var chunks = RagStreamProtocol.ParseSources(header);

        var chunk = Assert.Single(chunks);
        Assert.Equal(1, chunk.Rank);
        Assert.Equal("Doc A", chunk.Title);
        Assert.Equal("a.md", chunk.File);
        Assert.Equal("docs/a.md", chunk.Path);
        Assert.Equal(0.87f, chunk.Score, precision: 2);
        Assert.Equal("hello", chunk.Content);
    }

    [Fact]
    public void ParseTrace_leaves_optional_fields_null_when_the_payload_omits_them()
    {
        var token = "__RAG_TRACE__{\"Id\":\"t1\",\"RetrievalLatencyMs\":12,\"TotalLatencyMs\":34,\"GroundingScore\":0.5}__END_TRACE__";

        var update = RagStreamProtocol.ParseTrace(token);

        Assert.Equal("t1", update.Id);
        Assert.Equal(12, update.RetrievalLatencyMs);
        Assert.Equal(34, update.TotalLatencyMs);
        Assert.Equal(0.5f, update.GroundingScore);
        Assert.Null(update.ExpandedQuery);
        Assert.Null(update.PlannerNotes);
        Assert.Null(update.Refused);
        Assert.Null(update.RefusalReason);
    }

    [Fact]
    public void ParseTrace_reads_optional_fields_when_present()
    {
        var token = "__RAG_TRACE__{\"Id\":\"t2\",\"RetrievalLatencyMs\":1,\"TotalLatencyMs\":2,\"GroundingScore\":0.9,"
            + "\"ExpandedQuery\":\"expanded\",\"PlannerNotes\":\"notes\",\"Refused\":true,\"RefusalReason\":\"no context\"}__END_TRACE__";

        var update = RagStreamProtocol.ParseTrace(token);

        Assert.Equal("expanded", update.ExpandedQuery);
        Assert.Equal("notes", update.PlannerNotes);
        Assert.True(update.Refused);
        Assert.Equal("no context", update.RefusalReason);
    }
}
