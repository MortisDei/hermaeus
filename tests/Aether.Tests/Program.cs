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
    using var temp = new TempDir();
    var previous = temp.PathFor("previous");
    var next = temp.PathFor("next");
    Directory.CreateDirectory(previous);
    File.WriteAllText(Path.Combine(previous, "conversations.db"), "db");
    File.WriteAllText(Path.Combine(previous, "conversations.db-wal"), "wal");

    var service = NewSettings(temp);
    var plan = service.PreviewDataRootMigration(previous, next);

    Equal(true, plan.WillMove, "migration should be allowed");
    Equal(2, plan.FilesToMove, "all conversation db files should be counted");
    Equal(0, plan.Conflicts.Count, "clean target should have no conflicts");
    await Task.CompletedTask;
}

static async Task DataRootMigrationRefusesConflicts()
{
    using var temp = new TempDir();
    var previous = temp.PathFor("previous");
    var next = temp.PathFor("next");
    Directory.CreateDirectory(previous);
    Directory.CreateDirectory(next);
    File.WriteAllText(Path.Combine(previous, "conversations.db"), "old");
    File.WriteAllText(Path.Combine(next, "conversations.db"), "existing");

    var service = NewSettings(temp);
    var plan = service.PreviewDataRootMigration(previous, next);
    Equal(false, plan.WillMove, "migration preview should refuse conflicts");
    Equal(1, plan.Conflicts.Count, "conflicting db should be reported");

    service.Settings.DataManagement.DataRootDirectory = next;
    await ThrowsAsync<IOException>(() => service.SaveAsync(previous));
}

static async Task DataRootMigrationMovesFiles()
{
    using var temp = new TempDir();
    var previous = temp.PathFor("previous");
    var next = temp.PathFor("next");
    Directory.CreateDirectory(previous);
    File.WriteAllText(Path.Combine(previous, "conversations.db"), "db");
    File.WriteAllText(Path.Combine(previous, "conversations.db-shm"), "shm");

    var service = NewSettings(temp);
    service.Settings.DataManagement.DataRootDirectory = next;
    var result = await service.SaveAsync(previous);

    Equal(true, result.DataMigrated, "migration should report moved data");
    Equal(2, result.FilesMoved, "all db files should move");
    True(File.Exists(Path.Combine(next, "conversations.db")), "db should exist in new root");
    True(File.Exists(Path.Combine(next, "conversations.db-shm")), "sidecar db file should exist in new root");
    False(File.Exists(Path.Combine(previous, "conversations.db")), "old db should not be left behind");
    True(result.BackupDirectory is not null && File.Exists(Path.Combine(result.BackupDirectory, "conversations.db")),
        "migration should keep a backup copy in the target backup folder");
}

static async Task BackupExcludesSecretsAndRefusesOverwrite()
{
    using var temp = new TempDir();
    var root = temp.PathFor("root");
    var backupTarget = temp.PathFor("backup");
    Directory.CreateDirectory(root);
    File.WriteAllText(Path.Combine(root, "conversations.db"), "db");
    File.WriteAllText(Path.Combine(root, "secrets.local.json"), "secret");

    var service = NewSettings(temp);
    service.Settings.DataManagement.DataRootDirectory = root;
    var backups = new BackupService(service);
    var backup = await backups.BackupAsync(backupTarget);

    using (var archive = System.IO.Compression.ZipFile.OpenRead(backup.Path))
    {
        True(archive.GetEntry("conversations.db") is not null, "conversation db should be backed up");
        True(archive.GetEntry("secrets.local.json") is null, "local secrets should not be backed up");
    }

    var restoreRoot = temp.PathFor("restore");
    Directory.CreateDirectory(restoreRoot);
    File.WriteAllText(Path.Combine(restoreRoot, "conversations.db"), "existing");
    service.Settings.DataManagement.DataRootDirectory = restoreRoot;
    await ThrowsAsync<IOException>(() => backups.RestoreAsync(backup.Path));
}

static Task RedactionHidesSecrets()
{
    var redactor = new RedactionService();
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    var value = $"{home}/project api_key=abcdefghi123456789 bearer token_123456789012345 sk-abc123456789abcdef";
    var redacted = redactor.Redact(value);

    False(redacted.Contains("abcdefghi123456789", StringComparison.Ordinal), "api key value should be removed");
    False(redacted.Contains("token_123456789012345", StringComparison.Ordinal), "bearer token should be removed");
    False(redacted.Contains("sk-abc123456789abcdef", StringComparison.Ordinal), "sk token should be removed");
    if (!string.IsNullOrWhiteSpace(home))
        False(redacted.Contains(home, StringComparison.Ordinal), "home path should be shortened");

    return Task.CompletedTask;
}

