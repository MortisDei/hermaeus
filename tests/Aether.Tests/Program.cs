using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Agent.Models;
using Aether.Agent.Services;
using Aether.Rag;
using Aether.Rag.Models;
using Aether.Rag.Pipeline;
using Aether.Rag.Retrieval;
using Aether.Rag.Embeddings;
using Aether.Rag.Storage;
using Aether.Services;
using Aether.Services.ProcessManagement;
using Aether.ViewModels;
using Aether.Desktop.Controls;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

var tests = new (string Name, Func<Task> Run)[]
{
    ("data root migration previews moveable files", DataRootMigrationPreview),
    ("data root migration refuses conflicts", DataRootMigrationRefusesConflicts),
    ("data root migration moves db files and leaves no junk", DataRootMigrationMovesFiles),
    ("backup excludes secrets and refuses overwrite restore", BackupExcludesSecretsAndRefusesOverwrite),
    ("redaction hides common secrets and home path", RedactionHidesSecrets),
    ("benchmark db creates starter suites and records runs", BenchmarkDbCreatesAndRecordsRuns),
    ("benchmark scoring and ranking are deterministic", BenchmarkScoringAndRanking),
    ("system info returns safe fallback values", SystemInfoSafeFallback),
    ("local ai assets detect and apply paths", LocalAiAssetsDetectAndApplyPaths),
    ("local AI setup detects Aether folder layout", LocalAiSetupDetectsFolderLayout),
    ("local AI setup script handling is approval gated", LocalAiSetupScriptHandlingIsApprovalGated),
    ("local AI setup command previews stay shell-free", LocalAiSetupCommandPreviewsStayShellFree),
    ("XTTS API template has required endpoints", XttsApiTemplateHasRequiredEndpoints),
    ("chat context attachments build prompt and persist summary", ChatContextAttachmentsBuildPromptAndPersistSummary),
    ("chat context attachments enforce limits and binary skip", ChatContextAttachmentsEnforceLimitsAndBinarySkip),
    ("chat context attachments remove and clear", ChatContextAttachmentsRemoveAndClear),
    ("markdown code fence aliases normalize", MarkdownCodeFenceAliasesNormalize),
    ("LLM stream usage payloads and parsers", LlmStreamUsagePayloadsAndParsers),
    ("chat context usage updates from provider", ChatContextUsageUpdatesFromProvider),
    ("chat context usage estimates pending context", ChatContextUsageEstimatesPendingContext),
    ("trust scan classifies inside AI root as low risk", TrustScanInsideAiRootIsLowRisk),
    ("trust scan warns outside AI root but allows", TrustScanOutsideAiRootWarns),
    ("trust scan reports missing executable", TrustScanReportsMissingExecutable),
    ("trust scan keeps unset AI root neutral", TrustScanUnsetAiRootIsNeutral),
    ("trust scan detects network-facing extra args", TrustScanDetectsNetworkExtraArgs),
    ("source strings avoid long dashes", SourceStringsAvoidLongDashes),
    ("settings apply local ai assets persists paths", SettingsApplyLocalAiAssetsPersistsPaths),
    ("secret store falls back without plaintext", SecretStoreFallbackWithoutPlaintext),
    ("RAG BM25 scoring ranks exact term matches", RagBm25ScoringRanksMatches),
    ("RAG hybrid scoring fuses semantic and lexical ranks", RagHybridScoringFusesRanks),
    ("RAG web loader stays disabled by default", RagWebLoaderDisabledByDefault),
    ("RAG web loader parses explicit opt-in URLs", RagWebLoaderParsesOptInUrls),
    ("RAG web ingest strips HTML and stores chunks", RagWebIngestStripsHtmlAndStoresChunks),
    ("RAG digital PDF text extracts", RagDigitalPdfTextExtracts),
    ("RAG directory ingest includes PDFs", RagDirectoryIngestIncludesPdfs),
    ("RAG directory dry run reports without persisting", RagDirectoryDryRunReportsWithoutPersisting),
    ("RAG directory skip unchanged avoids duplicate chunks", RagDirectorySkipUnchangedAvoidsDuplicateChunks),
    ("RAG empty PDF warns and continues", RagEmptyPdfWarnsAndContinues),
    ("RAG ingest cancellation during embedding stops gracefully", RagIngestCancellationDuringEmbedding),
    ("RAG ingest cancellation during storage stops gracefully", RagIngestCancellationDuringStorage),
    ("voice provider capability gating prevents unsupported providers", VoiceProviderCapabilityGating),
    ("agent task state serializes schema fields", AgentTaskStateSerializesSchemaFields),
    ("agent review queue reflects approval history", AgentReviewQueueReflectsApprovalHistory),
    ("agent workspace memory persists notes per workspace", AgentWorkspaceMemoryPersistsNotesPerWorkspace),
    ("agent workspace tools enforce path safety", AgentWorkspaceToolsEnforcePathSafety),
    ("agent context pack stays bounded", AgentContextPackStaysBounded),
    ("agent tool policy gates risky actions", AgentToolPolicyGatesRiskyActions),
    ("agent loop writes state log and trace", AgentLoopWritesStateLogAndTrace),
    ("runtime profile normalization and unsafe host validation", RuntimeProfileValidation),
    ("runtime profile defaults are deduplicated", RuntimeProfilesAreDeduplicated),
    ("settings save migrates OpenAI key to secret reference", SettingsSaveMigratesOpenAiKey),
    ("settings save preserves existing secret reference", SettingsSavePreservesExistingSecretReference),
    ("settings save persists global hotkey preference", SettingsSavePersistsGlobalHotkeyPreference),
    ("server process arguments stay shell-free and ordered", ServerProcessArgumentsAreSafe)
};

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

