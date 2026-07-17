using Aether.Agent.Services;
using Aether.Core.Models;
using Aether.Rag;
using Aether.Rag.Retrieval;
using Aether.Rag.Storage;
using Aether.Services;
using Aether.ViewModels;
using Xunit;
using static Aether.Tests.Helpers;

namespace Aether.Tests;

/// <summary>
/// r12 03-runtime-vm-correctness.md 3.4 (stale SelectedModel/SelectedDataset
/// after a refresh) and 3.5 (the agent's default workspace used to be the
/// whole user profile, analyzed at every startup).
/// </summary>
public sealed class AgentViewModelWorkspaceTests
{
    private static async Task<(AgentViewModel vm, ScriptedModelsLlm llm)> NewViewModelAsync(TempDir temp, ScriptedModelsLlm llm)
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new FileAgentTaskStateStore(settings);
        await store.InitializeAsync();
        var memoryStore = new WorkspaceMemoryStore(new MemoryStore(settings), settings);
        await memoryStore.InitializeAsync();
        var tools = new AgentWorkspaceTools();
        var ragStore = new SqliteRagStore(settings);
        await ragStore.InitializeAsync();
        var rag = new RagQueryService(ragStore, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());
        var agentService = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), new FakeAgentLlm());
        var logs = new RuntimeLogService(settings);
        var profiles = new FileWorkspaceProfileStore(settings);
        var analysis = new WorkspaceAnalysisService(profiles, memoryStore);
        var manifests = new WorkspaceManifestService();
        var activation = new WorkspaceActivationService(manifests, profiles);

        var vm = new AgentViewModel(agentService, store, memoryStore, tools, llm, rag, logs, analysis, activation, manifests, settings);
        return (vm, llm);
    }

    private static LlmModel Model(string id) => new() { Id = id, Name = id, Provider = "Test" };

    // ── 3.5: the agent no longer treats the user profile as an implicit workspace ──

    [Fact]
    public async Task Fresh_agent_defaults_to_no_workspace_and_never_auto_creates_a_workspace_profile_memory()
    {
        using var temp = new TempDir();
        var (vm, _) = await NewViewModelAsync(temp, new ScriptedModelsLlm(() => [Model("a")]));

        Assert.Equal(string.Empty, vm.WorkspaceRoot);
        Assert.False(vm.HasWorkspace);

        await vm.LoadAsync();

        Assert.Empty(vm.WorkspaceMemory);
        Assert.False(vm.IsAnalyzingWorkspace);
    }

    // ── 3.4: LoadAsync must re-match SelectedModel/SelectedDataset by id, not keep a stale reference ──

    [Fact]
    public async Task LoadAsync_twice_with_fresh_model_instances_keeps_a_selection_whose_reference_is_current()
    {
        using var temp = new TempDir();
        var llm = new ScriptedModelsLlm(() => [Model("a"), Model("b")]);
        var (vm, _) = await NewViewModelAsync(temp, llm);

        await vm.LoadAsync();
        vm.SelectedModel = vm.AvailableModels.Single(m => m.Id == "b");

        await vm.LoadAsync();

        Assert.Equal("b", vm.SelectedModel?.Id);
        Assert.Same(vm.AvailableModels.Single(m => m.Id == "b"), vm.SelectedModel);
    }

    // ── 2.5: overlapping LoadAsync calls must not duplicate models ──

    [Fact]
    public async Task Concurrent_LoadAsync_calls_share_the_in_flight_load_and_never_duplicate_models()
    {
        using var temp = new TempDir();
        var gate = new TaskCompletionSource();
        var llm = new ScriptedModelsLlm(() => [Model("a")]) { DelayGate = gate };
        var (vm, _) = await NewViewModelAsync(temp, llm);

        var first = vm.LoadAsync();
        var second = vm.LoadAsync();
        gate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Single(vm.AvailableModels);
        Assert.Equal(1, llm.GetModelsCallCount);
    }
}