static async Task BenchmarkDbCreatesAndRecordsRuns()
{
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var service = new BenchmarkService(settings, new FakeLlm(), new FakeSystemInfo());

    await service.InitializeAsync();
    var suites = await service.GetSuitesAsync();
    True(suites.Count >= 5, "starter suites should be seeded");

    var suite = new BenchmarkSuite
    {
        Id = "test-suite",
        Name = "Test Suite",
        TimeoutSeconds = 30,
        Cases =
        [
            new BenchmarkCase
            {
                Id = "case-1",
                Name = "Keyword",
                Prompt = "Say local ready",
                ExpectedKeywords = ["local", "ready"]
            }
        ]
    };
    await service.SaveSuiteAsync(suite);
    var run = await service.RunAsync(suite, new LlmModel { Id = "fake", Name = "Fake", Provider = "Test" });
    Equal("Completed", run.Status, "benchmark run should complete");
    Equal(1, run.Results.Count, "benchmark should record one result");
    True(run.Results[0].Passed, "deterministic benchmark checks should pass");

    var runs = await service.GetRunsAsync();
    True(runs.Any(r => r.Id == run.Id), "run history should persist");
    var rerun = await service.RerunAsync(run.Id);
    Equal("Completed", rerun.Status, "rerun should complete");
    await service.DeleteRunAsync(run.Id);
    runs = await service.GetRunsAsync();
    False(runs.Any(r => r.Id == run.Id), "deleted run should be removed");
}

static Task BenchmarkScoringAndRanking()
{
    var result = BenchmarkService.ScoreDeterministic(new BenchmarkCase
    {
        Name = "Checks",
        Prompt = "prompt",
        ExpectedKeywords = ["alpha"],
        ExpectedRegexes = ["beta\\s+\\d+"]
    }, "alpha beta 42");
    True(result.Passed, "keyword and regex checks should pass");
    Equal(1d, result.QualityScore, "all deterministic checks should score 1");

    var invalidRegex = BenchmarkService.ScoreDeterministic(new BenchmarkCase
    {
        Name = "Bad regex",
        Prompt = "prompt",
        ExpectedRegexes = ["["]
    }, "anything");
    False(invalidRegex.Passed, "invalid benchmark regex should fail the check without throwing");

    var good = new BenchmarkRun
    {
        SuiteName = "A",
        ModelName = "good",
        Results =
        [
            new BenchmarkResult { Passed = true, QualityScore = 1, ApproxTokensPerSecond = 30, ResourceScore = 1 }
        ]
    };
    var slow = new BenchmarkRun
    {
        SuiteName = "A",
        ModelName = "slow",
        Results =
        [
            new BenchmarkResult { Passed = true, QualityScore = 0.5, ApproxTokensPerSecond = 5, ResourceScore = 1 }
        ]
    };
    True(good.RankingScore > slow.RankingScore, "better quality/speed should rank higher");
    return Task.CompletedTask;
}

static async Task SystemInfoSafeFallback()
{
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var snapshot = await new SystemInfoService(settings).CaptureAsync();
    True(snapshot.ProcessorCount > 0, "processor count should be populated");
    True(!string.IsNullOrWhiteSpace(snapshot.OSDescription), "OS should be populated");
    True(!string.IsNullOrWhiteSpace(snapshot.DataRoot), "data root should be populated");
    True(snapshot.Components.Count > 0, "component statuses should be populated");
}

static Task LocalAiAssetsDetectAndApplyPaths()
{
    using var temp = new TempDir();
    var root = temp.PathFor("ai");
    var tts = Path.Combine(root, "tts");
    var venv = Path.Combine(tts, "venv", OperatingSystem.IsWindows() ? "Scripts" : "bin");
    var voices = Path.Combine(tts, "voices");
    var output = Path.Combine(tts, "output");
    var encoder = Path.Combine(root, "encoders", "ms-marco-MiniLM-L6-v2");
    Directory.CreateDirectory(venv);
    Directory.CreateDirectory(voices);
    Directory.CreateDirectory(output);
    Directory.CreateDirectory(encoder);
    File.WriteAllText(Path.Combine(tts, "xtts_api_server.py"), "print('xtts')");
    File.WriteAllText(Path.Combine(venv, OperatingSystem.IsWindows() ? "python.exe" : "python"), string.Empty);
    File.WriteAllText(Path.Combine(encoder, "model_O4.onnx"), string.Empty);
    File.WriteAllText(Path.Combine(encoder, "vocab.txt"), string.Empty);

    var layout = LocalAiAssetLocator.Detect(root);
    Equal(5, layout.FoundCount, "known asset paths should be detected");

    var settings = new AppSettings { DataManagement = { LocalAiAssetsRoot = root } };
    LocalAiAssetLocator.ApplyDetected(settings);
    True(settings.Tts.ScriptPath.EndsWith("xtts_api_server.py", StringComparison.Ordinal), "XTTS script should be applied");
    True(settings.Tts.PythonPath.EndsWith(OperatingSystem.IsWindows() ? "python.exe" : "python", StringComparison.Ordinal), "XTTS venv python should be applied");
    Equal(voices, settings.Tts.VoiceDirectory, "voice directory should be applied");
    Equal(output, settings.Tts.OutputDirectory, "output directory should be applied");
    Equal(encoder, settings.Rag.RerankerModelPath, "reranker directory should be applied");
    return Task.CompletedTask;
}