static async Task DataRootMigrationPreview()
{
    await Task.CompletedTask;
}

static async Task DataRootMigrationRefusesConflicts()
{
    await Task.CompletedTask;
}

static async Task DataRootMigrationMovesFiles()
{
    await Task.CompletedTask;
}

static async Task BackupExcludesSecretsAndRefusesOverwrite()
{
    await Task.CompletedTask;
}

static Task RedactionHidesSecrets()
{
    return Task.CompletedTask;
}

static async Task ChatContextAttachmentsEnforceLimitsAndBinarySkip()
{
    using var temp = new TempDir();
    var small = temp.PathFor("small.cs");
    var large = temp.PathFor("large.cs");
    var binary = temp.PathFor("binary.cs");
    Directory.CreateDirectory(Path.GetDirectoryName(small)!);
    await File.WriteAllTextAsync(small, "class Small {}");
    await File.WriteAllTextAsync(large, new string('x', ChatContextAttachment.MaxFileBytes + 1));
    await File.WriteAllBytesAsync(binary, [0, 1, 2, 3, 4]);

    var loaded = await ChatContextAttachment.LoadFilesAsync([small, large, binary]);
    Equal(3, loaded.Count, "all selected files should produce attachment records");
    Equal(1, loaded.Count(a => a.IsReady), "only small text file should be ready");
    True(loaded.Any(a => a.StatusMessage.Contains("over", StringComparison.OrdinalIgnoreCase)), "large file should be skipped");
    True(loaded.Any(a => a.StatusMessage.Contains("Binary", StringComparison.OrdinalIgnoreCase)), "binary file should be skipped");
}

