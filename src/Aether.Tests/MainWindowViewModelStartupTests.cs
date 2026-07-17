using Aether.Agent.Services;
using Aether.Core.Services;
using Aether.Rag;
using Aether.Rag.Eval;
using Aether.Rag.Pipeline;
using Aether.Rag.Retrieval;
using Aether.Rag.Storage;
using Aether.Services;
using Aether.ViewModels;
using Xunit;
using static Aether.Tests.Helpers;

namespace Aether.Tests;

/// <summary>
/// r12 03-runtime-vm-correctness.md 3.1 (finishing/skipping the wizard used
/// to leave the app on a dead chat panel: no servers auto-started, no models
/// listed, no RAG/agent/benchmark data loaded until a restart or a lucky
/// panel navigation) and 3.2 (one failing startup step used to silently
/// abort every step after it).
/// </summary>
public sealed class MainWindowViewModelStartupTests
{
    private sealed record Harness(MainWindowViewModel Main, ScriptedModelsLlm Llm, IRuntimeLogService Logs, FakeToasts Toasts);

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
        var settingsVm = NewSettingsViewModel(settings, secrets);

        var models = new ModelManagementViewModel(llm, new ModelProfileService(settings), toasts, settings);

        var ragPipeline = new RagPipeline(ragStore, new FakeEmbeddingService());
        var ragEval = new RagEvalService(ragQuery, settings, new FakeEvalStore());
        var servicesVm = new ServicesViewModel(settings, new RuntimeProfileService(settings), toasts, new RedactionService(), new TrustService(), logs);
        var rag = new RagViewModel(ragQuery, ragPipeline, ragEval, toasts, logs, settings, servicesVm);

        var benchmarks = new BenchmarkViewModel(new BenchmarkService(settings, llm, new FakeSystemInfo(), new FakeEvalStore()), llm, new ModelProfileService(settings), settings, toasts);

        var traces = new SqliteTraceStore(settings);
        var privacyAudit = new PrivacyAuditService(settings, secrets, logs, new FakeVoiceProviderRegistry(settings), traces);
        var systemOverview = new SystemOverviewViewModel(new FakeSystemInfo(), toasts, privacyAudit);

        var doctor = new DoctorViewModel(new FakeDoctorService(), toasts, settings);
        var memories = new MemoriesViewModel(memoryStore, convStore, settings, toasts);
        var logsVm = new LogsViewModel(logs, new RedactionService());
        var wizard = new SetupWizardViewModel(settings, new RuntimeProfileService(settings), new FakeVoiceProviderRegistry(settings), new FakeDoctorService(), toasts, new FakeSystemInfo());

        var main = new MainWindowViewModel(
            convStore, chat, agent, settingsVm, models, rag, servicesVm, benchmarks, systemOverview, doctor, memories, logsVm, wizard,
            settings, toasts, logs, new ConversationExportService());

        return new Harness(main, llm, logs, toasts);
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
}