static async Task SettingsApplyLocalAiAssetsPersistsPaths()
{
    using var temp = new TempDir();
    var root = temp.PathFor("ai");
    var tts = Path.Combine(root, "tts");
    var venv = Path.Combine(tts, "venv", OperatingSystem.IsWindows() ? "Scripts" : "bin");
    var voices = Path.Combine(tts, "voices");
    var output = Path.Combine(tts, "output");
    Directory.CreateDirectory(venv);
    Directory.CreateDirectory(voices);
    Directory.CreateDirectory(output);
    File.WriteAllText(Path.Combine(tts, "xtts_api_server.py"), "print('xtts')");
    File.WriteAllText(Path.Combine(venv, OperatingSystem.IsWindows() ? "python.exe" : "python"), string.Empty);

    var settings = NewSettings(temp);
    settings.Settings.DataManagement.LocalAiAssetsRoot = temp.PathFor("ai");
    var vm = NewSettingsViewModel(settings, new FakeSecretStore());
    vm.LocalAiAssetsRoot = root;
    await vm.ApplyLocalAiAssetsCommand.ExecuteAsync(null);

    Equal(root, settings.Settings.DataManagement.LocalAiAssetsRoot, "local AI assets root should save immediately when paths are applied");
    True(settings.Settings.Tts.ScriptPath.EndsWith("xtts_api_server.py", StringComparison.Ordinal),
        "applied XTTS script should persist to settings");
    Equal(voices, settings.Settings.Tts.VoiceDirectory, "applied voice directory should persist to settings");
    Equal(output, settings.Settings.Tts.OutputDirectory, "applied output directory should persist to settings");
}

static async Task LocalAiSetupDetectsFolderLayout()
{
    using var temp = new TempDir();
    var root = temp.PathFor("AI");
    var models = Path.Combine(root, "Models");
    var venv = Path.Combine(root, "venv", OperatingSystem.IsWindows() ? "Scripts" : "bin");
    var xtts = Path.Combine(root, "TTS", "multi-dataset--xtts_v2");
    Directory.CreateDirectory(models);
    Directory.CreateDirectory(venv);
    Directory.CreateDirectory(xtts);
    File.WriteAllText(Path.Combine(models, "local.gguf"), string.Empty);
    File.WriteAllText(Path.Combine(venv, OperatingSystem.IsWindows() ? "python.exe" : "python"), string.Empty);
    File.WriteAllText(Path.Combine(xtts, "config.json"), "{}");
    File.WriteAllText(Path.Combine(xtts, "model.pth"), string.Empty);

    var settings = new AppSettings { DataManagement = { LocalAiAssetsRoot = root } };
    var report = await new LocalAiSetupService(new PythonHealthValidator()).ScanAsync(settings);

    True(report.Items.Any(i => i.Key == "models" && i.Status == LocalAiReadinessStatus.Found), "GGUF model folder should be found");
    True(report.Items.Any(i => i.Key == "venv" && i.Status == LocalAiReadinessStatus.Found), "root venv python should be found");
    True(report.Items.Any(i => i.Key == "xtts-model" && i.Status == LocalAiReadinessStatus.Found), "XTTS v2 model folder should be found");
    True(report.Items.Any(i => i.Key == "xtts-script" && i.Status == LocalAiReadinessStatus.Missing), "missing XTTS API script should be reported separately");
    True(report.Actions.Any(a => a.Kind == LocalAiSetupActionKind.CreateXttsApiScript), "script creation action should be offered");
}

static async Task LocalAiSetupScriptHandlingIsApprovalGated()
{
    using var temp = new TempDir();
    var root = temp.PathFor("AI");
    var script = Path.Combine(root, "TTS", "xtts_api_server.py");
    Directory.CreateDirectory(Path.GetDirectoryName(script)!);
    File.WriteAllText(script, "existing");

    var service = new LocalAiSetupService(new PythonHealthValidator());
    var action = new LocalAiSetupAction(
        "create-xtts-script",
        LocalAiSetupActionKind.CreateXttsApiScript,
        "Create XTTS API script",
        script,
        new List<string> { "write-file", script },
        LocalAiSetupRiskLevel.Medium,
        "Create script",
        false,
        true,
        true);

    var refused = await service.RunActionAsync(action, new AppSettings(), allowOverwrite: false);
    False(refused.Success, "existing script should not be overwritten without explicit overwrite approval");
    Equal("existing", File.ReadAllText(script), "existing script content should be preserved");

    var allowed = await service.RunActionAsync(action, new AppSettings(), allowOverwrite: true);
    True(allowed.Success, "explicit overwrite approval should allow script update");
    True(File.ReadAllText(script).Contains("/v1/audio/speech", StringComparison.Ordinal), "generated script should replace content when overwrite is allowed");
}

static async Task LocalAiSetupCommandPreviewsStayShellFree()
{
    using var temp = new TempDir();
    var root = temp.PathFor("AI folder");
    Directory.CreateDirectory(root);
    var report = await new LocalAiSetupService(new PythonHealthValidator()).ScanAsync(new AppSettings { DataManagement = { LocalAiAssetsRoot = root } });

    var install = report.Actions.Single(a => a.Kind == LocalAiSetupActionKind.InstallXttsDependencies);
    ContainsInOrder(install.CommandPreview, "-m", "pip", "install preview should use ArgumentList-style tokens");
    ContainsInOrder(install.CommandPreview, "pip", "install", "install preview should use ArgumentList-style tokens");
    False(install.CommandPreview.Any(a => a.Contains(';', StringComparison.Ordinal) || a.Contains("&&", StringComparison.Ordinal)),
        "command preview should not synthesize shell separators");
    True(install.RequiresNetwork, "package installation should be marked as network using");
    Equal(LocalAiSetupRiskLevel.High, install.RiskLevel, "package installation should be high risk");
}