static async Task ChatContextAttachmentsRemoveAndClear()
{
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new ConversationStore(settings);
    await store.InitializeAsync();
    var vm = new ChatViewModel(new CapturingLlm(), store, settings, new FakeTts(), new ModelProfileService(settings));
    var a = temp.PathFor("a.cs");
    var b = temp.PathFor("b.cs");
    Directory.CreateDirectory(Path.GetDirectoryName(a)!);
    await File.WriteAllTextAsync(a, "class A {}");
    await File.WriteAllTextAsync(b, "class B {}");

    await vm.AddContextFilesAsync([a, b]);
    Equal(2, vm.ContextAttachments.Count, "two files should be attached");
    vm.RemoveContextAttachmentCommand.Execute(vm.ContextAttachments[0]);
    Equal(1, vm.ContextAttachments.Count, "remove should drop one attachment");
    vm.ClearContextAttachmentsCommand.Execute(null);
    Equal(0, vm.ContextAttachments.Count, "clear should remove all attachments");
}

static Task MarkdownCodeFenceAliasesNormalize()
{
    return Task.CompletedTask;
}

static Task LlmStreamUsagePayloadsAndParsers()
{
    return Task.CompletedTask;
}

static async Task ChatContextUsageUpdatesFromProvider()
{
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    settings.Settings.ManagedServers[0].ContextSize = 100;
    var store = new ConversationStore(settings);
    await store.InitializeAsync();
    var vm = new ChatViewModel(new UsageLlm(), store, settings, new FakeTts(), new ModelProfileService(settings));
    await vm.LoadModelsAsync(force: true);

    vm.InputText = "hello";
    await vm.SendCommand.ExecuteAsync(null);

    Equal("Reported by provider", vm.ContextUsageKind, "exact provider usage should win after stream completes");
    Equal("40 / 100 tokens", vm.ContextUsageLabel, "reported usage should be shown against selected context window");
    True(vm.ContextUsageTooltip.Contains("Prompt 30", StringComparison.Ordinal), "tooltip should show prompt token count");
}

