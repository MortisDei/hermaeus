using Hermaeus.Agent.Services;
using Hermaeus.Core.Services;
using Hermaeus.Rag;
using Hermaeus.Rag.Eval;
using Hermaeus.Rag.Pipeline;
using Hermaeus.Rag.Retrieval;
using Hermaeus.Rag.Storage;
using Hermaeus.Services;
using Hermaeus.Services.Recall;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>
/// r12 03-runtime-vm-correctness.md 3.1 (finishing/skipping the wizard used
/// to leave the app on a dead chat panel: no servers auto-started, no models
/// listed, no RAG/agent/benchmark data loaded until a restart or a lucky
/// panel navigation) and 3.2 (one failing startup step used to silently
/// abort every step after it).
/// </summary>
public sealed class MainWindowViewModelStartupTests
{
    private sealed record Harness(MainWindowViewModel Main, ScriptedModelsLlm Llm, IRuntimeLogService Logs, FakeToasts Toasts, IConversationStore ConvStore);

    private static async Task<Harness> NewHarnessAsync(TempDir temp, bool initializeRagStore)
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var toasts = new FakeToasts();
        var logs = new RuntimeLogService(settings);
        var llm = new ScriptedModelsLlm(() => [new() { Id = "a", Name = "a", Provider = "Test" }]);

        var convStore = new ConversationStore(settings);
        await convStore.InitializeAsync();
        var memoryStore = new MemoryStore(settings);
        await memoryStore.InitializeAsync();

        var chat = new ChatViewModel(
            llm, convStore, memoryStore, settings, new FakeTts(), new ModelProfileService(settings),
            toasts, new FakeConversationMemoryService(), logs, new ConversationExportService());

        var agentStore = new FileAgentTaskStateStore(settings);
        await agentStore.InitializeAsync();
        var workspaceMemory = new WorkspaceMemoryStore(new MemoryStore(settings), settings);
        await workspaceMemory.InitializeAsync();
        var workspaceTools = new AgentWorkspaceTools();
        var ragStore = new SqliteRagStore(settings);
        if (initializeRagStore)
            await ragStore.InitializeAsync();
        var ragQuery = new RagQueryService(ragStore, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());
        var agentService = new AgentService(agentStore, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(workspaceTools), new FakeAgentLlm());
        var profiles = new FileWorkspaceProfileStore(settings);
        var analysis = new WorkspaceAnalysisService(profiles, workspaceMemory);
        var manifests = new WorkspaceManifestService();
        var activation = new WorkspaceActivationService(manifests, profiles);
        var agent = new AgentViewModel(agentService, agentStore, workspaceMemory, workspaceTools, llm, ragQuery, logs, analysis, activation, manifests, settings);

        var secrets = new FakeSecretStore();
        var tts = NewTtsSettingsViewModel(settings);
        var settingsVm = NewSettingsViewModel(settings, secrets, tts);

        var servicesVm = new ServicesViewModel(settings, new RuntimeProfileService(settings), toasts, new RedactionService(), new TrustService(), logs, tts);
        var models = new ModelManagementViewModel(llm, new ModelProfileService(settings), toasts, settings, new FakeSystemInfo(), servicesVm,
            new ModelManifestStore(settings), new HuggingFaceClient(), new ModelDownloadService());

        var ragPipeline = new RagPipeline(ragStore, new FakeEmbeddingService());
        var ragEval = new RagEvalService(ragQuery, settings, new FakeEvalStore());
        var rag = new RagViewModel(ragQuery, ragPipeline, ragEval, toasts, logs, settings, servicesVm);

        var benchmarks = new BenchmarkViewModel(new BenchmarkService(settings, llm, new FakeSystemInfo(), new FakeEvalStore()), llm, new ModelProfileService(settings), settings, toasts);

        var traces = new SqliteTraceStore(settings);
        var privacyAudit = new PrivacyAuditService(settings, secrets, logs, new FakeVoiceProviderRegistry(settings), traces);
        var systemOverview = new SystemOverviewViewModel(new FakeSystemInfo(), toasts, privacyAudit);

