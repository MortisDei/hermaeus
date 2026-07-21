using Aether.Core.Models;
using Aether.ViewModels;
using Xunit;

namespace Aether.Tests;

/// <summary>
/// r18 03-model-catalog-and-memory-ui.md 3.3: memory-sourced entries used to render as one
/// always-visible pill per recalled memory, indistinguishable from RAG citations. Sources now
/// split by <see cref="ProvenanceKind"/> so the view can collapse memory pills behind a count.
/// </summary>
public sealed class MessageViewModelTests
{
    [Fact]
    public void Sources_split_into_citation_and_memory_by_provenance_kind()
    {
        var message = new MessageViewModel { Role = "assistant" };
        var ragSource = new SourceReference(ProvenanceKind.Rag, "doc.md");
        var memorySourceA = new SourceReference(ProvenanceKind.Memory, "User prefers concise summaries");
        var memorySourceB = new SourceReference(ProvenanceKind.Memory, "User is a backend engineer");

        message.Sources.Add(ragSource);
        message.Sources.Add(memorySourceA);
        message.Sources.Add(memorySourceB);

        Assert.Single(message.CitationSources);
        Assert.Same(ragSource, message.CitationSources[0]);
        Assert.Equal(2, message.MemorySources.Count);
        Assert.True(message.HasMemorySources);
        Assert.Equal("Memories used: 2", message.MemorySourceSummary);
    }

    [Fact]
    public void HasMemorySources_is_false_when_only_citations_are_present()
    {
        var message = new MessageViewModel { Role = "assistant" };
        message.Sources.Add(new SourceReference(ProvenanceKind.Rag, "doc.md"));

        Assert.False(message.HasMemorySources);
        Assert.Equal("Memories used: 0", message.MemorySourceSummary);
    }

    [Fact]
    public void IsMemorySourcesExpanded_defaults_to_collapsed()
    {
        var message = new MessageViewModel { Role = "assistant" };

        Assert.False(message.IsMemorySourcesExpanded);
    }
}