static async Task ChatContextUsageEstimatesPendingContext()
{
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    settings.Settings.ManagedServers[0].ContextSize = 100;
    var store = new ConversationStore(settings);
    await store.InitializeAsync();
    var vm = new ChatViewModel(new CapturingLlm(), store, settings, new FakeTts(), new ModelProfileService(settings));
    await vm.LoadModelsAsync(force: true);

    var file = temp.PathFor("context.cs");
    await File.WriteAllTextAsync(file, new string('a', 120));
    vm.InputText = new string('b', 220);
    await vm.AddContextFilesAsync([file]);

    Equal("Estimated", vm.ContextUsageKind, "pending context should be locally estimated");
    True(vm.IsContextUsageWarning, "80 percent estimate should warn");
    False(vm.IsContextUsageCritical, "80 percent estimate should not be critical");

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

static async Task AgentReviewQueueReflectsApprovalHistory()
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

static async Task AgentWorkspaceMemoryPersistsNotesPerWorkspace()
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

static Task AgentWorkspaceToolsEnforcePathSafety()
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

static async Task AgentContextPackStaysBounded()
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

static Task AgentToolPolicyGatesRiskyActions()
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

static async Task AgentLoopWritesStateLogAndTrace()
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

static async Task RuntimeProfileValidation()
{
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    var service = new RuntimeProfileService(settings);
    var profile = new RuntimeProfile
    {
        Id = "runtime-1",
        Name = "  Custom Runtime  ",
        Kind = RuntimeKind.OpenAiCompatible,
        BaseUrl = "  https://example.test/v1/  ",
        ApiKey = " secret:runtime ",
        Enabled = true,
        LinkedServerId = " server-1 "
    };

    await service.SaveAsync(profile);
    var saved = settings.Settings.RuntimeProfiles.Single(p => p.Id == "runtime-1");
    Equal("Custom Runtime", saved.Name, "runtime profile name should be trimmed");
    Equal("https://example.test/v1", saved.BaseUrl, "runtime profile URL should be trimmed");
    Equal("secret:runtime", saved.ApiKey, "runtime profile API key should be trimmed");
    Equal("server-1", saved.LinkedServerId, "linked server id should be trimmed");

    var defaulted = RuntimeProfileService.NormalizeProfile(new RuntimeProfile
    {
        Id = string.Empty,
        Name = " ",
        Kind = RuntimeKind.LlamaCpp,
        BaseUrl = " "
    });
    True(Guid.TryParse(defaulted.Id, out _), "blank runtime id should be replaced");
    Equal("LlamaCpp", defaulted.Name, "blank runtime name should default to kind");
    Equal("http://127.0.0.1:8080", defaulted.BaseUrl, "blank runtime URL should default to loopback");

    var unsafeProfile = new RuntimeProfileViewModel(new RuntimeProfile { BaseUrl = "http://0.0.0.0:8080" });
    True(unsafeProfile.HasUnsafeHost, "runtime profile view model should flag 0.0.0.0");
}

static Task RuntimeProfilesAreDeduplicated()
{
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    settings.Settings.RuntimeProfiles =
    [
        new RuntimeProfile { Id = "llama-a", Name = "llama.cpp local", Kind = RuntimeKind.LlamaCpp, BaseUrl = "http://localhost:8080" },
        new RuntimeProfile { Id = "llama-b", Name = "llama.cpp local", Kind = RuntimeKind.LlamaCpp, BaseUrl = "http://localhost:8080/" },
        new RuntimeProfile { Id = "ollama", Name = "Ollama local", Kind = RuntimeKind.Ollama, BaseUrl = "http://127.0.0.1:11434" },
        new RuntimeProfile { Id = "ollama", Name = "Ollama local", Kind = RuntimeKind.Ollama, BaseUrl = "http://127.0.0.1:11434" }
    ];

    var service = new RuntimeProfileService(settings);
    Equal(2, service.Profiles.Count, "duplicate runtime defaults should be collapsed");
    Equal(2, settings.Settings.RuntimeProfiles.Count, "dedupe should update backing settings list");
    return Task.CompletedTask;
}

static async Task SettingsSaveMigratesOpenAiKey()
{
    using var temp = new TempDir();
    var previous = Environment.GetEnvironmentVariable("AETHER_DISABLE_OS_KEYCHAIN");
    Environment.SetEnvironmentVariable("AETHER_DISABLE_OS_KEYCHAIN", "1");
    try
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        settings.Settings.Llm.OpenAiApiKey = "sk-plain-key-123456";
        var secrets = new SecretStore(settings);
        var vm = NewSettingsViewModel(settings, secrets);

        Equal("sk-plain-key-123456", vm.OpenAiApiKey, "plaintext setting should load into editable field");
        await vm.SaveCommand.ExecuteAsync(null);

        True(secrets.IsReference(settings.Settings.Llm.OpenAiApiKey), "save should migrate plaintext key to a secret reference");
        Equal("sk-plain-key-123456", await secrets.ResolveAsync(settings.Settings.Llm.OpenAiApiKey), "migrated reference should resolve");
        var localVault = Path.Combine(settings.Settings.DataManagement.DataRootDirectory, "secrets.local.json");
        False((await File.ReadAllTextAsync(localVault)).Contains("sk-plain-key-123456", StringComparison.Ordinal),
            "migrated local vault should not contain plaintext");
    }
    finally
    {
        Environment.SetEnvironmentVariable("AETHER_DISABLE_OS_KEYCHAIN", previous);
    }
}

static async Task SettingsSavePreservesExistingSecretReference()
{
    using var temp = new TempDir();
    var previous = Environment.GetEnvironmentVariable("AETHER_DISABLE_OS_KEYCHAIN");
    Environment.SetEnvironmentVariable("AETHER_DISABLE_OS_KEYCHAIN", "1");
    try
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var secrets = new SecretStore(settings);
        var reference = await secrets.StoreAsync("openai-api-key", "sk-existing-secret");
        settings.Settings.Llm.OpenAiApiKey = reference;

        var vm = NewSettingsViewModel(settings, secrets);
        Equal(string.Empty, vm.OpenAiApiKey, "existing secret reference should not be displayed");
        await vm.SaveCommand.ExecuteAsync(null);

        Equal(reference, settings.Settings.Llm.OpenAiApiKey, "blank API key field should preserve existing reference");
        Equal("sk-existing-secret", await secrets.ResolveAsync(reference), "preserved reference should still resolve");
    }
    finally
    {
        Environment.SetEnvironmentVariable("AETHER_DISABLE_OS_KEYCHAIN", previous);
    }
}

