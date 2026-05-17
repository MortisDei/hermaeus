using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Aether.Agent.Models;
using Aether.Agent.Services;
using Aether.Core.Services;
using Aether.Rag;
using Aether.Rag.Embeddings;
using Aether.Rag.Storage;
using Aether.Rag.Retrieval;
using Aether.Services;
using Aether.Tests;
using Aether.ViewModels;
using Microsoft.Data.Sqlite;
using static Aether.Tests.Helpers;

namespace Aether.Tests;

internal static class AgentTests
{
    public static async Task AgentTaskStateSerializesSchemaFields()
    {
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new FileAgentTaskStateStore(settings);
    var state = new AgentTaskState
    {
        TaskId = "task-1",
        Goal = "Check project",
        Status = AgentTaskStatus.Running,
        ActiveStep = "Inspect",
        Constraints = ["local-first"],
        CompletedSteps = ["created"],
        PendingSteps = ["inspect"],
        Summary = "Ready"
    };

    await store.SaveAsync(state);
    var json = await File.ReadAllTextAsync(Path.Combine(store.GetTaskDirectory("task-1"), "task_state.json"));
    True(json.Contains("\"task_id\"", StringComparison.Ordinal), "task state should use schema task_id field");
    True(json.Contains("\"status\": \"running\"", StringComparison.Ordinal), "task state should serialize schema enum values");
    True(json.Contains("\"completed_steps\"", StringComparison.Ordinal), "task state should use schema completed_steps field");
    True(json.Contains("\"approval_history\"", StringComparison.Ordinal), "task state should include approval history");
    var loaded = await store.LoadAsync("task-1");
    Equal("Check project", loaded?.Goal, "stored task state should reload");
    }

    public static Task AgentTaskStateRejectsUnsafeTaskIds()
    {
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new FileAgentTaskStateStore(settings);

    Throws<InvalidOperationException>(() => store.GetTaskDirectory(".."));
    Throws<InvalidOperationException>(() => store.GetTaskDirectory(" "));
    Throws<InvalidOperationException>(() => store.GetTaskDirectory("con"));
    Throws<InvalidOperationException>(() => store.GetTaskDirectory("task/one"));
    Throws<InvalidOperationException>(() => store.GetTaskDirectory(new string('a', 81)));
    return Task.CompletedTask;
    }

    public static async Task AgentReviewQueueReflectsApprovalHistory()
    {
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new FileAgentTaskStateStore(settings);
    await store.InitializeAsync();

    var state = new AgentTaskState
    {
        Goal = "Review patch",
        Status = AgentTaskStatus.WaitingForUser,
        ActiveStep = "Wait for approval",
        Summary = "Needs review",
        ApprovalHistory =
        [
            new AgentApprovalRecord("draft_patch", true, DateTime.UtcNow.AddMinutes(-5)),
            new AgentApprovalRecord("publish", false, DateTime.UtcNow)
        ]
    };

    await store.SaveAsync(state);
    var queue = await store.ListReviewQueueAsync();

    True(queue.Any(item => item.TaskId == state.TaskId), "waiting task should appear in the review queue");
    var item = queue.Single(entry => entry.TaskId == state.TaskId);
    Equal(2, item.ApprovalCount, "review queue should include approval count");
    Equal("publish", item.LastApprovalAction, "review queue should surface the latest approval action");
    False(item.LastApprovalApproved ?? true, "review queue should surface the latest approval decision");
    }

    public static async Task AgentTaskStateUsesSqliteIndexForLists()
    {
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new FileAgentTaskStateStore(settings);
    await store.InitializeAsync();

    var state = new AgentTaskState
    {
        TaskId = "indexed-task",
        Goal = "Use indexed listing",
        Status = AgentTaskStatus.WaitingForUser,
        ActiveStep = "Review",
        Summary = "Indexed",
        ApprovalHistory = [new AgentApprovalRecord("draft_patch", true, DateTime.UtcNow)]
    };

    await store.SaveAsync(state);
    var indexPath = Path.Combine(settings.Settings.DataManagement.DataRootDirectory, "agent", "task_index.db");
    True(File.Exists(indexPath), "agent task index database should be created");

    await using (var c = new SqliteConnection($"Data Source={indexPath}"))
    {
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT version FROM aether_schema_versions WHERE scope = 'agent_task_index'";
        Equal(1L, (long)(await cmd.ExecuteScalarAsync() ?? 0L), "agent task index should record schema version");
    }

    File.Delete(Path.Combine(store.GetTaskDirectory("indexed-task"), "task_state.json"));
    var recent = await store.ListRecentAsync();
    True(recent.Any(item => item.TaskId == "indexed-task"), "recent task list should use the SQLite index");
    var review = await store.ListReviewQueueAsync();
    True(review.Any(item => item.TaskId == "indexed-task"), "review queue should use the SQLite index");
    }

