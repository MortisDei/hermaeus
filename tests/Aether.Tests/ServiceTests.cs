using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Net.Http;
using System.Threading.Tasks;
using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Desktop.Controls;
using Aether.Rag.Embeddings;
using Aether.Rag.Retrieval;
using Aether.Rag.Storage;
using Aether.Services;
using Aether.Services.ProcessManagement;
using Aether.ViewModels;
using static Aether.Tests.Helpers;

namespace Aether.Tests
{
    internal static class ServiceTests
    {
        public static Task RedactionHidesSecrets()
        {
            var redactor = new RedactionService();
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var value = $"{home}/project api_key=abcdefghi123456789 bearer token_123456789012345 sk-abc123456789abcdef ghp_abcdefghijklmnopqrstuvwxyz1234567890?token=querysecret123456&key=anothersecret123&password=urlpass123456 AWS=AKIA1234567890ABCDEF password=localpass123456 azure_key=azuresecret123456";
            var redacted = redactor.Redact(value);

            False(redacted.Contains(home, StringComparison.Ordinal), "home path should be redacted");
            False(redacted.Contains("abcdefghi123456789", StringComparison.Ordinal), "api key should be redacted");
            False(redacted.Contains("token_123456789012345", StringComparison.Ordinal), "bearer token should be redacted");
            False(redacted.Contains("sk-abc123456789abcdef", StringComparison.Ordinal), "openai key should be redacted");
            False(redacted.Contains("ghp_abcdefghijklmnopqrstuvwxyz1234567890", StringComparison.Ordinal), "GitHub token should be redacted");
            False(redacted.Contains("querysecret123456", StringComparison.Ordinal), "query token should be redacted");
            False(redacted.Contains("anothersecret123", StringComparison.Ordinal), "query key should be redacted");
            False(redacted.Contains("urlpass123456", StringComparison.Ordinal), "query password should be redacted");
            False(redacted.Contains("AKIA1234567890ABCDEF", StringComparison.Ordinal), "AWS access key should be redacted");
            False(redacted.Contains("localpass123456", StringComparison.Ordinal), "password assignment should be redacted");
            False(redacted.Contains("azuresecret123456", StringComparison.Ordinal), "Azure key assignment should be redacted");
            return Task.CompletedTask;
        }

        public static async Task BenchmarkDbCreatesAndRecordsRuns()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var service = new BenchmarkService(settings, new FakeLlm(), new FakeSystemInfo(), new FakeEvalStore());

            var suite = BenchmarkService.StarterSuites().First();
            suite.MaxCases = 1;

            var suites = await service.GetSuitesAsync();
            Equal(12, suites.Count, "fresh benchmark db should seed all starter suites");
            True(suites.Any(s => s.Id == suite.Id), "starter suite should be listed");

            var run = await service.RunAsync(suite, new LlmModel { Id = "fake-agent", Name = "Fake Agent", Provider = "Test" });
            True(run.Results.Count > 0, "benchmark run should record results");
            var runs = await service.GetRunsAsync();
            Equal(1, runs.Count, "recorded run should be listed");
            True(File.Exists(Path.Combine(settings.Settings.DataManagement.DataRootDirectory, "benchmarks.db")), "benchmark db should be created");
        }

        public static Task BenchmarkStarterSuitesIncludeExpandedDeterministicSet()
        {
            var suites = BenchmarkService.StarterSuites();
            var ids = suites.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);

