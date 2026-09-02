using System.Text.Json;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class MemoriesViewModelTests
{
    [Fact]
    public void Memory_item_exposes_live_pinned_state_and_action_label()
    {
        var item = new MemoryItemViewModel
        {
            Id = "memory-1", Category = "facts", Content = "Pinned fact",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, ImportanceScore = 0.8
        };

        Assert.Equal("Not pinned", item.PinStateLabel);
        Assert.Equal("Pin", item.PinButtonLabel);

        item.IsPinned = true;

        Assert.Equal("Pinned", item.PinStateLabel);
        Assert.Equal("Unpin", item.PinButtonLabel);
    }

    private static (MemoriesViewModel vm, ConversationStore conversations, MemoryStore memories, ISettingsService settings) NewViewModel(TempDir temp)
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var conversations = new ConversationStore(settings);
        var memories = new MemoryStore(settings);
        var vm = new MemoriesViewModel(memories, conversations, settings, new FakeToasts());
        return (vm, conversations, memories, settings);
    }

    [Fact]
    public async Task RefreshConversationFilters_loads_one_entry_per_conversation_with_counts()
    {
        using var temp = new TempDir();
        var (vm, conversations, memories, _) = NewViewModel(temp);
        await conversations.InitializeAsync();
        await memories.InitializeAsync();

        await conversations.SaveAsync(new Conversation { Id = "conv-A", Title = "Alpha" });
        await conversations.SaveAsync(new Conversation { Id = "conv-B", Title = "Beta" });
        await memories.SaveAsync(new Memory { Id = "m1", Content = "one", SourceConversationId = "conv-A" });
        await memories.SaveAsync(new Memory { Id = "m2", Content = "two", SourceConversationId = "conv-A" });
        await memories.SaveAsync(new Memory { Id = "m3", Content = "three", SourceConversationId = "conv-B" });

        await vm.RefreshConversationFiltersAsync();

        Assert.Equal(2, vm.ConversationFilters.Count);
        Assert.Equal(2, vm.ConversationFilters.Single(f => f.ConversationId == "conv-A").MemoryCount);
        Assert.Equal(1, vm.ConversationFilters.Single(f => f.ConversationId == "conv-B").MemoryCount);
    }

    [Fact]
    public async Task Selecting_a_conversation_filters_the_memory_list()
    {
        using var temp = new TempDir();
        var (vm, conversations, memories, _) = NewViewModel(temp);
        await conversations.InitializeAsync();
        await memories.InitializeAsync();

        await conversations.SaveAsync(new Conversation { Id = "conv-A", Title = "Alpha" });
        await conversations.SaveAsync(new Conversation { Id = "conv-B", Title = "Beta" });
        await memories.SaveAsync(new Memory { Id = "m1", Content = "one", SourceConversationId = "conv-A" });
        await memories.SaveAsync(new Memory { Id = "m2", Content = "two", SourceConversationId = "conv-B" });

        await vm.InitializeAsync();
        Assert.Equal(2, vm.Memories.Count);

        vm.SelectedConversationFilter = vm.ConversationFilters.Single(f => f.ConversationId == "conv-A");
        await vm.SearchAsync();

        Assert.Single(vm.Memories);
        Assert.Equal("one", vm.Memories[0].Content);
    }

    [Fact]
    public async Task Workspace_memories_stay_out_of_normal_memories_and_pinned_rows_are_grouped_first()
    {
        using var temp = new TempDir();
        var (vm, conversations, memories, _) = NewViewModel(temp);
        await conversations.InitializeAsync();
        await memories.InitializeAsync();

        await memories.SaveAsync(new Memory { Id = "global-pinned", Content = "Pinned fact", IsPinned = true });
        await memories.SaveAsync(new Memory { Id = "global-other", Content = "Other fact" });
        await memories.SaveAsync(new Memory
        {
            Id = "workspace-profile",
            Scope = MemoryScope.Workspace,
            ScopeId = temp.PathFor("workspace"),
            Title = "Workspace profile",
            Content = "RAG ingest plan should remain workspace-scoped.",
            Category = "workspace",
            Tags = ["workspace", "profile", "auto"]
        });
        Assert.Equal(MemoryScope.Workspace, (await memories.GetByIdAsync("workspace-profile"))!.Scope);

        await vm.InitializeAsync();

        Assert.Equal(["global-pinned", "global-other"], vm.Memories.Select(item => item.Id));
        Assert.Equal(["global-pinned"], vm.PinnedMemories.Select(item => item.Id));
        Assert.Equal(["global-other"], vm.OtherMemories.Select(item => item.Id));
    }

    [Fact]
    public async Task ExportConversationCsv_requires_a_selected_conversation_and_writes_a_file()
    {
        using var temp = new TempDir();
        var (vm, conversations, memories, settings) = NewViewModel(temp);
        await conversations.InitializeAsync();
        await memories.InitializeAsync();

        await conversations.SaveAsync(new Conversation { Id = "conv-A", Title = "Alpha" });
        await memories.SaveAsync(new Memory { Id = "m1", Content = "alpha content", SourceConversationId = "conv-A" });

        Assert.False(vm.ExportConversationCsvCommand.CanExecute(null));

        await vm.InitializeAsync();
        vm.SelectedConversationFilter = vm.ConversationFilters.Single(f => f.ConversationId == "conv-A");
        await vm.SearchAsync();

        Assert.True(vm.ExportConversationCsvCommand.CanExecute(null));
        await vm.ExportConversationCsvAsync();

        var exportDir = Path.Combine(SettingsService.ResolveDataRoot(settings.Settings), "exports");
        Assert.True(Directory.Exists(exportDir));
        var file = Directory.GetFiles(exportDir, "memories-conv-A-*.csv").Single();
        Assert.Contains("alpha content", File.ReadAllText(file));
    }

    [Fact]
    public async Task ExportMemoryHistoryJson_writes_versioned_redacted_lineage()
    {
        using var temp = new TempDir();
        var (vm, conversations, memories, settings) = NewViewModel(temp);
        await conversations.InitializeAsync();
        await memories.InitializeAsync();
        var first = await memories.CreateAssertionAsync(new KnowledgeRevisionDraft(
            new Memory { Id = "memory-versioned", Content = "api_key=secret-value" },
            SourceReferences: [new SourceReference(ProvenanceKind.Memory, "Memory source")],
            Decision: new KnowledgeRevisionDecision("create", "owner", "accepted", DateTime.UtcNow)));
        await memories.ReviseAssertionAsync("memory-versioned", first.RevisionId,
            new KnowledgeRevisionDraft(new Memory { Id = "memory-versioned", Content = "updated fact" }));

        await vm.InitializeAsync();
        await vm.ExportMemoryHistoryJsonAsync();

        var exportDir = Path.Combine(SettingsService.ResolveDataRoot(settings.Settings), "exports");
        var file = Directory.GetFiles(exportDir, "memories-history-*.json").Single();
        var json = File.ReadAllText(file);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(1, document.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(1, document.RootElement.GetProperty("assertions").GetArrayLength());
        Assert.Contains("updated fact", json);
        Assert.Contains("[redacted]", json);
        Assert.DoesNotContain("secret-value", json);
        Assert.Contains(first.RevisionId, json);
    }

    [Fact]
    public async Task InspectMemory_exposes_a_bounded_diff_for_adjacent_revisions()
    {
        using var temp = new TempDir();
        var (vm, conversations, memories, _) = NewViewModel(temp);
        await conversations.InitializeAsync();
        await memories.InitializeAsync();
        var first = await memories.CreateAssertionAsync(new KnowledgeRevisionDraft(
            new Memory { Id = "memory-diff", Content = "old content" }));
        await memories.ReviseAssertionAsync("memory-diff", first.RevisionId,
            new KnowledgeRevisionDraft(new Memory { Id = "memory-diff", Content = "new content" }));

        await vm.InitializeAsync();
        await vm.InspectMemoryAsync("memory-diff");

        Assert.Equal(2, vm.RevisionTimeline.Count);
        Assert.Contains("- old content + new content", vm.RevisionTimeline[0].DiffDisplay);
        Assert.Equal("Diff: initial revision", vm.RevisionTimeline[1].DiffDisplay);
    }

    [Fact]
    public async Task Contradiction_review_records_and_rejects_without_mutating_memories()
    {
        using var temp = new TempDir();
        var (vm, conversations, memories, _) = NewViewModel(temp);
        await conversations.InitializeAsync();
        await memories.InitializeAsync();
        await memories.CreateAssertionAsync(new KnowledgeRevisionDraft(
            new Memory { Id = "review-left", Content = "left fact" }));
        await memories.CreateAssertionAsync(new KnowledgeRevisionDraft(
            new Memory { Id = "review-right", Content = "right fact" }));

        await vm.InitializeAsync();
        await vm.InspectMemoryAsync("review-left");
        vm.ContradictionTarget = vm.Memories.Single(item => item.Id == "review-right");
        vm.ContradictionExplanation = "These facts need an owner review.";
        await vm.ProposeContradictionAsync();

        var proposal = Assert.Single(vm.ContradictionProposals);
        Assert.Contains("review-left", proposal.LeftRevision);
        Assert.Contains("review-right", proposal.RightRevision);
        await vm.RejectContradictionProposalAsync(proposal.ProposalId);

        Assert.Empty(vm.ContradictionProposals);
        Assert.Equal("left fact", (await memories.GetByIdAsync("review-left"))!.Content);
        Assert.Equal("right fact", (await memories.GetByIdAsync("review-right"))!.Content);
    }
}
