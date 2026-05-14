using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Aether.Agent.Models;
using Aether.Agent.Services;
using Aether.Core.Services;
using Aether.Rag;
using Aether.Rag.Embeddings;
using Aether.Rag.Storage;
using Aether.Rag.Retrieval;
using Aether.Services;
using Aether.Tests;
using static Aether.Tests.Helpers;

var tests = new (string Name, Func<Task> Run)[]
{
    ("data root migration previews moveable files", BackupMigrationTests.DataRootMigrationPreview),
    ("data root migration refuses conflicts", BackupMigrationTests.DataRootMigrationRefusesConflicts),
    ("data root migration moves db files and leaves no junk", BackupMigrationTests.DataRootMigrationMovesFiles),
    ("backup excludes secrets and refuses overwrite restore", BackupMigrationTests.BackupExcludesSecretsAndRefusesOverwrite),
    ("redaction hides common secrets and home path", ServiceTests.RedactionHidesSecrets),
    ("benchmark db creates starter suites and records runs", ServiceTests.BenchmarkDbCreatesAndRecordsRuns),
    ("benchmark scoring and ranking are deterministic", ServiceTests.BenchmarkScoringAndRanking),
    ("system info returns safe fallback values", ServiceTests.SystemInfoSafeFallback),
    ("local ai assets detect and apply paths", ServiceTests.LocalAiAssetsDetectAndApplyPaths),
    ("local AI setup detects Aether folder layout", ServiceTests.LocalAiSetupDetectsFolderLayout),
    ("local AI setup script handling is approval gated", ServiceTests.LocalAiSetupScriptHandlingIsApprovalGated),
    ("local AI setup command previews stay shell-free", ServiceTests.LocalAiSetupCommandPreviewsStayShellFree),
    ("local AI setup surfaces Kokoro onboarding", ServiceTests.LocalAiSetupSurfcesKokoroOnboarding),
    ("model download resumes with range request", ServiceTests.ModelDownloadResumesWithRangeRequest),
    ("llama-server release data covers supported platforms", ServiceTests.LlamaServerReleaseDataCoversSupportedPlatforms),
    ("XTTS API template has required endpoints", ServiceTests.XttsApiTemplateHasRequiredEndpoints),
    ("trust scan classifies inside AI root as low risk", ServiceTests.TrustScanInsideAiRootIsLowRisk),
    ("trust scan warns outside AI root but allows", ServiceTests.TrustScanOutsideAiRootWarns),
    ("trust scan reports missing executable", ServiceTests.TrustScanReportsMissingExecutable),
    ("trust scan keeps unset AI root neutral", ServiceTests.TrustScanUnsetAiRootIsNeutral),
    ("trust scan detects network-facing extra args", ServiceTests.TrustScanDetectsNetworkExtraArgs),
    ("source strings avoid long dashes", ServiceTests.SourceStringsAvoidLongDashes),
    ("secret store falls back without plaintext", ServiceTests.SecretStoreFallbackWithoutPlaintext),
    ("RAG web ingest strips HTML and stores chunks", RagTests.RagWebIngestStripsHtmlAndStoresChunks),
    ("RAG digital PDF text extracts", RagTests.RagDigitalPdfTextExtracts),
    ("RAG directory ingest includes PDFs", RagTests.RagDirectoryIngestIncludesPdfs),
    ("RAG directory dry run reports without persisting", RagTests.RagDirectoryDryRunReportsWithoutPersisting),
    ("RAG directory skip unchanged avoids duplicate chunks", RagTests.RagDirectorySkipUnchangedAvoidsDuplicateChunks),
    ("RAG empty PDF warns and continues", RagTests.RagEmptyPdfWarnsAndContinues),
    ("RAG ingest cancellation during embedding stops gracefully", RagTests.RagIngestCancellationDuringEmbedding),
    ("RAG ingest cancellation during storage stops gracefully", RagTests.RagIngestCancellationDuringStorage),
    ("voice provider capability gating prevents unsupported providers", TtsTests.VoiceProviderCapabilityGating),
    ("voice provider legacy requires local and TTS", TtsTests.VoiceProviderLegacyRequiresLocalAndTts),
    ("agent task state serializes schema fields", AgentTaskStateSerializesSchemaFields),
    ("agent review queue reflects approval history", AgentReviewQueueReflectsApprovalHistory),
    ("agent workspace memory persists notes per workspace", AgentWorkspaceMemoryPersistsNotesPerWorkspace),
    ("agent workspace tools enforce path safety", AgentWorkspaceToolsEnforcePathSafety),
    ("agent context pack stays bounded", AgentContextPackStaysBounded),
    ("agent tool policy gates risky actions", AgentToolPolicyGatesRiskyActions),
    ("agent loop writes state log and trace", AgentLoopWritesStateLogAndTrace),
    ("runtime profile normalization and unsafe host validation", ServiceTests.RuntimeProfileValidation),
    ("runtime profile defaults are deduplicated", ServiceTests.RuntimeProfilesAreDeduplicated),
    ("settings save migrates OpenAI key to secret reference", ServiceTests.SettingsSaveMigratesOpenAiKey),
    ("settings save preserves existing secret reference", ServiceTests.SettingsSavePreservesExistingSecretReference),
    ("settings save persists global hotkey preference", ServiceTests.SettingsSavePersistsGlobalHotkeyPreference),
    ("server process arguments stay shell-free and ordered", ServiceTests.ServerProcessArgumentsAreSafe)
};

// --- Agent tests moved here as local top-level functions ---
async Task AgentTaskStateSerializesSchemaFields()
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

async Task AgentReviewQueueReflectsApprovalHistory()
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

async Task AgentWorkspaceMemoryPersistsNotesPerWorkspace()
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

Task AgentWorkspaceToolsEnforcePathSafety()
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

async Task AgentContextPackStaysBounded()
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

Task AgentToolPolicyGatesRiskyActions()
{
    var gate = new AgentSafetyGate();
    var read = gate.Evaluate("read_file");
    Equal(AgentToolDisposition.Allowed, read.Disposition, "read-only tools should be allowed");
    Equal(AgentRiskLevel.Low, read.RiskLevel, "read-only tools should be low risk");

    var write = gate.Evaluate("apply_patch");
    Equal(AgentToolDisposition.RequiresApproval, write.Disposition, "write-like tools should require approval");
    Equal(AgentRiskLevel.Medium, write.RiskLevel, "write-like tools should be medium risk");

    var push = gate.Evaluate("push");
    Equal(AgentToolDisposition.RequiresApproval, push.Disposition, "push should require explicit approval");
    Equal(AgentRiskLevel.High, push.RiskLevel, "push should be high risk");

    var unknown = gate.Evaluate("desktop_control");
    Equal(AgentToolDisposition.Blocked, unknown.Disposition, "unknown tools should be blocked");
    return Task.CompletedTask;
}

async Task AgentLoopWritesStateLogAndTrace()
{
    using var temp = new TempDir();
    var root = temp.PathFor("workspace");
    Directory.CreateDirectory(root);
    File.WriteAllText(Path.Combine(root, "README.md"), "agent docs");
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new FileAgentTaskStateStore(settings);
    var service = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(), new FakeAgentLlm());
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

// Helpers are provided by Aether.Tests.Helpers; do not redefine here.

var failed = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}");
    }
}

if (failed > 0)
    return failed;

Console.WriteLine($"All {tests.Length} Aether tests passed.");
return 0;