    public static async Task AgentTaskIndexReconcilesJsonSourceOfTruth()
    {
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new FileAgentTaskStateStore(settings);
    await store.SaveAsync(new AgentTaskState
    {
        TaskId = "indexed-task",
        Goal = "Existing indexed task",
        Status = AgentTaskStatus.Running
    });

    var orphan = new AgentTaskState
    {
        TaskId = "json-only-task",
        Goal = "Recover JSON-only task",
        Status = AgentTaskStatus.WaitingForUser,
        ActiveStep = "Review",
        Summary = "Recovered from JSON",
        ApprovalHistory = [new AgentApprovalRecord("draft_patch", true, DateTime.UtcNow)]
    };
    var orphanDir = store.GetTaskDirectory(orphan.TaskId);
    Directory.CreateDirectory(orphanDir);
    var jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };
    await File.WriteAllTextAsync(Path.Combine(orphanDir, "task_state.json"), JsonSerializer.Serialize(orphan, jsonOptions));

    var reloaded = new FileAgentTaskStateStore(settings);
    var recent = await reloaded.ListRecentAsync();
    True(recent.Any(item => item.TaskId == orphan.TaskId), "initialization should reconcile JSON task files missing from the index");
    var review = await reloaded.ListReviewQueueAsync();
    True(review.Any(item => item.TaskId == orphan.TaskId), "review queue should include reconciled JSON task files");
    }

    public static async Task AgentWorkspaceMemoryPersistsNotesPerWorkspace()
    {
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new FileAgentWorkspaceMemoryStore(settings);
    await store.InitializeAsync();

    var workspace = temp.PathFor("workspace");
    Directory.CreateDirectory(workspace);

    var entry = new AgentWorkspaceMemoryEntry
    {
        WorkspaceRoot = workspace,
        Title = "Project note",
        Body = "Remember to keep the ingest report visible.",
        Tags = ["agent", "memory"]
    };

    await store.UpsertAsync(entry);
    var items = await store.ListAsync(workspace);
    True(items.Any(item => item.Title == "Project note"), "workspace memory should persist the note");

    await store.DeleteAsync(workspace, entry.Id);
    items = await store.ListAsync(workspace);
    Equal(0, items.Count, "workspace memory should delete the note");
    }

    public static async Task AgentWorkspaceAnalysisBuildsProfile()
    {
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var workspace = temp.PathFor("workspace");
    Directory.CreateDirectory(Path.Combine(workspace, "src"));
    Directory.CreateDirectory(Path.Combine(workspace, "tests"));
    await File.WriteAllTextAsync(Path.Combine(workspace, "Aether.Sample.sln"), "");
    await File.WriteAllTextAsync(Path.Combine(workspace, "README.md"), "# Sample\nLocal project.");
    await File.WriteAllTextAsync(Path.Combine(workspace, "AGENTS.md"), "# AGENTS.md\nRun local builds only.");
    await File.WriteAllTextAsync(Path.Combine(workspace, "src", "App.cs"), "namespace Sample;");
    await File.WriteAllTextAsync(Path.Combine(workspace, "tests", "AppTests.cs"), "namespace Sample.Tests;");

    var memory = new FileAgentWorkspaceMemoryStore(settings);
    var profiles = new FileWorkspaceProfileStore(settings);
    var analysis = new WorkspaceAnalysisService(profiles, memory);
    var report = await analysis.AnalyzeAsync(new AgentWorkspaceOptions(workspace, ModelId: "local-model"));

    Equal(".NET solution", report.RepoType, "workspace without .git but with solution should report solution type");
    True(report.Languages.Any(lang => lang.StartsWith("C#", StringComparison.Ordinal)), "analysis should detect C# files");
    True(report.Instructions.Any(file => file.RelativePath == "AGENTS.md"), "analysis should detect AGENTS.md");
    True(report.CommandRecipes.Any(recipe => recipe.Command == "dotnet build"), "analysis should suggest dotnet build");
    True(report.RagIngestPlan.Contains("reindex", StringComparison.OrdinalIgnoreCase), "analysis should include RAG reindex guidance");

    var saved = await profiles.LoadAsync(workspace);
    Equal("local-model", saved?.PreferredModelId, "workspace profile should persist preferred model");
    }

    public static Task AgentWorkspaceToolsEnforcePathSafety()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    var src = Path.Combine(root, "src");
    var git = Path.Combine(root, ".git");
    var bin = Path.Combine(root, "bin");
    Directory.CreateDirectory(src);
    Directory.CreateDirectory(git);
    Directory.CreateDirectory(bin);
    File.WriteAllText(Path.Combine(src, "note.txt"), "needle visible text");
    File.WriteAllText(Path.Combine(git, "config"), "needle hidden");
    File.WriteAllText(Path.Combine(bin, "generated.txt"), "needle hidden");
    File.WriteAllText(Path.Combine(src, "large.txt"), new string('x', 200));

    var tools = new AgentWorkspaceTools();
    var options = new AgentWorkspaceOptions(root, MaxFileBytes: 64, MaxSearchResults: 10);
    var listed = tools.ListFiles(options);
    True(listed.Contains("src/note.txt"), "safe text file should be listed");
    False(listed.Any(f => f.Contains(".git", StringComparison.Ordinal)), ".git files should be skipped");
    False(listed.Any(f => f.Contains("bin/", StringComparison.Ordinal)), "bin files should be skipped");
    False(listed.Contains("src/large.txt"), "oversized files should be skipped");

    var result = tools.SearchFiles(options, "needle");
    Equal(1, result.Count, "search should only return safe matching files");
    Equal("src/note.txt", result[0].RelativePath, "search should return relative safe path");
    var read = tools.ReadFile(options, "src/note.txt");
    True(read.Content.Contains("needle", StringComparison.Ordinal), "read should return file content");
    var summary = tools.SummarizeFile(options, "src/note.txt");
    True(summary.Summary.Contains("needle", StringComparison.Ordinal), "summary should include bounded readable content");
    Throws<InvalidOperationException>(() => tools.ReadFile(options, "../outside.txt"));
    Throws<InvalidOperationException>(() => tools.ReadFile(options, Path.Combine(root, "src", "note.txt")));
    Throws<InvalidOperationException>(() => tools.ReadFile(options, ".git/config"));
    return Task.CompletedTask;
    }

    public static async Task AgentTaskStatePersistsQueuedDraftPatches()
    {
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new FileAgentTaskStateStore(settings);
    await store.InitializeAsync();

    var state = new AgentTaskState
    {
        Goal = "Queue a patch",
        Status = AgentTaskStatus.WaitingForUser,
        ActiveStep = "Review draft",
        Summary = "Patch pending review",
        DraftPatches =
        [
            new AgentDraftPatch
            {
                RelativePath = "src/Feature.cs",
                Rationale = "Keep the patch approval gated",
                ProposedContent = "public sealed class Feature { }"
            }
        ]
    };

    await store.SaveAsync(state);
    var loaded = await store.LoadAsync(state.TaskId);

    True(loaded is not null, "task state should reload");
    Equal(1, loaded!.DraftPatches.Count, "draft patches should persist with the task state");
    Equal("src/Feature.cs", loaded.DraftPatches[0].RelativePath, "draft patch path should round-trip");
    Equal(AgentDraftPatchStatus.Pending, loaded.DraftPatches[0].Status, "new draft patches should remain pending");
    }

    public static async Task AgentTaskStatePersistsBlockedDraftPatches()
    {
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new FileAgentTaskStateStore(settings);
    await store.InitializeAsync();

    var state = new AgentTaskState
    {
        Goal = "Review blocked patch",
        Status = AgentTaskStatus.WaitingForUser,
        ActiveStep = "Review draft",
        Summary = "Patch blocked",
        DraftPatches =
        [
            new AgentDraftPatch
            {
                RelativePath = "src/Unsafe.cs",
                Rationale = "Needs manual review",
                ProposedContent = "public sealed class Unsafe {}",
                Status = AgentDraftPatchStatus.Blocked,
                BlockReason = "Too risky for this slice",
                BlockedAt = DateTime.UtcNow,
                BlockedBy = "User"
            }
        ]
    };

    await store.SaveAsync(state);
    var loaded = await store.LoadAsync(state.TaskId);

    True(loaded is not null, "task state should reload");
    Equal(AgentDraftPatchStatus.Blocked, loaded!.DraftPatches[0].Status, "blocked draft patch should persist its status");
    Equal("Too risky for this slice", loaded.DraftPatches[0].BlockReason, "blocked draft patch should persist its reason");
    }

    public static Task AgentDraftPatchViewModelShowsOutcomeLabels()
    {
    var vm = new AgentDraftPatchViewModel(new AgentDraftPatch
    {
        RelativePath = "src/Unsafe.cs",
        Rationale = "Needs manual review",
        ProposedContent = "public sealed class Unsafe {}",
        Status = AgentDraftPatchStatus.Blocked,
        BlockReason = "Too risky for this slice",
        BlockedAt = new DateTime(2026, 05, 16, 12, 0, 0, DateTimeKind.Utc),
        BlockedBy = "User"
    });

    Equal("Blocked 2026-05-16 12:00 by User: Too risky for this slice", vm.OutcomeLabel, "blocked outcome should render a clear status line");
    True(vm.CanReview, "blocked patches should remain visible for later review");
    return Task.CompletedTask;
    }

    public static async Task AgentContextPackStaysBounded()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    File.WriteAllText(Path.Combine(root, "alpha.txt"), "agent alpha context");
    File.WriteAllText(Path.Combine(root, "beta.txt"), "agent beta context");
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var ragStore = new SqliteRagStore(settings);
    var rag = new RagQueryService(ragStore, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());
    var builder = new AgentContextBuilder(new AgentWorkspaceTools(), rag, ragStore, new FileAgentWorkspaceMemoryStore(settings));
    var state = new AgentTaskState
    {
        Goal = "Find alpha",
        ActiveStep = "Inspect",
        Constraints = ["local-first"],
        Summary = "summary",
        ToolResults =
        [
            new AgentToolResult { Tool = "one", ResultSummary = "1" },
            new AgentToolResult { Tool = "two", ResultSummary = "2" },
            new AgentToolResult { Tool = "three", ResultSummary = "3" },
            new AgentToolResult { Tool = "four", ResultSummary = "4" },
            new AgentToolResult { Tool = "five", ResultSummary = "5" },
            new AgentToolResult { Tool = "six", ResultSummary = "6" }
        ]
    };

    var pack = await builder.BuildAsync(state, new AgentWorkspaceOptions(root, MaxContextItems: 1));
    Equal("Find alpha", pack.CurrentGoal, "context pack should include current goal");
    True(pack.RetrievedFiles.Count <= 1, "context pack should honor context item bound");
    Equal(5, pack.ToolResults.Count, "context pack should keep latest five tool results");
    Equal("two", pack.ToolResults[0].Tool, "context pack should drop oldest tool results");
    }

    public static Task AgentToolPolicyGatesRiskyActions()
    {
    var gate = new AgentSafetyGate();
    var read = gate.Evaluate("read_file");
    Equal(AgentToolDisposition.Allowed, read.Disposition, "read-only tools should be allowed");
    Equal(AgentRiskLevel.Low, read.RiskLevel, "read-only tools should be low risk");

    var write = gate.Evaluate("apply_patch");
    Equal(AgentToolDisposition.RequiresApproval, write.Disposition, "write-like tools should require approval");
    Equal(AgentRiskLevel.Medium, write.RiskLevel, "write-like tools should be medium risk");

    var push = gate.Evaluate("push");
    Equal(AgentToolDisposition.Blocked, push.Disposition, "push should be blocked");
    Equal(AgentRiskLevel.High, push.RiskLevel, "push should be high risk");

    var unknown = gate.Evaluate("desktop_control");
    Equal(AgentToolDisposition.Blocked, unknown.Disposition, "unknown tools should be blocked");
    return Task.CompletedTask;
    }

    public static async Task AgentLoopWritesStateLogAndTrace()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    File.WriteAllText(Path.Combine(root, "README.md"), "agent docs");
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new FileAgentTaskStateStore(settings);
    var tools = new AgentWorkspaceTools();
    var service = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), new FakeAgentLlm());
    var options = new AgentWorkspaceOptions(root, ModelId: "fake-agent");

    var state = await service.CreateTaskAsync("Review docs", options);
    var step = await service.RunStepAsync(state.TaskId, options);

    Equal(AgentTaskStatus.WaitingForUser, step.State.Status, "approval-required tool should pause task");
    True(step.State.CompletedSteps.Contains("inspected context"), "state update should record completed step");
    True(step.State.ToolResults.Any(t => t.Tool == "safety_gate"), "safety gate result should be recorded");
    True(File.Exists(Path.Combine(store.GetTaskDirectory(state.TaskId), "agent.log")), "agent log should be written");
    True(File.Exists(Path.Combine(store.GetTaskDirectory(state.TaskId), "agent.trace.jsonl")), "agent trace should be written");

    await service.AppendApprovalAsync(state.TaskId, "draft_patch", approved: false);
    var reloaded = await store.LoadAsync(state.TaskId);
    True(reloaded?.ApprovalHistory.Count == 1, "approval history should persist");
    }
}
