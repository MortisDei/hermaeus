using System.Text.Json;
using Hermaeus.Core.Models;
using Hermaeus.LocalApi;
using Xunit;

namespace Hermaeus.Tests;

public sealed class AgentLocalApiPolicyTests
{
    private static LocalApiTokenEntry Token(params LocalApiAgentOperation[] operations) => new()
    {
        Id = "token-a",
        Name = "caller-a",
        AgentScope = new LocalApiAgentScope
        {
            Enabled = true,
            AllowedOperations = operations.ToList(),
            AllowedWorkspaceProfileIds = ["workspace-a"],
            AllowedProjectIds = ["project-a"],
            MaxConcurrentRuns = 1
        }
    };

    private static AgentApiAuthorizationContext Owned(LocalApiAgentOperation operation, int textLength = 0) =>
        new(operation, TaskExists: true, ResourceOwnerTokenId: "token-a", TextLength: textLength);

    [Fact]
    public void Legacy_token_json_migrates_to_a_disabled_empty_agent_scope()
    {
        var token = JsonSerializer.Deserialize<LocalApiTokenEntry>("""{"Id":"old","Name":"legacy","SecretRef":"ref"}""")!;
        Assert.False(token.AgentScope.Enabled);
        Assert.Empty(token.AgentScope.AllowedOperations);
        Assert.Empty(token.AgentScope.AllowedWorkspaceProfileIds);
    }

    [Fact]
    public void Agent_scope_defaults_to_current_version_and_one_run()
    {
        var scope = new LocalApiAgentScope();
        Assert.Equal(1, scope.SchemaVersion);
        Assert.Equal(1, scope.MaxConcurrentRuns);
    }

    [Fact]
    public void Disabled_scope_denies_every_operation()
    {
        var token = Token(LocalApiAgentOperation.ReadTask);
        token.AgentScope.Enabled = false;
        Assert.Equal("agent_scope_disabled", AgentApiPolicy.Evaluate(token, Owned(LocalApiAgentOperation.ReadTask)).Code);
    }

    [Fact]
    public void Unknown_scope_version_fails_closed()
    {
        var token = Token(LocalApiAgentOperation.ReadTask);
        token.AgentScope.SchemaVersion = 99;
        Assert.Equal("unsupported_scope", AgentApiPolicy.Evaluate(token, Owned(LocalApiAgentOperation.ReadTask)).Code);
    }