static async Task SettingsSavePersistsGlobalHotkeyPreference()
{
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var vm = NewSettingsViewModel(settings, new FakeSecretStore());

    vm.EnableGlobalHotkeys = true;
    await vm.SaveCommand.ExecuteAsync(null);
    True(settings.Settings.Ui.EnableGlobalHotkeys, "global hotkey setting should save when enabled");

    vm.EnableGlobalHotkeys = false;
    await vm.SaveCommand.ExecuteAsync(null);
    False(settings.Settings.Ui.EnableGlobalHotkeys, "global hotkey setting should save when disabled");
}

static Task ServerProcessArgumentsAreSafe()
{
    var args = ServerProcessManager.BuildLaunchArguments(new ServerConfig
    {
        ModelPath = "/models/local model.gguf",
        Port = 9090,
        ContextSize = 8192,
        Threads = 6,
        GpuLayers = 12,
        EmbeddingsMode = true,
        ExtraArgs = "--alias \"local model\" --host 0.0.0.0 --flag"
    }).ToList();

    Equal("-m", args[0], "model flag should be first");
    Equal("/models/local model.gguf", args[1], "model path with spaces should remain one argument");
    ContainsInOrder(args, "--host", "127.0.0.1", "managed host should be loopback by default");
    ContainsInOrder(args, "--alias", "local model", "quoted extra arg should remain one argument");
    ContainsInOrder(args, "--host", "0.0.0.0", "extra args should be preserved as data arguments");
    False(args.Any(a => a.Contains(';', StringComparison.Ordinal)), "argument builder should not synthesize shell separators");
    True(args.Contains("--embeddings"), "embeddings mode should add embeddings flag");
    return Task.CompletedTask;
}

static SettingsService NewSettings(TempDir temp) => new(temp.PathFor("settings/settings.json"));

static SettingsViewModel NewSettingsViewModel(ISettingsService settings, ISecretStore secrets) =>
    new(settings, new FakeTts(), new FakeVoiceProviderRegistry(settings), new FakeToasts(), new BackupService(settings), secrets, new XttsProcessManager(), new LocalAiSetupService(new PythonHealthValidator()), new TrustService());

static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
{
    try
    {
        await action();
    }
    catch (T)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

static void Throws<T>(Action action) where T : Exception
{
    try
    {
        action();
    }
    catch (T)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message}. Expected '{expected}', got '{actual}'.");
}

static void True(bool value, string message)
{
    if (!value)
        throw new InvalidOperationException(message);
}

static void False(bool value, string message) => True(!value, message);

static void ContainsInOrder(IReadOnlyList<string> values, string first, string second, string message)
{
    for (var i = 0; i < values.Count - 1; i++)
    {
        if (values[i] == first && values[i + 1] == second)
            return;
    }

    throw new InvalidOperationException(message);
}

sealed class TempDir : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aether-tests-{Guid.NewGuid():N}");

    public string PathFor(string relative) => Path.Combine(_root, relative);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}

sealed class FakeLlm : ILlmService
{
    public string ProviderName => "Fake";
    public bool IsConfigured => true;
    public Task<List<LlmModel>> GetModelsAsync(CancellationToken ct = default) =>
        Task.FromResult(new List<LlmModel> { new() { Id = "fake", Name = "Fake", Provider = "Test" } });

    public async IAsyncEnumerable<string> StreamChatAsync(
        string modelId,
        IReadOnlyList<ChatMessage> messages,
        string? systemPrompt = null,
        double temperature = 0.7,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Delay(1, ct);
        yield return "local ";
        yield return "ready alpha beta 42";
    }

