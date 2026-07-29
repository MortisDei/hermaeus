using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Rag.Models;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>doc 01 1.6: a new conversation inherits the active project's default
/// model/prompt/dataset and project id. No project active behaves exactly as
/// 0.30.0 did (this file's baseline test), and switching afterward never
/// rewrites a conversation already created.</summary>
public sealed class ChatProjectDefaultsTests
{
    private static (ChatViewModel vm, ThrowingSaveConversationStore store) NewViewModel(TempDir temp)
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new ThrowingSaveConversationStore();
        var memoryStore = new MemoryStore(settings);
        memoryStore.InitializeAsync().GetAwaiter().GetResult();
        var vm = new ChatViewModel(
            new FakeLlm(), store, memoryStore, settings, new FakeTts(), new ModelProfileService(settings),
            new FakeToasts(), new FakeConversationMemoryService(), new RuntimeLogService(settings), new ConversationExportService());
        return (vm, store);
    }

    [Fact]
    public void NewConversation_with_no_active_project_behaves_exactly_as_before()
    {
        using var temp = new TempDir();
        var (vm, _) = NewViewModel(temp);
        vm.ActiveProjectProvider = () => null;

        vm.NewConversation();

        Assert.Equal(string.Empty, vm.RagDatasetId);
        Assert.Equal(string.Empty, vm.CurrentConversationId);
    }

    [Fact]
    public void NewConversation_inherits_the_active_projects_defaults()
    {
        using var temp = new TempDir();
        var (vm, _) = NewViewModel(temp);
        var project = new Project
        {
            Id = "p1",
            DefaultSystemPrompt = "Act as a terse reviewer.",
            DatasetId = "ds1"
        };
        vm.ActiveProjectProvider = () => project;

        vm.NewConversation();

        Assert.Equal("Act as a terse reviewer.", vm.SystemPrompt);
        Assert.Equal("ds1", vm.RagDatasetId);
    }

    [Fact]
    public async Task Switching_the_active_project_never_touches_a_conversation_already_loaded()
    {
        using var temp = new TempDir();
        var (vm, store) = NewViewModel(temp);
        var projectA = new Project { Id = "pA" };
        vm.ActiveProjectProvider = () => projectA;
        vm.NewConversation();

        vm.InputText = string.Empty;
        await vm.AttachRagDatasetAndPersistAsync(new RagDataset { Id = "ignored" });
        var savedId = vm.CurrentConversationId;
        var saved = await store.GetByIdAsync(savedId);
        Assert.Equal("pA", saved!.ProjectId);

        // The active project switches to something else entirely.
        vm.ActiveProjectProvider = () => new Project { Id = "pB" };

        // Loading the existing conversation must not pick up the new active project.
        await vm.LoadConversationAsync(savedId);
        await vm.AttachRagDatasetAndPersistAsync(new RagDataset { Id = "ignored2" });
        var reloaded = await store.GetByIdAsync(savedId);
        Assert.Equal("pA", reloaded!.ProjectId);
    }
}
