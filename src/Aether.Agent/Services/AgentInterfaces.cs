using Aether.Agent.Models;

namespace Aether.Agent.Services;

public interface IAgentTaskStateStore
{
    Task InitializeAsync(CancellationToken ct = default);
    Task SaveAsync(AgentTaskState state, CancellationToken ct = default);
    Task<AgentTaskState?> LoadAsync(string taskId, CancellationToken ct = default);
    Task<IReadOnlyList<AgentTaskListItem>> ListRecentAsync(int limit = 25, CancellationToken ct = default);
    Task<IReadOnlyList<AgentReviewQueueItem>> ListReviewQueueAsync(int limit = 25, CancellationToken ct = default);
    Task AppendLogAsync(string taskId, string line, CancellationToken ct = default);
    Task AppendTraceAsync(string taskId, object trace, CancellationToken ct = default);
    string GetTaskDirectory(string taskId);
}

public interface IAgentWorkspaceMemoryStore
{
    Task InitializeAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AgentWorkspaceMemoryEntry>> ListAsync(string workspaceRoot, CancellationToken ct = default);
    Task<AgentWorkspaceMemoryEntry> UpsertAsync(AgentWorkspaceMemoryEntry entry, CancellationToken ct = default);
    Task DeleteAsync(string workspaceRoot, string id, CancellationToken ct = default);
    string GetWorkspaceDirectory(string workspaceRoot);
}

public interface IAgentWorkspaceTools
{
    IReadOnlyList<string> ListFiles(AgentWorkspaceOptions options);
    IReadOnlyList<AgentFileSearchResult> SearchFiles(AgentWorkspaceOptions options, string query);
    AgentFileReadResult ReadFile(AgentWorkspaceOptions options, string relativePath);
    AgentFileSummaryResult SummarizeFile(AgentWorkspaceOptions options, string relativePath);
    string DraftPatch(string relativePath, string rationale, string proposedContent);
}

public interface IAgentSafetyGate
{
    AgentToolPolicyDecision Evaluate(string toolName, bool wouldMutate = false);
}

public interface IAgentContextBuilder
{
    Task<AgentContextPack> BuildAsync(AgentTaskState state, AgentWorkspaceOptions options, CancellationToken ct = default);
}

public interface IAgentService
{
    Task<AgentTaskState> CreateTaskAsync(string goal, AgentWorkspaceOptions options, CancellationToken ct = default);
    Task<AgentStepResult> RunStepAsync(string taskId, AgentWorkspaceOptions options, CancellationToken ct = default);
    Task<IReadOnlyList<AgentTaskListItem>> LoadRecentTasksAsync(CancellationToken ct = default);
    Task AppendApprovalAsync(string taskId, string action, bool approved, CancellationToken ct = default);
}