        var doctor = new DoctorViewModel(new FakeDoctorService(), toasts, settings);
        var memories = new MemoriesViewModel(memoryStore, convStore, settings, toasts);
        var logsVm = new LogsViewModel(logs, new RedactionService());
        var wizard = new SetupWizardViewModel(settings, new RuntimeProfileService(settings), new FakeVoiceProviderRegistry(settings), new FakeDoctorService(), toasts, new FakeSystemInfo());
        var projects = new ProjectViewModel(new ProjectStore(settings), settings, toasts, memoryStore, convStore, agentStore, ragStore);

        var recallIndex = new RecallIndexStore(settings, new FakeEmbeddingService());
        var recallIndexing = new RecallIndexingService(recallIndex, settings);
        var recallService = new RecallService(
        [
            new ConversationRecallSource(recallIndex),
            new TaskRecallSource(recallIndex),
            new MemoryRecallSource(memoryStore),
            new DocumentRecallSource(ragStore, new FakeEmbeddingService())
        ], new FakeEmbeddingService());
        var commandRegistry = new CommandRegistry();
        var palette = new PaletteViewModel(commandRegistry, recallService);

        var main = new MainWindowViewModel(
            convStore, chat, agent, settingsVm, models, rag, servicesVm, benchmarks, systemOverview, doctor, memories, logsVm, wizard, projects,
            commandRegistry, palette, settings, toasts, logs, new ConversationExportService(), recallIndexing);