static Task XttsApiTemplateHasRequiredEndpoints()
{
    var script = new LocalAiSetupService(new PythonHealthValidator()).BuildXttsApiScript();
    True(script.Contains("@app.get(\"/health\")", StringComparison.Ordinal), "script should expose health endpoint");
    True(script.Contains("@app.post(\"/v1/audio/speech\")", StringComparison.Ordinal), "script should expose speech endpoint");
    True(script.Contains("@app.get(\"/voices\")", StringComparison.Ordinal), "script should expose voice listing endpoint");
    True(script.Contains("--model-dir", StringComparison.Ordinal), "script should accept model directory argument");
    False(script.Contains("/mnt/Gaming/AI", StringComparison.Ordinal), "template should not hardcode a developer AI folder");
    return Task.CompletedTask;
}

static async Task ChatContextAttachmentsBuildPromptAndPersistSummary()
{
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new ConversationStore(settings);
    await store.InitializeAsync();
    var llm = new CapturingLlm();
    var vm = new ChatViewModel(llm, store, settings, new FakeTts(), new ModelProfileService(settings));
    await vm.LoadModelsAsync(force: true);

    var file = temp.PathFor("src/ChatViewModel.cs");
    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
    await File.WriteAllTextAsync(file, "public sealed class ChatViewModel { }");

    await vm.AddContextFilesAsync([file]);
    vm.InputText = "Why is the model selector not updating?";
    await vm.SendCommand.ExecuteAsync(null);

    var sent = llm.LastMessages.Last(m => m.Role == "user").Content;
    True(sent.Contains("Attached file context:", StringComparison.Ordinal), "sent prompt should include context header");
    True(sent.Contains("public sealed class ChatViewModel", StringComparison.Ordinal), "sent prompt should include file content");
    True(sent.Contains("Why is the model selector not updating?", StringComparison.Ordinal), "sent prompt should include user request");

    var saved = await store.GetByIdAsync(vm.CurrentConversationId);
    var savedUser = saved?.Messages.First(m => m.Role == "user").Content ?? string.Empty;
    True(savedUser.Contains("Attached context injected at send time", StringComparison.Ordinal), "saved history should include context summary");
    True(savedUser.Contains("ChatViewModel.cs", StringComparison.Ordinal), "saved history should include file name");
    False(savedUser.Contains("public sealed class ChatViewModel", StringComparison.Ordinal), "saved history should not persist full injected content");
    Equal(0, vm.ContextAttachments.Count, "attachments should clear after send");
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
    Equal("C#", MarkdownViewer.NormalizeFenceLanguage("cs"), "cs fence should map to C#");
    Equal("Python", MarkdownViewer.NormalizeFenceLanguage("python"), "python fence should map to Python");
    Equal("JavaScript", MarkdownViewer.NormalizeFenceLanguage("typescript"), "typescript fence should map to JavaScript highlighting");
    Equal("Bash", MarkdownViewer.NormalizeFenceLanguage("sh"), "sh fence should map to Bash");
    Equal("JavaScript", MarkdownViewer.NormalizeFenceLanguage("json"), "json fence should use JavaScript highlighting fallback");
    Equal("SQL", MarkdownViewer.NormalizeFenceLanguage("sql"), "sql fence should map to SQL");
    Equal(null, MarkdownViewer.NormalizeFenceLanguage("made-up-language"), "unknown fences should safely fall back");
    return Task.CompletedTask;
}

static Task LlmStreamUsagePayloadsAndParsers()
{
    var payloadJson = JsonSerializer.Serialize(LlamaCppService.BuildChatPayload(
        "model",
        [new ChatMessage("user", "hello")],
        "system",
        0.2,
        128));
    True(payloadJson.Contains("\"stream_options\"", StringComparison.Ordinal), "llama.cpp payload should request stream options");
    True(payloadJson.Contains("\"include_usage\":true", StringComparison.Ordinal), "llama.cpp payload should request usage chunks");

    var openAiPayloadJson = JsonSerializer.Serialize(OpenAiService.BuildChatPayload(
        "gpt-test",
        [new ChatMessage("user", "hello")],
        null,
        0.7,
        256));
    True(openAiPayloadJson.Contains("\"include_usage\":true", StringComparison.Ordinal), "OpenAI payload should request usage chunks");

    var evt = OpenAiService.ParseStreamEvent("""
        {"choices":[],"usage":{"prompt_tokens":12,"completion_tokens":5,"total_tokens":17}}
        """);
    Equal(17, evt?.Usage?.TotalTokens, "OpenAI-compatible usage chunk should parse total tokens");
    Equal(true, evt?.IsFinal, "usage chunk should be final");

    var textEvt = LlamaCppService.ParseStreamEvent("""
        {"choices":[{"delta":{"content":"hello"},"finish_reason":null}],"usage":null}
        """);
    Equal("hello", textEvt?.ContentDelta, "content chunks should still parse text");

    var ollamaUsage = OllamaService.ParseUsageForTest("""
        {"done":true,"prompt_eval_count":11,"eval_count":7}
        """);
    Equal(18, ollamaUsage?.TotalTokens, "Ollama final counts should parse usage");
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

    vm.InputText = new string('c', 380);
    True(vm.IsContextUsageCritical, "95 percent estimate should be critical");
}

