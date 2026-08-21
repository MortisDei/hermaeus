using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Desktop.Controls;
using Hermaeus.Rag.Embeddings;
using Hermaeus.Rag.Retrieval;
using Hermaeus.Rag.Storage;
using Hermaeus.Services;
using Hermaeus.Services.ProcessManagement;
using Hermaeus.ViewModels;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests
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
            Equal(13, suites.Count, "fresh benchmark db should seed all starter suites");
            True(suites.Any(s => s.Id == suite.Id), "starter suite should be listed");

            var run = await service.RunAsync(suite, new LlmModel { Id = "fake-agent", Name = "Fake Agent", Provider = "Test" });
            True(run.Results.Count > 0, "benchmark run should record results");
            var runs = await service.GetRunsAsync();
            Equal(1, runs.Count, "recorded run should be listed");
            True(File.Exists(Path.Combine(settings.Settings.DataManagement.DataRootDirectory, "benchmarks.db")), "benchmark db should be created");
        }

        /// <summary>r11 3.7: EnsureInitializedAsync lacked the SemaphoreSlim init gate every other store uses, so concurrent first calls could race the starter-suite seed (read `existing`, then insert) into a double-insert or a PK violation.</summary>
        public static async Task BenchmarkInitializationIsGatedAgainstConcurrentFirstCalls()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var service = new BenchmarkService(settings, new FakeLlm(), new FakeSystemInfo(), new FakeEvalStore());

            var results = await Task.WhenAll(
                service.GetSuitesAsync(),
                service.GetSuitesAsync(),
                service.GetSuitesAsync(),
                service.GetSuitesAsync());

            foreach (var suites in results)
                Equal(13, suites.Count, "every concurrent first call should observe exactly one set of seeded starter suites");

            var finalSuites = await service.GetSuitesAsync();
            Equal(13, finalSuites.Count, "starter suites should not be double-seeded by a concurrent first call race");
        }

        public static Task BenchmarkStarterSuitesIncludeExpandedDeterministicSet()
        {
            var suites = BenchmarkService.StarterSuites();
            var ids = suites.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);

            Equal(13, suites.Count, "starter suite count should include the original seven, five expanded suites, and r27's speed check");
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
                "hermaeus-workflows",
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

        public static async Task BenchmarkRerunPreservesCaseTags()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var service = new BenchmarkService(settings, new FakeLlm(), new FakeSystemInfo(), new FakeEvalStore());

            var suite = BenchmarkService.StarterSuites().First();
            suite.MaxCases = 1;
            suite.Cases[0].Tags = ["smoke", "critical"];

            var run = await service.RunAsync(suite, new LlmModel { Id = "fake-agent", Name = "Fake Agent", Provider = "Test" });
            True(run.Results.Count > 0 && run.Results.All(r => r.Tags.Contains("smoke") && r.Tags.Contains("critical")),
                "initial run results should carry case tags");

            var rerun = await service.RerunAsync(run.Id);

            True(rerun.Results.Count > 0, "rerun should produce results");
            True(rerun.Results.All(r => r.Tags.Contains("smoke") && r.Tags.Contains("critical")),
                "r11 2.7: rerun rebuilds cases from stored results and must preserve Tags, or reruns silently fall out of per-tag insights");
        }

        /// <summary>r11 2.6: the benchmark judge fields are removed from the UI (no code ever invoked a judge model) but must stay on the model classes so previously stored suite/run JSON still deserializes.</summary>
        public static Task BenchmarkJudgeFieldsRoundTripWithoutUiBinding()
        {
            var axamlPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Hermaeus.Desktop/Views/BenchmarkView.axaml"));
            var axaml = File.ReadAllText(axamlPath);
            False(axaml.Contains("UseJudge", StringComparison.Ordinal), "benchmark view should no longer bind UseJudge");
            False(axaml.Contains("JudgeModelId", StringComparison.Ordinal), "benchmark view should no longer bind JudgeModelId");

            const string json = """{"id":"legacy-suite","name":"Legacy","useJudge":true,"judgeModelId":"gpt-4o","cases":[]}""";
            var suite = JsonSerializer.Deserialize<BenchmarkSuite>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            True(suite is not null, "legacy stored suite JSON with judge fields should still deserialize");
            True(suite!.UseJudge, "stored useJudge=true should round-trip even though the UI no longer edits it");
            Equal("gpt-4o", suite.JudgeModelId, "stored judgeModelId should round-trip");
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

        /// <summary>r17 02-benchmark-truth.md 2.1: real llama-server timings, not chars/4 over
        /// total time, drive tok/s and the display fields when a provider reports them.</summary>
        public static async Task BenchmarkServerTimingsProduceRealTokensPerSecond()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var llm = new FakeCapturingTimingsLlm();
            var service = new BenchmarkService(settings, llm, new FakeSystemInfo(), new FakeEvalStore());

            var suite = BenchmarkService.StarterSuites().First();
            suite.MaxCases = 1;
            var run = await service.RunAsync(suite, new LlmModel { Id = "timings", Name = "Timings", Provider = "Test" });

            var result = run.Results[0];
            Equal("server-timings", result.MeasurementSource, "a provider reporting timings should be labeled as measured, not estimated");
            Equal(100d, result.ApproxTokensPerSecond, "predicted_n / predicted_ms * 1000 = 20/200*1000 = 100");
            Equal(100d, result.PromptTokensPerSecond, "prompt_n / prompt_ms * 1000 = 10/100*1000 = 100");
            Equal(10, result.PromptTokens, "prompt token count should come from ServerTimings.PromptTokens");
            Equal(20, result.GeneratedTokens, "generated token count should come from ServerTimings.PredictedTokens");
        }

        /// <summary>r17 02-benchmark-truth.md 2.1: a fake without timings must keep reproducing
        /// today's chars/4 numbers, just labeled as an estimate.</summary>
        public static async Task BenchmarkFallbackWithoutTimingsIsLabeledAsEstimated()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var service = new BenchmarkService(settings, new FakeLlm(), new FakeSystemInfo(), new FakeEvalStore());

            var suite = BenchmarkService.StarterSuites().First();
            suite.MaxCases = 1;
            var run = await service.RunAsync(suite, new LlmModel { Id = "fake-agent", Name = "Fake Agent", Provider = "Test" });

            var result = run.Results[0];
            Equal("chars-approx", result.MeasurementSource, "a provider without timings should be labeled as estimated, not measured");
            True(result.PromptTokens is null, "no provider timings means no prompt token count");
            True(result.GeneratedTokens is null, "no provider timings means no generated token count");
        }

        /// <summary>r17 02-benchmark-truth.md 2.2: the chars/4 fallback used to divide by total
        /// elapsed time including prompt processing, reporting a long-prefill run as slow twice.
        /// Exercised directly via reflection (mirrors the existing ToCsv reflection test) since
        /// FillTiming is private and its inputs need exact control that streaming timing cannot
        /// give deterministically.</summary>
        public static Task BenchmarkFallbackTokensPerSecondUsesTheDecodeWindowNotTotalTime()
        {
            var method = typeof(BenchmarkService).GetMethod("FillTiming", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?? throw new InvalidOperationException("FillTiming method not found");
            var result = new BenchmarkResult { Output = new string('x', 400) };

            method.Invoke(null, [result, 4000L, 5000L, null]);

            // Old behavior (denominator = total 5s): (400/4) / 5 = 20 tok/s.
            // New behavior (denominator = decode window 1s): (400/4) / 1 = 100 tok/s.
            Equal(100d, result.ApproxTokensPerSecond, "fallback tok/s should use the decode window (total - first token), not total elapsed time");
            NotEqual(20d, result.ApproxTokensPerSecond, "the old total-time-denominator bug must not resurface");
            return Task.CompletedTask;
        }

        /// <summary>r17 02-benchmark-truth.md 2.3: ResourceScore measured the Hermaeus process's
        /// own RSS delta, noise for a model running in a different process or remotely - it is
        /// now always neutral regardless of the observed delta.</summary>
        public static Task BenchmarkResourceScoreIsAlwaysNeutral()
        {
            var method = typeof(BenchmarkService).GetMethod("FillResources", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?? throw new InvalidOperationException("FillResources method not found");
            var result = new BenchmarkResult();
            var before = new SystemSnapshot { ProcessMemoryBytes = 100_000_000 };
            var after = new SystemSnapshot { ProcessMemoryBytes = 900_000_000 };

            method.Invoke(null, [result, before, after]);

            Equal(1.0, result.ResourceScore, "resource score should be neutral even with a large observed RSS delta");
            Equal(100_000_000L, result.ProcessMemoryBeforeBytes, "before/after memory snapshots should still be recorded for display");
            Equal(900_000_000L, result.ProcessMemoryAfterBytes, "after memory snapshot should still be recorded for display");
            return Task.CompletedTask;
        }

        /// <summary>r17 02-benchmark-truth.md 2.4: a run against a managed GGUF model should
        /// carry the server's real context/layers/threads/path and a non-empty quantization,
        /// not the app-process values every run used to stamp regardless of the model.</summary>
        public static async Task BenchmarkMetadataCarriesManagedServerConfigAndQuantization()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var modelPath = WriteMinimalLlamaGgufFixture(temp);
            settings.Settings.ManagedServers.Add(new ServerConfig
            {
                Name = "Chat",
                ModelPath = modelPath,
                ContextSize = 16384,
                GpuLayers = 24,
                Threads = 6,
                KvCacheTypeK = "q8_0",
                KvCacheTypeV = "q4_0",
                FlashAttention = "on"
            });
            var service = new BenchmarkService(settings, new FakeLlm(), new FakeSystemInfo(), new FakeEvalStore());

            var suite = BenchmarkService.StarterSuites().First();
            suite.MaxCases = 1;
            var run = await service.RunAsync(suite, new LlmModel { Id = modelPath, Name = "Local", Provider = "llama.cpp", ProviderTag = "llama.cpp" });

            Equal("llama.cpp", run.Metadata.RuntimeKind, "RuntimeKind should come from the model's ProviderTag");
            Equal(16384, run.Metadata.ContextSize, "ContextSize should come from the matching managed ServerConfig");
            Equal(24, run.Metadata.GpuLayers, "GpuLayers should come from the matching managed ServerConfig");
            Equal(6, run.Metadata.Threads, "Threads should come from the matching managed ServerConfig, not Environment.ProcessorCount");
            Equal(modelPath, run.Metadata.ModelPath, "ModelPath should come from the matching managed ServerConfig");
            False(string.IsNullOrEmpty(run.Metadata.Quantization), "a local GGUF model should carry a non-empty quantization label");
            Equal("q8_0", run.Metadata.KvCacheTypeK, "KV cache K type should come from the matching managed ServerConfig");
            Equal("q4_0", run.Metadata.KvCacheTypeV, "KV cache V type should come from the matching managed ServerConfig");
            Equal("on", run.Metadata.FlashAttention, "Flash Attention should come from the matching managed ServerConfig");

            var markdownPath = await service.ExportAsync(run.Id, temp.PathFor("exports"));
            var markdown = await File.ReadAllTextAsync(markdownPath);
            var json = await File.ReadAllTextAsync(Path.ChangeExtension(markdownPath, ".json"));
            var csv = await File.ReadAllTextAsync(Path.ChangeExtension(markdownPath, ".csv"));
            True(markdown.Contains("KV cache K type: `q8_0`", StringComparison.Ordinal), "Markdown export should preserve KV cache K type");
            True(markdown.Contains("Flash Attention: `on`", StringComparison.Ordinal), "Markdown export should preserve Flash Attention");
            True(json.Contains("\"KvCacheTypeV\": \"q4_0\"", StringComparison.Ordinal), "JSON export should preserve KV cache V type");
            True(csv.Contains("kv_cache_type_k", StringComparison.Ordinal), "CSV export should label engine configuration provenance");
            True(csv.Contains("\"q8_0\",\"q4_0\",\"on\"", StringComparison.Ordinal), "CSV export should preserve engine configuration provenance");
        }

        /// <summary>r17 02-benchmark-truth.md 2.5: RerunAsync used to rebuild the model as a bare
        /// {Id,Name,Provider}, losing DefaultContextSize and everything else that feeds 2.4's
        /// metadata. It must resolve the live model instance first when the model still exists.</summary>
        public static async Task BenchmarkRerunResolvesTheLiveModelInstance()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var service = new BenchmarkService(settings, new UsageLlm(), new FakeSystemInfo(), new FakeEvalStore());

            var suite = BenchmarkService.StarterSuites().First();
            suite.MaxCases = 1;
            // Deliberately thin, mirroring what an original run against a since-updated model
            // profile might have stored - DefaultContextSize is not carried on this instance.
            var run = await service.RunAsync(suite, new LlmModel { Id = "usage", Name = "Usage", Provider = "Test" });
            True(run.Metadata.ContextSize is null, "the original thin model instance should not have carried a context size");

            var rerun = await service.RerunAsync(run.Id);

            Equal(100, rerun.Metadata.ContextSize, "rerun should resolve the live model (DefaultContextSize 100 from UsageLlm.GetModelsAsync), not rebuild a hollow one");
        }

        /// <summary>r17 02-benchmark-truth.md 2.6: cache_prompt must be disabled only on
        /// iteration 0 (the Cold phase) so a warm KV cache from a prior request cannot make a
        /// "Cold" run's first case look faster than it genuinely was.</summary>
        public static async Task BenchmarkDisablesPromptCacheOnlyOnTheColdIteration()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var llm = new FakeCapturingTimingsLlm();
            var service = new BenchmarkService(settings, llm, new FakeSystemInfo(), new FakeEvalStore());

            var suite = BenchmarkService.StarterSuites().First();
            suite.MaxCases = 1;
            suite.IterationsPerCase = 2;
            await service.RunAsync(suite, new LlmModel { Id = "timings", Name = "Timings", Provider = "Test" });

            Equal(2, llm.CapturedOptions.Count, "one case with two iterations should send exactly two requests");
            True(llm.CapturedOptions[0].DisablePromptCache, "iteration 0 (Cold) should disable the prompt cache");
            False(llm.CapturedOptions[1].DisablePromptCache, "iteration 1 (Warm) should keep the prompt cache enabled");
        }

        private static string WriteMinimalLlamaGgufFixture(TempDir temp)
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                w.Write(System.Text.Encoding.ASCII.GetBytes("GGUF"));
                w.Write((uint)3);
                w.Write((ulong)0);
                w.Write((ulong)2);

                void WriteKey(string key)
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(key);
                    w.Write((ulong)bytes.Length);
                    w.Write(bytes);
                }

                WriteKey("general.architecture");
                w.Write((uint)8);
                var arch = System.Text.Encoding.UTF8.GetBytes("llama");
                w.Write((ulong)arch.Length);
                w.Write(arch);

                WriteKey("general.file_type");
                w.Write((uint)4);
                w.Write((uint)15); // Q4_K_M
            }

            var path = temp.PathFor("model.gguf");
            File.WriteAllBytes(path, ms.ToArray());
            return path;
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

        // ── r21 3.4: RAG chat-injection disclosure ────────────────────────

        public static async Task PrivacyAuditDisclosesRagInjectionOnlyWhenRagAvailableAndProviderRemote()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            settings.Settings.Llm.OpenAiEnabled = true;
            settings.Settings.Llm.OpenAiBaseUrl = "https://api.example.invalid/v1";
            var ragStore = new SqliteRagStore(settings);
            await ragStore.InitializeAsync();

            var withRag = new PrivacyAuditService(settings, new FakeSecretStore(), new RuntimeLogService(settings), new FakeVoiceProviderRegistry(settings), new SqliteTraceStore(settings), ragStore);
            var remoteWithRag = (await withRag.ScanAsync()).Single(i => i.Name == "Remote providers");
            True(remoteWithRag.Detail.Contains("Chat knowledge context", StringComparison.Ordinal),
                "a remote chat provider with the RAG subsystem available should disclose chat knowledge injection, regardless of whether any conversation currently has a dataset attached");

            var withoutRag = new PrivacyAuditService(settings, new FakeSecretStore(), new RuntimeLogService(settings), new FakeVoiceProviderRegistry(settings), new SqliteTraceStore(settings));
            var remoteWithoutRag = (await withoutRag.ScanAsync()).Single(i => i.Name == "Remote providers");
            False(remoteWithoutRag.Detail.Contains("Chat knowledge context", StringComparison.Ordinal),
                "without the RAG subsystem available, the disclosure must not appear");

            settings.Settings.Llm.OpenAiEnabled = false;
            settings.Settings.Llm.LlamaCppEnabled = true;
            var localOnly = new PrivacyAuditService(settings, new FakeSecretStore(), new RuntimeLogService(settings), new FakeVoiceProviderRegistry(settings), new SqliteTraceStore(settings), ragStore);
            var remoteLocalOnly = (await localOnly.ScanAsync()).Single(i => i.Name == "Remote providers");
            False(remoteLocalOnly.Detail.Contains("Chat knowledge context", StringComparison.Ordinal),
                "with only a local chat provider selected, the disclosure must not appear even if RAG is available");
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

        public static async Task PrivacyAuditCountsOutboundDestinationsAcrossChatVoiceMcp()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var privacyAudit = new PrivacyAuditService(settings, new FakeSecretStore(), new RuntimeLogService(settings), new FakeVoiceProviderRegistry(settings), new SqliteTraceStore(settings));

            Equal(0, await privacyAudit.CountOutboundDestinationsAsync(), "a fresh local-only settings file should count zero outbound destinations");

            settings.Settings.Llm.OpenAiEnabled = true;
            Equal(1, await privacyAudit.CountOutboundDestinationsAsync(), "an enabled remote chat provider should count once");

            settings.Settings.Tts.VoiceProvider = "F5Tts"; // FakeVoiceProviderRegistry marks F5-TTS as VoiceCapability.Remote
            Equal(2, await privacyAudit.CountOutboundDestinationsAsync(), "a remote voice provider should add one more");

            settings.Settings.Mcp.Servers.Add(new McpServerConfig { Name = "example" });
            Equal(3, await privacyAudit.CountOutboundDestinationsAsync(), "each configured MCP server should add one");
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

        public static async Task ModelUsageRollupUpsertsAndSkipsEmptyModelId()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var traces = new SqliteTraceStore(settings);

            await traces.AppendAsync(new TraceRecord { Kind = TraceKind.Chat, ModelId = "model-a", TotalTokens = 10 });
            await traces.AppendAsync(new TraceRecord { Kind = TraceKind.Chat, ModelId = "model-a", TotalTokens = 15 });
            await traces.AppendAsync(new TraceRecord { Kind = TraceKind.Chat, ModelId = string.Empty, TotalTokens = 99 });

            var usage = await traces.GetModelUsageAsync(TraceKind.Chat, 30);
            var row = usage.Single(u => u.ModelId == "model-a");
            Equal(2, row.CallCount, "two appends for the same kind/model/day should upsert into one row");
            Equal(25, row.TotalTokens, "tokens should sum across upserted rows");
            False(usage.Any(u => string.IsNullOrEmpty(u.ModelId)), "an empty model id should not create a usage row");
        }

        public static async Task ModelUsageRollupSurvivesTracePruning()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var traces = new SqliteTraceStore(settings);

            for (var i = 0; i < SqliteTraceStore.MaxTracesPerKind + 20; i++)
                await traces.AppendAsync(new TraceRecord { Kind = TraceKind.Chat, ModelId = "model-a", TotalTokens = 1 });

            var recent = await traces.GetRecentAsync(TraceKind.Chat, 1000);
            Equal(SqliteTraceStore.MaxTracesPerKind, recent.Count, "traces should still prune to the per-kind cap");

            var usage = await traces.GetModelUsageAsync(TraceKind.Chat, 30);
            Equal(SqliteTraceStore.MaxTracesPerKind + 20, usage.Single().CallCount, "model_usage rollup should not be pruned when traces are pruned");
        }

        public static async Task ModelUsageServiceSummarizesDominantModelPerKind()
        {
            IReadOnlyList<ModelUsageRow> rows =
            [
                new(TraceKind.Chat, "model-a", 8, 800),
                new(TraceKind.Chat, "model-b", 2, 200),
                new(TraceKind.Rag, "model-c", 5, 500)
            ];

            var summaries = ModelUsageService.Summarize(rows);

            var chat = summaries.Single(s => s.Kind == TraceKind.Chat);
            Equal(10, chat.TotalCalls, "chat total calls should sum across models");
            Equal("model-a", chat.Dominant!.ModelId, "the model with the most calls should be dominant and first");
            Equal(0.8, chat.Dominant.Share, "share should be call count over kind total");

            var rag = summaries.Single(s => s.Kind == TraceKind.Rag);
            Equal(1, rag.Models.Count, "a kind with a single model should still summarize");
            await Task.CompletedTask;
        }

        public static async Task PrivacyAuditDisclosesModelUsageCounters()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var privacyAudit = new PrivacyAuditService(settings, new FakeSecretStore(), new RuntimeLogService(settings), new FakeVoiceProviderRegistry(settings), new SqliteTraceStore(settings));

            var items = await privacyAudit.ScanAsync();

            True(items.Any(i => i.Name == "Model usage counters" && i.Detail.Contains("Never transmitted", StringComparison.Ordinal)),
                "privacy audit should always disclose the local model usage rollup");
        }

        public static async Task PrivacyAuditFlagsEnabledChannelsOnRemoteVoiceProvider()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.Tts.VoiceProvider = "F5Tts"; // FakeVoiceProviderRegistry marks F5-TTS as VoiceCapability.Remote
            settings.Settings.Tts.Channels["Chat"] = new VoiceChannelConfig { Enabled = true };
            var privacyAudit = new PrivacyAuditService(settings, new FakeSecretStore(), new RuntimeLogService(settings), new FakeVoiceProviderRegistry(settings), new SqliteTraceStore(settings));

            var items = await privacyAudit.ScanAsync();
            True(items.Any(i => i.Name == "Voice channels sending text remotely" && i.Detail.Contains("Chat", StringComparison.Ordinal)),
                "an enabled channel on a remote voice provider should be flagged by name");

            settings.Settings.Tts.VoiceProvider = "KokoroNative";
            var localItems = await privacyAudit.ScanAsync();
            False(localItems.Any(i => i.Name == "Voice channels sending text remotely"), "a local voice provider should not trigger the remote-channel item");
        }

        public static Task AgentContextReceiptOmitsEmptySectionsAndCountsPopulatedOnes()
        {
            var pack = new Hermaeus.Agent.Models.AgentContextPack();
            pack.RetrievedMemory.Add(new Hermaeus.Agent.Models.AgentRetrievedItem("workspace-memory", "note-1", "remember this", 1.0));
            pack.RetrievedMemory.Add(new Hermaeus.Agent.Models.AgentRetrievedItem("workspace-memory", "note-2", "and this too", 1.0));
            pack.Lessons.Add(new Hermaeus.Agent.Models.AgentRetrievedItem("lesson", "lesson-1", "a captured lesson", 0.8));
            // RetrievedFiles, ProjectInstructions, and RAG (Source == "rag" inside RetrievedMemory) are left empty.

            var sections = Hermaeus.Agent.Services.AgentContextReceiptBuilder.Build(pack);

            Equal(2, sections.Count, "only the two sections that actually contributed items should appear");
            var memory = sections.Single(s => s.SectionLabel == "Memory");
            Equal(2, memory.ItemCount, "memory section should count both injected items");
            True(memory.EstimatedTokens > 0, "a populated section should have a nonzero token estimate");
            Equal("note-1, note-2", string.Join(", ", memory.ItemIdentifiers), "identifiers should list the item titles in order");

            var lessons = sections.Single(s => s.SectionLabel == "Lessons");
            Equal(1, lessons.ItemCount, "lessons section should count the one captured lesson");

            False(sections.Any(s => s.SectionLabel == "RAG"), "RAG should be omitted, not shown empty");
            False(sections.Any(s => s.SectionLabel == "Workspace files"), "workspace files should be omitted, not shown empty");
            False(sections.Any(s => s.SectionLabel == "Project instructions"), "project instructions should be omitted, not shown empty");
            return Task.CompletedTask;
        }

        public static Task RagTraceChunkPlainLanguageSummaryIsDeterministic()
        {
            var strong = new Hermaeus.Rag.Models.RagTraceChunk
            {
                Rank = 2, OutOfCount = 8, VectorScore = 0.82f, MatchedTerm = "migration", MatchedTermCount = 3, RerankScore = 0.9f
            };
            Equal("Ranked 2nd of 8: strong semantic match, term 'migration' matched 3 times, reranker confirmed this ranking.",
                strong.PlainLanguageSummary, "all three signals present should render in a fixed order");

            var vectorOnly = new Hermaeus.Rag.Models.RagTraceChunk { Rank = 1, OutOfCount = 5, VectorScore = 0.3f };
            Equal("Ranked 1st of 5: weak semantic match.", vectorOnly.PlainLanguageSummary,
                "a low vector score with no other signals should still render a valid sentence");

            var moderate = new Hermaeus.Rag.Models.RagTraceChunk { Rank = 3, OutOfCount = 3, VectorScore = 0.6f };
            Equal("Ranked 3rd of 3: moderate semantic match.", moderate.PlainLanguageSummary, "0.5-0.75 should classify as moderate");

            var noSignals = new Hermaeus.Rag.Models.RagTraceChunk { Rank = 11, OutOfCount = 20 };
            Equal("Ranked 11th of 20.", noSignals.PlainLanguageSummary, "with no scored signals the summary should still name rank and count");

            var singleMatch = new Hermaeus.Rag.Models.RagTraceChunk { Rank = 1, OutOfCount = 1, MatchedTerm = "apple", MatchedTermCount = 1 };
            Equal("Ranked 1st of 1: term 'apple' matched 1 time.", singleMatch.PlainLanguageSummary, "a single match should not pluralize 'time'");

            return Task.CompletedTask;
        }

        // r6 02-usage-history-recommendations.md 2.3: the Doctor benchmark
        // advisory should append a usage-aware sentence when a Chat usage
        // insight recommends a switch, even standalone (no ranking-only
        // condition needed) so long as it clears the same gap threshold.
        public static async Task DoctorBenchmarkAdvisoryAppendsUsageAwareSentence()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            settings.Settings.Llm.DefaultModel = "model-a";

            var dominant = new ModelAggregate("model-a", "Dominant Model", "", "llama.cpp", 3, 30, 0.5, 10, 1, 0.4, 2.0, DateTime.UtcNow, false);
            var better = new ModelAggregate("model-b", "Better Model", "", "llama.cpp", 3, 30, 0.95, 60, 1, 0.9, 5.5, DateTime.UtcNow, false);
            var usageInsight = new UsageInsight(
                TraceKind.Chat, "model-a", "model-a", 1.0, 25, "Better Model", 50.0,
                "You mostly use model-a for chat; your benchmarks rank Better Model higher for overall tasks.");
            var report = new BenchmarkInsightsReport(
                TotalRuns: 6, ComparableRuns: 6, ModelCount: 2, OldestComparableRun: DateTime.UtcNow.AddDays(-5),
                Models: [better, dominant], TagLeaderboards: [], Comparisons: [], Caveats: [],
                UsageInsights: [usageInsight],
                // r25 doc 04 4.1: a report with ranked models always has a shared case basis.
                ComparisonBasisCaseCount: 30);

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
                benchmarkInsights: new FakeBenchmarkInsightsService(report));

            var scan = await doctor.ScanAsync();
            var check = scan.Checks.SingleOrDefault(c => c.Key == "benchmark-advisory");
            True(check is not null, "a usage insight with a recommendation should produce a benchmark-advisory check");
            Equal(DoctorCheckStatus.Info, check!.Status, "the usage-aware advisory should stay Info severity, never Warning/Error");
            True(check.Detail.Contains("Usage-aware:", StringComparison.Ordinal), "the usage-aware sentence should be appended to the check detail");
            True(check.Detail.Contains("Better Model", StringComparison.Ordinal), "the detail should name the recommended model");
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

        /// <summary>r18 03-model-catalog-and-memory-ui.md 3.2: verified against a real HF hub
        /// cache that the reported small-file clutter was mmproj vision-projector and mtp
        /// multi-token-prediction draft-weight companion files, not sharded GGUF fragments -
        /// neither is loadable as a standalone chat model in Hermaeus.</summary>
        public static Task LocalAiAssetsExcludesCompanionGgufFiles()
        {
            using var temp = new TempDir();
            var root = temp.PathFor("AI");
            var models = Path.Combine(root, "Models");
            Directory.CreateDirectory(models);
            var chat = Path.Combine(models, "gemma-4-E4B-it.gguf");
            var mmproj = Path.Combine(models, "mmproj-F16.gguf");
            var mtp = Path.Combine(models, "mtp-gemma-4-E4B-it.gguf");
            File.WriteAllText(chat, "model");
            File.WriteAllText(mmproj, "projector");
            File.WriteAllText(mtp, "draft weights");

            var found = LocalAiAssetLocator.FindGgufModels(root);

            Equal(1, found.Count, "companion GGUF files should not be listed as standalone models");
            True(found.Contains(chat), "the real chat model should still be discovered");
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

        // ── r19 1.4/1.5: tray-restore double-init and the honesty of the last-operation warning ──

        public static Task RecordStartupCalledTwiceDoesNotOverwritePreviousSession()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");

            var firstRun = new AppLifecycleJournalService(settings);
            firstRun.RecordStartup();
            firstRun.RecordOperation("some earlier operation");

            var secondRun = new AppLifecycleJournalService(settings);
            var previous = secondRun.RecordStartup();
            True(previous is not null, "the second journal's first RecordStartup should read back the first session");

            // Simulate App.axaml.cs re-raising window.Opened (a tray restore)
            // without the one-shot guard: RecordStartup called again on the
            // SAME instance must not clobber PreviousSession with the
            // current, still-running session.
            var againStartedAtUtc = previous!.StartedAtUtc;
            var second = secondRun.RecordStartup();
            Equal(previous.StartedAtUtc, second!.StartedAtUtc, "a repeated RecordStartup call must not overwrite the already-captured PreviousSession");
            Equal(againStartedAtUtc, secondRun.PreviousSession!.StartedAtUtc, "PreviousSession itself must be unchanged after a second RecordStartup call");
            return Task.CompletedTask;
        }

        public static async Task DoctorUsesNeutralWordingWhenNoRiskyOperationWasInFlight()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");

            var crashedRun = new AppLifecycleJournalService(settings);
            crashedRun.RecordStartup();
            crashedRun.RecordOperation("running");
            // No RecordCleanExit(): simulates a crash with no risky operation in flight.

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
            Equal(DoctorCheckStatus.Warning, check.Status, "Doctor should still warn about the unclean exit");
            False(check.Detail.Contains("last recorded operation was", StringComparison.Ordinal),
                "a neutral 'running' breadcrumb must not be presented as a named operation in flight");
            True(check.Detail.Contains("No risky operation", StringComparison.Ordinal),
                "the detail should say plainly that nothing risky was in flight");
        }

        public static async Task DoctorStillNamesARealInFlightOperation()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");

            var crashedRun = new AppLifecycleJournalService(settings);
            crashedRun.RecordStartup();
            crashedRun.RecordOperation("loading Kokoro native ONNX session (EnsureLoadedAsync)");
            // No matching "... session loaded" breadcrumb: the load itself crashed.

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
            True(check.Detail.Contains("loading Kokoro native ONNX session (EnsureLoadedAsync)", StringComparison.Ordinal),
                "a genuinely in-flight operation must still be named");
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
            if (OperatingSystem.IsWindows())
            {
                var hotkeyCheck = report.Checks.Single(c => c.Key == "hotkeys");
                Equal(DoctorCheckStatus.Ready, hotkeyCheck.Status, "Windows supports system-wide hotkeys and should report Ready, not a neutral Info");
            }
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
            True(raised.Any(t => t.Title == "Hermaeus Doctor found warnings" && t.Kind == ToastKind.Warning),
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

        /// <summary>r11 1.8: rocm5.8 has never been a published PyTorch index (verified: HTTP 403), so the ROCm branch always failed pip resolution; cu118 was years stale. This pins the current per-backend argument construction so a future edit is a visible, reviewable diff.</summary>
        public static Task LocalAiSetupBuildsCorrectTorchInstallArgsPerBackend()
        {
            var cuda = LocalAiSetupService.BuildTorchInstallArgs("cuda");
            Equal("--index-url", cuda[^2], "cuda args should end with an index-url flag");
            Equal(LocalAiSetupService.CudaTorchIndexUrl, cuda[^1], "cuda should use the pinned CUDA index");
            False(cuda[^1].Contains("cu118", StringComparison.Ordinal), "cuda index should not be the stale cu118");

            var rocm = LocalAiSetupService.BuildTorchInstallArgs("rocm");
            Equal(LocalAiSetupService.RocmTorchIndexUrl, rocm[^1], "rocm should use the pinned ROCm index");
            False(rocm[^1].Contains("rocm5.8", StringComparison.Ordinal), "rocm index should not be the never-published rocm5.8");

            var mps = LocalAiSetupService.BuildTorchInstallArgs("mps");
            False(mps.Contains("--index-url"), "mps should install from the default PyPI index");

            var cpu = LocalAiSetupService.BuildTorchInstallArgs("cpu");
            True(cpu[^1].EndsWith("/whl/cpu", StringComparison.Ordinal), "cpu should use the cpu-only index");
            return Task.CompletedTask;
        }

        public static Task LocalAiSetupDoesNotShipPlaceholderHashes()
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
            var file = Path.Combine(root, "src", "Hermaeus.Services", "LocalAiSetupService.cs");
            var source = File.ReadAllText(file);
            False(source.Contains("b0aca5b1", StringComparison.OrdinalIgnoreCase), "setup service should not ship placeholder hashes");
            False(source.Contains("Placeholder: Replace", StringComparison.OrdinalIgnoreCase), "setup service should not ship placeholder hash comments");
            return Task.CompletedTask;
        }

        /// <summary>r11 1.6: ModelHashes was an empty map, so the Phi-4 download's "verify hash for security if available" branch was dead code and it landed unverified.</summary>
        public static Task LocalAiSetupPinsARealSha256ForThePhi4Download()
        {
            var field = typeof(LocalAiSetupService).GetField("ModelHashes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var hashes = (System.Collections.Generic.Dictionary<string, string>)field!.GetValue(null)!;

            True(hashes.Count > 0, "ModelHashes should no longer be an empty dead map");
            True(hashes.TryGetValue(LocalAiSetupService.Phi4ModelUrl, out var sha256), "Phi-4 download URL should have a pinned hash");
            Equal(64, sha256!.Length, "SHA256 should be a 64-character hex string, not a placeholder");
            True(sha256.All(Uri.IsHexDigit), "pinned hash should be valid hex");
            return Task.CompletedTask;
        }

        /// <summary>A failed hash verification must delete the downloaded file, matching the Doctor embedding-model install pattern.</summary>
        public static async Task LocalAiSetupPhi4DownloadRejectsHashMismatchAndDeletesTheFile()
        {
            using var temp = new TempDir();
            var targetPath = temp.PathFor("models/phi-4-mini-reasoning-Q5_K_M.gguf");
            var downloads = new ModelDownloadService(new HttpClient(new CapturingRangeHttpHandler("tampered content, not the real model")));
            var service = new LocalAiSetupService(new PythonHealthValidator(), downloads);
            var action = new LocalAiSetupAction(
                "download-phi4-model",
                LocalAiSetupActionKind.DownloadGgufModel,
                "Download Phi-4 Mini Reasoning Model",
                targetPath,
                [LocalAiSetupService.Phi4ModelUrl],
                LocalAiSetupRiskLevel.Medium,
                "test",
                true, true, true);

            var settings = NewSettings(temp);
            var result = await service.RunActionAsync(action, settings.Settings);

            False(result.Success, "download should fail when the SHA256 does not match the pinned hash");
            False(File.Exists(targetPath), "a failed verification should remove the downloaded file");
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
                new FakeToasts(),
                new FakeSystemInfo());

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

        public static async Task DoctorEmbeddingInstallKeepsExistingNomicUntilVerifiedQwenIsReady()
        {
            using var temp = new TempDir();
            var root = temp.PathFor("AI");
            var embedDir = Path.Combine(root, "Models", "embed");
            Directory.CreateDirectory(embedDir);
            var nomicPath = Path.Combine(embedDir, "nomic-embed-text-v1.5-Q4_K_M.gguf");
            await File.WriteAllTextAsync(nomicPath, "existing nomic model");

            var settings = NewSettings(temp);
            settings.Settings.DataManagement.LocalAiAssetsRoot = root;
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            settings.Settings.Rag.EmbeddingModel = "nomic-embed-text-v1.5";
            settings.Settings.ManagedServers.Clear();
            settings.Settings.ManagedServers.Add(new ServerConfig
            {
                Name = "Embeddings",
                EmbeddingsMode = true,
                ModelPath = nomicPath
            });

            var qwenContent = "verified qwen model";
            var spec = new EmbeddingModelDownloadSpec(
                "Qwen3-Embedding-0.6B",
                "Qwen3-Embedding-0.6B-Q8_0.gguf",
                "https://example.test/Qwen3-Embedding-0.6B-Q8_0.gguf",
                ExpectedSha256(qwenContent));
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
                new ModelDownloadService(new HttpClient(new CapturingRangeHttpHandler(qwenContent))),
                spec);

            var ok = await doctor.InstallEmbeddingModelAsync();
            var qwenPath = Path.Combine(embedDir, spec.FileName);

            True(ok, "verified Qwen download should succeed");
            True(File.Exists(nomicPath), "installing Qwen must retain an existing Nomic model");
            Equal("existing nomic model", await File.ReadAllTextAsync(nomicPath), "installing Qwen must not alter Nomic's file");
            Equal(spec.ModelName, settings.Settings.Rag.EmbeddingModel, "settings should switch only after the Qwen file verifies");
            Equal(qwenPath, settings.Settings.ManagedServers.Single(s => s.EmbeddingsMode).ModelPath, "embedding server should switch to verified Qwen");
        }

        public static Task LlamaServerReleaseDataCoversSupportedPlatforms()
        {
            var service = new LlamaServerSetupService();
            var releases = service.GetSupportedReleaseInfo();

            Equal(6, releases.Count, "release data should cover all supported platforms");
            True(releases.All(entry => entry.Url.Contains(LlamaServerSetupService.PinnedTag, StringComparison.Ordinal)), "release urls should use the expected tag");
            True(releases.All(entry => entry.Url.EndsWith(".zip", StringComparison.Ordinal) || entry.Url.EndsWith(".tar.gz", StringComparison.Ordinal)),
                "release urls should point at real llama.cpp archive assets, not a bare exe");
            True(releases.Select(entry => entry.DisplayName).Distinct(StringComparer.Ordinal).Count() == releases.Count, "release labels should be unique");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Asset list captured from the live GitHub API for tag b10034 on
        /// 2026-07-16 (r11 1.1/1.2 fixture): every platform's default (no
        /// accelerator) build plus a representative sample of the
        /// cuda/rocm/hip/vulkan/sycl/opencl/openvino variants and non-binary
        /// assets it must NOT select.
        /// </summary>
        private static readonly GitHubReleaseAsset[] LlamaB10034Assets =
        [
            new("cudart-llama-bin-win-cuda-12.4-x64.zip", "https://example.test/cudart-win"),
            new("llama-b10034-bin-android-arm64.tar.gz", "https://example.test/android"),
            new("llama-b10034-bin-macos-arm64.tar.gz", "https://example.test/macos-arm64"),
            new("llama-b10034-bin-macos-x64.tar.gz", "https://example.test/macos-x64"),
            new("llama-b10034-bin-ubuntu-arm64.tar.gz", "https://example.test/linux-arm64"),
            new("llama-b10034-bin-ubuntu-openvino-2026.2.1-x64.tar.gz", "https://example.test/ubuntu-openvino"),
            new("llama-b10034-bin-ubuntu-rocm-7.2-x64.tar.gz", "https://example.test/ubuntu-rocm"),
            new("llama-b10034-bin-ubuntu-s390x.tar.gz", "https://example.test/s390x"),
            new("llama-b10034-bin-ubuntu-sycl-fp16-x64.tar.gz", "https://example.test/ubuntu-sycl"),
            new("llama-b10034-bin-ubuntu-vulkan-arm64.tar.gz", "https://example.test/ubuntu-vulkan-arm64"),
            new("llama-b10034-bin-ubuntu-vulkan-x64.tar.gz", "https://example.test/ubuntu-vulkan-x64"),
            new("llama-b10034-bin-ubuntu-x64.tar.gz", "https://example.test/linux-x64"),
            new("llama-b10034-bin-win-cpu-arm64.zip", "https://example.test/win-arm64"),
            new("llama-b10034-bin-win-cpu-x64.zip", "https://example.test/win-x64"),
            new("llama-b10034-bin-win-cuda-12.4-x64.zip", "https://example.test/win-cuda"),
            new("llama-b10034-bin-win-hip-radeon-x64.zip", "https://example.test/win-hip"),
            new("llama-b10034-bin-win-opencl-adreno-arm64.zip", "https://example.test/win-opencl"),
            new("llama-b10034-bin-win-openvino-2026.2.1-x64.zip", "https://example.test/win-openvino"),
            new("llama-b10034-bin-win-sycl-x64.zip", "https://example.test/win-sycl"),
            new("llama-b10034-bin-win-vulkan-x64.zip", "https://example.test/win-vulkan"),
            new("llama-b10034-ui.tar.gz", "https://example.test/ui"),
            new("llama-b10034-xcframework.zip", "https://example.test/xcframework")
        ];

        public static Task LlamaServerLatestAssetSelectionFindsCurrentPlatform()
        {
            foreach (var (platform, expectedUrl) in new[]
            {
                (LlamaPlatform.WinX64, "https://example.test/win-x64"),
                (LlamaPlatform.WinArm64, "https://example.test/win-arm64"),
                (LlamaPlatform.LinuxX64, "https://example.test/linux-x64"),
                (LlamaPlatform.LinuxArm64, "https://example.test/linux-arm64"),
                (LlamaPlatform.MacX64, "https://example.test/macos-x64"),
                (LlamaPlatform.MacArm64, "https://example.test/macos-arm64")
            })
            {
                var selected = LlamaServerSetupService.SelectDownloadAsset(LlamaB10034Assets, platform);
                Equal(expectedUrl, selected?.BrowserDownloadUrl, $"asset selection for {platform} should pick the default CPU build, not a cuda/rocm/vulkan/sycl/opencl/openvino variant");
            }

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
            var tools = new Hermaeus.Agent.Services.AgentWorkspaceTools();
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

            var tools = new Hermaeus.Agent.Services.AgentWorkspaceTools();
            var options = new Hermaeus.Agent.Models.AgentWorkspaceOptions(root);
            var result = await tools.ApplyDraftPatchAsync(options, "Program.cs", "new content\nsecond line\n");

            Equal("Program.cs", result.RelativePath, "applied patch should report the relative path");
            Equal("new content\nsecond line\n", await File.ReadAllTextAsync(Path.Combine(root, "Program.cs")), "applied patch should write the new file content");
        }

        public static Task AgentDraftPatchQueueAndApproval()
        {
            var patch = new Hermaeus.Agent.Models.AgentDraftPatch
            {
                RelativePath = "src/Utils.cs",
                Rationale = "Optimize helper function",
                ProposedContent = "public class Utils { }"
            };
            Equal(Hermaeus.Agent.Models.AgentDraftPatchStatus.Pending, patch.Status, "patch should start as pending");
            True(patch.CreatedAt <= DateTime.UtcNow, "patch created at should be set");

            patch.Status = Hermaeus.Agent.Models.AgentDraftPatchStatus.Approved;
            patch.ApprovedAt = DateTime.UtcNow;
            patch.ApprovedBy = "User";
            Equal(Hermaeus.Agent.Models.AgentDraftPatchStatus.Approved, patch.Status, "patch should be approved");
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

        // r6 3.1: InspectionEngine/IInspectionCheckProvider were a dead
        // aggregation path (nothing consumed InspectionEngine.RunAsync); each
        // of Doctor/Trust/PrivacyAuditService owns its checks and its own
        // view already, so this test now verifies that directly instead of
        // through the removed engine.
        public static async Task DoctorTrustPrivacyEachProduceOwnChecks()
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
            var trust = new TrustService();
            var privacy = new PrivacyAuditService(settings, new FakeSecretStore(), new RuntimeLogService(settings), new FakeVoiceProviderRegistry(settings), new SqliteTraceStore(settings));

            var doctorReport = await doctor.ScanAsync();
            True(doctorReport.Checks.Count > 0, "doctor should produce its own checks");

            var trustReport = await trust.ScanAsync(settings.Settings);
            True(trustReport.Items.Count > 0, "trust should produce its own items");

            var privacyItems = await privacy.ScanAsync();
            True(privacyItems.Count > 0, "privacy audit should produce its own items");
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

        /// <summary>
        /// r22 2.5: guards the floor, not full tooltip coverage (that stays a
        /// review-time judgement call, r22 doc 02.4). An icon-only Button/
        /// ToggleButton/RepeatButton with no visible text has no way to
        /// convey what it does except a tooltip.
        /// </summary>
        private static readonly HashSet<string> IconOnlyTooltipAllowlist = new(StringComparer.OrdinalIgnoreCase)
        {
            // Intentionally empty: the r22 sweep found full coverage already.
            // Add entries here only when a real exception is justified, one
            // reason per line, e.g.:
            // "Views/Example.axaml:42" // decorative, never rendered standalone
        };

        public static Task IconOnlyControlsHaveTooltips()
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
            var desktopRoot = Path.Combine(root, "src", "Hermaeus.Desktop");
            var offenders = new List<string>();

            foreach (var path in Directory.EnumerateFiles(desktopRoot, "*.axaml", SearchOption.AllDirectories))
            {
                var doc = XDocument.Load(path, LoadOptions.SetLineInfo);
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');

                foreach (var button in doc.Descendants().Where(e => e.Name.LocalName is "Button" or "ToggleButton" or "RepeatButton"))
                {
                    var hasIcon = button.Descendants().Any(e => e.Name.LocalName is "PathIcon" or "MossIcon");
                    if (!hasIcon)
                        continue;

                    var hasText = button.Descendants().Any(e => e.Name.LocalName == "TextBlock");
                    var hasContentAttribute = button.Attribute("Content") is not null;
                    var hasTooltip = button.Attribute("ToolTip.Tip") is not null;
                    if (hasText || hasContentAttribute || hasTooltip)
                        continue;

                    var line = ((IXmlLineInfo)button).LineNumber;
                    var offender = $"{relative}:{line} <{button.Name.LocalName}>";
                    if (!IconOnlyTooltipAllowlist.Contains($"{relative}:{line}"))
                        offenders.Add(offender);
                }
            }

            Equal(0, offenders.Count, $"icon-only controls need a ToolTip.Tip (or an allowlist entry with a reason): {string.Join(", ", offenders)}");
            return Task.CompletedTask;
        }

        public static string ExpectedSha256(string content) =>
            Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

        public static async Task SecretStoreFallbackWithoutPlaintext()
        {
            using var temp = new TempDir();
            var previous = Environment.GetEnvironmentVariable("HERMAEUS_DISABLE_OS_KEYCHAIN");
            Environment.SetEnvironmentVariable("HERMAEUS_DISABLE_OS_KEYCHAIN", "1");
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
                Environment.SetEnvironmentVariable("HERMAEUS_DISABLE_OS_KEYCHAIN", previous);
            }
        }

        public static async Task SecretStoreKeyFileIsWrittenAtomicallyWithRestrictedPermissions()
        {
            using var temp = new TempDir();
            var previous = Environment.GetEnvironmentVariable("HERMAEUS_DISABLE_OS_KEYCHAIN");
            Environment.SetEnvironmentVariable("HERMAEUS_DISABLE_OS_KEYCHAIN", "1");
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
                Environment.SetEnvironmentVariable("HERMAEUS_DISABLE_OS_KEYCHAIN", previous);
            }
        }

        public static async Task SecretStoreLogsWarningWhenStoredSecretCannotBeDecrypted()
        {
            using var temp = new TempDir();
            var previous = Environment.GetEnvironmentVariable("HERMAEUS_DISABLE_OS_KEYCHAIN");
            Environment.SetEnvironmentVariable("HERMAEUS_DISABLE_OS_KEYCHAIN", "1");
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
                Environment.SetEnvironmentVariable("HERMAEUS_DISABLE_OS_KEYCHAIN", previous);
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
            var previous = Environment.GetEnvironmentVariable("HERMAEUS_DISABLE_OS_KEYCHAIN");
            Environment.SetEnvironmentVariable("HERMAEUS_DISABLE_OS_KEYCHAIN", "1");
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
                Environment.SetEnvironmentVariable("HERMAEUS_DISABLE_OS_KEYCHAIN", previous);
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
            // Establish an isolated previous data root before the VM changes it
            // below. Leaving DataRootDirectory unset resolves the "previous"
            // root to the real %LocalAppData%\Hermaeus on save, so the
            // migration step tries to move files out from under whatever
            // Hermaeus instance is actually running on the machine.
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("initial-data");
            await settings.SaveAsync();
            var vm = NewSettingsViewModel(settings, new FakeSecretStore());

            vm.Llm.LlamaCppBaseUrl = "http://127.0.0.1:9000";
            vm.Llm.OpenAiEnabled = true;
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
            var previous = Environment.GetEnvironmentVariable("HERMAEUS_DISABLE_OS_KEYCHAIN");
            Environment.SetEnvironmentVariable("HERMAEUS_DISABLE_OS_KEYCHAIN", "1");
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
                Environment.SetEnvironmentVariable("HERMAEUS_DISABLE_OS_KEYCHAIN", previous);
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

        public static async Task SettingsSavePersistsShowNavLabelsPreference()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var vm = NewSettingsViewModel(settings, new FakeSecretStore());
            False(vm.Ui.ShowNavLabels, "toolbar labels should default off, matching the pre-r6 icon-only layout");

            vm.Ui.ShowNavLabels = true;
            await vm.SaveCommand.ExecuteAsync(null);
            True(settings.Settings.Ui.ShowNavLabels, "toolbar label preference should persist when enabled");

            vm.Ui.ShowNavLabels = false;
            await vm.SaveCommand.ExecuteAsync(null);
            False(settings.Settings.Ui.ShowNavLabels, "toolbar label preference should persist when disabled");
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
            Equal(10509, DoctorService.TryParseLlamaBuild("version: 0.1.2-dev (build 10509, commit fe8156f78)"), "current llama-server parenthesized build output should parse");
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

        public static Task EmbeddingClientUsesKnownQwenAndLegacyNomicDimensions()
        {
            using var temp = new TempDir();
            var qwenSettings = NewSettings(temp);
            qwenSettings.Settings.Rag.EmbeddingModel = "Qwen3-Embedding-0.6B";
            using var qwen = new LlamaCppEmbeddingService(qwenSettings);
            Equal(1024, qwen.Dimensions, "Qwen3-Embedding-0.6B should report its known 1024 dimensions before the first server response");

            var nomicSettings = NewSettings(temp);
            nomicSettings.Settings.Rag.EmbeddingModel = "nomic-embed-text-v1.5";
            using var nomic = new LlamaCppEmbeddingService(nomicSettings);
            Equal(768, nomic.Dimensions, "existing Nomic configurations should retain their known 768 dimensions");
            return Task.CompletedTask;
        }

        public static async Task ConversationStoreRoundTripsPerMessageModelAttribution()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new ConversationStore(settings);
            await store.InitializeAsync();

            var conversation = new Conversation
            {
                Id = "conv-model-attribution",
                Title = "Model switch mid-chat",
                Messages =
                [
                    new Message { Role = "user", Content = "First question" },
                    new Message { Role = "assistant", Content = "First answer", ModelId = "model-a", DurationMs = 1200 },
                    new Message { Role = "user", Content = "Second question" },
                    new Message { Role = "assistant", Content = "Second answer", ModelId = "model-b", DurationMs = 800 }
                ]
            };
            await store.SaveAsync(conversation);

            var reloaded = await store.GetByIdAsync("conv-model-attribution");
            True(reloaded is not null, "conversation should reload");
            var answers = reloaded!.Messages.Where(m => m.Role == "assistant").ToList();
            Equal("model-a", answers[0].ModelId, "each message should keep the model that actually produced it, not the conversation's current model");
            Equal("model-b", answers[1].ModelId, "a mid-conversation model switch should not overwrite earlier messages' attribution");
            Equal(1200, answers[0].DurationMs, "duration should round-trip alongside model id");

            // Pre-r6 rows have no modelId in their messages_json; simulate by
            // writing a legacy blob directly and confirming it still loads.
            await using var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(temp.PathFor("data"), "conversations.db")}");
            await c.OpenAsync();
            var legacyJson = """[{"Id":"legacy-1","Role":"user","Content":"Hi"},{"Id":"legacy-2","Role":"assistant","Content":"Hello"}]""";
            var update = c.CreateCommand();
            update.CommandText = "UPDATE conversations SET messages_json = $mj WHERE id = $id";
            update.Parameters.AddWithValue("$mj", legacyJson);
            update.Parameters.AddWithValue("$id", "conv-model-attribution");
            await update.ExecuteNonQueryAsync();

            var legacyReloaded = await store.GetByIdAsync("conv-model-attribution");
            True(legacyReloaded is not null, "legacy messages_json without modelId should still load");
            True(legacyReloaded!.Messages.All(m => m.ModelId == string.Empty), "legacy messages should default to an empty model id, not throw");
        }

        // ── r21 1.1: Conversation.RagDatasetId round-trip + legacy-row read ──

        public static async Task ConversationStoreRoundTripsRagDatasetIdAndLegacyRowsReadAsEmpty()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new ConversationStore(settings);
            await store.InitializeAsync();

            var conversation = new Conversation
            {
                Id = "conv-rag-dataset",
                Title = "Knowledge-attached chat",
                RagDatasetId = "dataset-123"
            };
            await store.SaveAsync(conversation);

            var reloaded = await store.GetByIdAsync("conv-rag-dataset");
            True(reloaded is not null, "conversation should reload");
            Equal("dataset-123", reloaded!.RagDatasetId, "RagDatasetId should survive a save/reload round trip");

            // A row written before the rag_dataset_id column existed (its
            // insert statement never mentions the column) must still read
            // back as an empty string, not null.
            await using var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(temp.PathFor("data"), "conversations.db")}");
            await c.OpenAsync();
            var insert = c.CreateCommand();
            insert.CommandText = @"
                INSERT INTO conversations (id, title, model_id, system_prompt, created_at, updated_at, messages_json)
                VALUES ($id, $title, '', '', $ca, $ua, '[]')";
            insert.Parameters.AddWithValue("$id", "conv-legacy-row");
            insert.Parameters.AddWithValue("$title", "Pre-r21 row");
            insert.Parameters.AddWithValue("$ca", DateTime.UtcNow.ToString("O"));
            insert.Parameters.AddWithValue("$ua", DateTime.UtcNow.ToString("O"));
            await insert.ExecuteNonQueryAsync();

            var legacyReloaded = await store.GetByIdAsync("conv-legacy-row");
            True(legacyReloaded is not null, "a pre-migration row missing rag_dataset_id in its insert should still load");
            Equal(string.Empty, legacyReloaded!.RagDatasetId, "a legacy row should read RagDatasetId as empty string, not null");
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

        public static async Task MemorySearchExcludesArchivedRowsFromBothFtsAndLikeFallback()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new MemoryStore(settings);
            await store.InitializeAsync();

            var archived = new Memory { Id = "archived-1", Content = "Forgotten secret plan.", IsArchived = true };
            await store.SaveAsync(archived);
            var visible = new Memory { Id = "visible-1", Content = "Forgotten does not apply here." };
            await store.SaveAsync(visible);

            // A term long enough to hit the FTS branch (r16 02-memory-integrity.md 2.1).
            var ftsResults = await store.SearchAsync("Forgotten");
            False(ftsResults.Any(m => m.Id == "archived-1"), "an archived memory must not be returned by the FTS search branch");
            True(ftsResults.Any(m => m.Id == "visible-1"), "a non-archived match should still be returned");

            // A single-character term is below BuildFtsQuery's 2-char floor,
            // so SearchAsync itself routes to the LIKE fallback branch.
            var likeResults = await store.SearchAsync("F");
            False(likeResults.Any(m => m.Id == "archived-1"), "an archived memory must not be returned by the LIKE fallback branch either");
        }

        public static async Task MemoryExpirationDateArchivesAndIsExcludedFromSearchEvenBeforeTheSweep()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new MemoryStore(settings);
            await store.InitializeAsync();

            var expired = new Memory { Id = "expired-1", Content = "Temporary note about a deadline.", ExpirationDate = DateTime.UtcNow.AddDays(-1) };
            await store.SaveAsync(expired);
            var pinnedExpired = new Memory { Id = "expired-pinned", Content = "Pinned deadline note.", ExpirationDate = DateTime.UtcNow.AddDays(-1), IsPinned = true };
            await store.SaveAsync(pinnedExpired);

            // Belt and braces (2.3): SearchAsync excludes an expired-but-not-yet-
            // archived row even before ArchiveStaleMemoriesAsync ever runs.
            var beforeSweep = await store.SearchAsync("deadline");
            False(beforeSweep.Any(m => m.Id == "expired-1"), "an expired, non-pinned memory should not inject even before the archive sweep");
            True(beforeSweep.Any(m => m.Id == "expired-pinned"), "pin wins over expiration, consistent with every other lifecycle rule");

            var archivedCount = await store.ArchiveStaleMemoriesAsync();
            Equal(1, archivedCount, "only the non-pinned expired memory should be archived by the sweep");

            var afterSweep = await store.GetByIdAsync("expired-1");
            True(afterSweep!.IsArchived, "the expired memory should be archived after the sweep");
            var pinnedAfterSweep = await store.GetByIdAsync("expired-pinned");
            False(pinnedAfterSweep!.IsArchived, "a pinned memory with a past expiry should survive the sweep");
        }

        public static async Task MemoryLifecycleDecaysUnrecalledMemoriesAndArchivesBelowFloor()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new MemoryStore(settings);
            await store.InitializeAsync();

            var stale = new Memory { Id = "stale", Content = "Old unused note.", ImportanceScore = 0.1 };
            await store.SaveAsync(stale);
            var pinned = new Memory { Id = "pinned", Content = "Pinned note.", ImportanceScore = 0.1, IsPinned = true };
            await store.SaveAsync(pinned);
            var fresh = new Memory { Id = "fresh", Content = "Recently used note.", ImportanceScore = 0.9 };
            await store.SaveAsync(fresh);
            await store.MarkRecalledAsync(["fresh"]);

            // Backdate the stale memory's updated_at so decay has something to act on
            // (SaveAsync always stamps UpdatedAt to now, so reach past the public API).
            await using (var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(temp.PathFor("data"), "memories.db")}"))
            {
                await c.OpenAsync();
                var cmd = c.CreateCommand();
                cmd.CommandText = "UPDATE memories SET updated_at = $old WHERE id = 'stale'";
                cmd.Parameters.AddWithValue("$old", DateTime.UtcNow.AddDays(-400).ToString("O"));
                await cmd.ExecuteNonQueryAsync();
            }

            var reloadedStale = await store.GetByIdAsync("stale");
            True(Hermaeus.Core.Services.MemoryLifecycle.ComputeEffectiveImportance(reloadedStale!) < 0.01,
                "a low-importance memory unrecalled for over a year should have decayed close to zero");

            var archivedCount = await store.ArchiveStaleMemoriesAsync(importanceFloor: 0.05, unrecalledForDays: 180);
            Equal(1, archivedCount, "only the stale, low-importance, unrecalled memory should be archived");

            var all = await store.GetAllAsync(includeArchived: false);
            False(all.Any(m => m.Id == "stale"), "the stale memory should no longer appear in the non-archived list");
            True(all.Any(m => m.Id == "pinned"), "a pinned memory should never be auto-archived even if its importance is low");
            True(all.Any(m => m.Id == "fresh"), "a recently recalled memory should not be archived");
        }

        public static async Task ConversationMemoryAppliesUpdateAndForgetMarkersOnlyForInjectedIds()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new MemoryStore(settings);
            await store.InitializeAsync();

            var injected = new Memory { Id = "injected-1", Content = "Old content." };
            await store.SaveAsync(injected);
            var notInjected = new Memory { Id = "not-injected", Content = "Should not change." };
            await store.SaveAsync(notInjected);
            var pinnedInjected = new Memory { Id = "pinned-injected", Content = "Pinned, should survive forget.", IsPinned = true };
            await store.SaveAsync(pinnedInjected);

            var logs = new RuntimeLogService(settings);
            var conversations = new ConversationStore(settings);
            await conversations.InitializeAsync();
            var extractor = new MemoryExtractionService();
            var service = new ConversationMemoryService(settings, conversations, store, extractor, new FakeLlm(), logs);

            var response = """
                Here is the answer. [MEMORY_UPDATE: injected-1 | New corrected content.]
                [MEMORY_UPDATE: not-injected | This should be ignored.]
                [MEMORY_FORGET: pinned-injected]
                """;

            var cleaned = await service.ApplyInjectedMemoryMarkersAsync(response, ["injected-1", "pinned-injected"]);

            False(cleaned.Contains("MEMORY_UPDATE", StringComparison.Ordinal), "all markers should be stripped from the response shown to the user");
            False(cleaned.Contains("MEMORY_FORGET", StringComparison.Ordinal), "all markers should be stripped from the response shown to the user");
            True(cleaned.Contains("Here is the answer.", StringComparison.Ordinal), "surrounding response text should be preserved");

            var updated = await store.GetByIdAsync("injected-1");
            Equal("New corrected content.", updated!.Content, "an update marker for an injected id should apply");

            var untouched = await store.GetByIdAsync("not-injected");
            Equal("Should not change.", untouched!.Content, "an update marker for an id that was not injected this turn should be ignored");

            var pinnedAfter = await store.GetByIdAsync("pinned-injected");
            False(pinnedAfter!.IsArchived, "a pinned memory should never be archived even by a valid forget marker");
        }

        public static async Task ConversationMemorySavesANewMemoryMarkerEvenWithZeroInjectedMemories()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new MemoryStore(settings);
            await store.InitializeAsync();

            var logs = new RuntimeLogService(settings);
            var conversations = new ConversationStore(settings);
            await conversations.InitializeAsync();
            var extractor = new MemoryExtractionService();
            var service = new ConversationMemoryService(settings, conversations, store, extractor, new FakeLlm(), logs);

            // The r16 02-memory-integrity.md 2.2 headline case: saving a NEW
            // memory must not depend on recall having any hits this turn -
            // injectedMemoryIds is empty here.
            var response = "Got it. [MEMORY: User prefers Australian English spelling.]";
            var cleaned = await service.ApplyMemoryMarkersAsync(response, injectedMemoryIds: [], conversationId: "conv-save");

            False(cleaned.Contains("[MEMORY:", StringComparison.Ordinal), "the marker syntax must never reach the persisted transcript");
            True(cleaned.Contains("Got it.", StringComparison.Ordinal), "surrounding response text should be preserved");

            var all = await store.GetAllAsync();
            True(all.Any(m => m.Content == "User prefers Australian English spelling." && m.SourceConversationId == "conv-save"),
                "the extracted memory should actually be saved, not just parsed");
        }

        public static async Task ConversationMemoryDedupesTheSameMarkerAcrossTurnsIntoOneRowWithBumpedFrequency()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new MemoryStore(settings);
            await store.InitializeAsync();

            var logs = new RuntimeLogService(settings);
            var conversations = new ConversationStore(settings);
            await conversations.InitializeAsync();
            var extractor = new MemoryExtractionService();
            var service = new ConversationMemoryService(settings, conversations, store, extractor, new FakeLlm(), logs);

            const string marker = "[MEMORY: User wants todos auto-continued without prompting.]";
            await service.ApplyMemoryMarkersAsync(marker, injectedMemoryIds: [], conversationId: "conv-dedupe");
            await service.ApplyMemoryMarkersAsync(marker, injectedMemoryIds: [], conversationId: "conv-dedupe");

            var all = await store.GetAllAsync();
            var matches = all.Where(m => m.Content == "User wants todos auto-continued without prompting.").ToList();
            Equal(1, matches.Count, "the same marker text saved twice across turns should dedupe into a single row, not pile up duplicates");
            Equal(2, matches[0].FrequencyCount, "the dedupe path should bump FrequencyCount on the second save");
        }

        public static async Task ConversationMemoryDedupesNearEquivalentMarkersAcrossTurns()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new MemoryStore(settings);
            await store.InitializeAsync();
            var conversations = new ConversationStore(settings);
            await conversations.InitializeAsync();
            var service = new ConversationMemoryService(settings, conversations, store,
                new MemoryExtractionService(), new FakeLlm(), new RuntimeLogService(settings));

            await service.ApplyMemoryMarkersAsync("[MEMORY: User prefers Australian English spelling in all replies.]", [], "conv-near-dedupe");
            await service.ApplyMemoryMarkersAsync("[MEMORY: User prefers Australian English spelling for every reply.]", [], "conv-near-dedupe");

            var memories = await store.GetAllAsync();
            Equal(1, memories.Count, "near-equivalent durable memories should reinforce one row rather than accumulating paraphrases");
            Equal(2, memories[0].FrequencyCount, "merging a near-equivalent memory should retain the reinforcement count");
        }

        public static async Task ConversationMemoryAlwaysStripsMarkersRegardlessOfInjectionOrExtraction()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var store = new MemoryStore(settings);
            await store.InitializeAsync();

            var logs = new RuntimeLogService(settings);
            var conversations = new ConversationStore(settings);
            await conversations.InitializeAsync();
            var extractor = new MemoryExtractionService();
            var service = new ConversationMemoryService(settings, conversations, store, extractor, new FakeLlm(), logs);

            // No [MEMORY: ...] block, nothing injected - cleanup must still run
            // and pass ordinary text through unchanged.
            var plain = await service.ApplyMemoryMarkersAsync("Just a normal reply.", injectedMemoryIds: [], conversationId: "conv-plain");
            Equal("Just a normal reply.", plain, "a response with no markers at all should pass through unchanged");
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

        public static async Task MemoryExtractionParsesStructuredJsonWithModelSuppliedMetadata()
        {
            var service = new MemoryExtractionService();
            var output = """
                Sure, here you go:
                ```json
                {"memories": [
                    {"content": "User prefers dark mode.", "category": "preferences", "importance": 0.7, "tags": ["ui", "preference"]},
                    {"content": "User is building a compiler in Rust.", "category": "facts", "importance": 0.9, "tags": []}
                ]}
                ```
                """;

            var memories = await service.ExtractStructuredMemoriesAsync(output, "conv-structured");
            Equal(2, memories.Count, "structured extraction should parse both memory objects");
            True(memories.Any(m => m.Category == "preferences" && Math.Abs(m.ImportanceScore - 0.7) < 0.001),
                "model-supplied category and importance should be used directly, not re-derived from keywords");
            True(memories.Single(m => m.Category == "preferences").Tags.Contains("ui"),
                "model-supplied tags should be used directly");
            True(memories.All(m => m.SourceConversationId == "conv-structured"), "structured memories should carry the source conversation id");
        }

        public static async Task MemoryExtractionStructuredParsingFallsBackGracefullyOnGarbage()
        {
            var service = new MemoryExtractionService();
            var memories = await service.ExtractStructuredMemoriesAsync("not json at all, just prose.", "conv-x");
            Equal(0, memories.Count, "unparsable output should return an empty list rather than throwing");

            var emptyShape = await service.ExtractStructuredMemoriesAsync("""{"memories": []}""", "conv-x");
            Equal(0, emptyShape.Count, "an explicit empty memories array should also return an empty list");

            var unknownCategory = await service.ExtractStructuredMemoriesAsync(
                """{"memories": [{"content": "Something notable.", "category": "nonsense", "importance": 5}]}""", "conv-x");
            Equal(1, unknownCategory.Count, "one memory object should parse despite the invalid category");
            Equal("facts", unknownCategory[0].Category, "an unrecognized category should default to facts");
            Equal(0.5, unknownCategory[0].ImportanceScore, "an out-of-range importance should fall back to the neutral default");
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

        public static async Task MemoryInjectionPrefersSearchRelevanceOverRawImportance()
        {
            var service = new MemoryInjectionService();
            var memories = new List<Memory>
            {
                // Higher importance but the search barely thought it relevant to this query.
                new() { Id = "stale-important", Category = "facts", Content = "General note.", ImportanceScore = 0.9, RelevanceScore = 0.05, UpdatedAt = DateTime.UtcNow.AddDays(-10) },
                // Lower importance but the search found it highly relevant.
                new() { Id = "relevant-match", Category = "facts", Content = "Directly answers the question.", ImportanceScore = 0.4, RelevanceScore = 0.95, UpdatedAt = DateTime.UtcNow.AddDays(-30) }
            };

            var selected = await service.SelectMemoriesForInjectionAsync(memories, tokenBudget: 500);
            Equal("relevant-match", selected[0].Id, "a memory the search found highly relevant should outrank one that is merely old and generically important");

            var noScore = new List<Memory>
            {
                new() { Id = "a", Category = "facts", Content = "Low importance note.", ImportanceScore = 0.2, UpdatedAt = DateTime.UtcNow },
                new() { Id = "b", Category = "facts", Content = "High importance note.", ImportanceScore = 0.8, UpdatedAt = DateTime.UtcNow.AddDays(-5) }
            };
            var selectedNoScore = await service.SelectMemoriesForInjectionAsync(noScore, tokenBudget: 500);
            Equal("b", selectedNoScore[0].Id, "without a relevance score, selection should still fall back to importance exactly as before");
        }

        public static Task XttsApiTemplateDelegatesToGenerator()
        {
            var serviceTemplate = new LocalAiSetupService(new PythonHealthValidator()).BuildXttsApiScript("/models", "/output");
            var generatorType = typeof(LocalAiSetupService).Assembly.GetType("Hermaeus.Services.LocalAiSetupScriptGenerator")
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

        /// <summary>r11 1.4: every backslash consumed the next character as an escape, so a bare Windows path like C:\models\proj.gguf became C:modelsproj.gguf before reaching llama-server.</summary>
        public static Task ExtraArgsParserPreservesBareWindowsPaths()
        {
            var args = ExtraArgsParser.Split(@"--mmproj C:\models\proj.gguf").ToList();
            Equal(2, args.Count, "parser should return two args");
            Equal("--mmproj", args[0], "first arg should be flag");
            Equal(@"C:\models\proj.gguf", args[1], "bare Windows path should round trip intact");
            return Task.CompletedTask;
        }

        /// <summary>A quoted Windows path (needed when it contains a space) must also keep its backslashes intact.</summary>
        public static Task ExtraArgsParserPreservesQuotedWindowsPathsWithSpaces()
        {
            var args = ExtraArgsParser.Split("--mmproj \"C:\\models\\a b\\x.gguf\"").ToList();
            Equal(2, args.Count, "parser should return two args");
            Equal("--mmproj", args[0], "first arg should be flag");
            Equal(@"C:\models\a b\x.gguf", args[1], "quoted Windows path with an embedded space should round trip intact");
            return Task.CompletedTask;
        }

        public static Task LocalApiProcessManagerResolvesPackagedExecutableFirst()
        {
            using var temp = new TempDir();
            var baseDir = temp.PathFor("desktop-out") + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(baseDir);
            var localApiDir = Path.Combine(baseDir, "LocalApi");
            Directory.CreateDirectory(localApiDir);
            var exeName = OperatingSystem.IsWindows() ? "Hermaeus.LocalApi.exe" : "Hermaeus.LocalApi";
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
            File.WriteAllText(Path.Combine(Directory.CreateDirectory(repoRoot).FullName, "Hermaeus.sln"), "");
            var desktopBin = Path.Combine(repoRoot, "src", "Hermaeus.Desktop", "bin", "Debug", "net10.0");
            Directory.CreateDirectory(desktopBin);
            var localApiBin = Path.Combine(repoRoot, "src", "Hermaeus.LocalApi", "bin", "Debug", "net10.0");
            Directory.CreateDirectory(localApiBin);
            File.WriteAllText(Path.Combine(localApiBin, "Hermaeus.LocalApi.dll"), "stub");

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
            public Task<bool> InstallSpeechRecognitionAssetsAsync(IProgress<string> progress, System.Threading.CancellationToken ct = default) => Task.FromResult(true);
        }

        private sealed class FakeBenchmarkInsightsService : IBenchmarkInsightsService
        {
            private readonly BenchmarkInsightsReport _report;
            public FakeBenchmarkInsightsService(BenchmarkInsightsReport report) => _report = report;
            public Task<BenchmarkInsightsReport> LoadReportAsync(System.Threading.CancellationToken ct = default) => Task.FromResult(_report);
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