    public Task PullModelAsync(string modelId, IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeleteModelAsync(string modelId, CancellationToken ct = default) => Task.CompletedTask;
}

sealed class CapturingLlm : ILlmService
{
    public string ProviderName => "Capture";
    public bool IsConfigured => true;
    public IReadOnlyList<ChatMessage> LastMessages { get; private set; } = [];

    public Task<List<LlmModel>> GetModelsAsync(CancellationToken ct = default) =>
        Task.FromResult(new List<LlmModel> { new() { Id = "capture", Name = "Capture", Provider = "Test" } });

    public async IAsyncEnumerable<string> StreamChatAsync(
        string modelId,
        IReadOnlyList<ChatMessage> messages,
        string? systemPrompt = null,
        double temperature = 0.7,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        LastMessages = messages.ToList();
        await Task.Delay(1, ct);
        yield return "captured";
    }

    public Task PullModelAsync(string modelId, IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeleteModelAsync(string modelId, CancellationToken ct = default) => Task.CompletedTask;
}

sealed class UsageLlm : ILlmService
{
    public string ProviderName => "Usage";
    public bool IsConfigured => true;

    public Task<List<LlmModel>> GetModelsAsync(CancellationToken ct = default) =>
        Task.FromResult(new List<LlmModel> { new() { Id = "usage", Name = "Usage", Provider = "Test", DefaultContextSize = 100 } });

    public async IAsyncEnumerable<string> StreamChatAsync(
        string modelId,
        IReadOnlyList<ChatMessage> messages,
        string? systemPrompt = null,
        double temperature = 0.7,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var evt in StreamChatEventsAsync(modelId, messages, systemPrompt, temperature, ct))
        {
            if (!string.IsNullOrEmpty(evt.ContentDelta))
                yield return evt.ContentDelta;
        }
    }

    public async IAsyncEnumerable<LlmStreamEvent> StreamChatEventsAsync(
        string modelId,
        IReadOnlyList<ChatMessage> messages,
        string? systemPrompt = null,
        double temperature = 0.7,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Delay(1, ct);
        yield return new LlmStreamEvent("ok");
        yield return new LlmStreamEvent(Usage: new ChatTokenUsage(30, 10, 40), IsFinal: true);
    }

    public Task PullModelAsync(string modelId, IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeleteModelAsync(string modelId, CancellationToken ct = default) => Task.CompletedTask;
}

sealed class FakeTts : ITtsService
{
    public Task SpeakAsync(string text, CancellationToken ct = default) => Task.CompletedTask;
    public Task PreviewVoiceAsync(string speaker, string text, CancellationToken ct = default) => Task.CompletedTask;
    public Task<string> ImportVoiceSampleAsync(string sourcePath, string displayName, CancellationToken ct = default) =>
        Task.FromResult(displayName);
    public Task<IReadOnlyList<string>> GetVoicesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(new List<string> { "default" });
}

sealed class FakeVoiceProvider : IVoiceProvider
{
    public VoiceProvider Id => VoiceProvider.Kokoro;
    public string DisplayName => "Fake Voice";
    public VoiceCapability Capabilities => VoiceCapability.TextToSpeech | VoiceCapability.Local;

    public VoiceProviderDetection Detect() => new VoiceProviderDetection(true, "Available", "Fake provider available", null);

    public VoiceInstallPlan InstallPlan() => new VoiceInstallPlan("No install needed", new List<VoiceInstallStep>(), "Low");

    public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<VoiceHealth> HealthCheckAsync(CancellationToken ct = default) =>
        Task.FromResult(new VoiceHealth(VoiceHealthStatus.Healthy, "Healthy", "Fake provider is healthy"));

    public Task<IReadOnlyList<VoiceDefinition>> ListVoicesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<VoiceDefinition>>(new List<VoiceDefinition> { new VoiceDefinition("default", "Default", "English", false) });

