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

    Equal(AgentTaskStatus.Running, result.State.Status, "the loop should stop mid-run once the step cap is hit even though the task is not finished");
    Equal(1, result.State.StepCount, "the loop should have executed exactly the capped number of steps");
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
}