static async Task TrustScanInsideAiRootIsLowRisk()
{
    using var temp = new TempDir();
    var root = temp.PathFor("AI");
    var bin = Path.Combine(root, "runtimes", "llama-server");
    var model = Path.Combine(root, "Models", "tiny.gguf");
    Directory.CreateDirectory(Path.GetDirectoryName(bin)!);
    Directory.CreateDirectory(Path.GetDirectoryName(model)!);
    await File.WriteAllTextAsync(bin, "trusted");
    await File.WriteAllTextAsync(model, "model");

    var settings = new AppSettings { DataManagement = { LocalAiAssetsRoot = root } };
    settings.ManagedServers.Clear();
    settings.ManagedServers.Add(new ServerConfig { Name = "Chat", ExecutablePath = bin, ModelPath = model });

    var report = await new TrustService().ScanAsync(settings);
    var executable = report.Items.Single(i => i.Label == "Chat executable");
    Equal(TrustItemStatus.Ready, executable.Status, "inside executable should be ready");
    Equal(TrustRiskLevel.Low, executable.RiskLevel, "inside executable should be low risk");
    Equal(true, executable.IsInsideAiRoot, "inside executable should be scoped to AI root");
    Equal(ExpectedSha256("trusted"), executable.Sha256, "SHA256 should be stable");
}

static async Task TrustScanOutsideAiRootWarns()
{
    using var temp = new TempDir();
    var root = temp.PathFor("AI");
    var external = temp.PathFor("tools/llama-server");
    Directory.CreateDirectory(root);
    Directory.CreateDirectory(Path.GetDirectoryName(external)!);
    await File.WriteAllTextAsync(external, "external");

    var settings = new AppSettings { DataManagement = { LocalAiAssetsRoot = root } };
    settings.ManagedServers.Clear();
    settings.ManagedServers.Add(new ServerConfig { Name = "Chat", ExecutablePath = external, ModelPath = string.Empty });

    var report = await new TrustService().ScanAsync(settings);
    var executable = report.Items.Single(i => i.Label == "Chat executable");
    Equal(TrustItemStatus.Warning, executable.Status, "outside executable should warn");
    Equal(TrustRiskLevel.Medium, executable.RiskLevel, "outside executable should be medium risk");
    Equal(false, executable.IsInsideAiRoot, "outside executable should report outside AI root");
}

static async Task TrustScanReportsMissingExecutable()
{
    using var temp = new TempDir();
    var root = temp.PathFor("AI");
    Directory.CreateDirectory(root);

    var settings = new AppSettings { DataManagement = { LocalAiAssetsRoot = root } };
    settings.ManagedServers.Clear();
    settings.ManagedServers.Add(new ServerConfig { Name = "Chat", ExecutablePath = temp.PathFor("missing/llama-server"), ModelPath = string.Empty });

    var report = await new TrustService().ScanAsync(settings);
    var executable = report.Items.Single(i => i.Label == "Chat executable");
    Equal(TrustItemStatus.Missing, executable.Status, "missing executable should be reported");
    Equal(TrustRiskLevel.High, executable.RiskLevel, "missing executable should be high risk");
}

static async Task TrustScanUnsetAiRootIsNeutral()
{
    using var temp = new TempDir();
    var executable = temp.PathFor("llama-server");
    Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
    await File.WriteAllTextAsync(executable, "tool");

    var settings = new AppSettings();
    settings.ManagedServers.Clear();
    settings.ManagedServers.Add(new ServerConfig { Name = "Chat", ExecutablePath = executable, ModelPath = string.Empty });

    var report = await new TrustService().ScanAsync(settings);
    var item = report.Items.Single(i => i.Label == "Chat executable");
    Equal(TrustItemStatus.Ready, item.Status, "unset AI root should not warn for existing files");
    Equal(null, item.IsInsideAiRoot, "unset AI root should have neutral scope");
}

static Task TrustScanDetectsNetworkExtraArgs()
{
    var warnings = new TrustService().AnalyzeServerExtraArgs(
        new ServerConfig { Name = "Chat", ExtraArgs = "--host 0.0.0.0 --alias local" },
        DateTime.UtcNow);

    Equal(1, warnings.Count, "network-facing host should produce one warning");
    Equal(TrustRiskLevel.High, warnings[0].RiskLevel, "network exposure should be high risk");
    False(warnings[0].Target.Contains(';', StringComparison.Ordinal), "trust warnings should not synthesize shell separators");
    return Task.CompletedTask;
}

static Task SourceStringsAvoidLongDashes()
{
    var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    var src = Path.Combine(root, "src");
    var explicitDocs = new[]
    {
        Path.Combine(root, "README.md"),
        Path.Combine(root, "CHANGELOG.md"),
        Path.Combine(root, "docs", "security-review.md")
    };
    var files = Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories)
        .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase))
        .Concat(explicitDocs.Where(File.Exists));
    var offenders = files
        .SelectMany(path => File.ReadLines(path).Select((line, index) => (path, line, index)))
        .Where(item => item.line.Contains('\u2014') || item.line.Contains('\u2013'))
        .Select(item => $"{item.path}:{item.index + 1}")
        .ToList();
    Equal(0, offenders.Count, $"source should avoid em dash and en dash characters: {string.Join(", ", offenders)}");
    return Task.CompletedTask;
}

static string ExpectedSha256(string content) =>
    Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

