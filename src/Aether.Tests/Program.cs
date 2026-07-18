using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Aether.Agent.Models;
using Aether.Agent.Services;
using Aether.Core.Models;
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

    // r6 01-first-five-minutes.md 1.7: the review queue is built from a
    // lightweight SQLite index that never stored PendingToolAction; a
    // WaitingForUser entry now gets it filled in from a full task load so
    // the queue can show risk and reason, not just status.
    public static async Task AgentReviewQueueIncludesPendingToolActionForWaitingTasks()
    {
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new FileAgentTaskStateStore(settings);
    await store.InitializeAsync();

    var state = new AgentTaskState
    {
        Goal = "Run a gated command",
        Status = AgentTaskStatus.WaitingForUser,
        ActiveStep = "Wait for approval",
        Summary = "Needs review",
        PendingToolAction = new AgentPendingToolAction
        {
            ToolName = "run_command",
            RiskLevel = AgentRiskLevel.Medium,
            Reason = "Template-family command execution always requires approval.",
            Arguments = new Dictionary<string, object?> { ["command"] = "npm test" }
        }
    };
    await store.SaveAsync(state);

    var queue = await store.ListReviewQueueAsync();
    var item = queue.Single(entry => entry.TaskId == state.TaskId);
    True(item.PendingToolAction is not null, "a waiting task should have its pending tool action populated");
    Equal("run_command", item.PendingToolAction!.ToolName, "pending tool name should carry through to the queue");
    Equal(AgentRiskLevel.Medium, item.PendingToolAction.RiskLevel, "pending risk level should carry through");
    Equal("Template-family command execution always requires approval.", item.PendingToolAction.Reason,
        "the safety gate's reason should carry through so approval is never a bare status label");
    }

    public static Task AgentApprovalPreviewDescribesNpmScriptBody()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    File.WriteAllText(Path.Combine(root, "package.json"), """{"scripts": {"test": "vitest run --coverage"}}""");

    var pending = new AgentPendingToolAction
    {
        ToolName = "run_command",
        Arguments = new Dictionary<string, object?> { ["command"] = "npm run test" }
    };
    var preview = AgentApprovalPreview.Describe(pending, new AgentWorkspaceOptions(root));
    Equal("Runs: vitest run --coverage", preview, "an npm run approval should show the exact script body from package.json");

    var dotnetPending = new AgentPendingToolAction
    {
        ToolName = "run_command",
        Arguments = new Dictionary<string, object?> { ["command"] = "dotnet test" }
    };
    var dotnetPreview = AgentApprovalPreview.Describe(dotnetPending, new AgentWorkspaceOptions(root));
    True(dotnetPreview.Contains("workspace-defined", StringComparison.Ordinal), "dotnet test should show the fixed provenance note");

    var editPending = new AgentPendingToolAction { ToolName = "edit_file" };
    Equal(string.Empty, AgentApprovalPreview.Describe(editPending, new AgentWorkspaceOptions(root)), "non-command tools should have no recipe preview");
    return Task.CompletedTask;
    }

    public static async Task AgentApprovalPreviewDescribesTheProposedSubtaskPlan()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new FileAgentTaskStateStore(settings);
    var tools = new AgentWorkspaceTools();
    var llm = new FakeSequencedAgentLlm([PlanTwoSubtasksResponse]);
    var service = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), llm, settings: settings);
    var options = new AgentWorkspaceOptions(root, ModelId: "fake-sequenced-agent");

    var state = await service.CreateTaskAsync("Fix the bug and add coverage", options);
    var step = await service.RunStepAsync(state.TaskId, options);

    var preview = AgentApprovalPreview.Describe(step.State.PendingToolAction!, options);
    True(preview.Contains("2 sub-task", StringComparison.Ordinal), "the preview should show the sub-task count");
    True(preview.Contains("[correctness] Fix the bug", StringComparison.Ordinal), "the preview should show each sub-task's profile and goal");
    True(preview.Contains("[tests] Add a regression test", StringComparison.Ordinal), "the preview should show each sub-task's profile and goal");

    var malformed = new AgentPendingToolAction { ToolName = "plan_subtasks", Arguments = new Dictionary<string, object?>() };
    Equal("Could not parse the proposed plan.", AgentApprovalPreview.Describe(malformed, options), "a malformed plan payload should degrade to a clear message");
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
        Equal(2L, (long)(await cmd.ExecuteScalarAsync() ?? 0L), "agent task index should record schema version");
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
    var store = new WorkspaceMemoryStore(new MemoryStore(settings), settings);
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

    var memory = new WorkspaceMemoryStore(new MemoryStore(settings), settings);
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

    public static async Task WorkspaceManifestRoundTripsThroughInRepoFile()
    {
    using var temp = new TempDir();
    var workspace = temp.PathFor("workspace");
    Directory.CreateDirectory(workspace);
    var manifests = new WorkspaceManifestService();
    var manifest = new WorkspaceManifest
    {
        PreferredModelId = "local-model",
        LinkedRagDatasetId = "dataset-1",
        InstructionPaths = ["AGENTS.md"]
    };

    await manifests.SaveAsync(workspace, manifest);
    True(File.Exists(Path.Combine(workspace, ".aether", "workspace.json")), "manifest should be written in-repo under .aether/");

    var loaded = await manifests.LoadAsync(workspace);
    Equal("local-model", loaded?.PreferredModelId, "loaded manifest should round-trip the preferred model");
    Equal("dataset-1", loaded?.LinkedRagDatasetId, "loaded manifest should round-trip the linked dataset");
    }

    public static async Task WorkspaceActivationPrefersManifestOverProfile()
    {
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var workspace = temp.PathFor("workspace");
    Directory.CreateDirectory(workspace);

    var profiles = new FileWorkspaceProfileStore(settings);
    await profiles.SaveAsync(new WorkspaceProfile { WorkspaceRoot = workspace, PreferredModelId = "profile-model" });

    var manifests = new WorkspaceManifestService();
    var activationService = new WorkspaceActivationService(manifests, profiles);

    var fromProfile = await activationService.ActivateAsync(workspace);
    Equal("profile-model", fromProfile.PreferredModelId, "activation should fall back to the app-side profile when no manifest exists");
    False(fromProfile.FromManifest, "activation should report it did not come from a manifest");

    await manifests.SaveAsync(workspace, new WorkspaceManifest { PreferredModelId = "manifest-model" });
    var fromManifest = await activationService.ActivateAsync(workspace);
    Equal("manifest-model", fromManifest.PreferredModelId, "activation should prefer the in-repo manifest once one exists");
    True(fromManifest.FromManifest, "activation should report it came from a manifest");
    }

    public static Task WorkspaceManifestRequiresAnExistingWorkspaceRoot()
    {
    using var temp = new TempDir();
    var missingWorkspace = temp.PathFor("does-not-exist");
    var manifests = new WorkspaceManifestService();
    Throws<DirectoryNotFoundException>(() => manifests.LoadAsync(missingWorkspace).GetAwaiter().GetResult());
    return Task.CompletedTask;
    }

    public static Task AgentSafetyGateAlwaysRequiresApprovalForMcpTools()
    {
    var gate = new AgentSafetyGate();

    var trusted = gate.Evaluate("mcp:filesystem:read_file");
    Equal(AgentToolDisposition.RequiresApproval, trusted.Disposition, "mcp: tools should never auto-allow even if their name looks read-only");

    var readNamed = gate.Evaluate("mcp:server1:read_something");
    Equal(AgentToolDisposition.RequiresApproval, readNamed.Disposition, "mcp: tools should require approval regardless of naming");
    return Task.CompletedTask;
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

    public static async Task AgentEditFileRequiresUniqueMatch()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    var file = Path.Combine(root, "Program.cs");
    await File.WriteAllTextAsync(file, "int a = 1;\nint b = 2;\nint a = 1;\n");

    var tools = new AgentWorkspaceTools();
    var options = new AgentWorkspaceOptions(root);

    await ThrowsAsync<InvalidOperationException>(() => tools.EditFileAsync(options, "Program.cs", "int a = 1;", "int a = 99;"));
    await ThrowsAsync<InvalidOperationException>(() => tools.EditFileAsync(options, "Program.cs", "int z = 9;", "int z = 10;"));

    var edited = await tools.EditFileAsync(options, "Program.cs", "int b = 2;", "int b = 42;");
    True(edited.Content.Contains("int b = 42;", StringComparison.Ordinal), "a unique old_string match should be replaced");
    Equal(2, edited.Content.Split("int a = 1;").Length - 1, "edit_file should leave non-matched occurrences of the ambiguous text untouched");

    await ThrowsAsync<FileNotFoundException>(() => tools.EditFileAsync(options, "missing.cs", "x", "y"));
    Throws<InvalidOperationException>(() => AgentWorkspaceTools.ResolveSafePath(root, "../outside.cs"));
    }

    public static async Task AgentCreateFileRefusesToOverwriteExisting()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);

    var tools = new AgentWorkspaceTools();
    var options = new AgentWorkspaceOptions(root);

    var created = await tools.CreateFileAsync(options, "src/New.cs", "namespace Sample;\n");
    True(File.Exists(Path.Combine(root, "src", "New.cs")), "create_file should create the file, including its parent directory");
    Equal("namespace Sample;\n", created.Content, "create_file should return the content it wrote");

    await ThrowsAsync<InvalidOperationException>(() => tools.CreateFileAsync(options, "src/New.cs", "different content"));
    }

    public static Task AgentGlobFilesMatchesPatterns()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(Path.Combine(root, "src"));
    Directory.CreateDirectory(Path.Combine(root, "src", "sub"));
    File.WriteAllText(Path.Combine(root, "src", "Foo.cs"), "class Foo {}");
    File.WriteAllText(Path.Combine(root, "src", "sub", "Bar.cs"), "class Bar {}");
    File.WriteAllText(Path.Combine(root, "README.md"), "# readme");

    var tools = new AgentWorkspaceTools();
    var options = new AgentWorkspaceOptions(root);

    var shallow = tools.GlobFiles(options, "src/*.cs");
    True(shallow.Contains("src/Foo.cs"), "single-star glob should match direct children");
    False(shallow.Contains("src/sub/Bar.cs"), "single-star glob should not match nested files");

    var deep = tools.GlobFiles(options, "src/**/*.cs");
    True(deep.Contains("src/Foo.cs") && deep.Contains("src/sub/Bar.cs"), "double-star glob should match at any depth");
    False(deep.Contains("README.md"), "glob should not match files outside the pattern");
    return Task.CompletedTask;
    }

    public static async Task AgentReadFilePagesByLine()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    var lines = Enumerable.Range(1, 10).Select(i => $"line {i}").ToArray();
    await File.WriteAllTextAsync(Path.Combine(root, "big.txt"), string.Join('\n', lines));

    var tools = new AgentWorkspaceTools();
    var options = new AgentWorkspaceOptions(root);

    var page = tools.ReadFile(options, "big.txt", lineOffset: 2, lineLimit: 3);
    Equal("line 3\nline 4\nline 5", page.Content, "read_file should page by the requested line range");
    True(page.Truncated, "a partial page should report truncation");

    var last = tools.ReadFile(options, "big.txt", lineOffset: 9, lineLimit: 5);
    Equal("line 10", last.Content, "read_file should stop at the end of the file without erroring");
    False(last.Truncated, "reaching the end of the file should not report truncation");
    }

    public static Task AgentSearchFilesSupportsRegexAndContext()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    File.WriteAllText(Path.Combine(root, "notes.txt"), "alpha\nTODO: fix this\nbeta\ngamma");

    var tools = new AgentWorkspaceTools();
    var options = new AgentWorkspaceOptions(root);

    var literal = tools.SearchFiles(options, "TODO");
    Equal(1, literal.Count, "literal search should find the matching file");

    var regexMiss = tools.SearchFiles(options, "^TODO$", regex: true);
    Equal(0, regexMiss.Count, "an anchored regex should not match a line with a prefix");

    var regexHit = tools.SearchFiles(options, "^TODO:", regex: true, contextLines: 1);
    Equal(1, regexHit.Count, "regex search should match the TODO line");
    True(regexHit[0].Snippet.Contains("alpha") && regexHit[0].Snippet.Contains("beta"), "context lines should surround the match");

    Throws<InvalidOperationException>(() => tools.SearchFiles(options, "[", regex: true));
    return Task.CompletedTask;
    }

    public static async Task AgentSetPlanUpdatesTaskStateWithoutApproval()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new FileAgentTaskStateStore(settings);
    var tools = new AgentWorkspaceTools();
    var setPlanResponse = """
        {
          "thought_summary": "Planning the work.",
          "current_step": "Draft a plan.",
          "next_action": {
            "type": "tool",
            "tool_name": "set_plan",
            "arguments": { "steps": [ { "description": "Read the docs", "status": "done" }, { "description": "Write the fix", "status": "in_progress" } ] },
            "requires_approval": false,
            "risk_level": "none"
          },
          "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": [] },
          "user_message": "Plan updated."
        }
        """;
    var llm = new FakeSequencedAgentLlm([setPlanResponse]);
    var service = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), llm, settings: settings);
    var options = new AgentWorkspaceOptions(root, ModelId: "fake-sequenced-agent");

    var state = await service.CreateTaskAsync("Plan the work", options);
    var step = await service.RunStepAsync(state.TaskId, options);

    Equal(AgentTaskStatus.Running, step.State.Status, "set_plan should never require approval or pause the task");
    Equal(2, step.State.Plan.Count, "set_plan should replace the task's plan");
    Equal(AgentPlanStepStatus.Done, step.State.Plan[0].Status, "plan step status should round-trip");
    Equal(AgentPlanStepStatus.InProgress, step.State.Plan[1].Status, "plan step status should round-trip");
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
    var retrieval = new AgentRetrievalService(rag, ragStore);
    var activation = new WorkspaceActivationService(new WorkspaceManifestService(), new FileWorkspaceProfileStore(settings));
    var taskStore = new FileAgentTaskStateStore(settings);
    var builder = new AgentContextBuilder(new AgentWorkspaceTools(), retrieval, new WorkspaceMemoryStore(new MemoryStore(settings), settings), activation, taskStore, settings);
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
    False(string.IsNullOrWhiteSpace(pack.RetrievedFiles[0].Locator), "retrieved workspace files should carry a locator back to the source path");
    }

    public static async Task AgentToolExecutorPopulatesSourceReferenceForFileTools()
    {
    using var temp = new TempDir();
    var workspace = temp.PathFor("workspace");
    Directory.CreateDirectory(workspace);
    await File.WriteAllTextAsync(Path.Combine(workspace, "notes.md"), "hello from notes");

    var executor = new AgentToolExecutor(new AgentWorkspaceTools());
    var options = new AgentWorkspaceOptions(workspace);

    var readResult = await executor.ExecuteAsync("read_file", new Dictionary<string, object?> { ["relative_path"] = "notes.md" }, options);
    True(readResult.Source is not null, "read_file should populate a source reference");
    Equal("notes.md", readResult.Source!.Locator, "read_file's source reference should point at the relative path");
    Equal(ProvenanceKind.Workspace, readResult.Source.Kind, "read_file's source reference should be workspace-kind");

    var listResult = await executor.ExecuteAsync("list_files", new Dictionary<string, object?>(), options);
    True(listResult.Source is null, "list_files has no single source, so it should not fabricate one");
    }

    public static async Task AgentContextPackIncludesActivatedProjectInstructions()
    {
    using var temp = new TempDir();
    var workspace = temp.PathFor("workspace");
    Directory.CreateDirectory(workspace);
    await File.WriteAllTextAsync(Path.Combine(workspace, "AGENTS.md"), "Always run tests before finishing.");

    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var ragStore = new SqliteRagStore(settings);
    var rag = new RagQueryService(ragStore, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());
    var retrieval = new AgentRetrievalService(rag, ragStore);

    var manifests = new WorkspaceManifestService();
    await manifests.SaveAsync(workspace, new WorkspaceManifest { InstructionPaths = ["AGENTS.md"] });
    var activation = new WorkspaceActivationService(manifests, new FileWorkspaceProfileStore(settings));

    var taskStore = new FileAgentTaskStateStore(settings);
    var builder = new AgentContextBuilder(new AgentWorkspaceTools(), retrieval, new WorkspaceMemoryStore(new MemoryStore(settings), settings), activation, taskStore, settings);
    var state = new AgentTaskState { Goal = "Ship a fix", ActiveStep = "Implement", Summary = "summary" };

    var pack = await builder.BuildAsync(state, new AgentWorkspaceOptions(workspace));
    True(pack.ProjectInstructions.Count == 1, "context pack should include the activated project instruction file");
    Equal("AGENTS.md", pack.ProjectInstructions[0].Locator, "project instruction items should carry their relative path as a locator");
    True(pack.ProjectInstructions[0].Content.Contains("Always run tests"), "project instruction content should be readable to the model");
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

    public static Task AgentSafetyGateEvaluateCommandOnlyAllowsDeclaredSafeRecipes()
    {
    var gate = new AgentSafetyGate();
    var declared = new List<WorkspaceCommandRecipe> { new("dotnet test", "Run tests.", AgentRiskLevel.Low) };

    var undeclared = gate.EvaluateCommand("dotnet build", declared);
    Equal(AgentToolDisposition.Blocked, undeclared.Disposition, "commands not declared by the workspace should be blocked even if in the fixed safe set");

    var notFixed = gate.EvaluateCommand("rm -rf /", [new WorkspaceCommandRecipe("rm -rf /", "malicious manifest entry", AgentRiskLevel.Low)]);
    Equal(AgentToolDisposition.Blocked, notFixed.Disposition, "a command outside the fixed executable dictionary should be blocked even if a manifest declares it");

    var allowed = gate.EvaluateCommand("dotnet test", declared);
    Equal(AgentToolDisposition.RequiresApproval, allowed.Disposition, "a declared, fixed-safe recipe should still require approval, never auto-allow");
    Equal(AgentRiskLevel.Medium, allowed.RiskLevel, "recipe execution should be medium risk");

    var empty = gate.EvaluateCommand(null, declared);
    Equal(AgentToolDisposition.Blocked, empty.Disposition, "a missing command should be blocked");
    return Task.CompletedTask;
    }

    public static async Task AgentToolExecutorRunsDeclaredCommandRecipe()
    {
    using var temp = new TempDir();
    var workspace = temp.PathFor("workspace");
    Directory.CreateDirectory(workspace);
    await File.WriteAllTextAsync(Path.Combine(workspace, "sample.csproj"), """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
        </Project>
        """);
    await File.WriteAllTextAsync(Path.Combine(workspace, "Program.cs"), "System.Console.WriteLine(\"hi\");");

    var executor = new AgentToolExecutor(new AgentWorkspaceTools());
    True(executor.CanExecute("run_command"), "executor should support run_command");

    var options = new AgentWorkspaceOptions(workspace);
    var result = await executor.ExecuteAsync("run_command", new Dictionary<string, object?> { ["command"] = "dotnet build" }, options);
    True(result.ResultSummary.Contains("Exit code", StringComparison.Ordinal), "run_command result should report an exit code");

    await ThrowsAsync<InvalidOperationException>(() =>
        executor.ExecuteAsync("run_command", new Dictionary<string, object?> { ["command"] = "rm -rf /" }, options));
    }

    public static async Task RunCommandAcceptsAnOptionalPathWithinTheWorkspace()
    {
    using var temp = new TempDir();
    var workspace = temp.PathFor("workspace");
    Directory.CreateDirectory(Path.Combine(workspace, "src"));
    await File.WriteAllTextAsync(Path.Combine(workspace, "src", "sample.csproj"), """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
        </Project>
        """);
    await File.WriteAllTextAsync(Path.Combine(workspace, "src", "Program.cs"), "System.Console.WriteLine(\"hi\");");

    var executor = new AgentToolExecutor(new AgentWorkspaceTools());
    var options = new AgentWorkspaceOptions(workspace);

    var result = await executor.ExecuteAsync("run_command", new Dictionary<string, object?> { ["command"] = "dotnet build src/sample.csproj" }, options);
    True(result.ResultSummary.Contains("Exit code", StringComparison.Ordinal), "dotnet build with a workspace-relative project path should run");

    await ThrowsAsync<InvalidOperationException>(() =>
        executor.ExecuteAsync("run_command", new Dictionary<string, object?> { ["command"] = "dotnet build ../outside.csproj" }, options));
    await ThrowsAsync<InvalidOperationException>(() =>
        executor.ExecuteAsync("run_command", new Dictionary<string, object?> { ["command"] = "dotnet build /etc/passwd" }, options));
    }

    public static async Task RunCommandNpmRunOnlyAllowsScriptsDeclaredInPackageJson()
    {
    using var temp = new TempDir();
    var workspace = temp.PathFor("workspace");
    Directory.CreateDirectory(workspace);
    await File.WriteAllTextAsync(Path.Combine(workspace, "package.json"), """{"scripts": {"lint": "eslint ."}}""");

    var gate = new AgentSafetyGate();
    var declared = new List<WorkspaceCommandRecipe> { new("npm run", "Run a declared npm script.", AgentRiskLevel.Low) };

    var allowedDecision = gate.EvaluateCommand("npm run lint", declared);
    Equal(AgentToolDisposition.RequiresApproval, allowedDecision.Disposition, "an npm run family declared by the workspace should still require approval, never auto-allow");

    // Not asserting a successful "npm run lint" execution here: the test
    // machine (and CI) may not have npm installed. The safety-relevant
    // behavior - refusing a script the workspace's own package.json never
    // declared - fails validation before any process would ever start, so
    // it's exercised without depending on npm actually being present.
    var executor = new AgentToolExecutor(new AgentWorkspaceTools());
    var options = new AgentWorkspaceOptions(workspace);
    await ThrowsAsync<InvalidOperationException>(() =>
        executor.ExecuteAsync("run_command", new Dictionary<string, object?> { ["command"] = "npm run undeclared-script" }, options));
    }

    public static Task AgentSafetyGateDeclaredFamilyCoversArgumentVariants()
    {
    var gate = new AgentSafetyGate();
    var declared = new List<WorkspaceCommandRecipe> { new("dotnet test", "Run tests.", AgentRiskLevel.Low) };

    var bare = gate.EvaluateCommand("dotnet test", declared);
    Equal(AgentToolDisposition.RequiresApproval, bare.Disposition, "the bare declared family should be allowed-with-approval");

    var withPath = gate.EvaluateCommand("dotnet test tests/Foo.Tests.csproj", declared);
    Equal(AgentToolDisposition.RequiresApproval, withPath.Disposition, "a declared bare family should also cover the same family with a project argument");

    var undeclaredFamily = gate.EvaluateCommand("cargo test", declared);
    Equal(AgentToolDisposition.Blocked, undeclaredFamily.Disposition, "a fixed family the workspace never declared should stay blocked");

    var unknownFamily = gate.EvaluateCommand("dotnet run --project x", declared);
    Equal(AgentToolDisposition.Blocked, unknownFamily.Disposition, "a family outside the fixed set should always be blocked regardless of manifest declarations");
    return Task.CompletedTask;
    }

    public static async Task AgentRememberedCommandApprovalOnlyAppliesToTheExactCommandInTheSameTask()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    await File.WriteAllTextAsync(Path.Combine(root, "sample.csproj"), """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
        </Project>
        """);
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new FileAgentTaskStateStore(settings);
    var tools = new AgentWorkspaceTools();
    var buildResponse = """
        {
          "thought_summary": "Building.",
          "current_step": "Build the project.",
          "next_action": { "type": "tool", "tool_name": "run_command", "arguments": { "command": "dotnet build" }, "requires_approval": true, "risk_level": "medium" },
          "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": [] },
          "user_message": "Building."
        }
        """;
    var testResponse = """
        {
          "thought_summary": "Testing.",
          "current_step": "Run tests.",
          "next_action": { "type": "tool", "tool_name": "run_command", "arguments": { "command": "dotnet test" }, "requires_approval": true, "risk_level": "medium" },
          "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": [] },
          "user_message": "Testing."
        }
        """;
    var llm = new FakeSequencedAgentLlm([buildResponse, buildResponse, testResponse]);
    var service = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), llm, settings: settings);
    var manifest = new WorkspaceManifestService();
    await manifest.SaveAsync(root, new WorkspaceManifest
    {
        AllowedCommands = [new WorkspaceCommandRecipe("dotnet build", "Build.", AgentRiskLevel.Low), new WorkspaceCommandRecipe("dotnet test", "Test.", AgentRiskLevel.Low)]
    });
    var serviceWithManifest = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), llm, manifests: manifest, settings: settings);
    var options = new AgentWorkspaceOptions(root, ModelId: "fake-sequenced-agent");

    var state = await serviceWithManifest.CreateTaskAsync("Build and test", options);
    var first = await serviceWithManifest.RunStepAsync(state.TaskId, options);
    Equal(AgentTaskStatus.WaitingForUser, first.State.Status, "the first dotnet build should still require approval");

    await serviceWithManifest.AppendApprovalAsync(state.TaskId, "run_command", approved: true, options);
    var afterApproval = await store.LoadAsync(state.TaskId);
    True(afterApproval!.RememberedCommandApprovals.Any(c => string.Equals(c, "dotnet build", StringComparison.OrdinalIgnoreCase)),
        "approving a run_command should remember the exact command for the rest of the task");

    var second = await serviceWithManifest.RunStepAsync(state.TaskId, options);
    Equal(AgentTaskStatus.Running, second.State.Status, "an identical repeat of the remembered command should auto-execute without pausing");

    var third = await serviceWithManifest.RunStepAsync(state.TaskId, options);
    Equal(AgentTaskStatus.WaitingForUser, third.State.Status, "a different command, even in the same task, should still require its own approval");
    }

    public static async Task RunCommandOutputSurfacesCompilerErrorsForTheModelToSee()
    {
    using var temp = new TempDir();
    var workspace = temp.PathFor("workspace");
    Directory.CreateDirectory(workspace);
    await File.WriteAllTextAsync(Path.Combine(workspace, "broken.csproj"), """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
        </Project>
        """);
    await File.WriteAllTextAsync(Path.Combine(workspace, "Program.cs"), "this is not valid C#;;;");

    var executor = new AgentToolExecutor(new AgentWorkspaceTools());
    var options = new AgentWorkspaceOptions(workspace);
    var result = await executor.ExecuteAsync("run_command", new Dictionary<string, object?> { ["command"] = "dotnet build broken.csproj" }, options);

    False(result.ResultSummary.Contains("Exit code 0", StringComparison.Ordinal), "a deliberately broken project should not report success");
    True(result.ResultSummary.Contains("error", StringComparison.OrdinalIgnoreCase), "the compiler error text should be visible in the tool result, not just an exit code");
    }

    public static async Task RunCommandRecipeCaseSensitivityMatchesBetweenGateAndExecutor()
    {
    // docs/review/01-code-audit.md P3-3 flagged the gate and executor as
    // potentially disagreeing on case for a declared recipe; on inspection
    // WorkspaceCommandRecipes.Executable already uses an OrdinalIgnoreCase
    // comparer, so both layers already agree. This locks that invariant in.
    using var temp = new TempDir();
    var workspace = temp.PathFor("workspace");
    Directory.CreateDirectory(workspace);
    await File.WriteAllTextAsync(Path.Combine(workspace, "sample.csproj"), """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
        </Project>
        """);

    var gate = new AgentSafetyGate();
    var declared = new List<WorkspaceCommandRecipe> { new("dotnet build", "Build the project.", AgentRiskLevel.Low) };
    var mixedCase = "DOTNET Build";

    var decision = gate.EvaluateCommand(mixedCase, declared);
    Equal(AgentToolDisposition.RequiresApproval, decision.Disposition,
        "the gate should recognize a declared recipe regardless of case");

    var executor = new AgentToolExecutor(new AgentWorkspaceTools());
    var options = new AgentWorkspaceOptions(workspace);
    var result = await executor.ExecuteAsync("run_command", new Dictionary<string, object?> { ["command"] = mixedCase }, options);
    True(result.ResultSummary.Contains("Exit code", StringComparison.Ordinal),
        "the executor should run the same mixed-case command the gate just approved, not throw on a case mismatch");
    }

    public static async Task AgentToolExecutorInspectGitDiffHandlesLargeStatusOutput()
    {
    using var temp = new TempDir();
    var workspace = temp.PathFor("workspace");
    Directory.CreateDirectory(workspace);
    await RunGitAsync(workspace, "init", "-q");

    // Enough untracked files that `git status --short` output exceeds a
    // typical OS pipe buffer, so reading stdout only after WaitForExit would
    // deadlock (docs/review/01-code-audit.md P2-4).
    for (var i = 0; i < 3000; i++)
        await File.WriteAllTextAsync(Path.Combine(workspace, $"file-{i}.txt"), "x");

    var executor = new AgentToolExecutor(new AgentWorkspaceTools());
    var options = new AgentWorkspaceOptions(workspace);
    var result = await executor.ExecuteAsync("inspect_git_diff", new Dictionary<string, object?>(), options)
        .WaitAsync(TimeSpan.FromSeconds(20));

    True(result.ResultSummary.Contains("file-0.txt", StringComparison.Ordinal),
        "large git status output should be read fully instead of deadlocking or timing out");
    }

    private static async Task RunGitAsync(string workingDirectory, params string[] args)
    {
    var psi = new System.Diagnostics.ProcessStartInfo
    {
        FileName = "git",
        WorkingDirectory = workingDirectory,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
    };
    foreach (var arg in args)
        psi.ArgumentList.Add(arg);

    using var process = System.Diagnostics.Process.Start(psi)!;
    await process.WaitForExitAsync();
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
    True(File.Exists(Path.Combine(store.GetTaskDirectory(state.TaskId), "transcript.jsonl")), "agent step transcript should be written");
    var transcript = await store.LoadTranscriptAsync(state.TaskId);
    True(transcript.Any(e => e.Role == "assistant" && e.Step == 1), "transcript should record the assistant's first-step thought");
    Equal(1, step.State.StepCount, "step count should advance once per RunStepAsync call");

    await service.AppendApprovalAsync(state.TaskId, "draft_patch", approved: false);
    var reloaded = await store.LoadAsync(state.TaskId);
    True(reloaded?.ApprovalHistory.Count == 1, "approval history should persist");
    }

    public static async Task AgentTranscriptFeedsFollowingStepContextPack()
    {
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new FileAgentTaskStateStore(settings);
    await store.InitializeAsync();

    var state = new AgentTaskState { TaskId = "transcript-task", Goal = "Investigate", Status = AgentTaskStatus.Running };
    await store.SaveAsync(state);

    await store.AppendTranscriptEntryAsync(state.TaskId, new AgentTranscriptEntry(1, "assistant", null, "Looked at README.md for context.", DateTime.UtcNow));
    await store.AppendTranscriptEntryAsync(state.TaskId, new AgentTranscriptEntry(1, "tool", "read_file", "README contents: hello world", DateTime.UtcNow));
    await store.AppendTranscriptEntryAsync(state.TaskId, new AgentTranscriptEntry(2, "assistant", null, "Now drafting a patch.", DateTime.UtcNow));

    var loaded = await store.LoadTranscriptAsync(state.TaskId);
    Equal(3, loaded.Count, "transcript should persist all appended entries");

    var workspace = temp.PathFor("workspace");
    Directory.CreateDirectory(workspace);
    var ragStore = new SqliteRagStore(settings);
    var rag = new RagQueryService(ragStore, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());
    var retrieval = new AgentRetrievalService(rag, ragStore);
    var activation = new WorkspaceActivationService(new WorkspaceManifestService(), new FileWorkspaceProfileStore(settings));
    var builder = new AgentContextBuilder(new AgentWorkspaceTools(), retrieval, new WorkspaceMemoryStore(new MemoryStore(settings), settings), activation, store, settings);

    var pack = await builder.BuildAsync(state, new AgentWorkspaceOptions(workspace));
    Equal(3, pack.TranscriptHistory.Count, "context pack should replay the full persisted transcript when it fits the budget");
    Equal("step-1", pack.TranscriptHistory[0].Locator, "transcript history should stay in chronological order");
    True(pack.TranscriptHistory[1].Content.Contains("README contents", StringComparison.Ordinal), "tool transcript entries should carry their result content");
    Equal("step-2", pack.TranscriptHistory[2].Locator, "the most recent step should be the last transcript entry");
    }

    private const string ListFilesToolResponse = """
        {
          "thought_summary": "Listing files.",
          "current_step": "Inspect workspace.",
          "next_action": { "type": "tool", "tool_name": "list_files", "arguments": {}, "requires_approval": false, "risk_level": "low" },
          "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": [] },
          "user_message": "Listed files."
        }
        """;

    public static async Task AgentRunAsyncLoopsUntilFinalAnswer()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    File.WriteAllText(Path.Combine(root, "README.md"), "agent docs");
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new FileAgentTaskStateStore(settings);
    var tools = new AgentWorkspaceTools();
    var llm = new FakeSequencedAgentLlm([ListFilesToolResponse, ListFilesToolResponse]);
    var service = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), llm, settings: settings);
    var options = new AgentWorkspaceOptions(root, ModelId: "fake-sequenced-agent");

    var state = await service.CreateTaskAsync("Review docs", options);
    var stepStatuses = new List<AgentTaskStatus>();
    var result = await service.RunAsync(state.TaskId, options, onStep: r => stepStatuses.Add(r.State.Status));

    Equal(AgentTaskStatus.Complete, result.State.Status, "the loop should run until the model returns a final answer");
    Equal(3, stepStatuses.Count, "the loop should have run two auto-allowed tool steps plus the final step");
    Equal(3, result.State.StepCount, "step count should reflect every step the loop ran");
    }

    public static async Task AgentRunAsyncStopsAtMaxAutoSteps()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    File.WriteAllText(Path.Combine(root, "README.md"), "agent docs");
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    settings.Settings.Agent.MaxAutoSteps = 1;
    var store = new FileAgentTaskStateStore(settings);
    var tools = new AgentWorkspaceTools();
    var llm = new FakeSequencedAgentLlm([ListFilesToolResponse, ListFilesToolResponse, ListFilesToolResponse]);
    var service = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), llm, settings: settings);
    var options = new AgentWorkspaceOptions(root, ModelId: "fake-sequenced-agent");

    var state = await service.CreateTaskAsync("Review docs", options);
    var result = await service.RunAsync(state.TaskId, options);

    Equal(AgentTaskStatus.WaitingForUser, result.State.Status, "hitting the step cap must hand the task back to the user, not leave it looking silently active");
    Equal(1, result.State.StepCount, "the loop should have executed exactly the capped number of steps");
    var transcript = await store.LoadTranscriptAsync(state.TaskId);
    True(transcript.Any(e => e.Content.Contains("step budget exhausted", StringComparison.Ordinal)), "the budget-exhausted note should be visible in the transcript");
    }

    public static async Task AgentRunAsyncStopsWhenApprovalIsRequired()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    File.WriteAllText(Path.Combine(root, "README.md"), "agent docs");
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new FileAgentTaskStateStore(settings);
    var tools = new AgentWorkspaceTools();
    var service = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), new FakeAgentLlm(), settings: settings);
    var options = new AgentWorkspaceOptions(root, ModelId: "fake-agent");

    var state = await service.CreateTaskAsync("Review docs", options);
    var result = await service.RunAsync(state.TaskId, options);

    Equal(AgentTaskStatus.WaitingForUser, result.State.Status, "the loop should pause, not auto-approve, when a gated action needs review");
    Equal(1, result.State.StepCount, "the loop should stop after the first step that required approval");
    }

    public static async Task AgentConsumesNativeToolCallsWithoutJsonTextParsing()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    File.WriteAllText(Path.Combine(root, "README.md"), "agent docs");
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new FileAgentTaskStateStore(settings);
    var tools = new AgentWorkspaceTools();
    var llm = new FakeToolCallingAgentLlm("read_file", """{"relative_path":"README.md"}""");
    var service = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), llm, settings: settings);
    var options = new AgentWorkspaceOptions(root, ModelId: "fake-tool-calling-agent");

    var state = await service.CreateTaskAsync("Review docs", options);
    var step = await service.RunStepAsync(state.TaskId, options);

    Equal("read_file", step.PlannerResponse.NextAction.ToolName, "a native tool call should populate next_action directly, bypassing JSON text parsing");
    Equal(AgentTaskStatus.Running, step.State.Status, "read_file is read-only and should execute immediately without approval");
    True(step.State.ToolResults.Any(t => t.Tool == "read_file" && t.ResultSummary.Contains("agent docs", StringComparison.Ordinal)),
        "the tool call's arguments should have been parsed correctly and the tool actually executed");
    }

    public static async Task AgentCapturesCommandFailureLessonAndInjectsItNextStep()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var taskStore = new FileAgentTaskStateStore(settings);
    var lessonStore = new SqliteLessonStore(settings);
    var tools = new AgentWorkspaceTools();

    var runCommandResponse = """
        {
          "thought_summary": "Testing.",
          "current_step": "Run tests.",
          "next_action": { "type": "tool", "tool_name": "run_command", "arguments": { "command": "dotnet test" }, "requires_approval": true, "risk_level": "medium" },
          "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": [] },
          "user_message": "Testing."
        }
        """;
    var llm = new FakeSequencedAgentLlm([runCommandResponse]);
    var manifest = new WorkspaceManifestService();
    await manifest.SaveAsync(root, new WorkspaceManifest { AllowedCommands = [new WorkspaceCommandRecipe("dotnet test", "Test.", AgentRiskLevel.Low)] });
    var serviceWithManifest = new AgentService(taskStore, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), llm, manifests: manifest, settings: settings, lessons: lessonStore);
    var options = new AgentWorkspaceOptions(root, ModelId: "fake-sequenced-agent");

    var state = await serviceWithManifest.CreateTaskAsync("Run tests", options);
    var step = await serviceWithManifest.RunStepAsync(state.TaskId, options);
    Equal(AgentTaskStatus.WaitingForUser, step.State.Status, "run_command should still require approval the first time");

    await serviceWithManifest.AppendApprovalAsync(state.TaskId, "run_command", approved: true, options);

    var lessons = await lessonStore.ListRelevantAsync(root, includeRetired: false, 50);
    True(lessons.Any(l => l.Kind == AgentLessonKind.Command), "a command lesson should be captured after the approved run");

    // Verify the context builder actually surfaces it (injection wiring).
    var ragStore = new SqliteRagStore(settings);
    var rag = new RagQueryService(ragStore, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());
    var retrieval = new AgentRetrievalService(rag, ragStore);
    var activation = new WorkspaceActivationService(manifest, new FileWorkspaceProfileStore(settings));
    var builder = new AgentContextBuilder(tools, retrieval, new WorkspaceMemoryStore(new MemoryStore(settings), settings), activation, taskStore, settings, lessonStore);
    var pack = await builder.BuildAsync(step.State, options);
    True(pack.Lessons.Count > 0, "the context pack should include the captured lesson on the next build");
    }

    public static async Task AgentCapturesApprovalRejectionLesson()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    File.WriteAllText(Path.Combine(root, "README.md"), "docs");
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var taskStore = new FileAgentTaskStateStore(settings);
    var lessonStore = new SqliteLessonStore(settings);
    var tools = new AgentWorkspaceTools();
    var service = new AgentService(taskStore, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), new FakeAgentLlm(), settings: settings, lessons: lessonStore);
    var options = new AgentWorkspaceOptions(root, ModelId: "fake-agent");

    var state = await service.CreateTaskAsync("Review docs", options);
    await service.RunStepAsync(state.TaskId, options);
    await service.AppendApprovalAsync(state.TaskId, "draft_patch", approved: false, options);

    var lessons = await lessonStore.ListAllAsync(includeRetired: false);
    True(lessons.Any(l => l.Kind == AgentLessonKind.Approval && l.Outcome == AgentLessonOutcome.UserRejected),
        "a rejected approval should be captured as an approval lesson");
    }

    public static async Task AgentCapturesStatedLessonMarkerAndStripsItFromUserMessage()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var taskStore = new FileAgentTaskStateStore(settings);
    var lessonStore = new SqliteLessonStore(settings);
    var tools = new AgentWorkspaceTools();
    var statedResponse = """
        {
          "thought_summary": "Noted a quirk.",
          "current_step": "Continue.",
          "next_action": { "type": "final", "requires_approval": false, "risk_level": "none" },
          "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": [] },
          "user_message": "Done. [LESSON: This workspace uses a non-standard build script.]"
        }
        """;
    var llm = new FakeSequencedAgentLlm([statedResponse]);
    var service = new AgentService(taskStore, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), llm, settings: settings, lessons: lessonStore);
    var options = new AgentWorkspaceOptions(root, ModelId: "fake-sequenced-agent");

    var state = await service.CreateTaskAsync("Investigate", options);
    var step = await service.RunStepAsync(state.TaskId, options);

    False(step.PlannerResponse.UserMessage.Contains("[LESSON:", StringComparison.Ordinal), "the marker should be stripped from the user-visible message");
    True(step.PlannerResponse.UserMessage.Contains("Done.", StringComparison.Ordinal), "surrounding text should be preserved");

    var lessons = await lessonStore.ListAllAsync(includeRetired: false);
    True(lessons.Any(l => l.Kind == AgentLessonKind.Stated && l.Claim.Contains("non-standard build script", StringComparison.Ordinal)),
        "the stated lesson should be recorded");
    }

    private const string AskUserResponse = """
        {
          "thought_summary": "Need clarification.",
          "current_step": "Ask user.",
          "next_action": { "type": "ask_user", "requires_approval": false, "risk_level": "none" },
          "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": [] },
          "user_message": "Which file should I look at?"
        }
        """;

    private const string FinalResponse = """
        {
          "thought_summary": "Done.",
          "current_step": "Done.",
          "next_action": { "type": "final", "requires_approval": false, "risk_level": "none" },
          "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": [] },
          "user_message": "Finished."
        }
        """;

    public static async Task AgentUserReplyRecordsTranscriptEntryAndResumesTheTask()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new FileAgentTaskStateStore(settings);
    var tools = new AgentWorkspaceTools();
    var llm = new FakeSequencedAgentLlm([AskUserResponse]);
    var service = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), llm, settings: settings);
    var options = new AgentWorkspaceOptions(root, ModelId: "fake-sequenced-agent");

    var state = await service.CreateTaskAsync("Investigate", options);
    var step = await service.RunStepAsync(state.TaskId, options);
    Equal(AgentTaskStatus.WaitingForUser, step.State.Status, "an ask_user action should pause the task");

    await service.AppendUserReplyAsync(state.TaskId, "Look at README.md");
    var afterReply = await store.LoadAsync(state.TaskId);
    Equal(AgentTaskStatus.Running, afterReply!.Status, "a reply should resume the task");

    var transcript = await store.LoadTranscriptAsync(state.TaskId);
    True(transcript.Any(e => e.Role == "user" && e.Content == "Look at README.md"), "the reply should be recorded in the transcript");

    await ThrowsAsync<InvalidOperationException>(() => service.AppendUserReplyAsync(state.TaskId, "second reply"));
    }

    public static async Task AgentUserReplyIsRefusedWhenAToolApprovalIsPending()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    File.WriteAllText(Path.Combine(root, "README.md"), "docs");
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new FileAgentTaskStateStore(settings);
    var tools = new AgentWorkspaceTools();
    var service = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), new FakeAgentLlm(), settings: settings);
    var options = new AgentWorkspaceOptions(root, ModelId: "fake-agent");

    var state = await service.CreateTaskAsync("Review docs", options);
    var step = await service.RunStepAsync(state.TaskId, options);
    Equal(AgentTaskStatus.WaitingForUser, step.State.Status, "a gated tool call should pause the task with a pending approval");

    await ThrowsAsync<InvalidOperationException>(() => service.AppendUserReplyAsync(state.TaskId, "approve it"));
    }

    public static async Task AgentApprovedToolExecutionReachesTheTranscript()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    File.WriteAllText(Path.Combine(root, "README.md"), "docs");
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new FileAgentTaskStateStore(settings);
    var tools = new AgentWorkspaceTools();
    var service = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), new FakeAgentLlm(), settings: settings);
    var options = new AgentWorkspaceOptions(root, ModelId: "fake-agent");

    var state = await service.CreateTaskAsync("Review docs", options);
    await service.RunStepAsync(state.TaskId, options);
    await service.AppendApprovalAsync(state.TaskId, "draft_patch", approved: true, options);

    var transcript = await store.LoadTranscriptAsync(state.TaskId);
    True(transcript.Any(e => e.Role == "tool" && e.ToolName == "draft_patch"),
        "the approved tool's result should reach the transcript, not just ToolResults' last-five window");
    }

    private const string CreateFileToolResponse = """
        {
          "thought_summary": "Adding a new file.",
          "current_step": "Wait for approval before any write.",
          "next_action": {
            "type": "tool",
            "tool_name": "create_file",
            "arguments": { "relative_path": "notes.md", "content": "brand new" },
            "requires_approval": true,
            "risk_level": "medium"
          },
          "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": [] },
          "user_message": "Ready to create notes.md."
        }
        """;

    // r6 01-first-five-minutes.md 1.8: edit_file/create_file/apply_draft_patch
    // approved directly through AppendApprovalAsync's PendingToolAction path
    // (as opposed to the manual draft_patch review queue) should still end up
    // as a revertible entry in DraftPatches, so the same Revert affordance
    // covers both approval paths.
    public static async Task AgentApprovedCreateFileToolRecordsARevertibleDraftPatch()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new FileAgentTaskStateStore(settings);
    var tools = new AgentWorkspaceTools();
    var llm = new FakeSequencedAgentLlm([CreateFileToolResponse]);
    var service = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), llm, settings: settings, workspaceTools: tools);
    var options = new AgentWorkspaceOptions(root, ModelId: "fake-sequenced-agent");

    var state = await service.CreateTaskAsync("Add notes", options);
    await service.RunStepAsync(state.TaskId, options);
    await service.AppendApprovalAsync(state.TaskId, "create_file", approved: true, options);

    var reloaded = await store.LoadAsync(state.TaskId);
    var recorded = reloaded!.DraftPatches.Single(p => p.RelativePath == "notes.md");
    Equal(AgentDraftPatchStatus.Applied, recorded.Status, "an approved create_file should be recorded as an applied, revertible patch");
    False(recorded.PreImageExisted, "notes.md did not exist before create_file ran");
    Equal("brand new", await File.ReadAllTextAsync(Path.Combine(root, "notes.md")), "create_file should have written the approved content");

    var patchReview = new AgentPatchReviewService(tools, store, service);
    var error = await patchReview.RevertAsync(reloaded, recorded, options);
    Equal(string.Empty, error, "reverting an approved create_file's recorded patch should succeed");
    False(File.Exists(Path.Combine(root, "notes.md")), "reverting a create_file-originated patch should delete the file it created");
    }

    public static async Task AgentFailsAfterThreeConsecutiveUnparseableResponses()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new FileAgentTaskStateStore(settings);
    var tools = new AgentWorkspaceTools();
    var llm = new FakeSequencedAgentLlm(["not json", "still not json", "nope"]);
    var service = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), llm, settings: settings);
    var options = new AgentWorkspaceOptions(root, ModelId: "fake-sequenced-agent");

    var state = await service.CreateTaskAsync("Investigate", options);
    AgentStepResult step = null!;
    for (var i = 0; i < 3; i++)
        step = await service.RunStepAsync(state.TaskId, options);

    Equal(AgentTaskStatus.Failed, step.State.Status, "three consecutive unparseable responses should fail the task");
    True(step.State.Decisions.Any(d => d.Decision == "Task failed"), "the failure should be recorded on the task state");
    var transcript = await store.LoadTranscriptAsync(state.TaskId);
    Equal(3, transcript.Count(e => e.Role == "assistant"), "every bad step should still be recorded in the transcript, not lost");
    }

    public static async Task AgentConsecutiveStepErrorCounterResetsOnAValidResponse()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    File.WriteAllText(Path.Combine(root, "README.md"), "docs");
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new FileAgentTaskStateStore(settings);
    var tools = new AgentWorkspaceTools();
    var llm = new FakeSequencedAgentLlm(["garbage", "garbage", ListFilesToolResponse, "garbage", "garbage"]);
    var service = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), llm, settings: settings);
    var options = new AgentWorkspaceOptions(root, ModelId: "fake-sequenced-agent");

    var state = await service.CreateTaskAsync("Investigate", options);
    AgentStepResult step = null!;
    for (var i = 0; i < 5; i++)
        step = await service.RunStepAsync(state.TaskId, options);

    True(step.State.Status != AgentTaskStatus.Failed,
        "the error counter should reset after the valid response in the middle, so two more spread-apart errors should not fail the task");
    }

    public static async Task AgentNativeToolCallPreservesProseAndListsDroppedCalls()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    File.WriteAllText(Path.Combine(root, "README.md"), "agent docs");
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new FileAgentTaskStateStore(settings);
    var tools = new AgentWorkspaceTools();
    var llm = new FakeMultiToolCallingAgentLlm();
    var service = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), llm, settings: settings);
    var options = new AgentWorkspaceOptions(root, ModelId: "fake-multi-tool-calling-agent");

    var state = await service.CreateTaskAsync("Review docs", options);
    var step = await service.RunStepAsync(state.TaskId, options);

    True(step.PlannerResponse.ThoughtSummary.Contains("list files", StringComparison.OrdinalIgnoreCase),
        "the model's own prose should become the thought summary instead of a synthetic 'Calling X.' placeholder");
    True(step.PlannerResponse.ThoughtSummary.Contains("read_file", StringComparison.Ordinal),
        "the dropped second tool call should be noted so the model sees next step that it did not run");
    Equal("list_files", step.PlannerResponse.NextAction.ToolName, "only the first tool call should execute");
    }

    public static async Task AgentCommandLessonSignatureExcludesOutcomeSoContradictionCanReachIt()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var taskStore = new FileAgentTaskStateStore(settings);
    var lessonStore = new SqliteLessonStore(settings);
    var tools = new AgentWorkspaceTools();

    var runCommandResponse = """
        {
          "thought_summary": "Testing.",
          "current_step": "Run tests.",
          "next_action": { "type": "tool", "tool_name": "run_command", "arguments": { "command": "dotnet test" }, "requires_approval": true, "risk_level": "medium" },
          "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": [] },
          "user_message": "Testing."
        }
        """;
    var llm = new FakeSequencedAgentLlm([runCommandResponse]);
    var manifest = new WorkspaceManifestService();
    await manifest.SaveAsync(root, new WorkspaceManifest { AllowedCommands = [new WorkspaceCommandRecipe("dotnet test", "Test.", AgentRiskLevel.Low)] });
    var service = new AgentService(taskStore, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), llm, manifests: manifest, settings: settings, lessons: lessonStore);
    var options = new AgentWorkspaceOptions(root, ModelId: "fake-sequenced-agent");

    var state = await service.CreateTaskAsync("Run tests", options);
    await service.RunStepAsync(state.TaskId, options);
    await service.AppendApprovalAsync(state.TaskId, "run_command", approved: true, options);

    var lessons = await lessonStore.ListAllAsync(includeRetired: false);
    var lesson = lessons.Single(l => l.Kind == AgentLessonKind.Command);
    Equal("command:dotnet test", lesson.Signature, "the command lesson signature must not bake in the outcome, or contradiction can never reach it");
    }

    public static async Task AgentApprovingAPreviouslyRejectedToolWeakensTheRejectionLesson()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    File.WriteAllText(Path.Combine(root, "README.md"), "docs");
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var taskStore = new FileAgentTaskStateStore(settings);
    var lessonStore = new SqliteLessonStore(settings);
    var tools = new AgentWorkspaceTools();
    var service = new AgentService(taskStore, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), new FakeAgentLlm(), settings: settings, lessons: lessonStore);
    var options = new AgentWorkspaceOptions(root, ModelId: "fake-agent");

    // Reject the same gated action across several tasks first so the
    // rejection lesson's confidence has actually built up (docs/review/02-lessons-v2.md L2);
    // a single piece of evidence starts too close to the retirement floor
    // for one contradiction to show a meaningful drop instead of just an
    // immediate reset to the same initial confidence.
    string? lessonId = null;
    double confidenceAfterReject = 0;
    for (var i = 0; i < 4; i++)
    {
        var rejectState = await service.CreateTaskAsync($"Review docs, round {i}", options);
        await service.RunStepAsync(rejectState.TaskId, options);
        await service.AppendApprovalAsync(rejectState.TaskId, "draft_patch", approved: false, options);

        var current = (await lessonStore.ListAllAsync(includeRetired: true)).Single(l => l.Kind == AgentLessonKind.Approval);
        lessonId = current.Id;
        confidenceAfterReject = current.Confidence;
    }
    True(confidenceAfterReject > 0.5, "several confirming rejections should build meaningful confidence before the counter-evidence test");

    var approveState = await service.CreateTaskAsync("Review docs, now approved", options);
    await service.RunStepAsync(approveState.TaskId, options);
    await service.AppendApprovalAsync(approveState.TaskId, "draft_patch", approved: true, options);

    var afterApprove = await lessonStore.GetByIdAsync(lessonId!);
    True(afterApprove!.Confidence < confidenceAfterReject, "approving the same tool afterwards should counter (weaken) the prior rejection lesson");
    }

    public static async Task AgentToolExecutorPopulatesStructuredExitCodeForRunCommand()
    {
    using var temp = new TempDir();
    var workspace = temp.PathFor("workspace");
    Directory.CreateDirectory(workspace);
    await File.WriteAllTextAsync(Path.Combine(workspace, "sample.csproj"), """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
        </Project>
        """);
    await File.WriteAllTextAsync(Path.Combine(workspace, "Program.cs"), "System.Console.WriteLine(\"hi\");");

    var executor = new AgentToolExecutor(new AgentWorkspaceTools());
    var options = new AgentWorkspaceOptions(workspace);
    var result = await executor.ExecuteAsync("run_command", new Dictionary<string, object?> { ["command"] = "dotnet build" }, options);

    Equal(0, result.ExitCode, "a successful build should report exit code 0 on the structured field, not just embedded in the summary text");
    False(result.TimedOut, "a normal completion should never be flagged as timed out");
    }

    public static async Task AgentRecordsTaskCompletionLessonOnlyWhenPriorFailureOccurred()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    File.WriteAllText(Path.Combine(root, "README.md"), "docs");
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var taskStore = new FileAgentTaskStateStore(settings);
    var lessonStore = new SqliteLessonStore(settings);
    var tools = new AgentWorkspaceTools();
    var options = new AgentWorkspaceOptions(root, ModelId: "fake-sequenced-agent");

    var uneventfulLlm = new FakeSequencedAgentLlm([FinalResponse]);
    var uneventfulService = new AgentService(taskStore, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), uneventfulLlm, settings: settings, lessons: lessonStore);
    var uneventfulState = await uneventfulService.CreateTaskAsync("Say hello", options);
    await uneventfulService.RunStepAsync(uneventfulState.TaskId, options);

    var afterUneventful = await lessonStore.ListAllAsync(includeRetired: false);
    False(afterUneventful.Any(l => l.Kind == AgentLessonKind.Task), "an uneventful success should not record a task lesson - it teaches nothing");

    var eventfulLlm = new FakeSequencedAgentLlm(["garbage", FinalResponse]);
    var eventfulService = new AgentService(taskStore, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), eventfulLlm, settings: settings, lessons: lessonStore);
    var eventfulState = await eventfulService.CreateTaskAsync("Say hello with trouble", options);
    await eventfulService.RunStepAsync(eventfulState.TaskId, options);
    await eventfulService.RunStepAsync(eventfulState.TaskId, options);

    var afterEventful = await lessonStore.ListAllAsync(includeRetired: false);
    True(afterEventful.Any(l => l.Kind == AgentLessonKind.Task && l.Outcome == AgentLessonOutcome.Worked),
        "a completion that recovered from a prior failure should record a Worked task lesson");
    }

    public static async Task AgentRecordsTaskFailureLessonWithBlockerWhenTaskFails()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var taskStore = new FileAgentTaskStateStore(settings);
    var lessonStore = new SqliteLessonStore(settings);
    var tools = new AgentWorkspaceTools();
    var llm = new FakeSequencedAgentLlm(["not json", "still not json", "nope"]);
    var service = new AgentService(taskStore, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), llm, settings: settings, lessons: lessonStore);
    var options = new AgentWorkspaceOptions(root, ModelId: "fake-sequenced-agent");

    var state = await service.CreateTaskAsync("Investigate deeply", options);
    for (var i = 0; i < 3; i++)
        await service.RunStepAsync(state.TaskId, options);

    var lessons = await lessonStore.ListAllAsync(includeRetired: false);
    True(lessons.Any(l => l.Kind == AgentLessonKind.Task && l.Outcome == AgentLessonOutcome.Failed
        && l.Claim.Contains("unparseable", StringComparison.OrdinalIgnoreCase)),
        "a task that failed via the parse-error path should record a Failed task lesson naming the blocker");
    }

    // r6 03-platform-cleanup.md 3.3: a genuinely new lesson should be
    // tracked on the task that created it (for the review strip), but
    // reinforcing that same lesson from a later task must not re-flag it.
    public static async Task AgentTracksNewLessonIdsOnlyForGenuinelyNewLessons()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var taskStore = new FileAgentTaskStateStore(settings);
    var lessonStore = new SqliteLessonStore(settings);
    var tools = new AgentWorkspaceTools();
    var options = new AgentWorkspaceOptions(root, ModelId: "fake-sequenced-agent");

    var firstLlm = new FakeSequencedAgentLlm(["not json", "still not json", "nope"]);
    var firstService = new AgentService(taskStore, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), firstLlm, settings: settings, lessons: lessonStore);
    var firstState = await firstService.CreateTaskAsync("Investigate deeply", options);
    AgentStepResult firstStep = null!;
    for (var i = 0; i < 3; i++)
        firstStep = await firstService.RunStepAsync(firstState.TaskId, options);

    Equal(1, firstStep.State.NewLessonIds.Count, "the task that actually creates a lesson should track exactly one new lesson id");
    var createdId = firstStep.State.NewLessonIds[0];
    var created = await lessonStore.GetByIdAsync(createdId);
    True(created is not null, "the tracked id should resolve to a real lesson");
    Equal(1, created!.EvidenceCount, "a freshly created lesson should have evidence count 1");

    var secondLlm = new FakeSequencedAgentLlm(["not json", "still not json", "nope"]);
    var secondService = new AgentService(taskStore, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), secondLlm, settings: settings, lessons: lessonStore);
    var secondState = await secondService.CreateTaskAsync("Investigate deeply", options);
    AgentStepResult secondStep = null!;
    for (var i = 0; i < 3; i++)
        secondStep = await secondService.RunStepAsync(secondState.TaskId, options);

    Equal(0, secondStep.State.NewLessonIds.Count, "reinforcing the same lesson signature from a second task should not flag it as new again");
    var reinforced = await lessonStore.GetByIdAsync(createdId);
    Equal(2, reinforced!.EvidenceCount, "the same lesson should still get reinforced by the second task's evidence");
    }

    public static async Task AgentConfirmsInjectedLessonsOnSuccessfulTaskCompletion()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    File.WriteAllText(Path.Combine(root, "README.md"), "docs");
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var taskStore = new FileAgentTaskStateStore(settings);
    var lessonStore = new SqliteLessonStore(settings);
    var tools = new AgentWorkspaceTools();
    var ragStore = new SqliteRagStore(settings);
    var rag = new RagQueryService(ragStore, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());
    var retrieval = new AgentRetrievalService(rag, ragStore);
    var activation = new WorkspaceActivationService(new WorkspaceManifestService(), new FileWorkspaceProfileStore(settings));
    var contextBuilder = new AgentContextBuilder(tools, retrieval, new WorkspaceMemoryStore(new MemoryStore(settings), settings), activation, taskStore, settings, lessonStore);
    var options = new AgentWorkspaceOptions(root, ModelId: "fake-sequenced-agent");

    var seeded = await lessonStore.RecordEvidenceAsync(new AgentLessonEvidence(
        AgentLessonScope.Global, "", AgentLessonKind.Stated, "stated:preseed",
        "Prefer small, focused commits in this environment.", "", AgentLessonOutcome.Observation));

    var llm = new FakeSequencedAgentLlm([FinalResponse]);
    var service = new AgentService(taskStore, contextBuilder, new AgentSafetyGate(), new AgentToolExecutor(tools), llm, settings: settings, lessons: lessonStore);
    var state = await service.CreateTaskAsync("Investigate readme", options);
    await service.RunStepAsync(state.TaskId, options);

    var confirmed = await lessonStore.GetByIdAsync(seeded.Id);
    True(confirmed!.EvidenceCount > seeded.EvidenceCount, "a lesson injected into a task that completed successfully should have its evidence confirmed");
    }

    public static async Task AgentContextBuilderRanksLessonsByGoalRelevanceOverRawConfidence()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var taskStore = new FileAgentTaskStateStore(settings);
    await taskStore.InitializeAsync();
    var lessonStore = new SqliteLessonStore(settings);
    var tools = new AgentWorkspaceTools();
    var ragStore = new SqliteRagStore(settings);
    var rag = new RagQueryService(ragStore, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());
    var retrieval = new AgentRetrievalService(rag, ragStore);
    var activation = new WorkspaceActivationService(new WorkspaceManifestService(), new FileWorkspaceProfileStore(settings));
    var builder = new AgentContextBuilder(tools, retrieval, new WorkspaceMemoryStore(new MemoryStore(settings), settings), activation, taskStore, settings, lessonStore);

    var unrelated = await lessonStore.RecordEvidenceAsync(new AgentLessonEvidence(
        AgentLessonScope.Global, "", AgentLessonKind.Stated, "stated:unrelated",
        "The user prefers dark mode in the desktop UI.", "", AgentLessonOutcome.Observation));
    for (var i = 0; i < 6; i++)
        unrelated = await lessonStore.RecordEvidenceAsync(new AgentLessonEvidence(
            AgentLessonScope.Global, "", AgentLessonKind.Stated, "stated:unrelated",
            "The user prefers dark mode in the desktop UI.", "", AgentLessonOutcome.Observation));

    var relevant = await lessonStore.RecordEvidenceAsync(new AgentLessonEvidence(
        AgentLessonScope.Global, "", AgentLessonKind.Command, "command:npm audit",
        "'npm audit' reports vulnerabilities in this workspace.", "Review the audit output before continuing.", AgentLessonOutcome.Failed));

    True(unrelated.Confidence > relevant.Confidence, "sanity check: the unrelated lesson should have higher raw confidence after several reinforcements");

    var state = new AgentTaskState { TaskId = "relevance-task", Goal = "Run npm audit and fix vulnerabilities", Status = AgentTaskStatus.Running };
    await taskStore.SaveAsync(state);
    var options = new AgentWorkspaceOptions(root);

    var pack = await builder.BuildAsync(state, options);

    True(pack.Lessons.Count >= 2, "both lessons should fit comfortably within the lessons token budget");
    Equal(relevant.Id, pack.Lessons[0].Locator, "the lesson sharing terms with the current goal should rank ahead of an unrelated, higher-confidence lesson");
    }

    // -- r15 sub-task orchestration --

    public static async Task AgentSubTaskFieldsAreJsonAdditiveAndPreR15StateLoadsUnchanged()
    {
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new FileAgentTaskStateStore(settings);

    var preR15Json = """
        {
          "task_id": "legacy-task",
          "goal": "Old task from before r15",
          "status": "waiting_for_user",
          "active_step": "Wait",
          "constraints": [],
          "completed_steps": [],
          "pending_steps": [],
          "decisions": [],
          "tool_results": [],
          "approval_history": [],
          "draft_patches": [],
          "summary": "legacy"
        }
        """;
    var dir = store.GetTaskDirectory("legacy-task");
    Directory.CreateDirectory(dir);
    await File.WriteAllTextAsync(Path.Combine(dir, "task_state.json"), preR15Json);

    var loaded = await store.LoadAsync("legacy-task");
    Equal("Old task from before r15", loaded?.Goal, "a pre-r15 task state should still load");
    True(loaded?.ParentTaskId is null, "a pre-r15 task should default to no parent");
    True(loaded?.SubTaskPlan.Count == 0, "a pre-r15 task should default to an empty sub-task plan");
    Equal(0, loaded!.OrchestrationStepsUsed, "a pre-r15 task should default OrchestrationStepsUsed to 0");

    var child = new AgentTaskState
    {
        TaskId = "child-task",
        Goal = "child goal",
        ParentTaskId = "legacy-task",
        SubTaskPlan = [new AgentSubTaskSpec { Goal = "g", ProfileName = "general", SuccessCriteria = "c" }]
    };
    await store.SaveAsync(child);
    var reloadedChild = await store.LoadAsync("child-task");
    Equal("legacy-task", reloadedChild?.ParentTaskId, "ParentTaskId should round trip");
    Equal(1, reloadedChild?.SubTaskPlan.Count, "SubTaskPlan should round trip");
    }

    public static Task AgentSpecialistProfilesAreNonEmptyAndUnknownNameFallsBackToGeneral()
    {
    foreach (var name in AgentSpecialistProfiles.Names)
        True(AgentSpecialistProfiles.Resolve(name).Count > 0, $"profile '{name}' should have at least one focus constraint");

    Equal(AgentSpecialistProfiles.Resolve("general"), AgentSpecialistProfiles.Resolve("totally-unknown-profile"), "an unknown profile name should fall back to general's constraints");
    True(AgentSpecialistProfiles.IsKnown("security"), "security should be a known profile");
    False(AgentSpecialistProfiles.IsKnown("totally-unknown-profile"), "an unrecognized name should not be known");
    return Task.CompletedTask;
    }

    private const string PlanTwoSubtasksResponse = """
        {
          "thought_summary": "This goal spans multiple domains.",
          "current_step": "Propose sub-tasks.",
          "next_action": {
            "type": "tool",
            "tool_name": "plan_subtasks",
            "arguments": { "subtasks": [
              { "goal": "Fix the bug", "profile": "correctness", "success_criteria": "the bug is fixed" },
              { "goal": "Add a regression test", "profile": "tests", "success_criteria": "a test covers the bug" }
            ] },
            "requires_approval": true,
            "risk_level": "medium"
          },
          "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": [] },
          "user_message": "Proposing a sub-task plan."
        }
        """;

    private const string PlanThreeSubtasksResponse = """
        {
          "thought_summary": "This goal spans multiple domains.",
          "current_step": "Propose sub-tasks.",
          "next_action": {
            "type": "tool",
            "tool_name": "plan_subtasks",
            "arguments": { "subtasks": [
              { "goal": "Fix the bug", "profile": "correctness", "success_criteria": "the bug is fixed" },
              { "goal": "Add a regression test", "profile": "tests", "success_criteria": "a test covers the bug" },
              { "goal": "Update the docs", "profile": "docs", "success_criteria": "docs mention the fix" }
            ] },
            "requires_approval": true,
            "risk_level": "medium"
          },
          "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": [] },
          "user_message": "Proposing a sub-task plan."
        }
        """;

    private const string OneSubtaskResponse = """
        {
          "thought_summary": "Just one.",
          "current_step": "Propose sub-tasks.",
          "next_action": {
            "type": "tool",
            "tool_name": "plan_subtasks",
            "arguments": { "subtasks": [ { "goal": "Only one", "profile": "general", "success_criteria": "done" } ] },
            "requires_approval": true,
            "risk_level": "medium"
          },
          "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": [] },
          "user_message": "Proposing a sub-task plan."
        }
        """;

    private const string PlanSubtasksFromChildResponse = """
        {
          "thought_summary": "Trying to delegate further.",
          "current_step": "Propose sub-tasks.",
          "next_action": {
            "type": "tool",
            "tool_name": "plan_subtasks",
            "arguments": { "subtasks": [
              { "goal": "a", "profile": "general", "success_criteria": "a" },
              { "goal": "b", "profile": "general", "success_criteria": "b" }
            ] },
            "requires_approval": true,
            "risk_level": "medium"
          },
          "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": [] },
          "user_message": "Trying to delegate."
        }
        """;

    private static string FinalResponseWithMessage(string message) => $$"""
        {
          "thought_summary": "Done.",
          "current_step": "Done.",
          "next_action": { "type": "final", "requires_approval": false, "risk_level": "none" },
          "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": [] },
          "user_message": "{{message}}"
        }
        """;

    public static async Task AgentPlanSubtasksAlwaysRequiresApprovalOnRootAndIsBlockedByDepthOnAChild()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new FileAgentTaskStateStore(settings);
    var tools = new AgentWorkspaceTools();

    var gate = new AgentSafetyGate();
    var decision = gate.Evaluate("plan_subtasks", wouldMutate: false);
    Equal(AgentToolDisposition.RequiresApproval, decision.Disposition, "plan_subtasks should always require approval");
    Equal(AgentRiskLevel.Medium, decision.RiskLevel, "plan_subtasks should be Medium risk");

    var llm = new FakeSequencedAgentLlm([PlanSubtasksFromChildResponse]);
    var service = new AgentService(store, new FakeAgentContextBuilder(), gate, new AgentToolExecutor(tools), llm, settings: settings);
    var options = new AgentWorkspaceOptions(root, ModelId: "fake-sequenced-agent");

    var child = new AgentTaskState { Goal = "child goal", ParentTaskId = "some-parent-id", Status = AgentTaskStatus.New };
    await store.SaveAsync(child);

    var step = await service.RunStepAsync(child.TaskId, options);
    Equal(AgentTaskStatus.Blocked, step.State.Status, "a child requesting plan_subtasks should be blocked, not paused for approval");
    True(step.State.SubTaskPlan.Count == 0, "a blocked plan_subtasks request should never materialize a plan on the child");
    var gateRow = step.State.ToolResults.Last(t => t.Tool == "safety_gate");
    Equal("Blocked", AgentToolExecutor.Arg(gateRow.Arguments, "disposition"), "the safety-gate row should record the depth block");
    }

    private static async Task<(FileAgentTaskStateStore Store, AgentService Service, AgentWorkspaceOptions Options, AgentTaskState State)> CreateApprovedTwoSubtaskPlanAsync(
        TempDir temp, ILlmService llm, string goal = "Fix the bug and add coverage")
    {
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new FileAgentTaskStateStore(settings);
    var tools = new AgentWorkspaceTools();
    var service = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), llm, settings: settings);
    var options = new AgentWorkspaceOptions(root, ModelId: "fake-sequenced-agent");

    var state = await service.CreateTaskAsync(goal, options);
    var proposed = await service.RunStepAsync(state.TaskId, options);
    Equal(AgentTaskStatus.WaitingForUser, proposed.State.Status, "plan_subtasks should always pause for approval");
    await service.AppendApprovalAsync(state.TaskId, "plan_subtasks", approved: true, options);

    return (store, service, options, state);
    }

    public static async Task AgentPlanSubtasksApprovalRejectsAnInvalidPlanInstead()
    {
    using var temp = new TempDir();
    var llm = new FakeSequencedAgentLlm([OneSubtaskResponse]);
    var (store, service, options, state) = await CreateApprovedTwoSubtaskPlanAsync(temp, llm);

    var afterApproval = await store.LoadAsync(state.TaskId);
    Equal(AgentTaskStatus.WaitingForUser, afterApproval!.Status, "an invalid plan (too few entries) should not materialize and should return to WaitingForUser");
    True(afterApproval.SubTaskPlan.Count == 0, "an invalid plan should never be materialized");
    True(afterApproval.PendingToolAction is null, "the rejected pending action should be cleared");
    True(afterApproval.ToolResults.Any(t => t.Tool == "plan_subtasks" && t.ResultSummary.Contains("between 2 and 6", StringComparison.Ordinal)),
        "the rejection should explain why via a tool result");
    }

    public static async Task AgentOrchestrationRunsChildrenSequentiallyThenSynthesizes()
    {
    using var temp = new TempDir();
    var llm = new FakeSequencedAgentLlm([
        PlanTwoSubtasksResponse,
        FinalResponseWithMessage("Fixed the bug in Foo.cs."),
        FinalResponseWithMessage("Added a regression test."),
        FinalResponseWithMessage("Both sub-tasks completed successfully.")
    ]);
    var (store, service, options, state) = await CreateApprovedTwoSubtaskPlanAsync(temp, llm);

    var afterApproval = await store.LoadAsync(state.TaskId);
    Equal(2, afterApproval!.SubTaskPlan.Count, "approval should materialize the proposed plan");
    True(afterApproval.SubTaskPlan.All(s => s.Status == AgentSubTaskStatus.Pending), "no child should be created yet at approval time");
    True(afterApproval.SubTaskPlan.All(s => string.IsNullOrEmpty(s.TaskId)), "no child task id should exist until the orchestration loop actually creates it");

    var result = await service.RunAsync(state.TaskId, options);

    Equal(AgentTaskStatus.Complete, result.State.Status, "the parent should complete once every sub-task and synthesis are done");
    Equal(2, result.State.SubTaskPlan.Count, "the plan should still have two entries");
    True(result.State.SubTaskPlan.All(s => s.Status == AgentSubTaskStatus.Complete), "both sub-tasks should be complete");
    True(result.State.SubTaskPlan.All(s => !string.IsNullOrWhiteSpace(s.ResultSummary)), "both sub-tasks should carry a non-empty result summary");
    True(result.State.SubTaskPlan.All(s => !string.IsNullOrWhiteSpace(s.TaskId)), "both sub-tasks should have created a real child task");
    True(result.State.SubTaskPlan.Select(s => s.TaskId).Distinct().Count() == 2, "the two children should be distinct tasks");

    var childState = await store.LoadAsync(result.State.SubTaskPlan[0].TaskId!);
    Equal(state.TaskId, childState?.ParentTaskId, "the child should carry the parent's task id");

    var reportPath = Path.Combine(store.GetTaskDirectory(state.TaskId), "report.md");
    True(File.Exists(reportPath), "synthesis should write report.md to the parent's task directory");
    }

    public static async Task AgentOrchestrationChildApprovalTargetsTheChildAndResumesTheParent()
    {
    using var temp = new TempDir();
    Directory.CreateDirectory(temp.PathFor("workspace"));
    File.WriteAllText(Path.Combine(temp.PathFor("workspace"), "notes.md"), "status: draft");

    var childEditRequest = """
        {
          "thought_summary": "Editing notes.md",
          "current_step": "Apply the edit",
          "next_action": {
            "type": "tool",
            "tool_name": "edit_file",
            "arguments": { "relative_path": "notes.md", "old_string": "status: draft", "new_string": "status: final" },
            "requires_approval": true,
            "risk_level": "medium"
          },
          "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": [] },
          "user_message": "Requesting edit."
        }
        """;

    var llm = new FakeSequencedAgentLlm([
        PlanTwoSubtasksResponse,
        childEditRequest,
        FinalResponseWithMessage("Edit applied."),
        FinalResponseWithMessage("Second sub-task done."),
        FinalResponseWithMessage("Synthesis report.")
    ]);
    var (store, service, options, state) = await CreateApprovedTwoSubtaskPlanAsync(temp, llm);

    var paused = await service.RunAsync(state.TaskId, options);
    Equal(AgentTaskStatus.WaitingForUser, paused.State.Status, "the run should pause when the first child needs approval");
    NotEqual(state.TaskId, paused.State.TaskId, "the paused result should describe the CHILD task, not the parent");
    Equal("edit_file", paused.State.PendingToolAction?.ToolName, "the child's pending action should be edit_file");

    await service.AppendApprovalAsync(paused.State.TaskId, "edit_file", approved: true, options);

    var resumed = await service.RunAsync(state.TaskId, options);
    Equal(AgentTaskStatus.Complete, resumed.State.Status, "resuming the parent should continue the same child and finish the run");
    True(resumed.State.SubTaskPlan.All(s => s.Status == AgentSubTaskStatus.Complete), "both sub-tasks should end complete after resuming");
    Equal("status: final", File.ReadAllText(Path.Combine(temp.PathFor("workspace"), "notes.md")), "the child's approved edit should actually apply to the shared workspace");
    }

    public static async Task AgentOrchestrationRememberedCommandApprovalsAreNotSharedBetweenChildren()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    var manifests = new WorkspaceManifestService();
    await manifests.SaveAsync(root, new WorkspaceManifest
    {
        AllowedCommands = [new WorkspaceCommandRecipe("dotnet build", "Build.", AgentRiskLevel.Low)]
    });

    var runBuildRequest = """
        {
          "thought_summary": "Building.",
          "current_step": "Run the build.",
          "next_action": { "type": "tool", "tool_name": "run_command", "arguments": { "command": "dotnet build" }, "requires_approval": true, "risk_level": "medium" },
          "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": [] },
          "user_message": "Requesting build."
        }
        """;

    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new FileAgentTaskStateStore(settings);
    var tools = new AgentWorkspaceTools();
    var llm = new FakeSequencedAgentLlm([
        PlanTwoSubtasksResponse,
        runBuildRequest,
        FinalResponseWithMessage("Build already approved once; done."),
        runBuildRequest
    ]);
    var service = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), llm, manifests: manifests, settings: settings);
    var options = new AgentWorkspaceOptions(root, ModelId: "fake-sequenced-agent");

    var state = await service.CreateTaskAsync("Build twice across two sub-tasks", options);
    await service.RunStepAsync(state.TaskId, options);
    await service.AppendApprovalAsync(state.TaskId, "plan_subtasks", approved: true, options);

    var firstPause = await service.RunAsync(state.TaskId, options);
    Equal("run_command", firstPause.State.PendingToolAction?.ToolName, "the first child should need approval for run_command");
    var firstChildId = firstPause.State.TaskId;
    await service.AppendApprovalAsync(firstChildId, "run_command", approved: true, options);

    var secondPause = await service.RunAsync(state.TaskId, options);
    Equal(AgentTaskStatus.WaitingForUser, secondPause.State.Status, "the second child should still need its own approval for the identical command");
    Equal("run_command", secondPause.State.PendingToolAction?.ToolName, "the second child's run_command should not auto-execute from the first child's approval");
    NotEqual(firstChildId, secondPause.State.TaskId, "the two children should be different tasks");
    }

    public static async Task AgentOrchestrationBudgetExhaustionSkipsRemainingSubtasksAndNotesTruncationInTheReport()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    settings.Settings.Agent.MaxOrchestrationSteps = 1;
    var store = new FileAgentTaskStateStore(settings);
    var tools = new AgentWorkspaceTools();
    var llm = new FakeSequencedAgentLlm([
        PlanThreeSubtasksResponse,
        FinalResponseWithMessage("First sub-task done."),
        FinalResponseWithMessage("All requested work is finished.")
    ]);
    var service = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), llm, settings: settings);
    var options = new AgentWorkspaceOptions(root, ModelId: "fake-sequenced-agent");

    var state = await service.CreateTaskAsync("A goal needing three sub-tasks", options);
    await service.RunStepAsync(state.TaskId, options);
    await service.AppendApprovalAsync(state.TaskId, "plan_subtasks", approved: true, options);

    var result = await service.RunAsync(state.TaskId, options);

    Equal(AgentTaskStatus.Complete, result.State.Status, "a budget-truncated orchestration should still end Complete");
    Equal(1, result.State.SubTaskPlan.Count(s => s.Status == AgentSubTaskStatus.Complete), "exactly one sub-task should have completed before the budget was hit");
    Equal(2, result.State.SubTaskPlan.Count(s => s.Status == AgentSubTaskStatus.Skipped), "the remaining sub-tasks should be marked Skipped");
    True(result.State.Summary.Contains("budget", StringComparison.OrdinalIgnoreCase), "the synthesis report should note the run was truncated by budget");
    }

    public static async Task AgentOrchestrationSynthesisFallsBackDeterministicallyWhenTheModelCallFails()
    {
    using var temp = new TempDir();
    var llm = new FakeSequencedThenThrowingAgentLlm([
        PlanTwoSubtasksResponse,
        FinalResponseWithMessage("First sub-task done."),
        FinalResponseWithMessage("Second sub-task done.")
        // no synthesis response queued: the synthesis call throws
    ]);
    var (store, service, options, state) = await CreateApprovedTwoSubtaskPlanAsync(temp, llm);

    var result = await service.RunAsync(state.TaskId, options);

    Equal(AgentTaskStatus.Complete, result.State.Status, "a failed synthesis call must not fail the whole run; the sub-task work already happened");
    True(result.State.SubTaskPlan.All(s => s.Status == AgentSubTaskStatus.Complete), "both sub-tasks should still show complete");
    True(result.State.Summary.Contains("Fix the bug", StringComparison.Ordinal), "the deterministic fallback report should list each sub-task's goal");
    True(result.State.Summary.Contains("Add a regression test", StringComparison.Ordinal), "the deterministic fallback report should list each sub-task's goal");
    var reportPath = Path.Combine(store.GetTaskDirectory(state.TaskId), "report.md");
    True(File.Exists(reportPath), "the fallback report should still be written to report.md");
    }

    public static async Task AgentContextBuilderIncludesSubTaskReportsForAnOrchestrationParent()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var taskStore = new FileAgentTaskStateStore(settings);
    await taskStore.InitializeAsync();
    var tools = new AgentWorkspaceTools();
    var ragStore = new SqliteRagStore(settings);
    var rag = new RagQueryService(ragStore, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());
    var retrieval = new AgentRetrievalService(rag, ragStore);
    var activation = new WorkspaceActivationService(new WorkspaceManifestService(), new FileWorkspaceProfileStore(settings));
    var builder = new AgentContextBuilder(tools, retrieval, new WorkspaceMemoryStore(new MemoryStore(settings), settings), activation, taskStore, settings);

    var state = new AgentTaskState
    {
        TaskId = "parent-task",
        Goal = "Broad goal",
        Status = AgentTaskStatus.Running,
        SubTaskPlan =
        [
            new AgentSubTaskSpec { Goal = "Fix the bug", ProfileName = "correctness", Status = AgentSubTaskStatus.Complete, ResultSummary = "Bug fixed in Foo.cs" },
            new AgentSubTaskSpec { Goal = "Add a test", ProfileName = "tests", Status = AgentSubTaskStatus.Pending }
        ]
    };
    await taskStore.SaveAsync(state);
    var options = new AgentWorkspaceOptions(root);

    var pack = await builder.BuildAsync(state, options);

    Equal(2, pack.SubTaskReports.Count, "one context item per sub-task spec");
    True(pack.SubTaskReports.Any(i => i.Content.Contains("Bug fixed in Foo.cs", StringComparison.Ordinal)), "the completed sub-task's result summary should be included");
    }

    public static async Task AgentGatedActionWithNoRegisteredExecutorEndsBlockedInsteadOfStranded()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new FileAgentTaskStateStore(settings);
    var tools = new AgentWorkspaceTools();

    // "mcp:some_tool" is gated (AgentSafetyGate always requires approval for
    // mcp: tools) but the executor has no bridge registered for it here, so
    // CanExecute returns false - the r15 3.2 hardening case.
    var mcpRequest = """
        {
          "thought_summary": "Trying an MCP tool.",
          "current_step": "Call it.",
          "next_action": { "type": "tool", "tool_name": "mcp:some_tool", "arguments": {}, "requires_approval": false, "risk_level": "none" },
          "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": [] },
          "user_message": "Calling an MCP tool."
        }
        """;
    var llm = new FakeSequencedAgentLlm([mcpRequest]);
    var service = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), llm, settings: settings);
    var options = new AgentWorkspaceOptions(root, ModelId: "fake-sequenced-agent");

    var state = await service.CreateTaskAsync("Try an MCP tool", options);
    var step = await service.RunStepAsync(state.TaskId, options);

    Equal(AgentTaskStatus.Blocked, step.State.Status, "a gated action with no registered executor should end Blocked, not stranded WaitingForUser with nothing to approve");
    True(step.State.PendingToolAction is null, "there should be nothing pending to approve");
    True(step.State.ToolResults.Any(t => t.Tool == "mcp:some_tool" && t.ResultSummary.Contains("no local executor", StringComparison.OrdinalIgnoreCase)),
        "the block should be explained");
    }

    public static async Task AgentBlockerReportedAlongsideASuccessfulToolExecutionEndsRunningWithProgressWinning()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new FileAgentTaskStateStore(settings);
    var tools = new AgentWorkspaceTools();

    var setPlanWithBlocker = """
        {
          "thought_summary": "Updating the plan.",
          "current_step": "Set the plan.",
          "next_action": {
            "type": "tool",
            "tool_name": "set_plan",
            "arguments": { "steps": [ { "description": "Step one", "status": "pending" } ] },
            "requires_approval": false,
            "risk_level": "none"
          },
          "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": ["missing a required credential"] },
          "user_message": "Plan updated, but blocked on a credential."
        }
        """;
    var llm = new FakeSequencedAgentLlm([setPlanWithBlocker]);
    var service = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), llm, settings: settings);
    var options = new AgentWorkspaceOptions(root, ModelId: "fake-sequenced-agent");

    var state = await service.CreateTaskAsync("Do something needing a credential", options);
    var step = await service.RunStepAsync(state.TaskId, options);

    Equal(AgentTaskStatus.Running, step.State.Status, "a step that both reports a blocker and successfully executes a tool should end Running - progress wins");
    True(step.State.Decisions.Any(d => d.Decision == "missing a required credential"), "the blocker should still be recorded in Decisions even though it did not change the status");
    }

    public static async Task AgentBlockerReportedWithoutASuccessfulExecutionEndsBlocked()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    File.WriteAllText(Path.Combine(root, "notes.md"), "status: draft");
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new FileAgentTaskStateStore(settings);
    var tools = new AgentWorkspaceTools();

    var editWithBlocker = """
        {
          "thought_summary": "Wants to edit but reports a blocker.",
          "current_step": "Request an edit.",
          "next_action": {
            "type": "tool",
            "tool_name": "edit_file",
            "arguments": { "relative_path": "notes.md", "old_string": "status: draft", "new_string": "status: final" },
            "requires_approval": true,
            "risk_level": "medium"
          },
          "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": ["cannot proceed without user input"] },
          "user_message": "Blocked before the edit could run."
        }
        """;
    var llm = new FakeSequencedAgentLlm([editWithBlocker]);
    var service = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), llm, settings: settings);
    var options = new AgentWorkspaceOptions(root, ModelId: "fake-sequenced-agent");

    var state = await service.CreateTaskAsync("Edit notes but something blocks it", options);
    var step = await service.RunStepAsync(state.TaskId, options);

    Equal(AgentTaskStatus.Blocked, step.State.Status, "a blocker reported alongside a tool that did not go on to execute should win over a plain WaitingForUser");
    True(step.State.Decisions.Any(d => d.Decision == "cannot proceed without user input"), "the blocker should still be recorded in Decisions");
    }

    // ── r16 01-orchestration-hardening.md ──

    public static async Task AgentTaskIndexTimestampsRoundTripWithUtcKind()
    {
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new FileAgentTaskStateStore(settings);
    await store.InitializeAsync();

    var state = new AgentTaskState
    {
        Goal = "Check timestamp kind",
        Status = AgentTaskStatus.WaitingForUser,
        ApprovalHistory = [new AgentApprovalRecord("x", true, DateTime.UtcNow)]
    };
    await store.SaveAsync(state);

    var recent = await store.ListRecentAsync();
    var recentItem = recent.Single(i => i.TaskId == state.TaskId);
    Equal(DateTimeKind.Utc, recentItem.UpdatedAt.Kind, "ListRecentAsync timestamps should parse with UTC kind, not local (1.7)");

    var queue = await store.ListReviewQueueAsync();
    var queueItem = queue.Single(i => i.TaskId == state.TaskId);
    Equal(DateTimeKind.Utc, queueItem.UpdatedAt.Kind, "ListReviewQueueAsync timestamps should parse with UTC kind, not local (1.7)");
    Equal(DateTimeKind.Utc, queueItem.LastApprovalAt!.Value.Kind, "the last-approval timestamp should also parse with UTC kind (1.7)");
    }

    public static async Task AgentTaskStateRoundTripsWorkspaceRootAndPreR16StateLoadsUnchanged()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new FileAgentTaskStateStore(settings);
    var tools = new AgentWorkspaceTools();
    var service = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), new FakeSequencedAgentLlm([]), settings: settings);
    var options = new AgentWorkspaceOptions(root, ModelId: "fake-sequenced-agent");

    var state = await service.CreateTaskAsync("Track my workspace", options);
    Equal(Path.GetFullPath(root), state.WorkspaceRoot, "creating a task should persist the resolved workspace root (1.4)");

    var reloaded = await store.LoadAsync(state.TaskId);
    Equal(Path.GetFullPath(root), reloaded!.WorkspaceRoot, "the stored root should round-trip through the store (1.4)");

    // A pre-r16 fixture with no WorkspaceRoot must still deserialize cleanly
    // (JSON-additive; snake_case matches AgentJson.Options).
    var legacyJson = """{"task_id":"legacy-task","goal":"pre-r16 task","status":"new"}""";
    var legacyDir = store.GetTaskDirectory("legacy-task");
    Directory.CreateDirectory(legacyDir);
    await File.WriteAllTextAsync(Path.Combine(legacyDir, "task_state.json"), legacyJson);
    var legacy = await store.LoadAsync("legacy-task");
    Equal(string.Empty, legacy!.WorkspaceRoot, "a pre-r16 state file with no WorkspaceRoot should load with an empty one, not throw (1.4)");
    }

    public static async Task AgentApprovalExecutesAgainstTheTasksOwnWorkspaceRootNotTheCallersOptions()
    {
    using var temp = new TempDir();
    var rootA = temp.PathFor("workspace-a");
    var rootB = temp.PathFor("workspace-b");
    Directory.CreateDirectory(rootA);
    Directory.CreateDirectory(rootB);
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new FileAgentTaskStateStore(settings);
    var tools = new AgentWorkspaceTools();

    var createFileRequest = """
        {
          "thought_summary": "Creating a file.",
          "current_step": "Create it.",
          "next_action": {
            "type": "tool",
            "tool_name": "create_file",
            "arguments": { "relative_path": "output.txt", "content": "hello" },
            "requires_approval": true,
            "risk_level": "medium"
          },
          "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": [] },
          "user_message": "Requesting file creation."
        }
        """;
    var llm = new FakeSequencedAgentLlm([createFileRequest]);
    var service = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), llm, settings: settings);
    var optionsA = new AgentWorkspaceOptions(rootA, ModelId: "fake-sequenced-agent");

    var state = await service.CreateTaskAsync("Create output.txt in workspace A", optionsA);
    var paused = await service.RunStepAsync(state.TaskId, optionsA);
    Equal(AgentTaskStatus.WaitingForUser, paused.State.Status, "create_file should pause for approval");

    // Simulate the review queue: approve with options pointed at a DIFFERENT
    // workspace (B), as would happen if the workbench had workspace B active
    // while approving a task created in workspace A (1.4 headline fix).
    var optionsB = new AgentWorkspaceOptions(rootB, ModelId: "fake-sequenced-agent");
    await service.AppendApprovalAsync(state.TaskId, "review_queue", approved: true, optionsB);

    True(File.Exists(Path.Combine(rootA, "output.txt")), "the approved action should execute against the TASK's own workspace root (A), not the caller's options (B)");
    False(File.Exists(Path.Combine(rootB, "output.txt")), "the file must not be created in the workspace the caller happened to have active");
    }

    public static async Task AgentApprovalWithNullOptionsAndNoStoredWorkspaceRootThrowsInsteadOfStranding()
    {
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new FileAgentTaskStateStore(settings);
    await store.InitializeAsync();

    // A pre-r16 task: no stored WorkspaceRoot, a pending action.
    var state = new AgentTaskState
    {
        Goal = "Pre-r16 task",
        Status = AgentTaskStatus.WaitingForUser,
        PendingToolAction = new AgentPendingToolAction
        {
            ToolName = "create_file",
            Arguments = new Dictionary<string, object?> { ["relative_path"] = "x.txt", ["content"] = "x" }
        }
    };
    await store.SaveAsync(state);

    var tools = new AgentWorkspaceTools();
    var service = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), new FakeSequencedAgentLlm([]), settings: settings);

    var threw = false;
    try { await service.AppendApprovalAsync(state.TaskId, "review_queue", approved: true, options: null); }
    catch (InvalidOperationException) { threw = true; }
    True(threw, "approving with no options and no stored workspace root should throw InvalidOperationException (1.5)");

    var reloaded = await store.LoadAsync(state.TaskId);
    Equal(AgentTaskStatus.WaitingForUser, reloaded!.Status, "a throw must not silently strand the task Running with a stale pending action (1.5)");
    True(reloaded.PendingToolAction is not null, "the pending action should remain since nothing executed (1.5)");
    }

    public static async Task AgentApprovalWithNullOptionsButStoredWorkspaceRootExecutesNormally()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new FileAgentTaskStateStore(settings);
    var tools = new AgentWorkspaceTools();

    var createFileRequest = """
        {
          "thought_summary": "Creating a file.",
          "current_step": "Create it.",
          "next_action": {
            "type": "tool",
            "tool_name": "create_file",
            "arguments": { "relative_path": "output.txt", "content": "hello" },
            "requires_approval": true,
            "risk_level": "medium"
          },
          "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": [] },
          "user_message": "Requesting file creation."
        }
        """;
    var llm = new FakeSequencedAgentLlm([createFileRequest]);
    var service = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), llm, settings: settings);
    var options = new AgentWorkspaceOptions(root, ModelId: "fake-sequenced-agent");

    var state = await service.CreateTaskAsync("Create a file", options);
    await service.RunStepAsync(state.TaskId, options);

    await service.AppendApprovalAsync(state.TaskId, "review_queue", approved: true, options: null);

    True(File.Exists(Path.Combine(root, "output.txt")), "a null-options approval should fall back to the task's own stored WorkspaceRoot and execute normally (1.5)");
    var reloaded = await store.LoadAsync(state.TaskId);
    Equal(AgentTaskStatus.Running, reloaded!.Status, "approval should return the task to Running");
    }

    public static async Task AgentReviewQueueChildEntriesCarryParentTaskIdAndWorkspaceRoot()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new FileAgentTaskStateStore(settings);
    await store.InitializeAsync();

    var parent = new AgentTaskState { Goal = "Parent goal", Status = AgentTaskStatus.Running, WorkspaceRoot = Path.GetFullPath(root) };
    await store.SaveAsync(parent);

    var child = new AgentTaskState
    {
        Goal = "Child goal",
        Status = AgentTaskStatus.WaitingForUser,
        ParentTaskId = parent.TaskId,
        WorkspaceRoot = Path.GetFullPath(root),
        PendingToolAction = new AgentPendingToolAction { ToolName = "edit_file" }
    };
    await store.SaveAsync(child);

    var queue = await store.ListReviewQueueAsync();
    var item = queue.Single(q => q.TaskId == child.TaskId);
    Equal(parent.TaskId, item.ParentTaskId, "a child's review-queue entry should carry the parent's task id so approval can resume the right task (1.1)");
    Equal(parent.Goal, item.ParentGoal, "a child's review-queue entry should still carry the parent's goal for display");
    Equal(Path.GetFullPath(root), item.WorkspaceRoot, "a review-queue entry with a pending action should carry the task's own workspace root (1.4)");
    }

    public static async Task AgentOrchestrationReconcilesAChildCompletedOutsideTheLoop()
    {
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new FileAgentTaskStateStore(settings);
    var tools = new AgentWorkspaceTools();
    var llm = new FakeSequencedAgentLlm([FinalResponseWithMessage("Synthesis report.")]);
    var service = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(), new AgentToolExecutor(tools), llm, settings: settings);
    var options = new AgentWorkspaceOptions(root, ModelId: "fake-sequenced-agent");

    var parent = new AgentTaskState
    {
        Goal = "Broad goal",
        Status = AgentTaskStatus.Running,
        WorkspaceRoot = Path.GetFullPath(root),
        SubTaskPlan = [new AgentSubTaskSpec { Goal = "First", ProfileName = "general", Status = AgentSubTaskStatus.Running, TaskId = "child-outside-1" }]
    };
    await store.SaveAsync(parent);

    // The child reached Complete OUTSIDE the orchestration loop entirely -
    // opened directly and stepped/run to completion, or resumed via the
    // review queue while it (not the parent) was the open task (r16
    // 01-orchestration-hardening.md 1.1, the round's agent headliner).
    var child = new AgentTaskState
    {
        TaskId = "child-outside-1",
        Goal = "First",
        Status = AgentTaskStatus.Complete,
        ParentTaskId = parent.TaskId,
        Summary = "Finished the first sub-task."
    };
    await store.SaveAsync(child);

    var result = await service.RunAsync(parent.TaskId, options);

    Equal(AgentTaskStatus.Complete, result.State.Status, "the parent should reconcile the externally-completed child and proceed to synthesis instead of throwing 'already finished'");
    Equal(AgentSubTaskStatus.Complete, result.State.SubTaskPlan[0].Status, "the reconcile should mark the externally-completed child's spec terminal");
    True(result.State.SubTaskPlan[0].ResultSummary.Contains("Finished the first sub-task", StringComparison.Ordinal), "the reconcile should copy the child's summary into the spec");
    }

    public static async Task AgentRunStepThrowsForAParentWithUnfinishedSubTasks()
    {
    using var temp = new TempDir();
    var llm = new FakeSequencedAgentLlm([PlanTwoSubtasksResponse]);
    var (store, service, options, state) = await CreateApprovedTwoSubtaskPlanAsync(temp, llm);

    var message = string.Empty;
    try { await service.RunStepAsync(state.TaskId, options); }
    catch (InvalidOperationException ex) { message = ex.Message; }
    True(message.Contains("RunAsync", StringComparison.Ordinal), "the error should tell the caller to use RunAsync instead of stepping the parent directly (1.2)");
    _ = store;
    }

    public static async Task AgentPlanSubtasksIsBlockedWhenProposedAgainDuringSynthesis()
    {
    using var temp = new TempDir();
    var llm = new FakeSequencedAgentLlm([
        PlanTwoSubtasksResponse,
        FinalResponseWithMessage("First sub-task done."),
        FinalResponseWithMessage("Second sub-task done."),
        PlanTwoSubtasksResponse
    ]);
    var (store, service, options, state) = await CreateApprovedTwoSubtaskPlanAsync(temp, llm);

    var result = await service.RunAsync(state.TaskId, options);

    Equal(AgentTaskStatus.Complete, result.State.Status, "synthesis falling back after a blocked re-proposal must still end the run Complete, not stranded (1.3)");
    Equal(2, result.State.SubTaskPlan.Count, "the original plan must survive a blocked re-proposal attempt, not be replaced (1.3)");
    True(result.State.SubTaskPlan.All(s => s.Status == AgentSubTaskStatus.Complete), "the original two sub-tasks should still show complete");
    _ = store;
    }

    public static async Task AgentPlanSubtasksApprovalRejectsWhenTheTaskAlreadyHasAPlan()
    {
    using var temp = new TempDir();
    var llm = new FakeSequencedAgentLlm([PlanTwoSubtasksResponse]);
    var (store, service, options, state) = await CreateApprovedTwoSubtaskPlanAsync(temp, llm);

    // Simulate a second, stale plan_subtasks approval racing in after the
    // first one already materialized the plan (r16 01-orchestration-hardening.md 1.3).
    var loaded = await store.LoadAsync(state.TaskId);
    var subtasksJson = JsonDocument.Parse("""
        [ { "goal": "racing-a", "profile": "general", "success_criteria": "a" },
          { "goal": "racing-b", "profile": "general", "success_criteria": "b" } ]
        """).RootElement;
    loaded!.PendingToolAction = new AgentPendingToolAction
    {
        ToolName = "plan_subtasks",
        Arguments = new Dictionary<string, object?> { ["subtasks"] = subtasksJson }
    };
    await store.SaveAsync(loaded);

    await service.AppendApprovalAsync(state.TaskId, "plan_subtasks", approved: true, options);

    var reloaded = await store.LoadAsync(state.TaskId);
    Equal(2, reloaded!.SubTaskPlan.Count, "the existing plan must not be replaced by a stale racing approval");
    Equal("Fix the bug", reloaded.SubTaskPlan[0].Goal, "the ORIGINAL plan's entries should survive, not the racing one's");
    }

    public static async Task AgentParentStatusMirrorsPausedChildStatusAndResumesToComplete()
    {
    using var temp = new TempDir();
    Directory.CreateDirectory(temp.PathFor("workspace"));
    File.WriteAllText(Path.Combine(temp.PathFor("workspace"), "notes.md"), "status: draft");

    var childEditRequest = """
        {
          "thought_summary": "Editing notes.md",
          "current_step": "Apply the edit",
          "next_action": {
            "type": "tool",
            "tool_name": "edit_file",
            "arguments": { "relative_path": "notes.md", "old_string": "status: draft", "new_string": "status: final" },
            "requires_approval": true,
            "risk_level": "medium"
          },
          "state_update": { "completed": [], "pending": [], "new_facts": [], "blockers": [] },
          "user_message": "Requesting edit."
        }
        """;

    var llm = new FakeSequencedAgentLlm([
        PlanTwoSubtasksResponse,
        childEditRequest,
        FinalResponseWithMessage("Edit applied."),
        FinalResponseWithMessage("Second sub-task done."),
        FinalResponseWithMessage("Synthesis report.")
    ]);
    var (store, service, options, state) = await CreateApprovedTwoSubtaskPlanAsync(temp, llm);

    var paused = await service.RunAsync(state.TaskId, options);
    Equal(AgentTaskStatus.WaitingForUser, paused.State.Status, "the child itself should be WaitingForUser");

    var parentAfterPause = await store.LoadAsync(state.TaskId);
    Equal(AgentTaskStatus.WaitingForUser, parentAfterPause!.Status, "the parent must truthfully mirror its paused child's status instead of sitting Running forever (1.6)");
    True(parentAfterPause.ActiveStep.Contains("Waiting on sub-task 1/2", StringComparison.Ordinal), "the parent's ActiveStep should name which sub-task it is waiting on (1.6)");

    await service.AppendApprovalAsync(paused.State.TaskId, "edit_file", approved: true, options);
    var resumed = await service.RunAsync(state.TaskId, options);
    Equal(AgentTaskStatus.Complete, resumed.State.Status, "resuming the parent (whose own status was WaitingForUser, not Running) should still complete the run (1.6)");
    }
}