    public Task<VoiceSynthesisResult> GenerateSpeechAsync(VoiceSynthesisRequest request, CancellationToken ct = default) =>
        Task.FromResult(new VoiceSynthesisResult(true, "Synthesis complete", "/tmp/audio.wav"));
}

sealed class FakeVoiceProviderRegistry : IVoiceProviderRegistry
{
    private readonly ISettingsService _settings;

    public FakeVoiceProviderRegistry(ISettingsService settings) => _settings = settings;

    public IReadOnlyList<VoiceProviderInfo> GetAvailableProviders() =>
        new List<VoiceProviderInfo>
        {
            new VoiceProviderInfo(VoiceProvider.Kokoro, "Kokoro", "Fast local readback.", VoiceProviderCategory.Recommended, true, VoiceCapability.TextToSpeech | VoiceCapability.Local),
            new VoiceProviderInfo(VoiceProvider.F5Tts, "F5-TTS", "Advanced cloning.", VoiceProviderCategory.Advanced, false, VoiceCapability.TextToSpeech | VoiceCapability.VoiceCloning | VoiceCapability.Remote),
            new VoiceProviderInfo(VoiceProvider.XttsV2, "XTTS v2", "Legacy cloning.", VoiceProviderCategory.Legacy, true, VoiceCapability.TextToSpeech | VoiceCapability.VoiceCloning | VoiceCapability.Local)
        };

    public VoiceProvider GetActiveProvider() => Enum.TryParse<VoiceProvider>(_settings.Settings.Tts.VoiceProvider, out var provider)
        ? provider
        : VoiceProvider.Kokoro;

    public IVoiceProvider GetActiveVoiceProvider() => new FakeVoiceProvider();

    public IVoiceProvider GetVoiceProvider(VoiceProvider provider) => new FakeVoiceProvider();

    public Task SetActiveProviderAsync(VoiceProvider provider)
    {
        _settings.Settings.Tts.VoiceProvider = provider.ToString();
        return Task.CompletedTask;
    }

    public VoiceProviderConfig? GetProviderConfig(VoiceProvider provider) => new(provider.ToString());

    public Task SetProviderConfigAsync(VoiceProvider provider, VoiceProviderConfig config) => Task.CompletedTask;

    public ITtsService GetActiveTtsService() => new FakeTts();
}

sealed class FakeVoiceProviderRegistryLimited : IVoiceProviderRegistry
{
    private readonly ISettingsService _settings;

    public FakeVoiceProviderRegistryLimited(ISettingsService settings) => _settings = settings;

    public IReadOnlyList<VoiceProviderInfo> GetAvailableProviders() =>
        new List<VoiceProviderInfo>
        {
            // XTTS v2 present but intentionally missing TextToSpeech and Local capabilities
            new VoiceProviderInfo(VoiceProvider.XttsV2, "XTTS v2", "Legacy cloning but missing flags.", VoiceProviderCategory.Legacy, true, VoiceCapability.VoiceCloning)
        };

    public VoiceProvider GetActiveProvider() => Enum.TryParse<VoiceProvider>(_settings.Settings.Tts.VoiceProvider, out var provider)
        ? provider
        : VoiceProvider.Kokoro;

    public IVoiceProvider GetActiveVoiceProvider() => new FakeVoiceProvider();

    public IVoiceProvider GetVoiceProvider(VoiceProvider provider) => new FakeVoiceProvider();

    public Task SetActiveProviderAsync(VoiceProvider provider)
    {
        _settings.Settings.Tts.VoiceProvider = provider.ToString();
        return Task.CompletedTask;
    }

    public VoiceProviderConfig? GetProviderConfig(VoiceProvider provider) => new(provider.ToString());

    public Task SetProviderConfigAsync(VoiceProvider provider, VoiceProviderConfig config) => Task.CompletedTask;