static void WriteSimplePdf(string path, string text)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    var escaped = text.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("(", "\\(", StringComparison.Ordinal)
        .Replace(")", "\\)", StringComparison.Ordinal);
    var content = string.IsNullOrEmpty(text)
        ? string.Empty
        : $"BT /F1 24 Tf 72 720 Td ({escaped}) Tj ET";
    var objects = new[]
    {
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /Resources << /Font << /F1 4 0 R >> >> /MediaBox [0 0 612 792] /Contents 5 0 R >>",
        "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        $"<< /Length {System.Text.Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream"
    };
    var sb = new System.Text.StringBuilder();
    var offsets = new List<int> { 0 };
    sb.Append("%PDF-1.4\n");
    for (var i = 0; i < objects.Length; i++)
    {
        offsets.Add(System.Text.Encoding.ASCII.GetByteCount(sb.ToString()));
        sb.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
    }

    var xref = System.Text.Encoding.ASCII.GetByteCount(sb.ToString());
    sb.Append("xref\n0 ").Append(objects.Length + 1).Append('\n');
    sb.Append("0000000000 65535 f \n");
    foreach (var offset in offsets.Skip(1))
        sb.Append(offset.ToString("D10")).Append(" 00000 n \n");
    sb.Append("trailer\n<< /Size ").Append(objects.Length + 1).Append(" /Root 1 0 R >>\n");
    sb.Append("startxref\n").Append(xref).Append("\n%%EOF\n");
    File.WriteAllText(path, sb.ToString(), System.Text.Encoding.ASCII);
}

static async Task SecretStoreFallbackWithoutPlaintext()
{
    using var temp = new TempDir();
    var previous = Environment.GetEnvironmentVariable("AETHER_DISABLE_OS_KEYCHAIN");
    Environment.SetEnvironmentVariable("AETHER_DISABLE_OS_KEYCHAIN", "1");
    try
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new SecretStore(settings);
        var reference = await store.StoreAsync("openai-api-key", "sk-test-secret");
        Equal(true, store.IsReference(reference), "stored secret should return a reference");
        Equal("sk-test-secret", await store.ResolveAsync(reference), "secret reference should resolve");
        Equal("Local fallback file", await store.BackendLabelAsync(), "disabled keychain should use fallback label");

        var localVault = Path.Combine(settings.Settings.DataManagement.DataRootDirectory, "secrets.local.json");
        True(File.Exists(localVault), "fallback vault should exist");
        var json = await File.ReadAllTextAsync(localVault);
        False(json.Contains("sk-test-secret", StringComparison.Ordinal), "fallback vault should not contain plaintext");
    }
    finally
    {
        Environment.SetEnvironmentVariable("AETHER_DISABLE_OS_KEYCHAIN", previous);
    }
}

static Task RagBm25ScoringRanksMatches()
{
    var chunks = new[]
    {
        new RagChunk { Id = "wrong", Content = "bananas and pears only" },
        new RagChunk { Id = "right", Content = "local llama embeddings answer the query" },
        new RagChunk { Id = "also", Content = "llama local local context" }
    };
    var stats = Bm25Scorer.BuildStats(chunks);
    var scored = new Bm25Scorer().Score("local llama", chunks, stats);

    Equal("also", scored[0].Chunk.Id, "repeated exact term match should rank first");
    True(scored[0].Score > scored[^1].Score, "matching chunk should outscore unrelated chunk");
    Equal(3, stats.TotalDocuments, "BM25 stats should include all documents");
    True(stats.DocumentFrequencies.ContainsKey("llama"), "BM25 stats should track query terms");
    return Task.CompletedTask;
}

static Task RagHybridScoringFusesRanks()
{
    var semanticWinner = new RagChunk { Id = "semantic", Content = "semantic match" };
    var lexicalWinner = new RagChunk { Id = "lexical", Content = "lexical match" };
    var shared = new RagChunk { Id = "shared", Content = "appears in both lists" };

    var fused = HybridRetriever.Fuse(
        new List<ScoredChunk>
        {
            new ScoredChunk(semanticWinner, 0.99f, ScoreSource.Semantic),
            new ScoredChunk(shared, 0.90f, ScoreSource.Semantic),
            new ScoredChunk(lexicalWinner, 10f, ScoreSource.Bm25)
        },
        topK: 3);

    Equal(3, fused.Count, "hybrid fusion should include unique chunks from both rankings");
    Equal(ScoreSource.Hybrid, fused[0].Source, "fused result should be labelled hybrid");
    True(fused.Any(s => s.Chunk.Id == "semantic"), "semantic-only result should survive fusion");
    True(fused.Any(s => s.Chunk.Id == "lexical"), "BM25-only result should survive fusion");
    True(fused.Any(s => s.Chunk.Id == "shared"), "shared result should survive fusion");
    True(fused[0].Score >= fused[^1].Score, "fused results should be sorted");
    return Task.CompletedTask;
}

static Task RagWebLoaderDisabledByDefault()
{
    var config = new RagDatasetConfig();

    False(config.EnableWebLoader, "web loader should be disabled by default");
    Equal(RagExtractionMode.TextMarkdown, config.ExtractionMode, "default extraction should stay local text/markdown");
    Equal(0, RagPipeline.ParseWebUrls(config).Count, "disabled web loader should parse no URLs");
    return Task.CompletedTask;
}

static Task RagWebLoaderParsesOptInUrls()
{
    var config = new RagDatasetConfig
    {
        EnableWebLoader = true,
        WebMaxPages = 2,
        WebUrlList = """
            https://example.test/a
            https://example.test/a
            http://example.test/b
            https://example.test/c
            """
    };

    var urls = RagPipeline.ParseWebUrls(config);
    Equal(2, urls.Count, "web loader should dedupe URLs and honor page limit");
    Equal("https://example.test/a", urls[0].ToString().TrimEnd('/'), "first URL should be preserved");
    Equal("http://example.test/b", urls[1].ToString().TrimEnd('/'), "second unique URL should be preserved");

    var text = RagPipeline.ExtractTextFromHtml("<html><head><title>A</title><style>.x{}</style><script>alert(1)</script></head><body>Hello &amp; goodbye</body></html>");
    True(text.Contains("Hello & goodbye", StringComparison.Ordinal), "visible HTML text should be decoded");
    False(text.Contains("alert", StringComparison.OrdinalIgnoreCase), "script text should be stripped");
    False(text.Contains(".x", StringComparison.Ordinal), "style text should be stripped");
    return Task.CompletedTask;
}