        return new Harness(main, llm, logs, toasts, convStore);
    }

    /// <summary>doc 04 4.1 guard: the registry is the app's public self-description
    /// and cannot ship half-filled - every registered command needs a non-empty
    /// Title/Area/Description and a unique Id.</summary>
    [Fact]
    public async Task Command_registry_has_no_duplicate_or_incomplete_commands()
    {
        using var temp = new TempDir();
        var harness = await NewHarnessAsync(temp, initializeRagStore: true);

        var all = harness.Main.Commands.All;
        Assert.NotEmpty(all);
        foreach (var command in all)
        {
            Assert.False(string.IsNullOrWhiteSpace(command.Id), "every command needs an Id");
            Assert.False(string.IsNullOrWhiteSpace(command.Title), $"'{command.Id}' needs a Title");
            Assert.False(string.IsNullOrWhiteSpace(command.Area), $"'{command.Id}' needs an Area");
            Assert.False(string.IsNullOrWhiteSpace(command.Description), $"'{command.Id}' needs a Description");
        }

        var duplicateIds = all.GroupBy(c => c.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(duplicateIds);
    }

    [Fact]
    public async Task InitializeAsync_stops_at_the_wizard_when_setup_is_not_complete()
    {
        using var temp = new TempDir();
        var harness = await NewHarnessAsync(temp, initializeRagStore: true);

        await harness.Main.InitializeAsync();

        Assert.Equal("wizard", harness.Main.ActivePanel);
        Assert.Empty(harness.Main.Chat.AvailableModels);
    }

    [Fact]
    public async Task Finishing_the_wizard_completes_post_setup_initialization_exactly_once()
    {
        using var temp = new TempDir();
        var harness = await NewHarnessAsync(temp, initializeRagStore: true);
        await harness.Main.InitializeAsync();
        Assert.Equal("wizard", harness.Main.ActivePanel);

        // Mirrors a real Finish/Skip click: the wizard marks setup complete
        // and raises WizardCompleted, which is what MainWindowViewModel reacts to.
        await harness.Main.Wizard.SkipCommand.ExecuteAsync(null);
        await WaitForAsync(() => harness.Main.Chat.AvailableModels.Count > 0);

        Assert.Equal("chat", harness.Main.ActivePanel);
        Assert.Single(harness.Main.Chat.AvailableModels);
        Assert.True(harness.Llm.GetModelsCallCount > 0, "chat models must load once the wizard finishes on a first run, not stay empty until a restart");
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
            await Task.Delay(10);
    }

    /// <summary>r16 03-workbench-and-desktop.md 3.3: every other destructive action of this weight is confirm-gated; a raw context-menu click was the one exception.</summary>
    [Fact]
    public async Task DeleteConversation_does_nothing_when_confirmation_returns_false()
    {
        using var temp = new TempDir();
        var harness = await NewHarnessAsync(temp, initializeRagStore: true);
        await harness.ConvStore.SaveAsync(new Hermaeus.Core.Models.Conversation { Id = "conv-keep", Title = "Keep me" });
        var item = new ConversationItemViewModel { Id = "conv-keep", Title = "Keep me", ModelId = "m", UpdatedAt = DateTime.UtcNow, Folder = string.Empty };
        harness.Main.Conversations.Add(item);
        harness.Main.RequestDeleteConversationConfirmation = _ => Task.FromResult(false);

        await harness.Main.DeleteConversationCommand.ExecuteAsync(item);

        Assert.Contains(harness.Main.Conversations, c => c.Id == "conv-keep");
        Assert.NotNull(await harness.ConvStore.GetByIdAsync("conv-keep"));
    }

    [Fact]
    public async Task DeleteConversation_deletes_when_confirmation_returns_true()
    {
        using var temp = new TempDir();
        var harness = await NewHarnessAsync(temp, initializeRagStore: true);
        await harness.ConvStore.SaveAsync(new Hermaeus.Core.Models.Conversation { Id = "conv-remove", Title = "Remove me" });
        var item = new ConversationItemViewModel { Id = "conv-remove", Title = "Remove me", ModelId = "m", UpdatedAt = DateTime.UtcNow, Folder = string.Empty };
        harness.Main.Conversations.Add(item);
        harness.Main.RequestDeleteConversationConfirmation = _ => Task.FromResult(true);

        await harness.Main.DeleteConversationCommand.ExecuteAsync(item);

        Assert.DoesNotContain(harness.Main.Conversations, c => c.Id == "conv-remove");
        Assert.Null(await harness.ConvStore.GetByIdAsync("conv-remove"));
    }

    /// <summary>
    /// r18 01-finish-the-open-work.md 1.1: every keystroke in the title/folder/tags fields of
    /// the details flyout used to save immediately and reload the whole Conversations list,
    /// swapping out every ConversationItemViewModel instance (including the one backing the
    /// open flyout) mid-edit. Editing must now debounce to a single save after a pause, and the
    /// same instance must remain in the list throughout.
    /// </summary>
    [Fact]
    public async Task Editing_a_conversation_title_debounces_the_save_and_updates_in_place()
    {
        using var temp = new TempDir();
        var harness = await NewHarnessAsync(temp, initializeRagStore: true);
        await harness.ConvStore.SaveAsync(new Hermaeus.Core.Models.Conversation { Id = "conv-1", Title = "Original" });

        // Toggling ShowArchivedConversations reloads the list through the real production path
        // (OnShowArchivedConversationsChanged), populating Conversations with a real item whose
        // MetadataChanged is wired up exactly like the details flyout would drive it.
        harness.Main.ShowArchivedConversations = true;
        await WaitForAsync(() => harness.Main.Conversations.Count > 0);
        var item = Assert.Single(harness.Main.Conversations);

        item.Title = "F";
        item.Title = "Fi";
        item.Title = "First keystroke test";

        // Well before the debounce window elapses, the store must be untouched and the same
        // instance must still back the list entry.
        await Task.Delay(150);
        var midEdit = await harness.ConvStore.GetByIdAsync("conv-1");
        Assert.Equal("Original", midEdit!.Title);
        Assert.Same(item, Assert.Single(harness.Main.Conversations));

        Hermaeus.Core.Models.Conversation? saved = null;
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            saved = await harness.ConvStore.GetByIdAsync("conv-1");
            if (saved?.Title == "First keystroke test") break;
            await Task.Delay(50);
        }

        Assert.Equal("First keystroke test", saved?.Title);
        Assert.Same(item, Assert.Single(harness.Main.Conversations));
    }
}