    [Fact]
    public void Operation_must_be_explicitly_allowed()
    {
        var decision = AgentApiPolicy.Evaluate(Token(LocalApiAgentOperation.ReadTask), Owned(LocalApiAgentOperation.StartTask));
        Assert.Equal("operation_not_allowed", decision.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void Invalid_concurrency_scope_fails_closed(int limit)
    {
        var token = Token(LocalApiAgentOperation.ReadTask);
        token.AgentScope.MaxConcurrentRuns = limit;
        Assert.Equal("invalid_scope", AgentApiPolicy.Evaluate(token, Owned(LocalApiAgentOperation.ReadTask)).Code);
    }

    [Fact]
    public void Valid_create_uses_saved_workspace_visible_model_and_allowed_project()
    {
        var token = Token(LocalApiAgentOperation.CreateTask);
        var context = new AgentApiAuthorizationContext(LocalApiAgentOperation.CreateTask,
            WorkspaceProfileId: "workspace-a", ModelId: "model-a", ProjectId: "project-a", TextLength: 12);
        Assert.True(AgentApiPolicy.Evaluate(token, context).Allowed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16385)]
    public void Create_rejects_empty_or_oversized_goal(int length)
    {
        var context = new AgentApiAuthorizationContext(LocalApiAgentOperation.CreateTask,
            WorkspaceProfileId: "workspace-a", TextLength: length);
        Assert.Equal("invalid_goal", AgentApiPolicy.Evaluate(Token(LocalApiAgentOperation.CreateTask), context).Code);
    }

    [Fact]
    public void Create_rejects_arbitrary_workspace_profile_id()
    {
        var context = new AgentApiAuthorizationContext(LocalApiAgentOperation.CreateTask,
            WorkspaceProfileId: "workspace-b", TextLength: 1);
        Assert.Equal("workspace_not_allowed", AgentApiPolicy.Evaluate(Token(LocalApiAgentOperation.CreateTask), context).Code);
    }

    [Fact]
    public void Create_rejects_removed_saved_workspace_profile()
    {
        var context = new AgentApiAuthorizationContext(LocalApiAgentOperation.CreateTask,
            WorkspaceProfileId: "workspace-a", WorkspaceProfileExists: false, TextLength: 1);
        Assert.Equal("workspace_not_allowed", AgentApiPolicy.Evaluate(Token(LocalApiAgentOperation.CreateTask), context).Code);
    }

    [Fact]
    public void Empty_model_allowlist_accepts_any_currently_visible_model()
    {
        var context = new AgentApiAuthorizationContext(LocalApiAgentOperation.CreateTask,
            WorkspaceProfileId: "workspace-a", ModelId: "visible", TextLength: 1);
        Assert.True(AgentApiPolicy.Evaluate(Token(LocalApiAgentOperation.CreateTask), context).Allowed);
    }

    [Fact]
    public void Create_requires_a_model_id()
    {
        var context = new AgentApiAuthorizationContext(LocalApiAgentOperation.CreateTask,
            WorkspaceProfileId: "workspace-a", TextLength: 1);
        Assert.Equal("model_not_allowed", AgentApiPolicy.Evaluate(Token(LocalApiAgentOperation.CreateTask), context).Code);
    }

    [Fact]
    public void Model_allowlist_is_exact_and_case_sensitive()
    {
        var token = Token(LocalApiAgentOperation.CreateTask);
        token.AgentScope.AllowedModelIds.Add("Model-A");
        var context = new AgentApiAuthorizationContext(LocalApiAgentOperation.CreateTask,
            WorkspaceProfileId: "workspace-a", ModelId: "model-a", TextLength: 1);
        Assert.Equal("model_not_allowed", AgentApiPolicy.Evaluate(token, context).Code);
    }

    [Fact]
    public void Invisible_model_is_rejected_even_when_allowlisted()
    {
        var token = Token(LocalApiAgentOperation.CreateTask);
        token.AgentScope.AllowedModelIds.Add("model-a");
        var context = new AgentApiAuthorizationContext(LocalApiAgentOperation.CreateTask,
            WorkspaceProfileId: "workspace-a", ModelId: "model-a", ModelIsVisible: false, TextLength: 1);
        Assert.Equal("model_not_allowed", AgentApiPolicy.Evaluate(token, context).Code);
    }

    [Fact]
    public void Project_must_be_explicitly_allowed()
    {
        var context = new AgentApiAuthorizationContext(LocalApiAgentOperation.CreateTask,
            WorkspaceProfileId: "workspace-a", ModelId: "model-a", ProjectId: "project-b", TextLength: 1);
        Assert.Equal("project_not_allowed", AgentApiPolicy.Evaluate(Token(LocalApiAgentOperation.CreateTask), context).Code);
    }

    [Fact]
    public void Removed_project_is_rejected_even_when_allowlisted()
    {
        var context = new AgentApiAuthorizationContext(LocalApiAgentOperation.CreateTask,
            WorkspaceProfileId: "workspace-a", ModelId: "model-a", ProjectId: "project-a", ProjectExists: false, TextLength: 1);
        Assert.Equal("project_not_allowed", AgentApiPolicy.Evaluate(Token(LocalApiAgentOperation.CreateTask), context).Code);
    }

    [Fact]
    public void Owner_can_read_its_task()
    {
        Assert.True(AgentApiPolicy.Evaluate(Token(LocalApiAgentOperation.ReadTask), Owned(LocalApiAgentOperation.ReadTask)).Allowed);
    }

    [Fact]
    public void Other_callers_task_is_hidden_as_not_found()
    {
        var context = Owned(LocalApiAgentOperation.ReadTask) with { ResourceOwnerTokenId = "token-b" };
        var decision = AgentApiPolicy.Evaluate(Token(LocalApiAgentOperation.ReadTask), context);
        Assert.Equal(404, decision.StatusCode);
        Assert.Equal("task_not_found", decision.Code);
    }

    [Fact]
    public void Explicit_broader_read_scope_allows_read_only_access()
    {
        var token = Token(LocalApiAgentOperation.ReadOutput);
        token.AgentScope.AllowReadOtherOwnedTasks = true;
        var context = Owned(LocalApiAgentOperation.ReadOutput) with { ResourceOwnerTokenId = "token-b" };
        Assert.True(AgentApiPolicy.Evaluate(token, context).Allowed);
    }

    [Fact]
    public void Broader_read_scope_never_allows_steering_another_callers_task()
    {
        var token = Token(LocalApiAgentOperation.SteerTask);
        token.AgentScope.AllowReadOtherOwnedTasks = true;
        var context = Owned(LocalApiAgentOperation.SteerTask, 4) with { ResourceOwnerTokenId = "token-b" };
        Assert.Equal("task_not_found", AgentApiPolicy.Evaluate(token, context).Code);
    }

    [Fact]
    public void Missing_task_is_not_disclosed()
    {
        var context = Owned(LocalApiAgentOperation.ReadTask) with { TaskExists = false };
        Assert.Equal(404, AgentApiPolicy.Evaluate(Token(LocalApiAgentOperation.ReadTask), context).StatusCode);
    }

    [Fact]
    public void Start_enforces_per_token_concurrency_limit()
    {
        var context = Owned(LocalApiAgentOperation.StartTask) with { ActiveRunsForToken = 1 };
        Assert.Equal(429, AgentApiPolicy.Evaluate(Token(LocalApiAgentOperation.StartTask), context).StatusCode);
    }

    [Fact]
    public void Start_stops_at_a_pending_desktop_decision()
    {
        var context = Owned(LocalApiAgentOperation.StartTask) with { HasPendingDecision = true };
        Assert.Equal("desktop_decision_required", AgentApiPolicy.Evaluate(Token(LocalApiAgentOperation.StartTask), context).Code);
    }

    [Fact]
    public void Continue_cannot_bypass_a_pending_desktop_decision()
    {
        var context = Owned(LocalApiAgentOperation.ContinueTask) with { HasPendingDecision = true };
        Assert.Equal("desktop_decision_required", AgentApiPolicy.Evaluate(Token(LocalApiAgentOperation.ContinueTask), context).Code);
    }

    [Fact]
    public void Continue_cannot_implicitly_answer_a_user_question()
    {
        var context = Owned(LocalApiAgentOperation.ContinueTask) with { IsWaitingForUserAnswer = true };
        Assert.Equal("user_answer_required", AgentApiPolicy.Evaluate(Token(LocalApiAgentOperation.ContinueTask), context).Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8193)]
    public void Steer_rejects_empty_or_oversized_instruction(int length)
    {
        Assert.Equal("invalid_instruction",
            AgentApiPolicy.Evaluate(Token(LocalApiAgentOperation.SteerTask), Owned(LocalApiAgentOperation.SteerTask, length)).Code);
    }

    [Fact]
    public void Continue_allows_an_empty_instruction_when_no_decision_is_pending()
    {
        Assert.True(AgentApiPolicy.Evaluate(Token(LocalApiAgentOperation.ContinueTask), Owned(LocalApiAgentOperation.ContinueTask)).Allowed);
    }

    [Fact]
    public void Operation_contract_contains_no_approval_or_denial_authority()
    {
        var names = Enum.GetNames<LocalApiAgentOperation>();
        Assert.DoesNotContain(names, name => name.Contains("approve", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("deny", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Conditional_route_contract_contains_no_approval_endpoint()
    {
        Assert.False(AgentApiContract.ExecutionRoutesAvailable);
        Assert.DoesNotContain(AgentApiContract.ConditionalRoutes,
            route => route.Contains("approve", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Decision_contract_requires_desktop_review_by_default()
    {
        var decision = new AgentPendingDecisionDto("opaque", "fingerprint", "medium", "Review write");
        Assert.True(decision.DesktopReviewRequired);
    }

    [Fact]
    public void Versioned_task_contract_round_trips_without_a_workspace_path()
    {
        var request = new AgentTaskCreateRequestV1(AgentApiContract.SchemaVersion, "Goal", "workspace-a", "model-a", "project-a");
        var json = JsonSerializer.Serialize(request);
        Assert.Contains("workspace-a", json, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkspaceRoot", json, StringComparison.Ordinal);
    }
}