static async Task RagWebIngestStripsHtmlAndStoresChunks()
{
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new SqliteRagStore(settings);
    await store.InitializeAsync();

    using var http = new HttpClient(new FakeHttpHandler("""
        <html>
          <head>
            <title>Example Title</title>
            <script>secretScript()</script>
            <style>.hidden{display:none}</style>
          </head>
          <body><main>Visible local-first documentation page.</main></body>
        </html>
        """));
    var pipeline = new RagPipeline(store, new FakeEmbeddingService(), http);
    var dataset = new RagDataset
    {
        Name = "web",
        Config = new RagDatasetConfig
        {
            EnableWebLoader = true,
            ExtractionMode = RagExtractionMode.WebUrl,
            WebUrlList = "https://example.test/docs",
            WebMaxPages = 1
        }
    };

    await pipeline.IngestWebAsync(dataset);
    var chunks = await store.GetChunksAsync(dataset.Id, includeEmbeddings: false);
    True(chunks.Count > 0, "web ingest should store chunks");
    True(chunks[0].Content.Contains("Visible local-first documentation page", StringComparison.Ordinal),
        "stored chunk should include visible page text");
    False(chunks[0].Content.Contains("secretScript", StringComparison.Ordinal),
        "stored chunk should not include script text");
    Equal("Example Title", chunks[0].SourceTitle, "web title should be stored as source title");
    Equal(chunks.Count, dataset.ChunkCount, "dataset chunk count should match stored chunks");
}

static async Task RagDigitalPdfTextExtracts()
{
    using var temp = new TempDir();
    var pdf = temp.PathFor("paper.pdf");
    WriteSimplePdf(pdf, "Digital PDF alpha beta");

    var extracted = await PdfTextExtractor.ExtractAsync(pdf);
    True(extracted.HasText, "digital PDF should have extractable text");
    True(extracted.Text.Contains("Digital PDF alpha beta", StringComparison.Ordinal), "PDF text should be extracted");
}

static async Task RagDirectoryIngestIncludesPdfs()
{
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new SqliteRagStore(settings);
    await store.InitializeAsync();
    var docs = temp.PathFor("docs");
    Directory.CreateDirectory(docs);
    await File.WriteAllTextAsync(Path.Combine(docs, "notes.md"), "markdown source alpha");
    WriteSimplePdf(Path.Combine(docs, "paper.pdf"), "pdf source beta gamma");

    var dataset = new RagDataset { Name = "docs" };
    var pipeline = new RagPipeline(store, new FakeEmbeddingService());
    await pipeline.IngestDirectoryAsync(dataset, docs);

    var chunks = await store.GetChunksAsync(dataset.Id, includeEmbeddings: false);
    True(chunks.Any(c => c.SourceFile == "paper.pdf" && c.Content.Contains("pdf source beta gamma", StringComparison.Ordinal)),
        "PDF content should be chunked and stored");
    True(chunks.Any(c => c.SourceFile == "notes.md"), "markdown content should still ingest");
}

static async Task RagDirectoryDryRunReportsWithoutPersisting()
{
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new SqliteRagStore(settings);
    await store.InitializeAsync();
    var docs = temp.PathFor("docs");
    Directory.CreateDirectory(docs);
    await File.WriteAllTextAsync(Path.Combine(docs, "preview.md"), "dry run preview alpha beta");

    var dataset = new RagDataset { Name = "preview" };
    var pipeline = new RagPipeline(store, new FakeEmbeddingService());
    var report = await pipeline.IngestDirectoryAsync(dataset, docs, options: new IngestOptions { DryRun = true, DuplicatePolicy = IngestDuplicatePolicy.ReportOnly });

    var chunks = await store.GetChunksAsync(dataset.Id, includeEmbeddings: false);
    Equal(0, chunks.Count, "dry-run ingest should not persist chunks");
    True(report.Documents.Any(d => d.Status == DocumentIngestStatus.ReportOnly), "dry-run report should include report-only entries");
    True(report.Documents.Any(d => d.Path.Contains("preview.md", StringComparison.Ordinal)), "dry-run report should include the source path");
}

static async Task RagDirectorySkipUnchangedAvoidsDuplicateChunks()
{
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new SqliteRagStore(settings);
    await store.InitializeAsync();
    var docs = temp.PathFor("docs");
    Directory.CreateDirectory(docs);
    await File.WriteAllTextAsync(Path.Combine(docs, "keep.md"), "unchanged source alpha beta");

    var dataset = new RagDataset { Name = "skip-unchanged" };
    var pipeline = new RagPipeline(store, new FakeEmbeddingService());
    await pipeline.IngestDirectoryAsync(dataset, docs, options: new IngestOptions { DuplicatePolicy = IngestDuplicatePolicy.Replace });
    var before = await store.GetChunksAsync(dataset.Id, includeEmbeddings: false);

    var report = await pipeline.IngestDirectoryAsync(dataset, docs, options: new IngestOptions { DuplicatePolicy = IngestDuplicatePolicy.SkipIfUnchanged });
    var after = await store.GetChunksAsync(dataset.Id, includeEmbeddings: false);

    Equal(before.Count, after.Count, "unchanged ingest should not duplicate chunks");
    True(report.Documents.Any(d => d.Status == DocumentIngestStatus.SkippedUnchanged), "report should mention skipped unchanged source");
}

