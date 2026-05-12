using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Rag.Models;
using Aether.Rag.Retrieval;
using Aether.Services;
using Aether.Services.ProcessManagement;
using Aether.ViewModels;

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
    ("secret store falls back without plaintext", SecretStoreFallbackWithoutPlaintext),
    ("RAG BM25 scoring ranks exact term matches", RagBm25ScoringRanksMatches),
    ("RAG hybrid scoring fuses semantic and lexical ranks", RagHybridScoringFusesRanks),
    ("runtime profile normalization and unsafe host validation", RuntimeProfileValidation),
    ("settings save migrates OpenAI key to secret reference", SettingsSaveMigratesOpenAiKey),
    ("settings save preserves existing secret reference", SettingsSavePreservesExistingSecretReference),
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

    service.Settings.DataRootDirectory = next;
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
    service.Settings.DataRootDirectory = next;
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
    service.Settings.DataRootDirectory = root;
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
    service.Settings.DataRootDirectory = restoreRoot;
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
    settings.Settings.DataRootDirectory = temp.PathFor("data");
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
    settings.Settings.DataRootDirectory = temp.PathFor("data");
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

    var settings = new AppSettings { LocalAiAssetsRoot = root };
    LocalAiAssetLocator.ApplyDetected(settings);
    True(settings.TtsScriptPath.EndsWith("xtts_api_server.py", StringComparison.Ordinal), "XTTS script should be applied");
    True(settings.TtsPythonPath.EndsWith(OperatingSystem.IsWindows() ? "python.exe" : "python", StringComparison.Ordinal), "XTTS venv python should be applied");
    Equal(voices, settings.TtsVoiceDirectory, "voice directory should be applied");
    Equal(output, settings.TtsOutputDirectory, "output directory should be applied");
    Equal(encoder, settings.RagRerankerModelPath, "reranker directory should be applied");
    return Task.CompletedTask;
}

static async Task SecretStoreFallbackWithoutPlaintext()
{
    using var temp = new TempDir();
    var previous = Environment.GetEnvironmentVariable("AETHER_DISABLE_OS_KEYCHAIN");
    Environment.SetEnvironmentVariable("AETHER_DISABLE_OS_KEYCHAIN", "1");
    try
    {
        var settings = NewSettings(temp);
        settings.Settings.DataRootDirectory = temp.PathFor("data");
        var store = new SecretStore(settings);
        var reference = await store.StoreAsync("openai-api-key", "sk-test-secret");
        Equal(true, store.IsReference(reference), "stored secret should return a reference");
        Equal("sk-test-secret", await store.ResolveAsync(reference), "secret reference should resolve");
        Equal("Local fallback file", await store.BackendLabelAsync(), "disabled keychain should use fallback label");

        var localVault = Path.Combine(settings.Settings.DataRootDirectory, "secrets.local.json");
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
        [
            new ScoredChunk(semanticWinner, 0.99f, ScoreSource.Semantic),
            new ScoredChunk(shared, 0.90f, ScoreSource.Semantic)
        ],
        [
            new ScoredChunk(lexicalWinner, 12f, ScoreSource.Bm25),
            new ScoredChunk(shared, 10f, ScoreSource.Bm25)
        ],
        topK: 3);

    Equal(3, fused.Count, "hybrid fusion should include unique chunks from both rankings");
    Equal(ScoreSource.Hybrid, fused[0].Source, "fused result should be labelled hybrid");
    True(fused.Any(s => s.Chunk.Id == "semantic"), "semantic-only result should survive fusion");
    True(fused.Any(s => s.Chunk.Id == "lexical"), "BM25-only result should survive fusion");
    True(fused.Any(s => s.Chunk.Id == "shared"), "shared result should survive fusion");
    True(fused[0].Score >= fused[^1].Score, "fused results should be sorted");
    return Task.CompletedTask;
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

static async Task SettingsSaveMigratesOpenAiKey()
{
    using var temp = new TempDir();
    var previous = Environment.GetEnvironmentVariable("AETHER_DISABLE_OS_KEYCHAIN");
    Environment.SetEnvironmentVariable("AETHER_DISABLE_OS_KEYCHAIN", "1");
    try
    {
        var settings = NewSettings(temp);
        settings.Settings.DataRootDirectory = temp.PathFor("data");
        settings.Settings.OpenAiApiKey = "sk-plain-key-123456";
        var secrets = new SecretStore(settings);
        var vm = NewSettingsViewModel(settings, secrets);

        Equal("sk-plain-key-123456", vm.OpenAiApiKey, "plaintext setting should load into editable field");
        await vm.SaveCommand.ExecuteAsync(null);

        True(secrets.IsReference(settings.Settings.OpenAiApiKey), "save should migrate plaintext key to a secret reference");
        Equal("sk-plain-key-123456", await secrets.ResolveAsync(settings.Settings.OpenAiApiKey), "migrated reference should resolve");
        var localVault = Path.Combine(settings.Settings.DataRootDirectory, "secrets.local.json");
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
        settings.Settings.DataRootDirectory = temp.PathFor("data");
        var secrets = new SecretStore(settings);
        var reference = await secrets.StoreAsync("openai-api-key", "sk-existing-secret");
        settings.Settings.OpenAiApiKey = reference;

        var vm = NewSettingsViewModel(settings, secrets);
        Equal(string.Empty, vm.OpenAiApiKey, "existing secret reference should not be displayed");
        await vm.SaveCommand.ExecuteAsync(null);

        Equal(reference, settings.Settings.OpenAiApiKey, "blank API key field should preserve existing reference");
        Equal("sk-existing-secret", await secrets.ResolveAsync(reference), "preserved reference should still resolve");
    }
    finally
    {
        Environment.SetEnvironmentVariable("AETHER_DISABLE_OS_KEYCHAIN", previous);
    }
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
    new(settings, new FakeTts(), new FakeToasts(), new BackupService(settings), secrets, new XttsProcessManager());

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

sealed class FakeTts : ITtsService
{
    public Task SpeakAsync(string text, CancellationToken ct = default) => Task.CompletedTask;
    public Task PreviewVoiceAsync(string speaker, string text, CancellationToken ct = default) => Task.CompletedTask;
    public Task<string> ImportVoiceSampleAsync(string sourcePath, string displayName, CancellationToken ct = default) =>
        Task.FromResult(displayName);
    public Task<IReadOnlyList<string>> GetVoicesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(["default"]);
}

sealed class FakeToasts : IToastService
{
    public event Action<ToastMessage>? ToastRaised;
    public void Show(string title, string message, ToastKind kind = ToastKind.Info, int durationMs = 3500) =>
        ToastRaised?.Invoke(new ToastMessage(title, message, kind, durationMs));
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
