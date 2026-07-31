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
/// What the workbench tells the user when a step goes wrong, and what it
/// asks them.
///
/// An unreadable model response is the model's problem, not the user's. It
/// used to synthesize an ask_user, which set the task WaitingForUser and
/// stopped the autonomous loop: the user was shown "the agent is waiting for
/// your reply" with no question to answer, and the three-strike budget was
/// unreachable because reaching it took three manual Run Step clicks. A real
/// question, meanwhile, has to survive long enough to be read and no longer.
/// </summary>
public sealed class AgentUnreadableResponseTests
{
    private const string Unreadable = "I'll go ahead and read those files for you now.";

    private const string SetPlanInTypeField = """
        ```json
        {
          "thought_summary": "Recording the plan before I start.",
          "current_step": "Plan the work",
          "next_action": {
            "type": "set_plan",
            "tool_name": null,
            "arguments": {
              "steps": [
                { "description": "Read the docs", "status": "pending" },
                { "description": "Write the code", "status": "pending" }
              ]
            },
            "requires_approval": false,
            "risk_level": "none"
          },
          "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": [] },
          "user_message": "Plan recorded."
        }
        ```
        """;

    private const string AskUserResponse = """
        {
          "thought_summary": "I need a decision from the user.",
          "current_step": "Waiting on the user",
          "next_action": { "type": "ask_user", "requires_approval": false, "risk_level": "none" },
          "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": [] },
          "user_message": "Should I target the Stone Age epoch first?"
        }
        """;

    private static async Task<(AgentService Agent, FileAgentTaskStateStore Store, AgentWorkspaceOptions Options)> BuildAsync(
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
        var tools = new AgentWorkspaceTools();
        var contextBuilder = new AgentContextBuilder(tools, retrieval, memoryStore, activation, store, settings);

        var agent = new AgentService(store, contextBuilder, new AgentSafetyGate(), new AgentToolExecutor(tools),
            llm, settings: settings, workspaceTools: tools);
        return (agent, store, new AgentWorkspaceOptions(workspace, null, "fake-sequenced-agent"));
    }

    [Fact]
    public async Task One_unreadable_response_does_not_stop_the_run()
    {
        using var temp = new TempDir();
        // Unreadable, then the fake's canned final answer.
        var (agent, store, options) = await BuildAsync(temp, new FakeSequencedAgentLlm([Unreadable]));

        var created = await agent.CreateTaskAsync("Do the thing", options);
        var result = await agent.RunAsync(created.TaskId, options);

        // The loop retried by itself and reached the end, instead of parking
        // the task on a question it had not asked.
        Assert.Equal(AgentTaskStatus.Complete, result.State.Status);
        Assert.Equal(1, result.State.TotalStepErrors);
        Assert.Equal(0, result.State.ConsecutiveStepErrors);
    }