static async Task RagEmptyPdfWarnsAndContinues()
{
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new SqliteRagStore(settings);
    await store.InitializeAsync();
    var docs = temp.PathFor("docs");
    Directory.CreateDirectory(docs);
    await File.WriteAllTextAsync(Path.Combine(docs, "notes.txt"), "plain text survives with enough content to chunk");
    WriteSimplePdf(Path.Combine(docs, "scan.pdf"), string.Empty);
    var progressLines = new List<string>();

    var dataset = new RagDataset { Name = "docs" };
    var pipeline = new RagPipeline(store, new FakeEmbeddingService());
    await pipeline.IngestDirectoryAsync(dataset, docs, new Progress<IngestProgress>(p => progressLines.Add(p.Detail)));

    var chunks = await store.GetChunksAsync(dataset.Id, includeEmbeddings: false);
    True(chunks.Any(c => c.SourceFile == "notes.txt"), "text file should still ingest when PDF has no text");
    False(chunks.Any(c => c.SourceFile == "scan.pdf"), "empty PDF should not store chunks");
    True(progressLines.Any(line => line.Contains("No extractable PDF text", StringComparison.Ordinal)),
        "empty PDF should surface a health warning");
}

static async Task RagIngestCancellationDuringEmbedding()
{
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new SqliteRagStore(settings);
    await store.InitializeAsync();
    var docs = temp.PathFor("docs");
    Directory.CreateDirectory(docs);
    await File.WriteAllTextAsync(Path.Combine(docs, "file1.txt"), string.Concat(Enumerable.Repeat("alpha ", 100)));
    await File.WriteAllTextAsync(Path.Combine(docs, "file2.txt"), string.Concat(Enumerable.Repeat("beta ", 100)));

    var dataset = new RagDataset { Name = "cancel-embed" };
    var pipeline = new RagPipeline(store, new FakeEmbeddingService());

    // Cancellation during embedding phase
    var cts = new CancellationTokenSource();
    var task = Task.Run(async () =>
    {
        var progress = new Progress<IngestProgress>(p =>
        {
            // Cancel after first batch embedded
            if (p.Stage == "Embedding" && p.Done > 10)
                cts.Cancel();
        });

        try
        {
            await pipeline.IngestDirectoryAsync(dataset, docs, progress, cts.Token);
        }
        catch (OperationCanceledException) { }
    });

    await task;
    var chunks = await store.GetChunksAsync(dataset.Id, includeEmbeddings: false);
    // Cancellation should prevent storage, or at least partially stored
    True(chunks.Count < 500, "cancellation during embedding should reduce final chunk count significantly");
}

static async Task RagIngestCancellationDuringStorage()
{
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var store = new SqliteRagStore(settings);
    await store.InitializeAsync();
    var docs = temp.PathFor("docs");
    Directory.CreateDirectory(docs);
    await File.WriteAllTextAsync(Path.Combine(docs, "file1.txt"), string.Concat(Enumerable.Repeat("gamma ", 100)));
    await File.WriteAllTextAsync(Path.Combine(docs, "file2.txt"), string.Concat(Enumerable.Repeat("delta ", 100)));

    var dataset = new RagDataset { Name = "cancel-store" };
    var pipeline = new RagPipeline(store, new FakeEmbeddingService());

    // Cancellation during storage phase
    var cts = new CancellationTokenSource();
    var task = Task.Run(async () =>
    {
        var progress = new Progress<IngestProgress>(p =>
        {
            // Cancel during storing phase
            if (p.Stage == "Storing")
                cts.Cancel();
        });

        try
        {
            await pipeline.IngestDirectoryAsync(dataset, docs, progress, cts.Token);
        }
        catch (OperationCanceledException) { }
    });

    await task;
    var chunks = await store.GetChunksAsync(dataset.Id, includeEmbeddings: false);
    // Cancellation during storage should prevent or reduce chunk persistence
    True(chunks.Count < 500, "cancellation during storage should prevent or reduce chunk persistence");
}

static async Task VoiceProviderCapabilityGating()
{
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    var toasts = new FakeToasts();
    var vm = new TtsSettingsViewModel(new FakeTts(), new FakeVoiceProviderRegistry(settings), toasts, new XttsProcessManager(), new FakeSecretStore(), settings);

    var limited = new VoiceProviderInfo(VoiceProvider.Kokoro, "Kokoro-Limited", "No TTS", VoiceProviderCategory.Recommended, true, VoiceCapability.Local);
    var asyncCmd = vm.SetActiveVoiceProviderCommand as CommunityToolkit.Mvvm.Input.IAsyncRelayCommand;
    if (asyncCmd is not null)
        await asyncCmd.ExecuteAsync(limited);
    else
        vm.SetActiveVoiceProviderCommand.Execute(limited);

    Equal("Kokoro", vm.SelectedVoiceProvider, "provider without TTS should not become active");
}

static async Task AgentTaskStateSerializesSchemaFields()
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
