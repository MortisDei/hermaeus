using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Rag;
using Hermaeus.Rag.Retrieval;
using Hermaeus.Rag.Storage;
using Hermaeus.Services;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>
/// r19 3.1: an agent task that reached a terminal state (or stalled) can be
/// reopened with a user instruction instead of being stuck - the owner's
/// live report was a task stuck on "Agent task is already finished" with 16
/// of its own planned steps never run.
/// </summary>
public sealed class AgentContinueTaskTests
{
    private const string FinalResponse = """
        {
          "thought_summary": "Nothing left to do.",
          "current_step": "Done.",
          "next_action": { "type": "final", "requires_approval": false, "risk_level": "none" },
          "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": [] },
          "user_message": "Finished."
        }
        """;

    private const string GatedToolResponse = """
        {
          "thought_summary": "Need to write something.",
          "current_step": "Draft a patch.",
          "next_action": {
            "type": "tool",
            "tool_name": "draft_patch",
            "arguments": { "path": "notes.md" },
            "requires_approval": true,
            "risk_level": "medium"
          },
          "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": [] },
          "user_message": "Proposing a patch."
        }
        """;

    private static async Task<(AgentService Agent, FileAgentTaskStateStore Store, AgentContextBuilder ContextBuilder, AgentWorkspaceOptions Options)> BuildAsync(
        TempDir temp, ILlmService llm)
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new FileAgentTaskStateStore(settings);
        await store.InitializeAsync();

        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);

        var ragStore = new SqliteRagStore(settings);
        await ragStore.InitializeAsync();
        var rag = new RagQueryService(ragStore, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());
        var retrieval = new AgentRetrievalService(rag, ragStore);
        var activation = new WorkspaceActivationService(new WorkspaceManifestService(), new FileWorkspaceProfileStore(settings));
        var memoryStore = new WorkspaceMemoryStore(new MemoryStore(settings), settings);
        await memoryStore.InitializeAsync();
        var contextBuilder = new AgentContextBuilder(new AgentWorkspaceTools(), retrieval, memoryStore, activation, store, settings);

        var tools = new AgentWorkspaceTools();
        var agent = new AgentService(store, contextBuilder, new AgentSafetyGate(), new AgentToolExecutor(tools), llm, settings: settings);
        var options = new AgentWorkspaceOptions(workspace, null, "fake-sequenced-agent");
        return (agent, store, contextBuilder, options);
    }

    [Fact]
    public async Task ContinueTaskAsync_reopens_a_completed_task_and_the_instruction_reaches_the_next_context()
    {
        using var temp = new TempDir();
        var llm = new FakeSequencedAgentLlmForContinue([FinalResponse]);
        var (agent, _, contextBuilder, options) = await BuildAsync(temp, llm);

        var created = await agent.CreateTaskAsync("Investigate the bug", options);
        var firstRun = await agent.RunAsync(created.TaskId, options);
        Assert.Equal(AgentTaskStatus.Complete, firstRun.State.Status);

        var reopened = await agent.ContinueTaskAsync(created.TaskId, "Please also add a regression test", options);
        Assert.Equal(AgentTaskStatus.Running, reopened.Status);

        var pack = await contextBuilder.BuildAsync(reopened, options);
        Assert.Contains(pack.TranscriptHistory, item => item.Content.Contains("Please also add a regression test", StringComparison.Ordinal));

        // A second completion is reachable after continuing.
        var secondRun = await agent.RunAsync(created.TaskId, options);
        Assert.Equal(AgentTaskStatus.Complete, secondRun.State.Status);
    }

    [Fact]
    public async Task ContinueTaskAsync_requires_explicit_instruction()
    {
        using var temp = new TempDir();
        var llm = new FakeSequencedAgentLlmForContinue([FinalResponse]);
        var (agent, store, _, options) = await BuildAsync(temp, llm);

        var created = await agent.CreateTaskAsync("Investigate the bug", options);
        await agent.RunAsync(created.TaskId, options);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => agent.ContinueTaskAsync(created.TaskId, string.Empty, options));

        Assert.Contains("Continue planned work", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(await store.LoadTranscriptAsync(created.TaskId),
            e => e.Content.StartsWith("continue:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ContinuePlannedTaskAsync_records_a_typed_transition()
    {
        using var temp = new TempDir();
        var llm = new FakeSequencedAgentLlmForContinue([FinalResponse]);
        var (agent, store, _, options) = await BuildAsync(temp, llm);

        var created = await agent.CreateTaskAsync("Investigate the bug", options);
        await agent.RunAsync(created.TaskId, options);
        var state = await store.LoadAsync(created.TaskId);
        state!.PendingSteps.Add("Run the regression test");
        await store.SaveAsync(state);

        var reopened = await agent.ContinuePlannedTaskAsync(created.TaskId, options);

        Assert.Equal(AgentTaskStatus.Running, reopened.Status);
        Assert.Equal(AgentTaskTransitionKind.ContinuePlannedWork, Assert.Single(reopened.UserTransitions).Kind);
        Assert.Contains(await store.LoadTranscriptAsync(created.TaskId),
            e => e.Content.Contains("remaining planned work", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FinishTaskAsync_ends_a_paused_run_without_erasing_history()
    {
        using var temp = new TempDir();
        var llm = new FakeSequencedAgentLlmForContinue([FinalResponse]);
        var (agent, store, _, options) = await BuildAsync(temp, llm);

        var created = await agent.CreateTaskAsync("Change a file", options);
        var paused = await store.LoadAsync(created.TaskId);
        paused!.Status = AgentTaskStatus.WaitingForUser;
        paused.LastUserMessage = "Please confirm the next step.";
        paused.Summary = "The run is waiting for a reply.";
        await store.SaveAsync(paused);
        var before = await store.LoadTranscriptAsync(created.TaskId);

        var finished = await agent.FinishTaskAsync(created.TaskId);

        Assert.Equal(AgentTaskStatus.Complete, finished.Status);
        Assert.Equal(AgentTaskTransitionKind.FinishRun, Assert.Single(finished.UserTransitions).Kind);
        Assert.Null(finished.PendingToolAction);
        Assert.True((await store.LoadTranscriptAsync(created.TaskId)).Count > before.Count);
    }

    [Fact]
    public async Task StopTaskAsync_records_a_resumable_stop_without_erasing_history()
    {
        using var temp = new TempDir();
        var llm = new FakeSequencedAgentLlmForContinue([GatedToolResponse]);
        var (agent, store, _, options) = await BuildAsync(temp, llm);

        var created = await agent.CreateTaskAsync("Change a file", options);
        var state = await store.LoadAsync(created.TaskId);
        state!.Status = AgentTaskStatus.Running;
        state.Summary = "A completed read remains recorded.";
        await store.SaveAsync(state);

        var stopped = await agent.StopTaskAsync(created.TaskId);

        Assert.Equal(AgentTaskStatus.Blocked, stopped.Status);
        Assert.Equal(AgentTaskTransitionKind.StopRun, Assert.Single(stopped.UserTransitions).Kind);
        Assert.Contains("Completed work remains", stopped.Summary, StringComparison.Ordinal);
        Assert.Contains("A completed read remains recorded.", stopped.Summary, StringComparison.Ordinal);
        Assert.Contains(await store.LoadTranscriptAsync(created.TaskId),
            e => e.Content.Contains("active work cancelled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ContinueTaskAsync_refuses_when_a_tool_approval_is_pending()
    {
        using var temp = new TempDir();
        var llm = new FakeSequencedAgentLlmForContinue([GatedToolResponse]);
        var (agent, store, _, options) = await BuildAsync(temp, llm);

        var created = await agent.CreateTaskAsync("Change a file", options);
        var stepResult = await agent.RunStepAsync(created.TaskId, options);
        Assert.NotNull(stepResult.State.PendingToolAction);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => agent.ContinueTaskAsync(created.TaskId, "go ahead", options));
        Assert.Contains("approval", ex.Message, StringComparison.OrdinalIgnoreCase);

        var reloaded = await store.LoadAsync(created.TaskId);
        Assert.Equal(AgentTaskStatus.WaitingForUser, reloaded!.Status);
    }

    [Fact]
    public async Task ContinueTaskAsync_refuses_a_child_subtask_and_names_the_parent()
    {
        using var temp = new TempDir();
        var llm = new FakeSequencedAgentLlmForContinue([FinalResponse]);
        var (agent, store, _, options) = await BuildAsync(temp, llm);

        var child = new AgentTaskState
        {
            TaskId = "child-task",
            Goal = "Fix the bug",
            Status = AgentTaskStatus.Complete,
            ParentTaskId = "parent-task"
        };
        await store.SaveAsync(child);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => agent.ContinueTaskAsync(child.TaskId, "keep going", options));
        Assert.Contains("parent", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ContinueTaskAsync_refuses_when_the_task_is_actively_running()
    {
        using var temp = new TempDir();
        var llm = new FakeSequencedAgentLlmForContinue([FinalResponse]);
        var (agent, store, _, options) = await BuildAsync(temp, llm);

        var running = new AgentTaskState { TaskId = "running-task", Goal = "Do something", Status = AgentTaskStatus.Running };
        await store.SaveAsync(running);

        await Assert.ThrowsAsync<InvalidOperationException>(() => agent.ContinueTaskAsync(running.TaskId, "keep going", options));
    }

    /// <summary>Like Helpers.FakeSequencedAgentLlm but scoped to this file for clarity; behaves identically (queued responses, then a canned final answer).</summary>
    private sealed class FakeSequencedAgentLlmForContinue : ILlmService
    {
        private readonly Queue<string> _responses;
        public FakeSequencedAgentLlmForContinue(IEnumerable<string> responses) => _responses = new Queue<string>(responses);

        public string ProviderName => "FakeSequencedAgentForContinue";
        public bool IsConfigured => true;
        public Task<List<LlmModel>> GetModelsAsync(CancellationToken ct = default) =>
            Task.FromResult(new List<LlmModel> { new() { Id = "fake-sequenced-agent", Name = "Fake", Provider = "Test" } });

        public async IAsyncEnumerable<LlmStreamEvent> StreamChatAsync(
            string modelId,
            IReadOnlyList<ChatMessage> messages,
            LlmChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Delay(1, ct);
            yield return new LlmStreamEvent(_responses.Count > 0 ? _responses.Dequeue() : FinalResponse);
        }
    }
}
