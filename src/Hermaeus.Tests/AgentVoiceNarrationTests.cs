using Hermaeus.Agent.Services;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Rag;
using Hermaeus.Rag.Retrieval;
using Hermaeus.Rag.Storage;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class AgentVoiceNarrationTests
{
    private static async Task<(AgentViewModel Vm, FakeVoiceOrchestrator Voice, string Workspace)> BuildAsync(TempDir temp)
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");

        var store = new FileAgentTaskStateStore(settings);
        await store.InitializeAsync();
        var memoryStore = new WorkspaceMemoryStore(new MemoryStore(settings), settings);
        await memoryStore.InitializeAsync();
        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);

        var tools = new AgentWorkspaceTools();
        var agentService = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), new FakeAgentLlm());
        var logs = new RuntimeLogService(settings);
        var profiles = new FileWorkspaceProfileStore(settings);
        var analysis = new WorkspaceAnalysisService(profiles, memoryStore);
        var manifests = new WorkspaceManifestService();
        var activation = new WorkspaceActivationService(manifests, profiles);
        var ragStore = new SqliteRagStore(settings);
        await ragStore.InitializeAsync();
        var rag = new RagQueryService(ragStore, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());
        var voice = new FakeVoiceOrchestrator();

        var vm = new AgentViewModel(agentService, store, memoryStore, tools, new FakeLlm(), rag, logs, analysis, activation, manifests, settings, lessons: null, voice: voice)
        {
            WorkspaceRoot = workspace,
            SelectedModel = new LlmModel { Id = "fake-agent", Name = "Fake Agent", Provider = "Test" },
            GoalText = "Investigate the workspace"
        };

        return (vm, voice, workspace);
    }

    [Fact]
    public async Task StartAsync_narrates_task_started_and_waiting_for_approval_at_critical_priority()
    {
        using var temp = new TempDir();
        var (vm, voice, _) = await BuildAsync(temp);

        // FakeAgentLlm always requests draft_patch with requires_approval: true,
        // so the very first step should land the task in WaitingForUser with a pending tool action.
        await vm.StartCommand.ExecuteAsync(null);

        Assert.Contains(voice.Enqueued, u => u.Channel == VoiceChannel.Agent && u.Text.Contains("started"));
        Assert.Contains(voice.Enqueued, u =>
            u.Channel == VoiceChannel.Agent && u.Priority == VoicePriority.Critical && u.Text.Contains("approval"));
    }

    [Fact]
    public async Task StartAsync_without_a_voice_orchestrator_does_not_throw()
    {
        using var temp = new TempDir();
        var (vm, _, _) = await BuildAsync(temp);
        // Rebuild without a voice orchestrator to confirm the narration hooks are optional.
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data-2");
        var store = new FileAgentTaskStateStore(settings);
        await store.InitializeAsync();
        var memoryStore = new WorkspaceMemoryStore(new MemoryStore(settings), settings);
        await memoryStore.InitializeAsync();
        var workspace = temp.PathFor("workspace-2");
        Directory.CreateDirectory(workspace);
        var tools = new AgentWorkspaceTools();
        var agentService = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), new FakeAgentLlm());
        var logs = new RuntimeLogService(settings);
        var profiles = new FileWorkspaceProfileStore(settings);
        var analysis = new WorkspaceAnalysisService(profiles, memoryStore);
        var manifests = new WorkspaceManifestService();
        var activation = new WorkspaceActivationService(manifests, profiles);
        var ragStore = new SqliteRagStore(settings);
        await ragStore.InitializeAsync();
        var rag = new RagQueryService(ragStore, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());

        var vmNoVoice = new AgentViewModel(agentService, store, memoryStore, tools, new FakeLlm(), rag, logs, analysis, activation, manifests, settings)
        {
            WorkspaceRoot = workspace,
            SelectedModel = new LlmModel { Id = "fake-agent", Name = "Fake Agent", Provider = "Test" },
            GoalText = "Investigate the workspace"
        };

        await vmNoVoice.StartCommand.ExecuteAsync(null);

        Assert.False(vmNoVoice.IsError, "agent run should complete without an orchestrator wired in");
    }

    [Fact]
    public async Task HasWorkspaceDrivesTheNoWorkspaceSelectedEmptyState()
    {
        using var temp = new TempDir();
        var (vm, _, _) = await BuildAsync(temp);

        vm.WorkspaceRoot = string.Empty;
        Assert.False(vm.HasWorkspace, "An empty workspace root must show the empty state.");

        vm.WorkspaceRoot = temp.PathFor("does-not-exist");
        Assert.False(vm.HasWorkspace, "A workspace root that does not exist on disk must show the empty state.");

        vm.WorkspaceRoot = temp.PathFor("workspace");
        Assert.True(vm.HasWorkspace, "An existing workspace directory must hide the empty state.");
    }
}
