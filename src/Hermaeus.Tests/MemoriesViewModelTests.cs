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
}
