using Hermaeus.Agent.Services;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Desktop;
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

    private static async Task<Harness> NewHarnessAsync(TempDir temp, bool initializeRagStore, IDoctorService? doctorService = null)
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
        var lab = new LabViewModel(new SqliteEmpiricalExperienceStore(settings, new RedactionService()), toasts);

        var traces = new SqliteTraceStore(settings);
        var privacyAudit = new PrivacyAuditService(settings, secrets, logs, new FakeVoiceProviderRegistry(settings), traces);
        var systemOverview = new SystemOverviewViewModel(new FakeSystemInfo(), toasts, privacyAudit);

        var doctor = new DoctorViewModel(doctorService ?? new FakeDoctorService(), toasts, settings);
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
        var activity = new ActivityViewModel(toasts, new SqliteTraceStore(settings));

        var main = new MainWindowViewModel(
            convStore, chat, agent, settingsVm, models, rag, servicesVm, benchmarks, lab, systemOverview, doctor, memories, logsVm, wizard, projects,
            commandRegistry, palette, activity, settings, toasts, logs, new ConversationExportService(), recallIndexing);

        return new Harness(main, llm, logs, toasts, convStore);
    }

    [Fact]
    public async Task Startup_loads_configured_data_root_before_composing_data_backed_stores()
    {
        using var temp = new TempDir();
        var settingsPath = temp.PathFor("settings.json");
        var configuredRoot = temp.PathFor("selected-data-root");
        var writer = new SettingsService(settingsPath);
        var candidate = writer.Settings.Clone();
        candidate.DataManagement.DataRootDirectory = configuredRoot;
        await writer.SaveAsync(candidate);

        var loaded = new SettingsService(settingsPath);
        App.LoadSettingsBeforeComposition(loaded);

        Assert.Equal(Path.GetFullPath(configuredRoot), SettingsService.ResolveDataRoot(loaded.Settings));

        var conversations = new ConversationStore(loaded);
        var rag = new SqliteRagStore(loaded);
        var agent = new FileAgentTaskStateStore(loaded);
        await conversations.InitializeAsync();
        await rag.InitializeAsync();
        await agent.InitializeAsync();

        Assert.True(File.Exists(Path.Combine(configuredRoot, "conversations.db")));
        Assert.True(File.Exists(Path.Combine(configuredRoot, "agent", "task_index.db")));
    }

    [Fact]
    public async Task Startup_settings_load_does_not_capture_the_UI_synchronization_context()
    {
        using var temp = new TempDir();
        var settingsPath = temp.PathFor("settings.json");
        var configuredRoot = temp.PathFor("selected-data-root");
        var writer = new SettingsService(settingsPath);
        var candidate = writer.Settings.Clone();
        candidate.DataManagement.DataRootDirectory = configuredRoot;
        await writer.SaveAsync(candidate);

        var loaded = new SettingsService(settingsPath);
        var loadTask = Task.Run(() =>
        {
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new BlockingSynchronizationContext());
            try
            {
                App.LoadSettingsBeforeComposition(loaded);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        });

        var completed = await Task.WhenAny(loadTask, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(loadTask, completed);
        await loadTask;
        Assert.Equal(Path.GetFullPath(configuredRoot), SettingsService.ResolveDataRoot(loaded.Settings));
    }

    [Fact]
    public async Task Data_storage_distinguishes_persisted_root_from_current_effective_root_until_restart()
    {
        using var temp = new TempDir();
        var service = NewSettings(temp);
        var oldRoot = temp.PathFor("old-data-root");
        var newRoot = temp.PathFor("new-data-root");
        service.Settings.DataManagement.DataRootDirectory = oldRoot;

        var viewModel = new DataManagementSettingsViewModel(
            service,
            new BackupService(service),
            new FakeToasts(),
            () => SettingsService.ResolveDataRoot(service.Settings));
        viewModel.ReloadFrom(service.Settings);

        var candidate = service.Settings.Clone();
        candidate.DataManagement.DataRootDirectory = newRoot;
        await service.SaveAsync(candidate);

        Assert.Contains($"Configured: {Path.GetFullPath(newRoot)}", viewModel.DataRootStateSummary, StringComparison.Ordinal);
        Assert.Contains($"Currently effective: {Path.GetFullPath(oldRoot)}", viewModel.DataRootStateSummary, StringComparison.Ordinal);
        Assert.Contains("Restart Hermaeus", viewModel.DataRootStateSummary, StringComparison.Ordinal);
    }

    private sealed class BlockingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) { }
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
    public async Task Incomplete_onboarding_can_visit_logs_and_resume_the_same_step()
    {
        using var temp = new TempDir();
        var harness = await NewHarnessAsync(temp, initializeRagStore: true);
        await harness.Main.InitializeAsync();
        harness.Main.Wizard.StepIndex = 4;

        harness.Main.ActivePanel = "logs";

        Assert.True(harness.Main.ShowSetupResume);
        Assert.Equal(4, harness.Main.Wizard.StepIndex);

        harness.Main.ShowWizardPanelCommand.Execute(null);

        Assert.Equal("wizard", harness.Main.ActivePanel);
        Assert.Equal(4, harness.Main.Wizard.StepIndex);
        Assert.False(harness.Main.ShowSetupResume);
    }

    [Fact]
    public async Task Doctor_download_survives_repeated_navigation_and_keeps_one_operation_state()
    {
        using var temp = new TempDir();
        var doctorService = new ControlledEmbeddingInstallDoctorService();
        var harness = await NewHarnessAsync(temp, initializeRagStore: true, doctorService);
        var operationOwner = harness.Main.Doctor;
        var check = new DoctorCheck(
            "embedding-model",
            "Embedding model",
            DoctorCheckStatus.Warning,
            "Missing",
            "Install it.",
            "Install",
            true,
            string.Empty,
            "RAG");

        harness.Main.ActivePanel = "doctor";
        var install = harness.Main.Doctor.RunFixCommand.ExecuteAsync(check);
        await doctorService.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        harness.Main.ActivePanel = "logs";
        harness.Main.ActivePanel = "doctor";
        harness.Main.ActivePanel = "logs";

        Assert.True(harness.Main.Doctor.IsInstallingEmbeddingModel);
        Assert.Same(operationOwner, harness.Main.Doctor);

        doctorService.Complete.TrySetResult();
        await install;

        Assert.False(harness.Main.Doctor.IsInstallingEmbeddingModel);
    }

    [Fact]
    public async Task Error_notifications_are_also_recorded_in_runtime_logs()
    {
        using var temp = new TempDir();
        var harness = await NewHarnessAsync(temp, initializeRagStore: true);

        harness.Toasts.Show("Data root not changed", "The current process owns hermaeus.lock.", ToastKind.Error);

        Assert.Contains(harness.Logs.GetEntries(), entry =>
            entry.Level == RuntimeLogLevel.Error
            && entry.Message.Contains("Data root not changed", StringComparison.Ordinal)
            && entry.Message.Contains("hermaeus.lock", StringComparison.Ordinal));
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
        await WaitForAsync(() => harness.Main.Chat.AvailableModels.Count > 0, "chat models loading at startup");

        Assert.Equal("chat", harness.Main.ActivePanel);
        Assert.Single(harness.Main.Chat.AvailableModels);
        Assert.Single(harness.Main.Agent.AvailableModels);
        Assert.Equal("a", harness.Main.Agent.SelectedModel?.Id);
        Assert.True(harness.Llm.GetModelsCallCount > 0, "chat models must load once the wizard finishes on a first run, not stay empty until a restart");
    }

    [Fact]
    public async Task Shutdown_waits_for_a_server_triggered_model_refresh_before_teardown()
    {
        using var temp = new TempDir();
        var harness = await NewHarnessAsync(temp, initializeRagStore: true);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Llm.DelayGate = gate;

        var server = harness.Main.Services.Servers.First(s => !s.EmbeddingsMode);
        server.Port += 100;
        await server.SaveConfigCommand.ExecuteAsync(null);
        await WaitForAsync(() => harness.Llm.GetModelsCallCount > 0, "server-triggered model refresh");

        var shutdown = harness.Main.ShutdownAsync();
        Assert.False(shutdown.IsCompleted, "shutdown must wait for the in-flight refresh before services are disposed");

        gate.TrySetResult();
        await shutdown;

        Assert.DoesNotContain(harness.Logs.GetEntries(), entry =>
            entry.Message.Contains("Model refresh failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Server_triggered_model_refresh_also_selects_an_agent_model_for_scenario_evals()
    {
        using var temp = new TempDir();
        var harness = await NewHarnessAsync(temp, initializeRagStore: true);
        var server = harness.Main.Services.Servers.First(s => !s.EmbeddingsMode);

        server.Port += 100;
        await server.SaveConfigCommand.ExecuteAsync(null);

        await WaitForAsync(() => harness.Main.Agent.AvailableModels.Count > 0, "agent models loading after server availability change");

        Assert.Equal("a", harness.Main.Agent.SelectedModel?.Id);
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
    public async Task Inline_delete_requires_the_visible_confirmation_state()
    {
        using var temp = new TempDir();
        var harness = await NewHarnessAsync(temp, initializeRagStore: true);
        await harness.ConvStore.SaveAsync(new Hermaeus.Core.Models.Conversation { Id = "conv-inline", Title = "Inline" });
        var item = new ConversationItemViewModel { Id = "conv-inline", Title = "Inline", ModelId = "m", UpdatedAt = DateTime.UtcNow, Folder = string.Empty };
        harness.Main.Conversations.Add(item);

        await harness.Main.DeleteConversationAfterInlineConfirmationAsync(item);
        Assert.NotNull(await harness.ConvStore.GetByIdAsync("conv-inline"));

        item.IsDeleteConfirmationVisible = true;
        await harness.Main.DeleteConversationAfterInlineConfirmationAsync(item);
        Assert.Null(await harness.ConvStore.GetByIdAsync("conv-inline"));
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

    [Fact]
    public async Task Delete_active_conversation_requests_chat_input_focus()
    {
        using var temp = new TempDir();
        var harness = await NewHarnessAsync(temp, initializeRagStore: true);
        await harness.ConvStore.SaveAsync(new Hermaeus.Core.Models.Conversation { Id = "conv-active", Title = "Active" });
        await harness.Main.Chat.LoadConversationAsync("conv-active");
        var item = new ConversationItemViewModel { Id = "conv-active", Title = "Active", ModelId = "m", UpdatedAt = DateTime.UtcNow, Folder = string.Empty };
        harness.Main.Conversations.Add(item);
        harness.Main.RequestDeleteConversationConfirmation = _ => Task.FromResult(true);
        var focusRequests = 0;
        harness.Main.Chat.RequestInputFocus = () => focusRequests++;

        await harness.Main.DeleteConversationCommand.ExecuteAsync(item);

        Assert.Equal(string.Empty, harness.Main.Chat.CurrentConversationId);
        Assert.Equal(1, focusRequests);
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
        await WaitForAsync(() => harness.Main.Conversations.Count > 0, "the conversation list loading");
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

    private sealed class ControlledEmbeddingInstallDoctorService : IDoctorService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Complete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<DoctorReport> ScanAsync(CancellationToken ct = default) =>
            Task.FromResult(new DoctorReport([], DateTime.UtcNow, "ok"));

        public Task<bool> InstallRerankerAssetsAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> InstallRerankerAssetsAsync(IProgress<string> progress, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> InstallEmbeddingModelAsync(CancellationToken ct = default) => InstallEmbeddingModelAsync(new Progress<string>(), ct);

        public async Task<bool> InstallEmbeddingModelAsync(IProgress<string> progress, CancellationToken ct = default)
        {
            Started.TrySetResult();
            progress.Report("Downloading embedding model... 42%");
            await Complete.Task.WaitAsync(ct);
            return true;
        }

        public Task<bool> InstallLlamaServerUpdateAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> InstallLlamaServerUpdateAsync(IProgress<string> progress, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> InstallNativeKokoroAssetsAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> InstallNativeKokoroAssetsAsync(IProgress<string> progress, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> InstallSpeechRecognitionAssetsAsync(IProgress<string> progress, CancellationToken ct = default) => Task.FromResult(true);
    }
}