    [Fact]
    public async Task A_single_unreadable_step_leaves_the_task_runnable_rather_than_waiting_on_the_user()
    {
        using var temp = new TempDir();
        var (agent, store, options) = await BuildAsync(temp, new FakeSequencedAgentLlm([Unreadable]));

        var created = await agent.CreateTaskAsync("Do the thing", options);
        var stepped = await agent.RunStepAsync(created.TaskId, options);

        Assert.Equal(AgentTaskStatus.Running, stepped.State.Status);
        Assert.Null(stepped.State.PendingToolAction);
        Assert.Contains("could not be read", stepped.State.LastUserMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_model_is_told_what_was_wrong_so_the_retry_can_do_better()
    {
        using var temp = new TempDir();
        var (agent, store, options) = await BuildAsync(temp, new FakeSequencedAgentLlm([Unreadable]));

        var created = await agent.CreateTaskAsync("Do the thing", options);
        await agent.RunStepAsync(created.TaskId, options);

        var transcript = await store.LoadTranscriptAsync(created.TaskId);
        Assert.Contains(transcript, e => e.Role == "user"
            && e.Content.Contains("could not be parsed", StringComparison.OrdinalIgnoreCase)
            && e.Content.Contains("next_action.type", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Three_unreadable_responses_in_a_row_still_fail_the_task()
    {
        using var temp = new TempDir();
        var (agent, store, options) = await BuildAsync(temp,
            new FakeSequencedAgentLlm([Unreadable, Unreadable, Unreadable]));

        var created = await agent.CreateTaskAsync("Do the thing", options);
        var result = await agent.RunAsync(created.TaskId, options);

        Assert.Equal(AgentTaskStatus.Failed, result.State.Status);
        Assert.Equal(3, result.State.ConsecutiveStepErrors);
    }

    [Fact]
    public async Task A_tool_name_in_the_action_type_field_executes_instead_of_stalling_the_run()
    {
        using var temp = new TempDir();
        var (agent, store, options) = await BuildAsync(temp, new FakeSequencedAgentLlm([SetPlanInTypeField]));

        var created = await agent.CreateTaskAsync("Do the thing", options);
        var result = await agent.RunAsync(created.TaskId, options);

        // Not counted as a parse failure at all, and set_plan actually ran.
        Assert.Equal(0, result.State.TotalStepErrors);
        Assert.Equal(AgentTaskStatus.Complete, result.State.Status);
        Assert.Equal(2, result.State.Plan.Count);
        Assert.Equal("Read the docs", result.State.Plan[0].Description);
    }

    [Fact]
    public async Task A_real_question_is_persisted_so_the_workbench_can_show_it()
    {
        using var temp = new TempDir();
        var (agent, store, options) = await BuildAsync(temp, new FakeSequencedAgentLlm([AskUserResponse]));

        var created = await agent.CreateTaskAsync("Do the thing", options);
        var result = await agent.RunAsync(created.TaskId, options);

        Assert.Equal(AgentTaskStatus.WaitingForUser, result.State.Status);
        Assert.Equal("Should I target the Stone Age epoch first?", result.State.LastUserMessage);

        // And it survives a reload, since this is what the reply box renders.
        var reloaded = await store.LoadAsync(created.TaskId);
        Assert.Equal("Should I target the Stone Age epoch first?", reloaded!.LastUserMessage);
    }

    [Fact]
    public async Task Answering_a_question_clears_it()
    {
        using var temp = new TempDir();
        var (agent, store, options) = await BuildAsync(temp, new FakeSequencedAgentLlm([AskUserResponse]));

        var created = await agent.CreateTaskAsync("Do the thing", options);
        await agent.RunAsync(created.TaskId, options);

        await agent.AppendUserReplyAsync(created.TaskId, "Yes, start with the Stone Age.");

        var reloaded = await store.LoadAsync(created.TaskId);
        Assert.Equal(AgentTaskStatus.Running, reloaded!.Status);
        Assert.Equal(string.Empty, reloaded.LastUserMessage);
    }

    [Fact]
    public async Task Continuing_a_task_clears_whatever_it_was_asking()
    {
        using var temp = new TempDir();
        var (agent, store, options) = await BuildAsync(temp, new FakeSequencedAgentLlm([AskUserResponse]));

        var created = await agent.CreateTaskAsync("Do the thing", options);
        await agent.RunAsync(created.TaskId, options);

        await agent.ContinueTaskAsync(created.TaskId, "keep going", options);

        var reloaded = await store.LoadAsync(created.TaskId);
        Assert.Equal(string.Empty, reloaded!.LastUserMessage);
    }

    [Fact]
    public async Task A_pause_for_the_step_budget_states_its_own_reason_rather_than_an_old_question()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        settings.Settings.Agent.MaxAutoSteps = 2;

        var store = new FileAgentTaskStateStore(settings);
        await store.InitializeAsync();
        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);

        var ragStore = new SqliteRagStore(settings);
        await ragStore.InitializeAsync();
        var rag = new RagQueryService(ragStore, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());
        var tools = new AgentWorkspaceTools();
        var contextBuilder = new AgentContextBuilder(
            tools,
            new AgentRetrievalService(rag, ragStore),
            await BuildMemoryStoreAsync(settings),
            new WorkspaceActivationService(new WorkspaceManifestService(), new FileWorkspaceProfileStore(settings)),
            store,
            settings);

        // A tool step keeps the task Running, so the loop runs until the
        // budget stops it rather than reaching a final answer.
        const string keepGoing = """
            {
              "thought_summary": "Still looking.",
              "current_step": "Listing files",
              "next_action": { "type": "tool", "tool_name": "list_files", "arguments": {}, "requires_approval": false, "risk_level": "low" },
              "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": [] },
              "user_message": "Have a look at this earlier question."
            }
            """;

        var agent = new AgentService(store, contextBuilder, new AgentSafetyGate(), new AgentToolExecutor(tools),
            new FakeSequencedAgentLlm([keepGoing, keepGoing]), settings: settings, workspaceTools: tools);
        var options = new AgentWorkspaceOptions(workspace, null, "fake-sequenced-agent");

        var created = await agent.CreateTaskAsync("Do the thing", options);
        var result = await agent.RunAsync(created.TaskId, options);

        Assert.Equal(AgentTaskStatus.WaitingForUser, result.State.Status);
        Assert.Contains("step budget", result.State.LastUserMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("earlier question", result.State.LastUserMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<WorkspaceMemoryStore> BuildMemoryStoreAsync(ISettingsService settings)
    {
        var memoryStore = new WorkspaceMemoryStore(new MemoryStore(settings), settings);
        await memoryStore.InitializeAsync();
        return memoryStore;
    }
}
