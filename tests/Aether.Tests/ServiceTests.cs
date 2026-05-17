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
            var value = $"{home}/project api_key=abcdefghi123456789 bearer token_123456789012345 sk-abc123456789abcdef ghp_abcdefghijklmnopqrstuvwxyz1234567890?token=querysecret123456&key=anothersecret123";
            var redacted = redactor.Redact(value);

            False(redacted.Contains(home, StringComparison.Ordinal), "home path should be redacted");
            False(redacted.Contains("abcdefghi123456789", StringComparison.Ordinal), "api key should be redacted");
            False(redacted.Contains("token_123456789012345", StringComparison.Ordinal), "bearer token should be redacted");
            False(redacted.Contains("sk-abc123456789abcdef", StringComparison.Ordinal), "openai key should be redacted");
            False(redacted.Contains("ghp_abcdefghijklmnopqrstuvwxyz1234567890", StringComparison.Ordinal), "GitHub token should be redacted");
            False(redacted.Contains("querysecret123456", StringComparison.Ordinal), "query token should be redacted");
            False(redacted.Contains("anothersecret123", StringComparison.Ordinal), "query key should be redacted");
            return Task.CompletedTask;
        }

        public static async Task BenchmarkDbCreatesAndRecordsRuns()
        {
            using var temp = new TempDir();
            var settings = NewSettings(temp);
            settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
            var service = new BenchmarkService(settings, new FakeLlm(), new FakeSystemInfo());

            var suite = BenchmarkService.StarterSuites().First();
            suite.MaxCases = 1;
            await service.SaveSuiteAsync(suite);

            var suites = await service.GetSuitesAsync();
            True(suites.Any(s => s.Id == suite.Id), "saved suite should be listed");

            var run = await service.RunAsync(suite, new LlmModel { Id = "fake-agent", Name = "Fake Agent", Provider = "Test" });
            True(run.Results.Count > 0, "benchmark run should record results");
            var runs = await service.GetRunsAsync();
            Equal(1, runs.Count, "recorded run should be listed");
            True(File.Exists(Path.Combine(settings.Settings.DataManagement.DataRootDirectory, "benchmarks.db")), "benchmark db should be created");
        }

        public static Task BenchmarkScoringAndRanking()
        {
            var service = new BenchmarkService(NewSettings(new TempDir()), new FakeLlm(), new FakeSystemInfo());
            var slow = new BenchmarkRun
            {
                Results = [new BenchmarkResult { QualityScore = 0.2, ApproxTokensPerSecond = 2, ResourceScore = 0.2 }]
            };
            var fast = new BenchmarkRun
            {
                Results = [new BenchmarkResult { QualityScore = 1, ApproxTokensPerSecond = 40, ResourceScore = 1 }]
            };

            var scores = service.Rank(new[] { slow, fast });

            Equal(fast.Id, scores[0].Id, "highest scoring run should rank first");
            True(scores[0].RankingScore >= scores[^1].RankingScore, "scores should be sorted descending");
            return Task.CompletedTask;
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

            var vm = new SystemOverviewViewModel(
                new FakeSystemInfo(),
                new FakeToasts(),
                settings,
                new FakeSecretStore(),
                new RuntimeLogService(settings));

            await vm.RefreshPrivacyAuditCommand.ExecuteAsync(null);

            True(vm.PrivacyAuditItems.Any(i => i.Name == "Remote providers" && i.Status == "Review"), "remote providers should require review");
            True(vm.PrivacyAuditItems.Any(i => i.Name == "Exposed local servers" && i.Status == "Warning"), "network-facing server args should warn");
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

            Equal(Path.Combine(root, "Models"), layout.ModelsDirectory, "asset detection should prefer the existing Models folder with GGUF files");
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
            return Task.CompletedTask;
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

        public static Task LlamaServerReleaseDataCoversSupportedPlatforms()
        {
            var service = new LlamaServerSetupService();
            var releases = service.GetSupportedReleaseInfo();

            Equal(6, releases.Count, "release data should cover all supported platforms");
            True(releases.All(entry => entry.Url.Contains("b4341", StringComparison.Ordinal)), "release urls should use the expected tag");
            True(releases.Select(entry => entry.DisplayName).Distinct(StringComparer.Ordinal).Count() == releases.Count, "release labels should be unique");
            return Task.CompletedTask;
        }

        public static Task XttsApiTemplateHasRequiredEndpoints()
        {
            var template = new LocalAiSetupService(new PythonHealthValidator()).BuildXttsApiScript();
            True(template.Contains("/v1/audio/speech", StringComparison.Ordinal), "template should expose speech endpoint");
            True(template.Contains("/health", StringComparison.Ordinal), "template should expose health endpoint");
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
            var result = tools.ApplyDraftPatch(options, "Program.cs", "new content\nsecond line\n");

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
            }
            finally
            {
                Environment.SetEnvironmentVariable("AETHER_DISABLE_OS_KEYCHAIN", previous);
            }
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
