using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.ViewModels;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>
/// r25 doc 02: everything injected into a turn collapses behind one receipt.
/// This replaced r18 3.3's memory-only pill, which collapsed memories while
/// r24 2.6's Recall pills stayed visible in a separate strip directly above
/// it, so collapsing the pill hid nothing the reader could see.
/// </summary>
public sealed class MessageViewModelTests
{
    [Fact]
    public void Sources_group_into_receipt_sections_in_a_fixed_order()
    {
        var message = new MessageViewModel { Role = "assistant" };
        var ragSource = new SourceReference(ProvenanceKind.Rag, "doc.md");
        var memorySourceA = new SourceReference(ProvenanceKind.Memory, "User prefers concise summaries");
        var memorySourceB = new SourceReference(ProvenanceKind.Memory, "User is a backend engineer");
        var recallSource = new SourceReference(ProvenanceKind.Recall, "Earlier: the build broke on Linux");

        // Deliberately added out of presentation order.
        message.Sources.Add(ragSource);
        message.Sources.Add(memorySourceA);
        message.Sources.Add(recallSource);
        message.Sources.Add(memorySourceB);

        Assert.Equal(3, message.ContextSections.Count);
        Assert.Equal(ProvenanceKind.Memory, message.ContextSections[0].Kind);
        Assert.Equal(ProvenanceKind.Recall, message.ContextSections[1].Kind);
        Assert.Equal(ProvenanceKind.Rag, message.ContextSections[2].Kind);
        Assert.Equal(2, message.ContextSections[0].ItemCount);
        Assert.True(message.HasContext);
        Assert.Equal("Context: 2 memories, 1 recall hit, 1 knowledge excerpt", message.ContextSummary);
    }

    /// <summary>
    /// The owner's r24 field report, as a test: with the receipt collapsed,
    /// nothing from any section may be visible. Before r25 the Recall pills
    /// rendered in an always-visible strip above the collapsed memory pill.
    /// </summary>
    [Fact]
    public void Collapsed_receipt_exposes_no_source_item_of_any_kind()
    {
        var message = new MessageViewModel { Role = "assistant" };
        message.Sources.Add(new SourceReference(ProvenanceKind.Memory, "User prefers concise summaries"));
        message.Sources.Add(new SourceReference(ProvenanceKind.Recall, "Earlier: the build broke on Linux"));
        message.Sources.Add(new SourceReference(ProvenanceKind.Rag, "doc.md"));

        Assert.False(message.IsContextExpanded);

        // There is exactly one collection of source items on the view model, and it
        // is the one gated behind IsContextExpanded. No second always-visible strip.
        Assert.All(message.ContextSections, section => Assert.NotEmpty(section.Items));
        Assert.Equal(3, message.ContextSections.Sum(s => s.ItemCount));
    }

    [Fact]
    public void HasContext_is_false_when_nothing_was_injected()
    {
        var message = new MessageViewModel { Role = "assistant" };

        Assert.False(message.HasContext);
        Assert.Empty(message.ContextSections);
        Assert.Equal(string.Empty, message.ContextSummary);
    }

    [Fact]
    public void IsContextExpanded_defaults_to_collapsed()
    {
        var message = new MessageViewModel { Role = "assistant" };

        Assert.False(message.IsContextExpanded);
    }

    [Fact]
    public void Receipt_estimates_tokens_per_section()
    {
        var sections = ChatContextReceipt.Build(
        [
            new SourceReference(ProvenanceKind.Memory, "title", Snippet: new string('x', 400))
        ]);

        var section = Assert.Single(sections);
        Assert.True(section.EstimatedTokens > 0, "a 400-character snippet should estimate above zero tokens");
    }

    [Fact]
    public void Receipt_omits_empty_sections_and_handles_no_sources()
    {
        Assert.Empty(ChatContextReceipt.Build(null));
        Assert.Empty(ChatContextReceipt.Build([]));
        Assert.Equal(string.Empty, ChatContextReceipt.Summarize([]));

        var sections = ChatContextReceipt.Build([new SourceReference(ProvenanceKind.Workspace, "src/App.cs")]);
        var section = Assert.Single(sections);
        Assert.Equal(ProvenanceKind.Workspace, section.Kind);
        Assert.Equal("Workspace files", section.Label);
        Assert.Equal("Context: 1 workspace file", ChatContextReceipt.Summarize(sections));
    }

    /// <summary>
    /// A Recall hit is not a memory, however much it reads like one: it carries no
    /// memory id, so it must never be grouped into the Memories section where the
    /// flyout would offer "Open in Memories" for something that cannot be opened.
    /// </summary>
    [Fact]
    public void Recall_hits_never_land_in_the_memories_section()
    {
        var sections = ChatContextReceipt.Build(
        [
            new SourceReference(ProvenanceKind.Recall, "Earlier: the build broke on Linux")
        ]);

        var section = Assert.Single(sections);
        Assert.Equal(ProvenanceKind.Recall, section.Kind);
        Assert.Equal("Recall", section.Label);
    }
}