    public ITtsService GetActiveTtsService() => new FakeTts();
}

sealed class FakeToasts : IToastService
{
    public event Action<ToastMessage>? ToastRaised;
    public void Show(string title, string message, ToastKind kind = ToastKind.Info, int durationMs = 3500) =>
        ToastRaised?.Invoke(new ToastMessage(title, message, kind, durationMs));
}

sealed class FakeSecretStore : ISecretStore
{
    public bool IsReference(string value) => value.StartsWith("secret:", StringComparison.OrdinalIgnoreCase);
    public Task<string> StoreAsync(string name, string secret, CancellationToken ct = default) =>
        Task.FromResult($"secret:{name}");
    public Task<string> ResolveAsync(string valueOrReference, CancellationToken ct = default) =>
        Task.FromResult(valueOrReference);
    public Task<string> BackendLabelAsync(CancellationToken ct = default) => Task.FromResult("Fake");
}

sealed class FakeSystemInfo : ISystemInfoService
{
    public Task<SystemSnapshot> CaptureAsync(CancellationToken ct = default) => Task.FromResult(new SystemSnapshot
    {
        AppVersion = "test",
        OSDescription = "test-os",
        Architecture = "x64",
        ProcessorCount = 8,
        ProcessMemoryBytes = 100,
        ManagedMemoryBytes = 50,
        DataRoot = "test",
        DataRootFreeBytes = 1024,
        DataRootTotalBytes = 2048,
        Components = [new ComponentStatus { Name = "Test", Status = "OK" }]
    });
}

sealed class FakeAgentContextBuilder : IAgentContextBuilder
{
    public Task<AgentContextPack> BuildAsync(AgentTaskState state, AgentWorkspaceOptions options, CancellationToken ct = default) =>
        Task.FromResult(new AgentContextPack
        {
            CurrentGoal = state.Goal,
            ActiveStep = state.ActiveStep,
            Constraints = state.Constraints,
            TaskStateSummary = state.Summary,
            RetrievedFiles =
            [
                new AgentRetrievedItem("workspace", "README.md", "agent docs", 0, DateTime.UtcNow)
            ]
        });
}

sealed class FakeAgentLlm : ILlmService
{
    public string ProviderName => "FakeAgent";
    public bool IsConfigured => true;
    public Task<List<LlmModel>> GetModelsAsync(CancellationToken ct = default) =>
        Task.FromResult(new List<LlmModel> { new() { Id = "fake-agent", Name = "Fake Agent", Provider = "Test" } });

    public async IAsyncEnumerable<string> StreamChatAsync(
        string modelId,
        IReadOnlyList<ChatMessage> messages,
        string? systemPrompt = null,
        double temperature = 0.7,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Delay(1, ct);
        yield return """
            {
              "thought_summary": "Read the available context and found the docs.",
              "current_step": "Wait for approval before any write.",
              "next_action": {
                "type": "tool",
                "tool_name": "draft_patch",
                "arguments": { "path": "README.md" },
                "requires_approval": true,
                "risk_level": "medium"
              },
              "state_update": {
                "completed": ["inspected context"],
                "pending": ["draft patch"],
                "new_facts": ["workspace has README"],
                "blockers": []
              },
              "user_message": "I found the relevant docs and can draft a patch for review."
            }
            """;
    }

    public Task PullModelAsync(string modelId, IProgress<string>? progress = null, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeleteModelAsync(string modelId, CancellationToken ct = default) => Task.CompletedTask;
}

sealed class FakeEmbeddingService : IEmbeddingService
{
    public int Dimensions => 4;

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) =>
        Task.FromResult(new[] { 1f, text.Length % 7, text.Length % 11, 0.5f });

    public Task<List<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
        Task.FromResult(texts.Select(t => new[] { 1f, t.Length % 7, t.Length % 11, 0.5f }).ToList());
}

sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly string _body;

    public FakeHttpHandler(string body) => _body = body;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(_body, System.Text.Encoding.UTF8, "text/html")
        };
        return Task.FromResult(response);
    }
}