            Equal(12, suites.Count, "starter suite count should include the original seven plus five expanded suites");
            True(ids.IsSupersetOf([
                "speed-smoke",
                "instruction-following",
                "reasoning-light",
                "rag-answer-style",
                "refusal-safety",
                "coding-assistant",
                "context-pressure",
                "code-generation",
                "structured-output-stress",
                "multi-step-reasoning",
                "aether-workflows",
                "hallucination-resistance"
            ]), "starter suites should include every built-in suite id");
            True(suites.Single(s => s.Id == "hallucination-resistance").Cases.All(c => c.ShouldRefuse), "hallucination resistance cases should reward uncertainty/refusal behavior");
            True(suites.Single(s => s.Id == "code-generation").Cases.All(c => c.ExpectedRegexes.Count > 0), "code generation cases should have structural regex checks");
            return Task.CompletedTask;
        }

        public static async Task BenchmarkSingleIterationRunExportsColdRunMode()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var service = new BenchmarkService(settings, new FakeLlm(), new FakeSystemInfo(), new FakeEvalStore());

            var suite = BenchmarkService.StarterSuites().First();
            suite.MaxCases = 1;
            suite.IterationsPerCase = 1;

            var run = await service.RunAsync(suite, new LlmModel { Id = "fake-agent", Name = "Fake Agent", Provider = "Test" });
            Equal("Cold", run.RunMode, "single-iteration benchmark runs should be labeled cold");

            var exportPath = await service.ExportAsync(run.Id, temp.PathFor("exports"));
            var markdown = await File.ReadAllTextAsync(exportPath);
            True(markdown.Contains("- Run mode: `Cold`", StringComparison.Ordinal), "markdown export should show cold run mode");
        }

        public static async Task BenchmarkRunHistoryCanBeCleared()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var service = new BenchmarkService(settings, new FakeLlm(), new FakeSystemInfo(), new FakeEvalStore());

            var suite = BenchmarkService.StarterSuites().First();
            suite.MaxCases = 1;
            await service.SaveSuiteAsync(suite);
            await service.RunAsync(suite, new LlmModel { Id = "fake-agent", Name = "Fake Agent", Provider = "Test" });

            True((await service.GetRunsAsync()).Count > 0, "benchmark run should exist before clearing history");

            await service.ClearRunsAsync();

            Equal(0, (await service.GetRunsAsync()).Count, "clearing history should remove saved runs");
            True((await service.GetSuitesAsync()).Any(s => s.Id == suite.Id), "clearing history should not remove suites");
        }

        public static Task BenchmarkScoringAndRanking()
        {
            var service = new BenchmarkService(NewSettings(new TempDir()), new FakeLlm(), new FakeSystemInfo(), new FakeEvalStore());
            var slow = new BenchmarkRun
            {
                ModelId = "model-a",
                ModelName = "Model A",
                StartedAt = DateTime.UtcNow.AddMinutes(-10),
                Results = [new BenchmarkResult { QualityScore = 0.2, ApproxTokensPerSecond = 2, ResourceScore = 0.2 }]
            };
            var fast = new BenchmarkRun
            {
                ModelId = "model-a",
                ModelName = "Model A",
                StartedAt = DateTime.UtcNow,
                Results = [new BenchmarkResult { QualityScore = 1, ApproxTokensPerSecond = 40, ResourceScore = 1 }]
            };
            var other = new BenchmarkRun
            {
                ModelId = "model-b",
                ModelName = "Model B",
                StartedAt = DateTime.UtcNow.AddMinutes(-5),
                Results = [new BenchmarkResult { QualityScore = 0.6, ApproxTokensPerSecond = 12, ResourceScore = 0.8 }]
            };

            var scores = service.Rank(new[] { slow, fast, other });

            Equal(fast.Id, scores[0].Id, "highest scoring run should rank first");
            Equal(2, scores.Count, "duplicate models should collapse to their best run in rankings");
            True(scores[0].RankingScore >= scores[^1].RankingScore, "scores should be sorted descending");
            return Task.CompletedTask;
        }

        public static async Task BenchmarkExportAllCreatesBatchFolder()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var service = new BenchmarkService(settings, new FakeLlm(), new FakeSystemInfo(), new FakeEvalStore());

            var suite = BenchmarkService.StarterSuites().First();
            suite.MaxCases = 1;
            await service.SaveSuiteAsync(suite);
            await service.RunAsync(suite, new LlmModel { Id = "fake-a", Name = "Fake A", Provider = "Test" });
            await service.RunAsync(suite, new LlmModel { Id = "fake-b", Name = "Fake B", Provider = "Test" });

            var exportRoot = temp.PathFor("exports");
            var indexPath = await service.ExportAllAsync(exportRoot);

            True(File.Exists(indexPath), "bulk export should create an index markdown file");
            True(Directory.Exists(Path.GetDirectoryName(indexPath)!), "bulk export should create an export folder");
            True(Directory.GetFiles(Path.GetDirectoryName(indexPath)!, "*.md", SearchOption.AllDirectories).Length >= 3, "bulk export should include each run export plus the index");
        }

        public static async Task SystemInfoSafeFallback()
        {
            var snapshot = await new FakeSystemInfo().CaptureAsync();
            Equal("test", snapshot.AppVersion, "snapshot should come from fake service");
            Equal(1, snapshot.Components.Count, "snapshot should include a component");
        }

        public static async Task PrivacyAuditReportsRemoteAndNetworkExposure()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.Llm.OpenAiEnabled = true;
            settings.Settings.Llm.OpenAiBaseUrl = "https://api.example.invalid/v1";
            settings.Settings.ManagedServers.Clear();
            settings.Settings.ManagedServers.Add(new ServerConfig
            {
                Name = "Chat",
                Port = 8080,
                ExtraArgs = "--host 0.0.0.0"
            });

            var privacyAudit = new PrivacyAuditService(settings, new FakeSecretStore(), new RuntimeLogService(settings), new FakeVoiceProviderRegistry(settings), new SqliteTraceStore(settings));
            var vm = new SystemOverviewViewModel(
                new FakeSystemInfo(),
                new FakeToasts(),
                privacyAudit);

            await vm.RefreshPrivacyAuditCommand.ExecuteAsync(null);

            True(vm.PrivacyAuditItems.Any(i => i.Name == "Remote providers" && i.Status == "Review"), "remote providers should require review");
            True(vm.PrivacyAuditItems.Any(i => i.Name == "Exposed local servers" && i.Status == "Warning"), "network-facing server args should warn");
        }

        public static async Task PrivacyAuditFlagsRemoteVoiceProviderWithNoChatProviderEnabled()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.Llm.OpenAiEnabled = false;
            settings.Settings.Llm.LlamaCppEnabled = false;
            settings.Settings.Tts.VoiceProvider = "F5Tts"; // FakeVoiceProviderRegistry marks F5-TTS as VoiceCapability.Remote

            var privacyAudit = new PrivacyAuditService(settings, new FakeSecretStore(), new RuntimeLogService(settings), new FakeVoiceProviderRegistry(settings), new SqliteTraceStore(settings));
            var items = await privacyAudit.ScanAsync();

            var remote = items.Single(i => i.Name == "Remote providers");
            Equal("Review", remote.Status, "a remote voice provider alone should require review, driven by VoiceCapability not a hardcoded provider name");
            True(remote.Detail.Contains("F5-TTS", StringComparison.Ordinal), "detail should name the remote voice provider");
        }

        public static async Task PrivacyAuditShowsPerAppLocalApiActivity()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var traces = new SqliteTraceStore(settings);
            var privacyAudit = new PrivacyAuditService(settings, new FakeSecretStore(), new RuntimeLogService(settings), new FakeVoiceProviderRegistry(settings), traces);

            var disabledItems = await privacyAudit.ScanAsync();
            var disabled = disabledItems.Single(i => i.Name == "Local API activity");
            Equal("Disabled", disabled.Status, "local API activity should report disabled when the host is off");

            settings.Settings.LocalApi.Enabled = true;
            var noCallsItems = await privacyAudit.ScanAsync();
            Equal("No calls yet", noCallsItems.Single(i => i.Name == "Local API activity").Status, "an enabled but unused local API should report no calls yet");

            await traces.AppendAsync(new TraceRecord { Kind = TraceKind.LocalApi, SourceId = "my-app", Operation = "chat.completions" });
            await traces.AppendAsync(new TraceRecord { Kind = TraceKind.LocalApi, SourceId = "my-app", Operation = "models.list" });
            await traces.AppendAsync(new TraceRecord { Kind = TraceKind.LocalApi, SourceId = "other-app", Operation = "memory.query" });

            var activeItems = await privacyAudit.ScanAsync();
            var active = activeItems.Single(i => i.Name == "Local API activity");
            Equal("Review", active.Status, "recorded local API calls should surface as review");
            True(active.Detail.Contains("my-app", StringComparison.Ordinal), "detail should name the calling app");
            True(active.Detail.Contains("other-app", StringComparison.Ordinal), "detail should name every distinct calling app");
        }

        public static Task LocalAiAssetsDetectAndApplyPaths()
        {
            using var temp = new TempDir();
            var root = temp.PathFor("assets");
            Directory.CreateDirectory(Path.Combine(root, "models"));
            Directory.CreateDirectory(Path.Combine(root, "TTS", "voices"));
            Directory.CreateDirectory(Path.Combine(root, "TTS", "output"));
            Directory.CreateDirectory(Path.Combine(root, "TTS", "multi-dataset--xtts_v2"));
            File.WriteAllText(Path.Combine(root, "TTS", "xtts_api_server.py"), "print('xtts')");
            File.WriteAllText(Path.Combine(root, "TTS", "multi-dataset--xtts_v2", "config.json"), "{}");
            File.WriteAllText(Path.Combine(root, "TTS", "multi-dataset--xtts_v2", "model.pth"), "model");
            Directory.CreateDirectory(Path.Combine(root, "models", "reranker"));
            File.WriteAllText(Path.Combine(root, "models", "reranker", "model_O4.onnx"), "model");
            File.WriteAllText(Path.Combine(root, "models", "reranker", "vocab.txt"), "vocab");

            var settings = NewSettings(temp);
            settings.Settings.DataManagement.LocalAiAssetsRoot = root;
            LocalAiAssetLocator.ApplyDetected(settings.Settings, overwrite: true);

            Equal(Path.Combine(root, "TTS", "xtts_api_server.py"), settings.Settings.Tts.ScriptPath, "xtts script should be applied");
            Equal(Path.Combine(root, "TTS", "voices"), settings.Settings.Tts.VoiceDirectory, "voice directory should be applied");
            Equal(Path.Combine(root, "TTS", "output"), settings.Settings.Tts.OutputDirectory, "output directory should be applied");
            Equal(Path.Combine(root, "TTS", "multi-dataset--xtts_v2"), settings.Settings.Tts.ModelDirectory, "xtts model directory should be applied");
            Equal(Path.Combine(root, "models", "reranker"), settings.Settings.Rag.RerankerModelPath, "reranker path should be applied");
            return Task.CompletedTask;
        }

        public static Task LocalAiAssetsPreferExistingModelsDirectoryWithGgufs()
        {
            using var temp = new TempDir();
            var root = temp.PathFor("AI");
            Directory.CreateDirectory(Path.Combine(root, "models"));
            Directory.CreateDirectory(Path.Combine(root, "Models"));
            File.WriteAllText(Path.Combine(root, "Models", "chat-model.gguf"), "model");

            var layout = LocalAiAssetLocator.Detect(root);

            // On case-insensitive filesystems "models" and "Models" are the same
            // directory; expect whichever on-disk casing actually holds the GGUF.
            var expected = Directory.EnumerateDirectories(root)
                .Single(path => Directory.EnumerateFiles(path, "*.gguf").Any());
            Equal(expected, layout.ModelsDirectory, "asset detection should prefer the existing models folder with GGUF files");
            return Task.CompletedTask;
        }

        public static Task LocalAiAssetsListsDiscoveredGgufModels()
        {
            using var temp = new TempDir();
            var root = temp.PathFor("AI");
            var models = Path.Combine(root, "Models");
            Directory.CreateDirectory(Path.Combine(models, "nested"));
            Directory.CreateDirectory(Path.Combine(models, "embed"));
            var first = Path.Combine(models, "alpha.gguf");
            var second = Path.Combine(models, "nested", "beta.gguf");
            var embedding = Path.Combine(models, "embed", "nomic-embed-text-v1.5-Q4_K_M.gguf");
            File.WriteAllText(first, "model");
            File.WriteAllText(second, "model");
            File.WriteAllText(embedding, "embedding");

            var found = LocalAiAssetLocator.FindGgufModels(root);

            Equal(2, found.Count, "GGUF discovery should include nested model files");
            True(found.Contains(first), "GGUF discovery should include root model file");
            True(found.Contains(second), "GGUF discovery should include nested model file");
            False(found.Contains(embedding), "chat GGUF discovery should exclude embedding subfolder files");
            return Task.CompletedTask;
        }

        public static Task LocalAiAssetsListsDiscoveredEmbeddingModels()
        {
            using var temp = new TempDir();
            var root = temp.PathFor("AI");
            var models = Path.Combine(root, "Models");
            Directory.CreateDirectory(Path.Combine(models, "embed"));
            var chat = Path.Combine(models, "NextCoder-7B.Q4_K_M.gguf");
            var embedding = Path.Combine(models, "embed", "nomic-embed-text-v1.5-Q4_K_M.gguf");
            File.WriteAllText(chat, "chat");
            File.WriteAllText(embedding, "embedding");

            var found = LocalAiAssetLocator.FindEmbeddingModels(root);

            Equal(1, found.Count, "embedding discovery should only include dedicated embedding models");
            Equal(embedding, found[0], "embedding discovery should scan Models/embed");
            return Task.CompletedTask;
        }

        public static Task LocalAiAssetsListsDiscoveredRerankerDirectories()
        {
            using var temp = new TempDir();
            var root = temp.PathFor("AI");
            var reranker = Path.Combine(root, "Models", "rerank", "ms-marco-MiniLM-L6-v2");
            Directory.CreateDirectory(reranker);
            File.WriteAllText(Path.Combine(reranker, "model_O4.onnx"), "model");
            File.WriteAllText(Path.Combine(reranker, "vocab.txt"), "vocab");

            var found = LocalAiAssetLocator.FindRerankerDirectories(root);

            Equal(1, found.Count, "reranker discovery should include valid reranker model folders");
            Equal(reranker, found[0], "reranker discovery should prefer folders under Models/rerank");
            return Task.CompletedTask;
        }

        public static Task RagSettingsPreservesConfiguredEmbeddingModelOption()
        {
            using var temp = new TempDir();
            var root = temp.PathFor("AI");
            Directory.CreateDirectory(Path.Combine(root, "Models"));
            File.WriteAllText(Path.Combine(root, "Models", "NextCoder-7B.Q4_K_M.gguf"), "model");

            var settings = NewSettings(temp);
            settings.Settings.DataManagement.LocalAiAssetsRoot = root;
            settings.Settings.Rag.EmbeddingModel = "nomic-embed-text";
            var vm = new RagSettingsViewModel(() => root);

            vm.ReloadFrom(settings.Settings, root);

            Equal("nomic-embed-text", vm.EmbeddingModelOptions[0], "current embedding model should remain selectable before discovered GGUFs");
            Equal("nomic-embed-text", vm.EmbeddingModel, "current embedding model should not be replaced by the first local GGUF");
            False(vm.EmbeddingModelOptions.Any(option => option.Contains("NextCoder", StringComparison.OrdinalIgnoreCase)),
                "embedding selector should not list chat GGUF models");
            return Task.CompletedTask;
        }

        public static Task RagSettingsDiscoversAndSelectsInstalledReranker()
        {
            using var temp = new TempDir();
            var root = temp.PathFor("AI");
            var reranker = Path.Combine(root, "Models", "rerank", "ms-marco-MiniLM-L6-v2");
            Directory.CreateDirectory(reranker);
            File.WriteAllText(Path.Combine(reranker, "model_O4.onnx"), "model");
            File.WriteAllText(Path.Combine(reranker, "vocab.txt"), "vocab");

            var settings = NewSettings(temp);
            settings.Settings.DataManagement.LocalAiAssetsRoot = root;
            settings.Settings.Rag.RerankerModelPath = string.Empty;
            var vm = new RagSettingsViewModel(() => root);

            vm.ReloadFrom(settings.Settings, root);

            Equal(reranker, vm.RerankerModelPathOptions[0], "installed reranker should be available as a settings option");
            Equal(reranker, vm.RagRerankerModelPath, "settings should select the installed reranker when no explicit path is configured");
            return Task.CompletedTask;
        }

        public static async Task AppLifecycleJournalTracksCleanAndUncleanExits()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");

            var firstRun = new AppLifecycleJournalService(settings);
            var previousBeforeAnyRun = firstRun.RecordStartup();
            Equal<AppLifecycleRecord?>(null, previousBeforeAnyRun, "the very first run should have no previous session recorded");

            firstRun.RecordOperation("loading reranker ONNX session (EnsureLoadedAsync)");
            // Simulate the process ending without RecordCleanExit ever running (a crash).

            var secondRun = new AppLifecycleJournalService(settings);
            var previousAfterCrash = secondRun.RecordStartup();
            True(previousAfterCrash is not null, "a new journal instance should read back the prior session's record");
            False(previousAfterCrash!.CleanExit, "a session that never recorded a clean exit should be reported as unclean");
            Equal("loading reranker ONNX session (EnsureLoadedAsync)", previousAfterCrash.LastOperation,
                "the last recorded operation before the simulated crash should be preserved");

            secondRun.RecordCleanExit();
            var thirdRun = new AppLifecycleJournalService(settings);
            var previousAfterCleanExit = thirdRun.RecordStartup();
            True(previousAfterCleanExit is not null, "a third journal instance should still read back a record");
            True(previousAfterCleanExit!.CleanExit, "a session that called RecordCleanExit should be reported as clean");
        }

        public static async Task DoctorWarnsWhenPreviousSessionDidNotExitCleanly()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");

            var crashedRun = new AppLifecycleJournalService(settings);
            crashedRun.RecordStartup();
            crashedRun.RecordOperation("loading Kokoro native ONNX session (EnsureLoadedAsync)");
            // No RecordCleanExit(): simulates the process dying mid-operation.

            var currentRun = new AppLifecycleJournalService(settings);
            currentRun.RecordStartup();

            var doctor = new DoctorService(
                settings,
                new RuntimeProfileService(settings),
                new FakeVoiceProviderRegistry(settings),
                new FakeSecretStore(),
                new SqliteRagStore(settings),
                new ThrowingEmbeddingService(),
                new FakeSystemInfo(),
                new PythonHealthValidator(),
                new NoOpReranker(),
                lifecycleJournal: currentRun);

            var report = await doctor.ScanAsync();
            var check = report.Checks.Single(c => c.Key == "clean-shutdown");
            Equal(DoctorCheckStatus.Warning, check.Status, "Doctor should warn when the previous session did not exit cleanly");
            True(check.Detail.Contains("loading Kokoro native ONNX session (EnsureLoadedAsync)", StringComparison.Ordinal),
                "the warning detail should name the last recorded operation");
        }

        public static async Task DoctorDoesNotTreatChatGgufAsEmbeddingModel()
        {
            using var temp = new TempDir();
            var root = temp.PathFor("AI");
            Directory.CreateDirectory(Path.Combine(root, "Models"));
            File.WriteAllText(Path.Combine(root, "Models", "NextCoder-7B.Q4_K_M.gguf"), "model");

            var settings = NewSettings(temp);
            settings.Settings.DataManagement.LocalAiAssetsRoot = root;
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            settings.Settings.Rag.EmbeddingModel = "NextCoder-7B.Q4_K_M";
            settings.Settings.ManagedServers.Clear();
            settings.Settings.ManagedServers.Add(new ServerConfig
            {
                Name = "llama.cpp",
                ExecutablePath = Environment.ProcessPath ?? "dotnet"
            });

            var doctor = new DoctorService(
                settings,
                new RuntimeProfileService(settings),
                new FakeVoiceProviderRegistry(settings),
                new FakeSecretStore(),
                new SqliteRagStore(settings),
                new ThrowingEmbeddingService(),
                new FakeSystemInfo(),
                new PythonHealthValidator(),
                new NoOpReranker());

            var report = await doctor.ScanAsync();
            var modelCheck = report.Checks.Single(c => c.Key == "embedding-model");
            var backendCheck = report.Checks.Single(c => c.Key == "embeddings");

            Equal(DoctorCheckStatus.Warning, modelCheck.Status, "chat GGUF names should not satisfy embedding model availability");
            Equal(DoctorCheckStatus.Info, backendCheck.Status, "embedding backend should be skipped until an embedding model exists");
            if (OperatingSystem.IsLinux())
                False(report.Checks.Any(c => c.Key == "hotkeys"), "Linux global hotkey support should not be reported as a Doctor problem");
        }

        public static async Task DoctorWarnsForUntunedLocalGgufModels()
        {
            using var temp = new TempDir();
            var root = temp.PathFor("AI");
            Directory.CreateDirectory(Path.Combine(root, "Models"));
            var model = Path.Combine(root, "Models", "chat.gguf");
            File.WriteAllText(model, "model");

            var settings = NewSettings(temp);
            settings.Settings.DataManagement.LocalAiAssetsRoot = root;
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");

            var doctor = new DoctorService(
                settings,
                new RuntimeProfileService(settings),
                new FakeVoiceProviderRegistry(settings),
                new FakeSecretStore(),
                new SqliteRagStore(settings),
                new ThrowingEmbeddingService(),
                new FakeSystemInfo(),
                new PythonHealthValidator(),
                new NoOpReranker());

            var report = await doctor.ScanAsync();
            var tuneCheck = report.Checks.Single(c => c.Key == "llama-tune-profiles");

            Equal(DoctorCheckStatus.Warning, tuneCheck.Status, "untuned GGUF models should be surfaced in Doctor");
            True(tuneCheck.Diagnostics.Contains(model, StringComparison.Ordinal), "Doctor diagnostics should list untuned model paths");

            var info = new FileInfo(model);
            settings.Settings.LlamaTuneProfiles.Add(new LlamaTuneProfile
            {
                ModelPath = model,
                ModelSizeBytes = info.Length,
                ModelModifiedAtUtc = info.LastWriteTimeUtc,
                GpuLayers = 12,
                Threads = 6,
                ContextSize = 8192
            });

            report = await doctor.ScanAsync();
            tuneCheck = report.Checks.Single(c => c.Key == "llama-tune-profiles");
            Equal(DoctorCheckStatus.Ready, tuneCheck.Status, "matching tune profiles should satisfy Doctor");
        }

        public static async Task DoctorStartupScanRaisesProblemToast()
        {
            using var temp = new TempDir();
            var toasts = new FakeToasts();
            var raised = new List<ToastMessage>();
            toasts.ToastRaised += raised.Add;
            var report = new DoctorReport(
                [
                    new DoctorCheck(
                        "startup-warning",
                        "Startup warning",
                        DoctorCheckStatus.Warning,
                        "Needs attention",
                        "A startup check found a problem.",
                        "Open Settings",
                        true,
                        "diagnostics",
                        "System")
                ],
                DateTime.UtcNow,
                "Doctor scan found 0 error(s) and 1 warning(s).");
            var vm = new DoctorViewModel(new StaticDoctorService(report), toasts, NewSettings(temp));

            await vm.RunStartupScanAsync();

            Equal(1, vm.Checks.Count, "startup scan should populate Doctor checks");
            Equal(report.Summary, vm.Summary, "startup scan should update the summary");
            True(raised.Any(t => t.Title == "Aether Doctor found warnings" && t.Kind == ToastKind.Warning),
                "startup scan should notify the user when warnings are found");
        }

        public static async Task LocalAiSetupDetectsFolderLayout()
        {
            using var temp = new TempDir();
            var root = temp.PathFor("AI");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "xtts_api_server.py"), "print('xtts')");
            Directory.CreateDirectory(Path.Combine(root, "models"));
            File.WriteAllText(Path.Combine(root, "models", "chat.gguf"), "model");
            Directory.CreateDirectory(Path.Combine(root, "TTS", "voices"));
            Directory.CreateDirectory(Path.Combine(root, "TTS", "output"));

            var settings = NewSettings(temp);
            settings.Settings.DataManagement.LocalAiAssetsRoot = root;
            var service = new LocalAiSetupService(new PythonHealthValidator());
            var report = await service.ScanAsync(settings.Settings);

            True(report.Items.Any(item => item.Status == LocalAiReadinessStatus.Found), "scan should find ready items");
            True(report.Actions.Count > 0, "scan should produce setup actions");
        }

        public static async Task LocalAiSetupScriptHandlingIsApprovalGated()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            Directory.CreateDirectory(settings.Settings.DataManagement.LocalAiAssetsRoot = temp.PathFor("AI"));
            var service = new LocalAiSetupService(new PythonHealthValidator());
            var report = await service.ScanAsync(settings.Settings);

            True(report.Actions.Any(action => action.RequiresApproval), "setup report should contain gated actions");
        }

        public static async Task LocalAiSetupCommandPreviewsStayShellFree()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            Directory.CreateDirectory(settings.Settings.DataManagement.LocalAiAssetsRoot = temp.PathFor("AI"));
            var service = new LocalAiSetupService(new PythonHealthValidator());
            var report = await service.ScanAsync(settings.Settings);

            False(report.SetupCommands.Contains(';', StringComparison.Ordinal), "command previews should not synthesize shell separators");
        }

        public static Task LocalAiSetupDoesNotShipPlaceholderHashes()
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
            var file = Path.Combine(root, "src", "Aether.Services", "LocalAiSetupService.cs");
            var source = File.ReadAllText(file);
            False(source.Contains("b0aca5b1", StringComparison.OrdinalIgnoreCase), "setup service should not ship placeholder hashes");
            False(source.Contains("Placeholder: Replace", StringComparison.OrdinalIgnoreCase), "setup service should not ship placeholder hash comments");
            return Task.CompletedTask;
        }

        public static async Task LocalAiSetupSurfcesKokoroOnboarding()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.Tts.VoiceProvider = "Kokoro";
            var wizard = new SetupWizardViewModel(
                settings,
                new RuntimeProfileService(settings),
                new FakeVoiceProviderRegistryKokoroInstall(settings),
                new FakeDoctorService(),
                new FakeToasts());

            True(wizard.VoiceOnboardingSummary.Contains("Kokoro", StringComparison.Ordinal), "wizard should surface Kokoro onboarding summary");
            True(wizard.VoiceOnboardingSteps.Any(step => step.Contains("Install Kokoro packages", StringComparison.Ordinal)), "wizard should include Kokoro install step");
        }

        public static async Task ModelDownloadResumesWithRangeRequest()
        {
            using var temp = new TempDir();
            var destination = temp.PathFor("models/test.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await File.WriteAllTextAsync(destination + ".tmp", "hello");

            var handler = new CapturingRangeHttpHandler("world");
            var client = new HttpClient(handler);
            var service = new ModelDownloadService(client);

            var result = await service.DownloadAsync("https://example.test/model.bin", destination);

            True(result.Success, "download should succeed");
            Equal("bytes=5-", handler.LastRequest?.Headers.Range?.ToString(), "resume request should send byte range");
            Equal("helloworld", await File.ReadAllTextAsync(destination), "download should append to existing temp file");
        }

        public static async Task DoctorEmbeddingInstallVerifiesHashAndConfiguresServer()
        {
            using var temp = new TempDir();
            var root = temp.PathFor("AI");
            Directory.CreateDirectory(Path.Combine(root, "Models"));
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.LocalAiAssetsRoot = root;
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            settings.Settings.Rag.EmbeddingModel = "chat-model";
            settings.Settings.ManagedServers.Clear();
            settings.Settings.ManagedServers.Add(new ServerConfig
            {
                Name = "Embeddings",
                EmbeddingsMode = true,
                ModelPath = temp.PathFor("missing/wrong.gguf")
            });

            var content = "fake embedding model";
            var downloads = new ModelDownloadService(new HttpClient(new CapturingRangeHttpHandler(content)));
            var spec = new EmbeddingModelDownloadSpec(
                "test-embed-model",
                "test-embed-model.gguf",
                "https://example.test/test-embed-model.gguf",
                ExpectedSha256(content));
            var doctor = new DoctorService(
                settings,
                new RuntimeProfileService(settings),
                new FakeVoiceProviderRegistry(settings),
                new FakeSecretStore(),
                new SqliteRagStore(settings),
                new ThrowingEmbeddingService(),
                new FakeSystemInfo(),
                new PythonHealthValidator(),
                new NoOpReranker(),
                downloads,
                spec);

            var ok = await doctor.InstallEmbeddingModelAsync();
            var expectedPath = Path.Combine(root, "Models", "embed", "test-embed-model.gguf");

            True(ok, "embedding model install should succeed when hash matches");
            Equal(content, await File.ReadAllTextAsync(expectedPath), "downloaded embedding model should be written to the embed folder");
            Equal("test-embed-model", settings.Settings.Rag.EmbeddingModel, "install should configure the embedding model name when the previous value was not an embedding model");
            Equal(expectedPath, settings.Settings.ManagedServers.Single(s => s.EmbeddingsMode).ModelPath, "install should update stale embedding server model paths");
        }

        public static async Task DoctorEmbeddingInstallRejectsHashMismatch()
        {
            using var temp = new TempDir();
            var root = temp.PathFor("AI");
            Directory.CreateDirectory(Path.Combine(root, "Models"));
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.LocalAiAssetsRoot = root;
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var downloads = new ModelDownloadService(new HttpClient(new CapturingRangeHttpHandler("tampered")));
            var spec = new EmbeddingModelDownloadSpec(
                "test-embed-model",
                "test-embed-model.gguf",
                "https://example.test/test-embed-model.gguf",
                ExpectedSha256("expected"));
            var doctor = new DoctorService(
                settings,
                new RuntimeProfileService(settings),
                new FakeVoiceProviderRegistry(settings),
                new FakeSecretStore(),
                new SqliteRagStore(settings),
                new ThrowingEmbeddingService(),
                new FakeSystemInfo(),
                new PythonHealthValidator(),
                new NoOpReranker(),
                downloads,
                spec);

            var ok = await doctor.InstallEmbeddingModelAsync();

            False(ok, "embedding model install should fail when SHA256 verification fails");
            False(File.Exists(Path.Combine(root, "Models", "embed", "test-embed-model.gguf")), "failed verification should remove the downloaded file");
        }

        public static async Task DoctorEmbeddingInstallMigratesRootEmbeddingModel()
        {
            using var temp = new TempDir();
            var root = temp.PathFor("AI");
            Directory.CreateDirectory(Path.Combine(root, "Models"));
            var sourcePath = Path.Combine(root, "Models", "test-embed-model.gguf");
            var content = "fake embedding model";
            await File.WriteAllTextAsync(sourcePath, content);
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.LocalAiAssetsRoot = root;
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            settings.Settings.ManagedServers.Clear();
            settings.Settings.ManagedServers.Add(new ServerConfig
            {
                Name = "Embeddings",
                EmbeddingsMode = true
            });
            var spec = new EmbeddingModelDownloadSpec(
                "test-embed-model",
                "test-embed-model.gguf",
                "https://example.test/test-embed-model.gguf",
                ExpectedSha256(content));
            var doctor = new DoctorService(
                settings,
                new RuntimeProfileService(settings),
                new FakeVoiceProviderRegistry(settings),
                new FakeSecretStore(),
                new SqliteRagStore(settings),
                new ThrowingEmbeddingService(),
                new FakeSystemInfo(),
                new PythonHealthValidator(),
                new NoOpReranker(),
                new ModelDownloadService(new HttpClient(new CapturingRangeHttpHandler("unused"))),
                spec);

            var ok = await doctor.InstallEmbeddingModelAsync();
            var expectedPath = Path.Combine(root, "Models", "embed", "test-embed-model.gguf");

            True(ok, "existing verified embedding model should be accepted");
            False(File.Exists(sourcePath), "root embedding model should be moved out of the chat model folder");
            Equal(content, await File.ReadAllTextAsync(expectedPath), "embedding model should be moved into Models/embed");
            Equal(expectedPath, settings.Settings.ManagedServers.Single(s => s.EmbeddingsMode).ModelPath, "embedding server should point at the migrated model");
        }

        public static Task LlamaServerReleaseDataCoversSupportedPlatforms()
        {
            var service = new LlamaServerSetupService();
            var releases = service.GetSupportedReleaseInfo();

            Equal(6, releases.Count, "release data should cover all supported platforms");
            True(releases.All(entry => entry.Url.Contains("b4341", StringComparison.Ordinal)), "release urls should use the expected tag");
            True(releases.Select(entry => entry.DisplayName).Distinct(StringComparer.Ordinal).Count() == releases.Count, "release labels should be unique");
            return Task.CompletedTask;
        }

        public static Task LlamaServerLatestAssetSelectionFindsCurrentPlatform()
        {
            var selected = LlamaServerSetupService.SelectDownloadAsset(
            [
                new GitHubReleaseAsset("llama-server-b9999-linux-x64", "https://example.test/linux"),
                new GitHubReleaseAsset("llama-server-b9999-win-avx2.exe", "https://example.test/win"),
                new GitHubReleaseAsset("llama-server-b9999-macos-arm64", "https://example.test/macos")
            ]);

            if (OperatingSystem.IsLinux() && System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.X64)
                Equal("https://example.test/linux", selected?.BrowserDownloadUrl, "latest llama asset selection should match Linux x64");
            else
                True(selected is not null, "latest llama asset selection should match the current supported platform");

            return Task.CompletedTask;
        }

        public static async Task OpenAiVoiceResolvesSecretReferences()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.Llm.OpenAiApiKey = "secret:openai-api-key";
            settings.Settings.Llm.OpenAiBaseUrl = "https://api.example.test";
            var secrets = new ResolvingSecretStore("resolved-openai-key");
            var handler = new CapturingSpeechHandler();
            using var http = new HttpClient(handler);
            using var provider = new OpenAiVoiceProvider(settings, secrets, http);
            var output = temp.PathFor("voice.wav");

            var result = await provider.GenerateSpeechAsync(new VoiceSynthesisRequest("hello", OutputPath: output, PlayAudio: false));

            True(result.Success, "OpenAI voice request should succeed with fake response");
            Equal("Bearer resolved-openai-key", handler.AuthorizationHeader, "OpenAI voice should resolve secret references before sending auth");
            Equal("https://api.example.test/v1/audio/speech", handler.RequestUri, "OpenAI voice should call configured endpoint");
            True(File.Exists(output), "voice response should be written to requested output path");
        }

        public static Task LlamaServerPathLookupSkipsEmptyPathSegments()
        {
            using var temp = new TempDir();
            var previousPath = Environment.GetEnvironmentVariable("PATH");
            var previousCwd = Environment.CurrentDirectory;
            try
            {
                var cwd = temp.PathFor("cwd");
                Directory.CreateDirectory(cwd);
                Environment.CurrentDirectory = cwd;
                File.WriteAllText(Path.Combine(cwd, OperatingSystem.IsWindows() ? "llama-server.exe" : "llama-server"), "not trusted");
                Environment.SetEnvironmentVariable("PATH", $"{Path.PathSeparator}{Path.PathSeparator}");

                False(new LlamaServerSetupService().IsInstalled(temp.PathFor("missing-install")),
                    "empty PATH segments should not make the current directory look like an installed llama-server");
            }
            finally
            {
                Environment.SetEnvironmentVariable("PATH", previousPath);
                Environment.CurrentDirectory = previousCwd;
            }

            return Task.CompletedTask;
        }

        public static Task XttsApiTemplateHasRequiredEndpoints()
        {
            var template = new LocalAiSetupService(new PythonHealthValidator()).BuildXttsApiScript();
            True(template.Contains("/v1/audio/speech", StringComparison.Ordinal), "template should expose speech endpoint");
            True(template.Contains("/health", StringComparison.Ordinal), "template should expose health endpoint");
            return Task.CompletedTask;
        }

        public static Task XttsApiTemplateEscapesConfiguredPaths()
        {
            var template = new LocalAiSetupService(new PythonHealthValidator()).BuildXttsApiScript("/models/'''/xtts", "/tmp/out\nnext");
            True(template.Contains("MODEL_DIR = '/models/\\'\\'\\'/xtts'", StringComparison.Ordinal), "template should escape embedded Python quotes");
            True(template.Contains("OUTPUT_DIR = '/tmp/out\\nnext'", StringComparison.Ordinal), "template should escape embedded newlines");
            False(template.Contains("MODEL_DIR = r'''", StringComparison.Ordinal), "template should not use injectable raw triple-quoted path literals");
            return Task.CompletedTask;
        }

        public static async Task EmbeddingClientSurfacesActionableHintWhenEndpointIsNotImplemented()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.Llm.LlamaCppBaseUrl = "http://localhost:8080";

            using var http = new HttpClient(new FixedStatusHandler(HttpStatusCode.NotImplemented, "embeddings disabled"));
            var embed = new LlamaCppEmbeddingService(settings, http);

            try
            {
                await embed.EmbedBatchAsync(["hello world"]);
                throw new InvalidOperationException("Expected an InvalidOperationException for HTTP 501 embedding endpoint response.");
            }
            catch (InvalidOperationException ex)
            {
                True(ex.Message.Contains("--embeddings", StringComparison.Ordinal), "error should explain how to enable embedding support");
                True(ex.Message.Contains("LlamaCppBaseUrl", StringComparison.Ordinal), "error should mention embedding base url configuration");
                True(ex.Message.Contains("embeddings disabled", StringComparison.Ordinal), "error should include server response details when available");
            }
        }

        public static async Task TrustScanInsideAiRootIsLowRisk()
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

        public static async Task TrustScanOutsideAiRootWarns()
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

        public static async Task TrustScanReportsMissingExecutable()
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

        public static Task AgentWorkspaceDraftPatch()
        {
            var tools = new Aether.Agent.Services.AgentWorkspaceTools();
            var draft = tools.DraftPatch("src/Program.cs", "Fix header", "// new content\npublic class Program {}\n");
            True(draft.Contains("Draft patch for src/Program.cs"), "draft should reference the file path");
            True(draft.Contains("Rationale:"), "draft should include rationale header");
            True(draft.Contains("Proposed content:"), "draft should include proposed content header");
            return Task.CompletedTask;
        }

        public static async Task AgentWorkspaceApplyDraftPatchWritesFile()
        {
            using var temp = new TempDir();
            var root = temp.PathFor("workspace");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "Program.cs"), "old content\n");

            var tools = new Aether.Agent.Services.AgentWorkspaceTools();
            var options = new Aether.Agent.Models.AgentWorkspaceOptions(root);
            var result = await tools.ApplyDraftPatchAsync(options, "Program.cs", "new content\nsecond line\n");

            Equal("Program.cs", result.RelativePath, "applied patch should report the relative path");
            Equal("new content\nsecond line\n", await File.ReadAllTextAsync(Path.Combine(root, "Program.cs")), "applied patch should write the new file content");
        }

        public static Task AgentDraftPatchQueueAndApproval()
        {
            var patch = new Aether.Agent.Models.AgentDraftPatch
            {
                RelativePath = "src/Utils.cs",
                Rationale = "Optimize helper function",
                ProposedContent = "public class Utils { }"
            };
            Equal(Aether.Agent.Models.AgentDraftPatchStatus.Pending, patch.Status, "patch should start as pending");
            True(patch.CreatedAt <= DateTime.UtcNow, "patch created at should be set");

            patch.Status = Aether.Agent.Models.AgentDraftPatchStatus.Approved;
            patch.ApprovedAt = DateTime.UtcNow;
            patch.ApprovedBy = "User";
            Equal(Aether.Agent.Models.AgentDraftPatchStatus.Approved, patch.Status, "patch should be approved");
            True(patch.ApprovedAt.HasValue, "approved at should be set");
            return Task.CompletedTask;
        }

        public static async Task TrustScanUnsetAiRootIsNeutral()
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

        public static Task TrustScanDetectsNetworkExtraArgs()
        {
            var warnings = new TrustService().AnalyzeServerExtraArgs(
                new ServerConfig { Name = "Chat", ExtraArgs = "--host 0.0.0.0 --listen-host=:: --alias local" },
                DateTime.UtcNow);

            Equal(2, warnings.Count, "network-facing host flags should produce warnings");
            Equal(TrustRiskLevel.High, warnings[0].RiskLevel, "network exposure should be high risk");
            False(warnings[0].Target.Contains(';', StringComparison.Ordinal), "trust warnings should not synthesize shell separators");
            return Task.CompletedTask;
        }

        private sealed class FakeCheckProvider : IInspectionCheckProvider
        {
            private readonly Func<Task<IReadOnlyList<InspectionCheck>>> _factory;
            public IReadOnlyList<string> Views { get; }

            public FakeCheckProvider(IReadOnlyList<string> views, Func<Task<IReadOnlyList<InspectionCheck>>> factory)
            {
                Views = views;
                _factory = factory;
            }

            public Task<IReadOnlyList<InspectionCheck>> GetChecksAsync(CancellationToken ct = default) => _factory();
        }

        private static InspectionCheck Check(string id, string view, CheckSeverity severity = CheckSeverity.Ready) =>
            new(id, view, "Test", id, severity, "summary", "detail", string.Empty, false, string.Empty);

        public static async Task InspectionEngineFiltersProvidersByView()
        {
            var doctorProvider = new FakeCheckProvider(["doctor"], () => Task.FromResult<IReadOnlyList<InspectionCheck>>([Check("d1", "doctor")]));
            var trustProvider = new FakeCheckProvider(["trust"], () => Task.FromResult<IReadOnlyList<InspectionCheck>>([Check("t1", "trust")]));
            var engine = new InspectionEngine([doctorProvider, trustProvider]);

            var doctorReport = await engine.RunAsync("doctor");
            True(doctorReport.Checks.Select(c => c.Id).SequenceEqual(["d1"]), "doctor view should only include doctor checks");

            var allReport = await engine.RunAsync();
            Equal(2, allReport.Checks.Count, "null view should run every provider");
        }

        public static async Task InspectionEngineReportsProviderFailureAsErrorCheck()
        {
            var failing = new FakeCheckProvider(["doctor"], () => throw new InvalidOperationException("boom"));
            var engine = new InspectionEngine([failing]);

            var report = await engine.RunAsync("doctor");
            var check = report.Checks.Single();
            Equal(CheckSeverity.Error, check.Severity, "a throwing provider should surface as an error check, not crash the scan");
            True(check.Diagnostics.Contains("boom", StringComparison.Ordinal), "diagnostics should include the exception detail");
        }

        public static async Task DoctorTrustPrivacyContributeChecksToOwnView()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");

            var doctor = new DoctorService(
                settings,
                new RuntimeProfileService(settings),
                new FakeVoiceProviderRegistry(settings),
                new FakeSecretStore(),
                new SqliteRagStore(settings),
                new ThrowingEmbeddingService(),
                new FakeSystemInfo(),
                new PythonHealthValidator(),
                new NoOpReranker());
            var trust = new TrustService(settings);
            var privacy = new PrivacyAuditService(settings, new FakeSecretStore(), new RuntimeLogService(settings), new FakeVoiceProviderRegistry(settings), new SqliteTraceStore(settings));
            var engine = new InspectionEngine([doctor, trust, privacy]);

            var doctorReport = await engine.RunAsync("doctor");
            True(doctorReport.Checks.Count > 0, "doctor view should include Doctor's own checks");
            True(doctorReport.Checks.All(c => c.View == "doctor"), "doctor view should not leak trust or privacy checks");

            var trustReport = await engine.RunAsync("trust");
            True(trustReport.Checks.All(c => c.View == "trust"), "trust view should only contain trust checks");

            var privacyReport = await engine.RunAsync("privacy");
            True(privacyReport.Checks.Count > 0, "privacy view should include Privacy Audit's checks");
        }

        public static Task SourceStringsAvoidLongDashes()
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

        public static string ExpectedSha256(string content) =>
            Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

        public static async Task SecretStoreFallbackWithoutPlaintext()
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
                var localKey = Path.Combine(settings.Settings.DataManagement.DataRootDirectory, "secrets.local.key");
                True(File.Exists(localVault), "fallback vault should exist");
                True(File.Exists(localKey), "fallback key should exist");
                var json = await File.ReadAllTextAsync(localVault);
                True(json.Contains("v2:", StringComparison.Ordinal), "fallback vault should use versioned encrypted values");
                False(json.Contains("sk-test-secret", StringComparison.Ordinal), "fallback vault should not contain plaintext");

                var values = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
                var firstCiphertext = values["openai-api-key"];
                await store.StoreAsync("openai-api-key", "sk-test-secret");
                values = JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(localVault)) ?? [];
                var secondCiphertext = values["openai-api-key"];
                True(secondCiphertext.StartsWith("v2:", StringComparison.Ordinal), "fallback ciphertext should keep the versioned prefix");
                False(string.Equals(firstCiphertext, secondCiphertext, StringComparison.Ordinal), "fallback ciphertext should use a fresh per-secret salt");
                var decoded = Convert.FromBase64String(secondCiphertext["v2:".Length..]);
                True(decoded.Length > 32, "fallback ciphertext should include salt, IV, and encrypted payload");
            }
            finally
            {
                Environment.SetEnvironmentVariable("AETHER_DISABLE_OS_KEYCHAIN", previous);
            }
        }

        public static async Task SecretStoreKeyFileIsWrittenAtomicallyWithRestrictedPermissions()
        {
            using var temp = new TempDir();
            var previous = Environment.GetEnvironmentVariable("AETHER_DISABLE_OS_KEYCHAIN");
            Environment.SetEnvironmentVariable("AETHER_DISABLE_OS_KEYCHAIN", "1");
            try
            {
                var settings = NewSettings(temp);
                var dataRoot = temp.PathFor("data");
                settings.Settings.DataManagement.DataRootDirectory = dataRoot;
                var store = new SecretStore(settings);

                await store.StoreAsync("openai-api-key", "sk-test-secret");

                var localKey = Path.Combine(dataRoot, "secrets.local.key");
                True(File.Exists(localKey), "fallback key file should be created");
                var leftoverTemp = Directory.GetFiles(dataRoot, "secrets.local.key.*.tmp");
                Equal(0, leftoverTemp.Length, "the key file's temp-write artifact should not survive a successful write");

                if (!OperatingSystem.IsWindows())
                {
                    var mode = File.GetUnixFileMode(localKey);
                    Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode, "the key file should be restricted to the owner");
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("AETHER_DISABLE_OS_KEYCHAIN", previous);
            }
        }

        public static async Task SecretStoreLogsWarningWhenStoredSecretCannotBeDecrypted()
        {
            using var temp = new TempDir();
            var previous = Environment.GetEnvironmentVariable("AETHER_DISABLE_OS_KEYCHAIN");
            Environment.SetEnvironmentVariable("AETHER_DISABLE_OS_KEYCHAIN", "1");
            try
            {
                var settings = NewSettings(temp);
                var dataRoot = temp.PathFor("data");
                settings.Settings.DataManagement.DataRootDirectory = dataRoot;
                var log = new RuntimeLogService(settings);
                var store = new SecretStore(settings, log);

                var reference = await store.StoreAsync("openai-api-key", "sk-test-secret");

                var localKey = Path.Combine(dataRoot, "secrets.local.key");
                await File.WriteAllTextAsync(localKey, Convert.ToBase64String(new byte[32]));

                var resolved = await store.ResolveAsync(reference);
                Equal(string.Empty, resolved, "a secret that fails to decrypt under a replaced key should resolve empty rather than throw");
                True(log.GetEntries().Any(e => e.Level == RuntimeLogLevel.Warning && e.Message.Contains("could not be decrypted", StringComparison.OrdinalIgnoreCase)),
                    "a total decrypt failure should be logged instead of silently swallowed");
            }
            finally
            {
                Environment.SetEnvironmentVariable("AETHER_DISABLE_OS_KEYCHAIN", previous);
            }
        }

        public static async Task SettingsSavePrunesPerConversationMemoryOverrides()
        {
            using var temp = new TempDir();
            var path = temp.PathFor("settings.json");
            var service = new SettingsService(path);
            service.Settings.Memory.Enabled = true;
            service.Settings.Memory.EnabledPerConversation["inherits-global"] = true;
            service.Settings.Memory.EnabledPerConversation["explicit-off"] = false;

            await service.SaveAsync();

            False(service.Settings.Memory.EnabledPerConversation.ContainsKey("inherits-global"),
                "settings should drop redundant per-conversation memory overrides");
            Equal(false, service.Settings.Memory.EnabledPerConversation["explicit-off"],
                "settings should keep overrides that differ from the global memory setting");
        }

        public static async Task SettingsSaveDeduplicatesDefaultManagedServers()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            var firstChat = new ServerConfig { Name = "Chat", EmbeddingsMode = false, Port = 8080 };
            var duplicateChat = new ServerConfig { Name = "Chat", EmbeddingsMode = false, Port = 8080 };
            var firstEmbedding = new ServerConfig { Name = "Embeddings", EmbeddingsMode = true, Port = 8080 };
            var duplicateEmbedding = new ServerConfig { Name = "Embeddings", EmbeddingsMode = true, Port = 8080 };
            settings.Settings.ManagedServers =
            [
                firstChat,
                duplicateChat,
                firstEmbedding,
                duplicateEmbedding
            ];

            await settings.SaveAsync();

            Equal(2, settings.Settings.ManagedServers.Count, "default managed server cards should not duplicate");
            Equal(1, settings.Settings.ManagedServers.Count(s => !s.EmbeddingsMode && s.Name == "Chat"), "one chat card should remain");
            Equal(1, settings.Settings.ManagedServers.Count(s => s.EmbeddingsMode && s.Name == "Embeddings"), "one embeddings card should remain");
        }

        public static async Task RuntimeProfileValidation()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            var service = new RuntimeProfileService(settings);
            var profile = new RuntimeProfile
            {
                Id = "runtime-1",
                Name = "  Custom Runtime  ",
                Kind = RuntimeKind.LlamaCpp,
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
            Equal("server-1", saved.LinkedServerId, "linked server id should be trimmed for a llama.cpp profile");

            var nonLlama = RuntimeProfileService.NormalizeProfile(new RuntimeProfile
            {
                Kind = RuntimeKind.OpenAiCompatible,
                StartManagedLlamaServer = true,
                LinkedServerId = "server-1"
            });
            False(nonLlama.StartManagedLlamaServer, "StartManagedLlamaServer is a llama.cpp-only concept and should be inert for other runtime kinds");
            Equal(string.Empty, nonLlama.LinkedServerId, "LinkedServerId is a llama.cpp-only concept and should be cleared for other runtime kinds");

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

        public static Task RuntimeProfilesAreDeduplicated()
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

        public static async Task SettingsSaveMigratesOpenAiKey()
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

        public static async Task SettingsLoadMigratesLegacySharedLocalApiTokenToNamedEntry()
        {
            using var temp = new TempDir();
            var path = temp.PathFor("settings/settings.json");

            var writer = new SettingsService(path);
            writer.Settings.LocalApi.ApiToken = "secret:local-api-token-legacy";
            await writer.SaveAsync();

            var reader = new SettingsService(path);
            await reader.LoadAsync();

            Equal(1, reader.Settings.LocalApi.Tokens.Count, "the legacy shared token should migrate into exactly one named entry");
            Equal("Default", reader.Settings.LocalApi.Tokens[0].Name, "the migrated entry should be named Default");
            Equal("secret:local-api-token-legacy", reader.Settings.LocalApi.Tokens[0].SecretRef, "the migrated entry should carry the original secret reference");
            Equal(string.Empty, reader.Settings.LocalApi.ApiToken, "the legacy field should be cleared once migrated");

            // Migration should be idempotent: loading again must not duplicate the entry.
            var reloaded = new SettingsService(path);
            await reloaded.LoadAsync();
            Equal(1, reloaded.Settings.LocalApi.Tokens.Count, "re-loading already-migrated settings should not duplicate the token entry");
        }

        public static async Task SettingsLoadBacksUpUnreadableJson()
        {
            using var temp = new TempDir();
            var path = temp.PathFor("settings/settings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, "{ not valid json");

            var settings = new SettingsService(path);
            await settings.LoadAsync();

            True(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "settings.json.corrupt-*").Any(),
                "unreadable settings should be copied aside before defaults are loaded");
        }

        public static async Task SettingsChildViewModelsApplyToSettings()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            var vm = NewSettingsViewModel(settings, new FakeSecretStore());

            vm.Llm.LlamaCppBaseUrl = "http://127.0.0.1:9000";
            vm.Llm.OpenAiEnabled = true;
            vm.Rag.EmbeddingModel = "local-embed";
            vm.Rag.RagRerankerModelPath = temp.PathFor("reranker");
            vm.Data.DataRootDirectory = temp.PathFor("data");
            vm.Data.LocalAiAssetsRoot = temp.PathFor("ai");
            vm.Ui.SelectedTheme = "Dark";
            vm.Ui.EnableGlobalHotkeys = true;
            vm.Memory.MemoryFeatureEnabled = true;
            vm.Memory.MemoryInjectionTokenBudget = 700;

            await vm.SaveCommand.ExecuteAsync(null);

            Equal("http://127.0.0.1:9000", settings.Settings.Llm.LlamaCppBaseUrl, "llm section should apply base URL");
            Equal(true, settings.Settings.Llm.OpenAiEnabled, "llm section should apply remote toggle");
            Equal("local-embed", settings.Settings.Rag.EmbeddingModel, "rag section should apply embedding model");
            Equal(temp.PathFor("reranker"), settings.Settings.Rag.RerankerModelPath, "rag section should apply reranker path");
            Equal(temp.PathFor("data"), settings.Settings.DataManagement.DataRootDirectory, "data section should apply data root");
            Equal(temp.PathFor("ai"), settings.Settings.DataManagement.LocalAiAssetsRoot, "data section should apply AI assets root");
            Equal("Dark", settings.Settings.Ui.Theme, "ui section should apply theme");
            Equal(true, settings.Settings.Ui.EnableGlobalHotkeys, "ui section should apply global hotkey toggle");
            Equal(true, settings.Settings.Memory.Enabled, "memory section should apply enable flag");
            Equal(700, settings.Settings.Memory.InjectionTokenBudget, "memory section should apply token budget");
        }

        public static async Task DraftPatchPreviewDecisionCompletes()
        {
            var vm = new DraftPatchDiffViewModel(new PatchDiffService());
            await vm.LoadAsync("src/File.cs", "old", "new");
            bool? decision = null;
            vm.DecisionCompleted += value => decision = value;

            await vm.ApplyCommand.ExecuteAsync(null);
            Equal(true, decision, "apply should complete with true");

            decision = null;
            await vm.CancelCommand.ExecuteAsync(null);
            Equal(false, decision, "cancel should complete with false");
        }

        public static async Task SettingsSavePreservesExistingSecretReference()
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

        public static async Task SettingsSavePersistsGlobalHotkeyPreference()
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

        public static Task ServerProcessArgumentsAreSafe()
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
            ContainsInOrder(args, "--pooling", "mean", "embeddings mode should default to OAI-compatible mean pooling");
            return Task.CompletedTask;
        }

        public static Task ServerProcessArgumentsKeepExplicitPoolingChoice()
        {
            var args = ServerProcessManager.BuildLaunchArguments(new ServerConfig
            {
                ModelPath = "/models/local model.gguf",
                Port = 9091,
                ContextSize = 4096,
                EmbeddingsMode = true,
                ExtraArgs = "--pooling cls --flag"
            }).ToList();

            Equal(1, args.Count(a => string.Equals(a, "--pooling", StringComparison.Ordinal)), "explicit pooling should not be duplicated");
            ContainsInOrder(args, "--pooling", "cls", "explicit pooling value should be preserved");
            return Task.CompletedTask;
        }

        public static Task ServerAutoTunePlansDescendingGpuCandidates()
        {
            var args = ServerProcessManager.BuildGpuLayerCandidates(42);

            ContainsInOrder(args.Select(x => x.ToString()).ToList(), "999", "128", "auto-tune should prefer high GPU layer candidates first");
            True(args.Contains(42), "configured GPU layer count should be included as a candidate");
            Equal(0, args[^1], "CPU fallback should be the final auto-tune candidate");
            Equal(12, ServerProcessManager.ParseGpuLayerLog("llm_load_tensors: offloaded 12/33 layers to GPU").Used, "offloaded layer logs should parse");
            Equal(4341, DoctorService.TryParseLlamaBuild("llama.cpp b4341"), "llama.cpp build tags should parse");
            return Task.CompletedTask;
        }

        public static async Task EmbeddingClientSurfacesPoolingHintWhenServerRejectsNonePooling()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.Llm.LlamaCppBaseUrl = "http://localhost:8080";

            var body = "{\"error\":{\"code\":400,\"message\":\"Pooling type 'none' is not OAI compatible. Please use a different pooling type\",\"type\":\"invalid_request_error\"}}";
            using var http = new HttpClient(new FixedStatusHandler(HttpStatusCode.BadRequest, body));
            var embed = new LlamaCppEmbeddingService(settings, http);

            try
            {
                await embed.EmbedBatchAsync(["hello world"]);
                throw new InvalidOperationException("Expected an InvalidOperationException for pooling-incompatible HTTP 400 response.");
            }
            catch (InvalidOperationException ex)
            {
                True(ex.Message.Contains("--pooling mean", StringComparison.Ordinal), "error should suggest an OAI-compatible pooling mode");
                True(ex.Message.Contains("embedding model", StringComparison.OrdinalIgnoreCase), "error should suggest using an embedding model");
            }
        }

        public static async Task ConversationAutoSummaryStoresMemoriesWhenImportant()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            settings.Settings.Memory.Enabled = true;
            settings.Settings.Memory.AutoSummarizeImportanceThreshold = 0.2;
            settings.Settings.Memory.MaxMemoriesPerConversation = 10;
            settings.Settings.Llm.DefaultModel = "memory-test";

            var conversationStore = new ConversationStore(settings);
            var memoryStore = new MemoryStore(settings);
            await conversationStore.InitializeAsync();
            await memoryStore.InitializeAsync();

            var conversation = new Conversation
            {
                Id = "conv-1",
                Title = "Memory worthy chat",
                ModelId = "memory-test",
                Messages =
                [
                    new Message { Role = "user", Content = "I prefer Australian spelling and optimisation focused solutions." },
                    new Message { Role = "assistant", Content = "Noted, I will use Australian English and performance-first approaches." },
                    new Message { Role = "user", Content = "Please remember this preference for future sessions too." },
                    new Message { Role = "assistant", Content = "I can store that as durable memory." }
                ]
            };
            await conversationStore.SaveAsync(conversation);

            var service = new ConversationMemoryService(
                settings,
                conversationStore,
                memoryStore,
                new MemoryExtractionService(),
                new MemoryMarkerLlm("[MEMORY: User prefers Australian English spelling.] [MEMORY: User prioritises performance optimisation.]"),
                new RuntimeLogService(settings));

            await service.RunAutoSummaryAsync("conv-1");

            var memories = await memoryStore.GetAllAsync(includeArchived: true);
            True(memories.Count >= 2, "auto-summary should persist extracted memories");
            True(memories.Any(m => m.Tags.Contains("auto_summary")), "auto-summary memories should be tagged");
            True(memories.All(m => m.SourceConversationId == "conv-1"), "memories should keep source conversation id");
        }

        public static async Task MemoryStoreCrudAndSearchWorks()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new MemoryStore(settings);
            await store.InitializeAsync();

            var memory = new Memory
            {
                Id = "mem-1",
                Category = "preferences",
                Content = "User prefers concise summaries.",
                Tags = ["preference", "summary"],
                ImportanceScore = 0.85,
                SourceConversationId = "conv-42"
            };

            await store.SaveAsync(memory);
            var byId = await store.GetByIdAsync("mem-1");
            True(byId is not null, "saved memory should be retrievable by id");
            Equal("preferences", byId!.Category, "saved category should round trip");

            var byCategory = await store.GetByCategoryAsync("preferences");
            True(byCategory.Any(m => m.Id == "mem-1"), "category query should include saved memory");

            var found = await store.SearchAsync("concise");
            True(found.Any(m => m.Id == "mem-1"), "search should match content");

            byId.IsArchived = true;
            await store.SaveAsync(byId);
            var visible = await store.GetAllAsync(includeArchived: false);
            False(visible.Any(m => m.Id == "mem-1"), "archived memory should be hidden when includeArchived is false");

            await store.DeleteAsync("mem-1");
            var deleted = await store.GetByIdAsync("mem-1");
            Equal<Memory?>(null, deleted, "deleted memory should not exist");
        }

        public static async Task MemoryStoreRoundTripsExplicitSourceAndBackfillsLegacyRows()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new MemoryStore(settings);
            await store.InitializeAsync();

            var withSource = new Memory
            {
                Id = "mem-with-source",
                Category = "facts",
                Content = "Explicit source memory.",
                SourceConversationId = "conv-1",
                Source = new SourceReference(ProvenanceKind.Memory, "Conversation", Locator: "conv-1", Snippet: "hi")
            };
            await store.SaveAsync(withSource);
            var reloaded = await store.GetByIdAsync("mem-with-source");
            True(reloaded?.Source is not null, "an explicitly set source should round-trip");
            Equal(ProvenanceKind.Memory, reloaded!.Source!.Kind, "round-tripped source should keep its kind");
            Equal("conv-1", reloaded.Source!.Locator, "round-tripped source should keep its locator");
            Equal("hi", reloaded.Source!.Snippet, "round-tripped source should keep its snippet");

            // A memory saved without an explicit Source (only the legacy
            // SourceConversationId field) should still get a source_json of
            // null on disk, and backfill a SourceReference purely at read
            // time from the conversation id (docs/review/03-next-level-roadmap.md
            // Phase 1: "no data rewrite").
            var legacy = new Memory
            {
                Id = "mem-legacy",
                Category = "facts",
                Content = "Legacy memory with no structured source.",
                SourceConversationId = "conv-2"
            };
            await store.SaveAsync(legacy);
            var reloadedLegacy = await store.GetByIdAsync("mem-legacy");
            True(reloadedLegacy?.Source is not null, "a legacy row should backfill a source reference at read time");
            Equal(ProvenanceKind.Memory, reloadedLegacy!.Source!.Kind, "backfilled source should be memory-kind");
            Equal("conv-2", reloadedLegacy.Source!.Locator, "backfilled source should point at the source conversation id");

            var noSourceAtAll = new Memory { Id = "mem-none", Category = "facts", Content = "No conversation link." };
            await store.SaveAsync(noSourceAtAll);
            var reloadedNone = await store.GetByIdAsync("mem-none");
            Equal<SourceReference?>(null, reloadedNone!.Source, "a memory with no conversation id and no explicit source should stay null");
        }

        public static async Task MemoryStoreCountsByConversationWork()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new MemoryStore(settings);
            await store.InitializeAsync();

            var memA = new Memory { Id = "c1-m1", Category = "facts", Content = "A1", SourceConversationId = "conv-A" };
            var memB = new Memory { Id = "c1-m2", Category = "facts", Content = "A2", SourceConversationId = "conv-A" };
            var memC = new Memory { Id = "c2-m1", Category = "preferences", Content = "B1", SourceConversationId = "conv-B" };

            await store.SaveAsync(memA);
            await store.SaveAsync(memB);
            await store.SaveAsync(memC);

            var countA = await store.GetCountByConversationAsync("conv-A");
            var countB = await store.GetCountByConversationAsync("conv-B");
            Equal(2, countA, "conv-A should have two stored memories");
            Equal(1, countB, "conv-B should have one stored memory");

            var batch = await store.GetCountsByConversationAsync(new[] { "conv-A", "conv-B", "conv-missing" });
            Equal(2, batch["conv-A"], "batch count for conv-A should match");
            Equal(1, batch["conv-B"], "batch count for conv-B should match");
            Equal(0, batch["conv-missing"], "missing conversations should count as zero");
        }

        public static async Task MemoryExtractionParsesAndCleansMarkers()
        {
            var service = new MemoryExtractionService();
            var output = "Great, noted. [MEMORY: User prefers Australian English.] I can help. [MEMORY: User values performance over quick fixes.]";

            var memories = await service.ExtractMemoriesAsync(output, "conv-extract");
            Equal(2, memories.Count, "extractor should parse two markers");
            True(memories.All(m => m.SourceConversationId == "conv-extract"), "extracted memories should preserve source conversation id");
            True(memories.Any(m => m.Category == "preferences"), "preference-like text should be categorised as preferences");
            True(memories.All(m => m.Source is { Kind: ProvenanceKind.Memory, Locator: "conv-extract" }),
                "extracted memories should carry a structured source reference pointing at the conversation");

            var cleaned = service.CleanMemoryMarkers(output);
            False(cleaned.Contains("[MEMORY:", StringComparison.Ordinal), "cleaned output should remove marker syntax");
        }

        public static async Task MemoryInjectionRespectsTokenBudgetAndPriority()
        {
            var service = new MemoryInjectionService();
            var memories = new List<Memory>
            {
                new() { Id = "1", Category = "facts", Content = "Pinned memory with high value.", IsPinned = true, ImportanceScore = 0.9, UpdatedAt = DateTime.UtcNow },
                new() { Id = "2", Category = "preferences", Content = new string('x', 1200), IsPinned = false, ImportanceScore = 0.8, UpdatedAt = DateTime.UtcNow.AddMinutes(-1) },
                new() { Id = "3", Category = "interests", Content = "Secondary memory.", IsPinned = false, ImportanceScore = 0.2, UpdatedAt = DateTime.UtcNow.AddMinutes(-2) }
            };

            var selected = await service.SelectMemoriesForInjectionAsync(memories, tokenBudget: 80);
            True(selected.Count >= 1, "selection should return at least one memory under budget");
            Equal("1", selected[0].Id, "pinned high-importance memory should be selected first");

            var context = service.BuildMemoryContext(selected);
            True(context.Contains("Stored Memories", StringComparison.Ordinal), "memory context should include heading");
        }

        public static async Task MemoryInjectionUsesFullBudget()
        {
            var service = new MemoryInjectionService();
            var memories = Enumerable.Range(1, 6)
                .Select(i => new Memory
                {
                    Id = i.ToString(),
                    Category = "facts",
                    Content = new string((char)('a' + i), 40),
                    ImportanceScore = 1.0 - (i / 10.0),
                    UpdatedAt = DateTime.UtcNow.AddMinutes(-i)
                })
                .ToList();

            var selected = await service.SelectMemoriesForInjectionAsync(memories, tokenBudget: 100);
            True(selected.Count > memories.Count / 2, "selection should not stop at half the candidate count");
        }

        public static Task XttsApiTemplateDelegatesToGenerator()
        {
            var serviceTemplate = new LocalAiSetupService(new PythonHealthValidator()).BuildXttsApiScript("/models", "/output");
            var generatorType = typeof(LocalAiSetupService).Assembly.GetType("Aether.Services.LocalAiSetupScriptGenerator")
                ?? throw new InvalidOperationException("Generator type missing.");
            var method = generatorType.GetMethod("BuildXttsApiScript", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?? throw new InvalidOperationException("Generator method missing.");
            var generatorTemplate = (string)method.Invoke(null, ["/models", "/output"])!;
            Equal(generatorTemplate, serviceTemplate, "service template should delegate to generator output");
            return Task.CompletedTask;
        }

        public static Task ExtraArgsParserHandlesEscapedQuotes()
        {
            var args = ExtraArgsParser.Split("--arg \"value with \\\"inner\\\" quotes\" --flag").ToList();
            Equal(3, args.Count, "parser should return three args");
            Equal("--arg", args[0], "first arg should be flag");
            Equal("value with \"inner\" quotes", args[1], "escaped quotes should be preserved");
            Equal("--flag", args[2], "trailing arg should parse");
            return Task.CompletedTask;
        }

        public static Task LocalApiProcessManagerResolvesPackagedExecutableFirst()
        {
            using var temp = new TempDir();
            var baseDir = temp.PathFor("desktop-out") + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(baseDir);
            var localApiDir = Path.Combine(baseDir, "LocalApi");
            Directory.CreateDirectory(localApiDir);
            var exeName = OperatingSystem.IsWindows() ? "Aether.LocalApi.exe" : "Aether.LocalApi";
            File.WriteAllText(Path.Combine(localApiDir, exeName), "stub");

            var (fileName, args) = LocalApiProcessManager.ResolveLaunchTarget(baseDir);
            True(fileName is not null && fileName.EndsWith(exeName, StringComparison.Ordinal),
                "a packaged sibling LocalApi executable should be preferred");
            Equal(0, args.Count, "the packaged executable needs no launch arguments");
            return Task.CompletedTask;
        }

        public static Task LocalApiProcessManagerFallsBackToDevBuildOutput()
        {
            using var temp = new TempDir();
            var repoRoot = temp.PathFor("repo");
            File.WriteAllText(Path.Combine(Directory.CreateDirectory(repoRoot).FullName, "Aether.sln"), "");
            var desktopBin = Path.Combine(repoRoot, "src", "Aether.Desktop", "bin", "Debug", "net10.0");
            Directory.CreateDirectory(desktopBin);
            var localApiBin = Path.Combine(repoRoot, "src", "Aether.LocalApi", "bin", "Debug", "net10.0");
            Directory.CreateDirectory(localApiBin);
            File.WriteAllText(Path.Combine(localApiBin, "Aether.LocalApi.dll"), "stub");

            var dll = LocalApiProcessManager.ResolveDevBuildDll(desktopBin);
            True(dll is not null && File.Exists(dll), "dev fallback should locate the sibling project's own build output");

            var (fileName, args) = LocalApiProcessManager.ResolveLaunchTarget(desktopBin);
            Equal("dotnet", fileName, "dev fallback should launch through the dotnet muxer");
            Equal(1, args.Count, "dev fallback should pass exactly the resolved dll path");
            return Task.CompletedTask;
        }

        public static Task LocalApiProcessManagerReturnsNullWhenNothingIsBuilt()
        {
            using var temp = new TempDir();
            var baseDir = temp.PathFor("nowhere");
            Directory.CreateDirectory(baseDir);

            var (fileName, _) = LocalApiProcessManager.ResolveLaunchTarget(baseDir);
            True(fileName is null, "with no packaged install and no dev build output, there is nothing to launch");
            return Task.CompletedTask;
        }

        public static Task BenchmarkCsvNormalizesEmbeddedNewlines()
        {
            var run = new BenchmarkRun
            {
                Results =
                [
                    new BenchmarkResult
                    {
                        CaseName = "case\none",
                        Phase = "phase",
                        Error = "line\r\ntwo",
                        FailureCategory = "quoted \"value\""
                    }
                ]
            };
            var method = typeof(BenchmarkService).GetMethod("ToCsv", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?? throw new InvalidOperationException("ToCsv method missing.");
            var csv = (string)method.Invoke(null, [run])!;
            Equal(2, csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length, "CSV should contain header plus one data row");
            True(csv.Contains("\"case one\"", StringComparison.Ordinal), "case newline should become a space");
            True(csv.Contains("\"line two\"", StringComparison.Ordinal), "error newline should become a space");
            True(csv.Contains("\"quoted \"\"value\"\"\"", StringComparison.Ordinal), "quotes should stay escaped");
            return Task.CompletedTask;
        }

        public static async Task RerankerHashVerificationRejectsMismatch()
        {
            using var temp = new TempDir();
            var file = temp.PathFor("asset.bin");
            await File.WriteAllTextAsync(file, "known");
            True(await OnnxCrossEncoderReranker.VerifyFileSha256Async(file, Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("known"))).ToLowerInvariant()),
                "known hash should verify");
            False(await OnnxCrossEncoderReranker.VerifyFileSha256Async(file, OnnxCrossEncoderReranker.VocabSha256),
                "mismatched hash should fail");
        }

        public static Task ConversationExportProducesMarkdownAndJson()
        {
            var service = new ConversationExportService();
            var conversation = new Conversation
            {
                Id = "conv-export",
                Title = "Export Test",
                SystemPrompt = "Be concise.",
                Messages =
                [
                    new Message { Role = "user", Content = "Hello", CreatedAt = DateTime.UtcNow },
                    new Message { Role = "assistant", Content = "Hi", IsError = true, ModelId = "model-a", CreatedAt = DateTime.UtcNow }
                ]
            };

            var md = service.BuildExport(conversation, ConversationExportFormat.Markdown);
            var json = service.BuildExport(conversation, ConversationExportFormat.Json);
            True(md.Contains("## System Prompt", StringComparison.Ordinal), "markdown should include system prompt");
            True(md.Contains("Status: `error or incomplete`", StringComparison.Ordinal), "markdown should mark incomplete/error messages");
            True(json.Contains("\"Id\": \"conv-export\"", StringComparison.Ordinal), "json should include conversation id");
            return Task.CompletedTask;
        }

        private sealed class FixedStatusHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _statusCode;
            private readonly string _body;

            public FixedStatusHandler(HttpStatusCode statusCode, string body)
            {
                _statusCode = statusCode;
                _body = body;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
            {
                return Task.FromResult(new HttpResponseMessage(_statusCode)
                {
                    Content = new StringContent(_body)
                });
            }
        }

        private sealed class CapturingSpeechHandler : HttpMessageHandler
        {
            public string AuthorizationHeader { get; private set; } = string.Empty;
            public string RequestUri { get; private set; } = string.Empty;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
            {
                AuthorizationHeader = request.Headers.Authorization?.ToString() ?? string.Empty;
                RequestUri = request.RequestUri?.ToString() ?? string.Empty;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(System.Text.Encoding.ASCII.GetBytes("RIFFfakeWAVE"))
                });
            }
        }

        private sealed class ResolvingSecretStore : ISecretStore
        {
            private readonly string _resolved;

            public ResolvingSecretStore(string resolved)
            {
                _resolved = resolved;
            }

            public bool IsReference(string value) => value.StartsWith("secret:", StringComparison.OrdinalIgnoreCase);
            public Task<string> StoreAsync(string name, string secret, System.Threading.CancellationToken ct = default) => Task.FromResult(secret);
            public Task<string> ResolveAsync(string valueOrReference, System.Threading.CancellationToken ct = default) =>
                Task.FromResult(IsReference(valueOrReference) ? _resolved : valueOrReference);
            public Task<string> BackendLabelAsync(System.Threading.CancellationToken ct = default) => Task.FromResult("Resolving fake");
        }

        private sealed class StaticDoctorService : IDoctorService
        {
            private readonly DoctorReport _report;

            public StaticDoctorService(DoctorReport report)
            {
                _report = report;
            }

            public Task<DoctorReport> ScanAsync(System.Threading.CancellationToken ct = default) => Task.FromResult(_report);
            public Task<bool> InstallRerankerAssetsAsync(System.Threading.CancellationToken ct = default) => Task.FromResult(true);
            public Task<bool> InstallRerankerAssetsAsync(IProgress<string> progress, System.Threading.CancellationToken ct = default) => Task.FromResult(true);
            public Task<bool> InstallEmbeddingModelAsync(System.Threading.CancellationToken ct = default) => Task.FromResult(true);
            public Task<bool> InstallEmbeddingModelAsync(IProgress<string> progress, System.Threading.CancellationToken ct = default) => Task.FromResult(true);
            public Task<bool> InstallLlamaServerUpdateAsync(System.Threading.CancellationToken ct = default) => Task.FromResult(true);
            public Task<bool> InstallLlamaServerUpdateAsync(IProgress<string> progress, System.Threading.CancellationToken ct = default) => Task.FromResult(true);
            public Task<bool> InstallNativeKokoroAssetsAsync(System.Threading.CancellationToken ct = default) => Task.FromResult(true);
            public Task<bool> InstallNativeKokoroAssetsAsync(IProgress<string> progress, System.Threading.CancellationToken ct = default) => Task.FromResult(true);
        }

        private sealed class ThrowingEmbeddingService : IEmbeddingService
        {
            public int Dimensions => 0;

            public Task<float[]> EmbedAsync(string text, System.Threading.CancellationToken ct = default) =>
                throw new InvalidOperationException("Embedding backend should not be called when no embedding model is installed.");

            public Task<List<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, System.Threading.CancellationToken ct = default) =>
                throw new InvalidOperationException("Embedding backend should not be called when no embedding model is installed.");
        }
    }
}
