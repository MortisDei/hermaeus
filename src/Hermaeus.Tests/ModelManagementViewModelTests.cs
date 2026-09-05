using System.Net;
using System.Text;
using Hermaeus.Core.Models;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

// r13 02-model-library.md 2.1: filter box narrows the already-loaded list without a refetch.
public sealed class ModelManagementViewModelTests
{
    [Fact]
    public async Task Refresh_groups_generation_embedding_and_reranker_roles_without_mixing_cards()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var assets = temp.PathFor("assets");
        var models = Path.Combine(assets, "Models");
        Directory.CreateDirectory(models);
        var embeddingPath = Path.Combine(models, "embedding.gguf");
        File.WriteAllText(embeddingPath, "embedding");
        var reranker = Path.Combine(models, "reranker");
        Directory.CreateDirectory(reranker);
        File.WriteAllText(Path.Combine(reranker, "model_O4.onnx"), "onnx");
        File.WriteAllText(Path.Combine(reranker, "vocab.txt"), "vocab");
        settings.Settings.DataManagement.LocalAiAssetsRoot = assets;
        settings.Settings.ManagedServers[1].ModelPath = embeddingPath;
        settings.Settings.ManagedServers[1].EmbeddingsMode = true;
        settings.Settings.Rag.RerankerModelPath = reranker;

        var llm = new ScriptedModelsLlm(() =>
        [
            new LlmModel { Id = "chat", Name = "Chat model", Provider = "llama.cpp", ProviderTag = "llama.cpp" }
        ]);
        var vm = new ModelManagementViewModel(llm, new ModelProfileService(settings), new FakeToasts(), settings,
            new FakeSystemInfo(), NewServicesViewModel(settings), new ModelManifestStore(settings), new HuggingFaceClient(), new ModelDownloadService());

        await vm.RefreshAsync();

        Assert.Equal(3, vm.Models.Count);
        Assert.Equal("Chat & Generation", Assert.Single(vm.ModelSections, section => section.Title == "Chat & Generation").Title);
        Assert.Single(vm.ModelSections.Single(section => section.Title == "Chat & Generation").Models);
        Assert.Single(vm.ModelSections.Single(section => section.Title == "Embeddings").Models);
        Assert.Single(vm.ModelSections.Single(section => section.Title == "Rerankers").Models);
        Assert.Equal(ModelCatalogRole.Embedding, vm.Models.Single(model => model.ModelId == embeddingPath).CatalogRole);
        Assert.Equal(ModelCatalogRole.Reranker, vm.Models.Single(model => model.ModelId == reranker).CatalogRole);
    }

    [Fact]
    public async Task Refresh_hides_manifest_mapped_companions_without_filename_guessing()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var assets = temp.PathFor("assets");
        var models = Path.Combine(assets, "Models");
        Directory.CreateDirectory(models);
        var primaryPath = Path.Combine(models, "primary.gguf");
        var companionPath = Path.Combine(models, "supporting.gguf");
        File.WriteAllText(primaryPath, "primary");
        File.WriteAllText(companionPath, "companion");
        settings.Settings.DataManagement.LocalAiAssetsRoot = assets;

        var manifest = new ModelManifestStore(settings);
        await manifest.UpsertAsync(new ModelManifestEntry
        {
            FilePath = primaryPath,
            Companions = [new ModelCompanionManifestEntry
            {
                LocalFilePath = companionPath,
                Role = "projector",
                SizeBytes = new FileInfo(companionPath).Length
            }]
        });

        var vm = new ModelManagementViewModel(
            new ScriptedModelsLlm(() => []),
            new ModelProfileService(settings),
            new FakeToasts(),
            settings,
            new FakeSystemInfo(),
            NewServicesViewModel(settings),
            manifest,
            new HuggingFaceClient(),
            new ModelDownloadService());

        await vm.RefreshAsync();

        Assert.Contains(vm.Models, item => item.ModelId == primaryPath);
        Assert.DoesNotContain(vm.Models, item => item.ModelId == companionPath);
    }

    [Fact]
    public void Catalog_classification_keeps_capability_facts_separate_from_readiness()
    {
        using var temp = new TempDir();
        var projectorPath = temp.PathFor("projector.gguf");
        var draftPath = temp.PathFor("draft.gguf");
        File.WriteAllBytes(projectorPath, [1, 2]);
        var primary = new ModelProfileItemViewModel(
            new LlmModel { Id = temp.PathFor("primary.gguf"), Name = "MoE model", Provider = "local GGUF" },
            new ModelProfile { ModelId = temp.PathFor("primary.gguf") });
        var manifest = new ModelManifestEntry
        {
            FilePath = primary.ModelId,
            Companions =
            [
                new ModelCompanionManifestEntry { LocalFilePath = projectorPath, Role = "projector", SizeBytes = 2 },
                new ModelCompanionManifestEntry { LocalFilePath = draftPath, Role = "draft_head", SizeBytes = 2 }
            ]
        };

        primary.ApplyCatalogClassification(
            ModelCatalogRole.ChatGeneration,
            new GgufModelInfo("mixtral", "Q4_K_M", 32, 8192, 4096, 32, 8, 128, 128,
                NextnPredictLayers: 4, ExpertCount: 8, ExpertUsedCount: 2),
            manifest);

        Assert.Equal("Chat & Generation", primary.RoleLabel);
        Assert.Contains("MoE", primary.CapabilityBadges);
        Assert.Contains("MTP", primary.CapabilityBadges);
        Assert.Contains("Draft", primary.CapabilityBadges);
        Assert.Contains("Vision / Projector", primary.CapabilityBadges);
        Assert.Equal(2, primary.Companions.Count);
        Assert.Equal("Present", primary.Companions[0].StateLabel);
        Assert.Equal("Missing", primary.Companions[1].StateLabel);
        Assert.Contains("missing", primary.CompanionSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refresh_does_not_duplicate_a_local_model_reported_with_a_normalized_path()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var assets = temp.PathFor("assets");
        var models = Path.Combine(assets, "Models");
        Directory.CreateDirectory(models);
        var localPath = Path.Combine(models, "model.gguf");
        File.WriteAllText(localPath, "fake model");
        settings.Settings.DataManagement.LocalAiAssetsRoot = assets;
        var reportedPath = Path.Combine(models, "nested", "..", "model.gguf");
        var llm = new ScriptedModelsLlm(() =>
        [new LlmModel { Id = reportedPath, Name = "model", Provider = "llama.cpp" }]);
        var vm = new ModelManagementViewModel(llm, new ModelProfileService(settings), new FakeToasts(), settings,
            new FakeSystemInfo(), NewServicesViewModel(settings), new ModelManifestStore(settings), new HuggingFaceClient(), new ModelDownloadService());

        await vm.RefreshAsync();

        Assert.Single(vm.Models);
        Assert.Equal(reportedPath, vm.Models[0].ModelId);
    }

    [Fact]
    public async Task Refresh_hydrates_the_model_editor_from_the_shared_tune_profile_and_save_persists_edits()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var modelPath = temp.PathFor("model.gguf");
        File.WriteAllText(modelPath, "fake model");
        var file = new FileInfo(modelPath);
        settings.Settings.LlamaTuneProfiles.Add(new LlamaTuneProfile
        {
            ModelPath = modelPath,
            ModelSizeBytes = file.Length,
            ModelModifiedAtUtc = file.LastWriteTimeUtc,
            GpuLayers = 24,
            TotalLayers = 32,
            Threads = 8,
            ContextSize = 8192,
            TunedAtUtc = DateTime.UtcNow
        });
        var llm = new ScriptedModelsLlm(() =>
        [new LlmModel { Id = modelPath, Name = "model", Provider = "local GGUF" }]);
        var vm = new ModelManagementViewModel(llm, new ModelProfileService(settings), new FakeToasts(), settings,
            new FakeSystemInfo(), NewServicesViewModel(settings), new ModelManifestStore(settings), new HuggingFaceClient(), new ModelDownloadService());

        await vm.RefreshAsync();

        var item = Assert.Single(vm.Models);
        Assert.True(item.HasTuneProfile);
        Assert.Equal(24, item.TunedGpuLayers);
        Assert.Equal(8, item.TunedThreads);
        Assert.Equal(8192, item.TunedContextSize);

        item.TunedGpuLayers = 20;
        item.TunedThreads = 6;
        item.TunedContextSize = 4096;
        await vm.SaveProfileCommand.ExecuteAsync(item);

        var saved = Assert.Single(settings.Settings.LlamaTuneProfiles);
        Assert.Equal(20, saved.GpuLayers);
        Assert.Equal(6, saved.Threads);
        Assert.Equal(4096, saved.ContextSize);
    }

    [Fact]
    public void Local_model_path_comparison_policy_matches_the_current_platform()
    {
        // Pure policy coverage: this does not claim that the test filesystem
        // supports both case-sensitive and case-insensitive files. The Linux
        // leg must keep these identities distinct; Windows must treat them as
        // aliases under its normal filesystem semantics.
        var lower = Path.Combine("Models", "gemma.gguf");
        var upper = Path.Combine("Models", "GEMMA.gguf");
        var expected = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        Assert.Equal(expected, ModelPathSafety.LocalPathComparison);
        Assert.Equal(OperatingSystem.IsWindows(), ModelPathSafety.AreSameLocalPath(lower, upper));
    }

    [Fact]
    public async Task FilterText_narrows_list_by_name_case_insensitively()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var llm = new ScriptedModelsLlm(() =>
        [
            new LlmModel { Id = "a", Name = "Llama 3 8B Instruct", Provider = "llama.cpp" },
            new LlmModel { Id = "b", Name = "Mistral 7B Instruct", Provider = "llama.cpp" },
        ]);
        var vm = new ModelManagementViewModel(llm, new ModelProfileService(settings), new FakeToasts(), settings, new FakeSystemInfo(), NewServicesViewModel(settings), new ModelManifestStore(settings), new HuggingFaceClient(), new ModelDownloadService());

        await vm.RefreshAsync();
        Assert.Equal(2, vm.Models.Count);

        vm.FilterText = "mistral";
        Assert.Single(vm.Models);
        Assert.Equal("Mistral 7B Instruct", vm.Models[0].RawName);

        vm.FilterText = "";
        Assert.Equal(2, vm.Models.Count);
    }

    [Fact]
    public async Task FilterText_matches_tags()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var profiles = new ModelProfileService(settings);
        await profiles.SaveAsync(new ModelProfile { ModelId = "b", Tags = ["coding"] });

        var llm = new ScriptedModelsLlm(() =>
        [
            new LlmModel { Id = "a", Name = "Llama 3 8B Instruct", Provider = "llama.cpp" },
            new LlmModel { Id = "b", Name = "Mistral 7B Instruct", Provider = "llama.cpp" },
        ]);
        var vm = new ModelManagementViewModel(llm, profiles, new FakeToasts(), settings, new FakeSystemInfo(), NewServicesViewModel(settings), new ModelManifestStore(settings), new HuggingFaceClient(), new ModelDownloadService());

        await vm.RefreshAsync();
        vm.FilterText = "coding";

        var match = Assert.Single(vm.Models);
        Assert.Equal("b", match.ModelId);
    }

    [Fact]
    public async Task Refresh_leaves_new_rows_collapsed_by_default()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var llm = new ScriptedModelsLlm(() => [new LlmModel { Id = "a", Name = "Llama 3", Provider = "llama.cpp" }]);
        var vm = new ModelManagementViewModel(llm, new ModelProfileService(settings), new FakeToasts(), settings, new FakeSystemInfo(), NewServicesViewModel(settings), new ModelManifestStore(settings), new HuggingFaceClient(), new ModelDownloadService());

        await vm.RefreshAsync();

        Assert.False(Assert.Single(vm.Models).IsExpanded);
    }

    // ── 2.3 per-model Auto tune refusal paths (no live llama-server in tests) ────────────
    private static void MakeExecutableUnresolvable(SettingsService settings) =>
        settings.Settings.ManagedServers.ForEach(s => s.ExecutablePath = Path.Combine(
            Path.GetTempPath(), "hermaeus-tests-no-such-dir", "llama-server-does-not-exist.exe"));

    [Fact]
    public async Task AutoTuneModel_refuses_a_running_model_without_touching_the_tune_profile_store()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var modelPath = temp.PathFor("model.gguf");
        File.WriteAllText(modelPath, "fake");
        var vm = new ModelManagementViewModel(new ScriptedModelsLlm(() => []), new ModelProfileService(settings), new FakeToasts(), settings, new FakeSystemInfo(), NewServicesViewModel(settings), new ModelManifestStore(settings), new HuggingFaceClient(), new ModelDownloadService());
        var runningItem = new ModelProfileItemViewModel(
            new LlmModel { Id = modelPath, Name = "model", Provider = "local GGUF" },
            new ModelProfile { ModelId = modelPath },
            isRunning: true);

        await vm.AutoTuneModelCommand.ExecuteAsync(runningItem);

        Assert.Empty(settings.Settings.LlamaTuneProfiles);
    }

    [Fact]
    public async Task AutoTuneModel_refuses_when_no_managed_executable_resolves()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        MakeExecutableUnresolvable(settings);
        var modelPath = temp.PathFor("model.gguf");
        File.WriteAllText(modelPath, "fake");
        var toasts = new FakeToasts();
        var vm = new ModelManagementViewModel(new ScriptedModelsLlm(() => []), new ModelProfileService(settings), toasts, settings, new FakeSystemInfo(), NewServicesViewModel(settings), new ModelManifestStore(settings), new HuggingFaceClient(), new ModelDownloadService());
        var item = new ModelProfileItemViewModel(
            new LlmModel { Id = modelPath, Name = "model", Provider = "local GGUF" },
            new ModelProfile { ModelId = modelPath });

        await vm.AutoTuneModelCommand.ExecuteAsync(item);

        Assert.Empty(settings.Settings.LlamaTuneProfiles);
        Assert.Contains("managed llama-server executable", toasts.LastShown?.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AutoTuneModel_ignores_remote_provider_rows()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var vm = new ModelManagementViewModel(new ScriptedModelsLlm(() => []), new ModelProfileService(settings), new FakeToasts(), settings, new FakeSystemInfo(), NewServicesViewModel(settings), new ModelManifestStore(settings), new HuggingFaceClient(), new ModelDownloadService());
        var remoteItem = new ModelProfileItemViewModel(
            new LlmModel { Id = "gpt-4o", Name = "gpt-4o", Provider = "OpenAI" },
            new ModelProfile { ModelId = "gpt-4o" });

        Assert.False(remoteItem.IsLocalGguf);
        await vm.AutoTuneModelCommand.ExecuteAsync(remoteItem);

        Assert.Empty(settings.Settings.LlamaTuneProfiles);
    }

    // ── 2.4 Auto-tune all: staleness-based selection ──────────────────────────────────────
    [Fact]
    public async Task AutoTuneAll_reports_nothing_to_do_when_every_local_model_is_already_fresh()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var modelsDir = temp.PathFor(Path.Combine("assets", "Models"));
        Directory.CreateDirectory(modelsDir);
        var modelPath = Path.Combine(modelsDir, "model.gguf");
        File.WriteAllText(modelPath, "fake");
        var file = new FileInfo(modelPath);
        settings.Settings.LlamaTuneProfiles.Add(new LlamaTuneProfile
        {
            ModelPath = modelPath,
            ModelSizeBytes = file.Length,
            ModelModifiedAtUtc = file.LastWriteTimeUtc,
            GpuLayers = 20,
            Threads = 8,
            TunedAtUtc = DateTime.UtcNow
        });
        settings.Settings.DataManagement.LocalAiAssetsRoot = temp.PathFor("assets");
        var toasts = new FakeToasts();
        var vm = new ModelManagementViewModel(new ScriptedModelsLlm(() => []), new ModelProfileService(settings), toasts, settings, new FakeSystemInfo(), NewServicesViewModel(settings), new ModelManifestStore(settings), new HuggingFaceClient(), new ModelDownloadService());
        await vm.RefreshAsync();

        await vm.AutoTuneAllCommand.ExecuteAsync(null);

        Assert.Contains("already", toasts.LastShown?.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(settings.Settings.LlamaTuneProfiles);
    }

    // ── 3.2 Check for updates ──────────────────────────────────────────────────────────────
    private sealed class RoutedFakeHandler(Func<string, (HttpStatusCode Status, string Body)> route) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var (status, body) = route(request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private sealed class BytesHandler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            response.Content.Headers.ContentLength = bytes.Length;
            return Task.FromResult(response);
        }
    }

    private sealed class RoutedBytesHandler(Func<string, byte[]> route) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var bytes = route(request.RequestUri!.ToString());
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            response.Content.Headers.ContentLength = bytes.Length;
            return Task.FromResult(response);
        }
    }

    private sealed class UpdateArtworkHandler(string revision, string modelHash, int modelSize, byte[] image) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains("/tree/", StringComparison.Ordinal))
            {
                var tree = "[{\"path\":\"model.gguf\",\"size\":" + modelSize + ",\"lfs\":{\"oid\":\"" + modelHash + "\"}},{\"path\":\"art.png\",\"size\":" + image.Length + "}]";
                return Task.FromResult(Response(tree));
            }
            if (url.Contains("/api/models/", StringComparison.Ordinal))
                return Task.FromResult(Response("{\"sha\":\"" + revision + "\",\"cardData\":{\"thumbnail\":\"art.png\"}}"));

            var artwork = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(image)
            };
            artwork.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            artwork.Content.Headers.ContentLength = image.Length;
            return Task.FromResult(artwork);
        }
    }

    private sealed class PublisherAvatarArtworkHandler(string revision, string modelHash, int modelSize, byte[] image) : HttpMessageHandler
    {
        public List<string> RequestedUrls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            RequestedUrls.Add(url);
            if (url.Contains("/tree/", StringComparison.Ordinal))
            {
                var tree = "[{\"path\":\"model.gguf\",\"size\":" + modelSize + ",\"lfs\":{\"oid\":\"" + modelHash + "\"}}]";
                return Task.FromResult(Response(tree));
            }
            if (url.Contains("/api/models/", StringComparison.Ordinal))
                return Task.FromResult(Response("{\"sha\":\"" + revision + "\",\"author\":\"org\",\"cardData\":{}}"));
            if (url.Contains("/api/organizations/org/overview", StringComparison.Ordinal))
                return Task.FromResult(Response("{\"avatarUrl\":\"https://cdn-avatars.huggingface.co/v1/production/uploads/org/avatar.png\"}"));

            var artwork = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(image)
            };
            artwork.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            artwork.Content.Headers.ContentLength = image.Length;
            return Task.FromResult(artwork);
        }
    }

    private static HttpResponseMessage Response(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static (string Metadata, string MetadataHash, string ModelHash, string CompanionHash) CompanionFixture(
        byte[] modelBytes, byte[] companionBytes)
    {
        const string metadata = "{\"models\":[{\"model_path\":\"model.gguf\",\"companions\":[{\"path\":\"mmproj.gguf\",\"role\":\"projector\"}]}]}";
        return (
            metadata,
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(metadata))),
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(modelBytes)),
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(companionBytes)));
    }

    [Fact]
    public async Task CheckForUpdates_reports_nothing_when_no_models_are_linked()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var toasts = new FakeToasts();
        var vm = new ModelManagementViewModel(new ScriptedModelsLlm(() => []), new ModelProfileService(settings), toasts, settings, new FakeSystemInfo(), NewServicesViewModel(settings), new ModelManifestStore(settings), new HuggingFaceClient(), new ModelDownloadService());

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.Contains("no models are linked", toasts.LastShown?.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckForUpdates_marks_matching_oid_as_up_to_date()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var modelsDir = temp.PathFor(Path.Combine("assets", "Models"));
        Directory.CreateDirectory(modelsDir);
        var modelPath = Path.Combine(modelsDir, "model.gguf");
        File.WriteAllText(modelPath, "fake");
        settings.Settings.DataManagement.LocalAiAssetsRoot = temp.PathFor("assets");

        var manifest = new ModelManifestStore(settings);
        await manifest.UpsertAsync(new ModelManifestEntry { FilePath = modelPath, RepoId = "org/repo", RepoFile = "model.gguf", Sha256 = "abc123", Source = "hf-browser" });

        var treeJson = """[{"type":"file","oid":"x","size":4,"lfs":{"oid":"abc123","size":4},"path":"model.gguf"}]""";
        var hf = new HuggingFaceClient(new HttpClient(new RoutedFakeHandler(_ => (HttpStatusCode.OK, treeJson))));

        var vm = new ModelManagementViewModel(new ScriptedModelsLlm(() => []), new ModelProfileService(settings), new FakeToasts(), settings, new FakeSystemInfo(), NewServicesViewModel(settings), manifest, hf, new ModelDownloadService());
        await vm.RefreshAsync();

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        var item = Assert.Single(vm.Models);
        Assert.Equal(ModelUpdateStatus.UpToDate, item.UpdateStatus);
    }

    [Fact]
    public async Task CheckForUpdates_marks_oid_drift_as_update_available()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var modelsDir = temp.PathFor(Path.Combine("assets", "Models"));
        Directory.CreateDirectory(modelsDir);
        var modelPath = Path.Combine(modelsDir, "model.gguf");
        File.WriteAllText(modelPath, "fake");
        settings.Settings.DataManagement.LocalAiAssetsRoot = temp.PathFor("assets");

        var manifest = new ModelManifestStore(settings);
        await manifest.UpsertAsync(new ModelManifestEntry { FilePath = modelPath, RepoId = "org/repo", RepoFile = "model.gguf", Sha256 = "old-oid", Source = "hf-browser" });

        var treeJson = """[{"type":"file","oid":"x","size":9,"lfs":{"oid":"new-oid","size":9},"path":"model.gguf"}]""";
        var hf = new HuggingFaceClient(new HttpClient(new RoutedFakeHandler(_ => (HttpStatusCode.OK, treeJson))));

        var vm = new ModelManagementViewModel(new ScriptedModelsLlm(() => []), new ModelProfileService(settings), new FakeToasts(), settings, new FakeSystemInfo(), NewServicesViewModel(settings), manifest, hf, new ModelDownloadService());
        await vm.RefreshAsync();

        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        var item = Assert.Single(vm.Models);
        Assert.Equal(ModelUpdateStatus.UpdateAvailable, item.UpdateStatus);

        var entry = await manifest.FindAsync(modelPath);
        Assert.Equal("new-oid", entry!.PendingSha256);
    }

    [Fact]
    public async Task CheckForUpdates_backfills_revision_pinned_artwork_without_changing_update_state()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var assets = temp.PathFor(Path.Combine("assets", "Models"));
        Directory.CreateDirectory(assets);
        var modelPath = Path.Combine(assets, "model.gguf");
        var modelBytes = Encoding.UTF8.GetBytes("fake model");
        await File.WriteAllBytesAsync(modelPath, modelBytes);
        settings.Settings.DataManagement.LocalAiAssetsRoot = temp.PathFor("assets");

        const string revision = "0123456789abcdef0123456789abcdef01234567";
        var modelHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(modelBytes));
        var image = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        var manifest = new ModelManifestStore(settings);
        await manifest.UpsertAsync(new ModelManifestEntry
        {
            FilePath = modelPath,
            RepoId = "org/repo",
            RepoFile = "model.gguf",
            Sha256 = modelHash,
            SizeBytes = modelBytes.Length,
            Source = "hf-browser"
        });

        using var http = new HttpClient(new UpdateArtworkHandler(revision, modelHash, modelBytes.Length, image));
        var vm = new ModelManagementViewModel(
            new ScriptedModelsLlm(() => []),
            new ModelProfileService(settings),
            new FakeToasts(),
            settings,
            new FakeSystemInfo(),
            NewServicesViewModel(settings),
            manifest,
            new HuggingFaceClient(http),
            new ModelDownloadService(),
            artwork: new HuggingFaceArtworkService(http));

        await vm.RefreshAsync();
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        var item = Assert.Single(vm.Models);
        Assert.Equal(ModelUpdateStatus.UpToDate, item.UpdateStatus);
        await WaitForAsync(() => item.ArtworkState == HfArtworkState.Available, "revision-pinned artwork backfill");
        Assert.NotNull(item.ArtworkPath);
        Assert.Contains(revision, item.ArtworkTooltip, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckForUpdates_backfills_the_publisher_avatar_when_the_repo_has_no_declared_artwork()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var assets = temp.PathFor(Path.Combine("assets", "Models"));
        Directory.CreateDirectory(assets);
        var modelPath = Path.Combine(assets, "model.gguf");
        var modelBytes = Encoding.UTF8.GetBytes("fake model");
        await File.WriteAllBytesAsync(modelPath, modelBytes);
        settings.Settings.DataManagement.LocalAiAssetsRoot = temp.PathFor("assets");
        const string revision = "0123456789abcdef0123456789abcdef01234567";
        var modelHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(modelBytes));
        var manifest = new ModelManifestStore(settings);
        await manifest.UpsertAsync(new ModelManifestEntry
        {
            FilePath = modelPath,
            RepoId = "org/repo",
            RepoFile = "model.gguf",
            Sha256 = modelHash,
            SizeBytes = modelBytes.Length,
            Source = "hf-browser"
        });

        var image = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        var handler = new PublisherAvatarArtworkHandler(revision, modelHash, modelBytes.Length, image);
        using var http = new HttpClient(handler);
        var logs = new RuntimeLogService(settings);
        var vm = new ModelManagementViewModel(
            new ScriptedModelsLlm(() => []),
            new ModelProfileService(settings),
            new FakeToasts(),
            settings,
            new FakeSystemInfo(),
            NewServicesViewModel(settings),
            manifest,
            new HuggingFaceClient(http),
            new ModelDownloadService(),
            artwork: new HuggingFaceArtworkService(http),
            runtimeLogs: logs);

        await vm.RefreshAsync();
        await vm.CheckForUpdatesCommand.ExecuteAsync(null);

        var item = Assert.Single(vm.Models);
        await WaitForAsync(() => item.ArtworkState == HfArtworkState.Available, "publisher avatar fallback");
        Assert.Equal(ModelUpdateStatus.UpToDate, item.UpdateStatus);
        Assert.Equal(HfArtworkSourceKind.HuggingFaceAuthorAvatar, item.ArtworkSource);
        Assert.Contains("Publisher avatar fallback", item.ArtworkTooltip, StringComparison.Ordinal);
        Assert.NotNull(item.ArtworkPath);
        Assert.Contains(logs.GetEntries(), entry => entry.Message.Contains("update check started", StringComparison.Ordinal));
        Assert.Contains(logs.GetEntries(), entry => entry.Message.Contains("repository and revision resolution completed", StringComparison.Ordinal));
        Assert.Contains(logs.GetEntries(), entry => entry.Message.Contains("artwork declaration gate evaluated", StringComparison.Ordinal));
        Assert.Contains(logs.GetEntries(), entry => entry.Message.Contains("model-card binding completed", StringComparison.Ordinal));
        Assert.Collection(handler.RequestedUrls,
            cardUrl => Assert.Contains("/api/models/org/repo", cardUrl, StringComparison.Ordinal),
            treeUrl =>
            {
                Assert.Contains($"/tree/{revision}", treeUrl, StringComparison.Ordinal);
                Assert.DoesNotContain("/resolve/", treeUrl, StringComparison.Ordinal);
            },
            avatarMetadataUrl => Assert.Contains("/api/organizations/org/overview", avatarMetadataUrl, StringComparison.Ordinal),
            avatarImageUrl =>
            {
                Assert.Equal("cdn-avatars.huggingface.co", new Uri(avatarImageUrl).Host);
                Assert.DoesNotContain("/resolve/", avatarImageUrl, StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task Refresh_exposes_actionable_repair_state_for_a_missing_verified_companion()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var assets = temp.PathFor("assets");
        var modelDir = Path.Combine(assets, "Models", "llm", "org__repo");
        Directory.CreateDirectory(modelDir);
        settings.Settings.DataManagement.LocalAiAssetsRoot = assets;
        var modelBytes = System.Text.Encoding.UTF8.GetBytes("model");
        var companionBytes = System.Text.Encoding.UTF8.GetBytes("projector");
        var fixture = CompanionFixture(modelBytes, companionBytes);
        var modelPath = Path.Combine(modelDir, "model.gguf");
        var companionPath = Path.Combine(modelDir, "mmproj.gguf");
        File.WriteAllBytes(modelPath, modelBytes);
        const string revision = "0123456789abcdef0123456789abcdef01234567";
        var manifest = new ModelManifestStore(settings);
        await manifest.UpsertAsync(new ModelManifestEntry
        {
            FilePath = modelPath,
            RepoId = "org/repo",
            RepoFile = "model.gguf",
            RevisionSha = revision,
            Sha256 = fixture.ModelHash,
            Source = "hf-browser",
            Companions = [new ModelCompanionManifestEntry
            {
                LocalFilePath = companionPath,
                RepoFile = "mmproj.gguf",
                RevisionSha = revision,
                Role = "projector",
                Sha256 = fixture.CompanionHash,
                SizeBytes = companionBytes.Length
            }]
        });
        var vm = new ModelManagementViewModel(new ScriptedModelsLlm(() => []), new ModelProfileService(settings), new FakeToasts(), settings, new FakeSystemInfo(), NewServicesViewModel(settings), manifest, new HuggingFaceClient(), new ModelDownloadService());

        await vm.RefreshAsync();

        var item = Assert.Single(vm.Models);
        Assert.True(item.HasMissingCompanions);
        Assert.True(item.HasCompanionRepair);
        Assert.True(item.CanReacquireCompanions);
        Assert.False(item.RequiresManualCompanionRepair);
        Assert.Contains("Verified compatible replacement available", item.CompanionStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refresh_exposes_manual_paths_when_no_verified_companion_replacement_exists()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var assets = temp.PathFor("assets");
        var modelDir = Path.Combine(assets, "Models", "llm", "org__repo");
        Directory.CreateDirectory(modelDir);
        settings.Settings.DataManagement.LocalAiAssetsRoot = assets;
        var modelBytes = System.Text.Encoding.UTF8.GetBytes("model");
        var companionBytes = System.Text.Encoding.UTF8.GetBytes("projector");
        var fixture = CompanionFixture(modelBytes, companionBytes);
        var modelPath = Path.Combine(modelDir, "model.gguf");
        var companionPath = Path.Combine(modelDir, "mmproj-review.gguf");
        File.WriteAllBytes(modelPath, modelBytes);
        const string revision = "0123456789abcdef0123456789abcdef01234567";
        var manifest = new ModelManifestStore(settings);
        await manifest.UpsertAsync(new ModelManifestEntry
        {
            FilePath = modelPath,
            RepoId = "org/repo",
            RepoFile = "model.gguf",
            RevisionSha = revision,
            Sha256 = fixture.ModelHash,
            Source = "hf-browser",
            Companions = [new ModelCompanionManifestEntry
            {
                LocalFilePath = companionPath,
                RepoFile = "mmproj-review.gguf",
                RevisionSha = revision,
                Role = "projector",
                Sha256 = fixture.CompanionHash,
                SizeBytes = companionBytes.Length,
                RequiresUserConfirmation = true
            }]
        });
        var vm = new ModelManagementViewModel(new ScriptedModelsLlm(() => []), new ModelProfileService(settings), new FakeToasts(), settings, new FakeSystemInfo(), NewServicesViewModel(settings), manifest, new HuggingFaceClient(), new ModelDownloadService());

        await vm.RefreshAsync();

        var item = Assert.Single(vm.Models);
        Assert.True(item.HasCompanionRepair);
        Assert.False(item.CanReacquireCompanions);
        Assert.True(item.RequiresManualCompanionRepair);
        Assert.Contains("No verified replacement is available", item.CompanionStatus, StringComparison.Ordinal);
        Assert.Contains("projector", item.ManualCompanionRepairLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Mixed_companion_repair_state_exposes_verified_and_manual_roles()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var assets = temp.PathFor("assets");
        var modelDir = Path.Combine(assets, "Models", "llm", "org__repo");
        Directory.CreateDirectory(modelDir);
        settings.Settings.DataManagement.LocalAiAssetsRoot = assets;
        var modelPath = Path.Combine(modelDir, "model.gguf");
        File.WriteAllText(modelPath, "model");
        var hash = new string('a', 64);
        var manifest = new ModelManifestStore(settings);
        await manifest.UpsertAsync(new ModelManifestEntry
        {
            FilePath = modelPath,
            RepoId = "org/repo",
            RepoFile = "model.gguf",
            RevisionSha = "0123456789abcdef0123456789abcdef01234567",
            Sha256 = hash,
            Companions =
            [
                new ModelCompanionManifestEntry { LocalFilePath = Path.Combine(modelDir, "mmproj.gguf"), RepoFile = "mmproj.gguf", Role = "projector", Sha256 = hash },
                new ModelCompanionManifestEntry { LocalFilePath = Path.Combine(modelDir, "mtp.gguf"), RepoFile = "mtp.gguf", Role = "draft_head", Sha256 = hash, RequiresUserConfirmation = true }
            ]
        });
        var vm = new ModelManagementViewModel(new ScriptedModelsLlm(() => []), new ModelProfileService(settings), new FakeToasts(), settings,
            new FakeSystemInfo(), NewServicesViewModel(settings), manifest, new HuggingFaceClient(), new ModelDownloadService());

        await vm.RefreshAsync();

        var item = Assert.Single(vm.Models);
        Assert.True(item.CanReacquireCompanions);
        Assert.True(item.RequiresManualCompanionRepair);
        Assert.Contains("MTP draft head", item.ManualCompanionRepairLabel, StringComparison.Ordinal);
        Assert.Contains("Remaining manual repair", item.CompanionStatus, StringComparison.Ordinal);
    }

    // ── 3.3 Apply update ───────────────────────────────────────────────────────────────────
    [Fact]
    public async Task UpdateModel_refuses_a_running_model()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var modelPath = temp.PathFor("model.gguf");
        File.WriteAllText(modelPath, "old content");
        var manifest = new ModelManifestStore(settings);
        await manifest.UpsertAsync(new ModelManifestEntry { FilePath = modelPath, RepoId = "org/repo", RepoFile = "model.gguf", Sha256 = "old", PendingSha256 = "new", Source = "hf-browser" });

        var vm = new ModelManagementViewModel(new ScriptedModelsLlm(() => []), new ModelProfileService(settings), new FakeToasts(), settings, new FakeSystemInfo(), NewServicesViewModel(settings), manifest, new HuggingFaceClient(), new ModelDownloadService());
        var item = new ModelProfileItemViewModel(
            new LlmModel { Id = modelPath, Name = "model", Provider = "local GGUF" },
            new ModelProfile { ModelId = modelPath },
            isRunning: true) { UpdateStatus = ModelUpdateStatus.UpdateAvailable };

        await vm.UpdateModelCommand.ExecuteAsync(item);

        Assert.Equal("old content", File.ReadAllText(modelPath));
    }

    [Fact]
    public async Task UpdateModel_happy_path_swaps_the_file_and_clears_the_pending_update()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var modelPath = temp.PathFor("model.gguf");
        File.WriteAllText(modelPath, "old content");
        var manifest = new ModelManifestStore(settings);

        var newBytes = System.Text.Encoding.UTF8.GetBytes("new content");
        var newHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(newBytes));
        await manifest.UpsertAsync(new ModelManifestEntry { FilePath = modelPath, RepoId = "org/repo", RepoFile = "model.gguf", Sha256 = "old-hash", PendingSha256 = newHash, PendingSizeBytes = newBytes.Length, Source = "hf-browser" });

        var downloader = new ModelDownloadService(new HttpClient(new BytesHandler(newBytes)));
        var vm = new ModelManagementViewModel(new ScriptedModelsLlm(() => []), new ModelProfileService(settings), new FakeToasts(), settings, new FakeSystemInfo(), NewServicesViewModel(settings), manifest, new HuggingFaceClient(), downloader);
        var item = new ModelProfileItemViewModel(
            new LlmModel { Id = modelPath, Name = "model", Provider = "local GGUF" },
            new ModelProfile { ModelId = modelPath }) { UpdateStatus = ModelUpdateStatus.UpdateAvailable };

        await vm.UpdateModelCommand.ExecuteAsync(item);

        Assert.Equal("new content", File.ReadAllText(modelPath));
        Assert.Equal(ModelUpdateStatus.UpToDate, item.UpdateStatus);
        Assert.True(item.RetuneRecommended);
        var entry = await manifest.FindAsync(modelPath);
        Assert.Equal(newHash, entry!.Sha256);
        Assert.Null(entry.PendingSha256);
        Assert.False(File.Exists(modelPath + ".previous"));
        Assert.False(File.Exists(modelPath + ".update.tmp"));
    }

    [Fact]
    public async Task UpdateModel_hash_mismatch_leaves_the_original_file_untouched()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var modelPath = temp.PathFor("model.gguf");
        File.WriteAllText(modelPath, "old content");
        var manifest = new ModelManifestStore(settings);
        await manifest.UpsertAsync(new ModelManifestEntry { FilePath = modelPath, RepoId = "org/repo", RepoFile = "model.gguf", Sha256 = "old-hash", PendingSha256 = "expected-hash-that-will-not-match", Source = "hf-browser" });

        var downloader = new ModelDownloadService(new HttpClient(new BytesHandler(System.Text.Encoding.UTF8.GetBytes("wrong bytes"))));
        var toasts = new FakeToasts();
        var vm = new ModelManagementViewModel(new ScriptedModelsLlm(() => []), new ModelProfileService(settings), toasts, settings, new FakeSystemInfo(), NewServicesViewModel(settings), manifest, new HuggingFaceClient(), downloader);
        var item = new ModelProfileItemViewModel(
            new LlmModel { Id = modelPath, Name = "model", Provider = "local GGUF" },
            new ModelProfile { ModelId = modelPath }) { UpdateStatus = ModelUpdateStatus.UpdateAvailable };

        await vm.UpdateModelCommand.ExecuteAsync(item);

        Assert.Equal("old content", File.ReadAllText(modelPath));
        Assert.Equal(ModelUpdateStatus.UpdateAvailable, item.UpdateStatus);
        Assert.Contains("hash", toasts.LastShown?.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── 3.4 "Get models" browser ───────────────────────────────────────────────────────────
    [Fact]
    public async Task SearchHuggingFace_populates_results_from_the_search_fixture()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var searchJson = """[{"id":"org/repo-a","downloads":100},{"id":"org/repo-b","downloads":50}]""";
        var hf = new HuggingFaceClient(new HttpClient(new RoutedFakeHandler(_ => (HttpStatusCode.OK, searchJson))));
        var vm = new ModelManagementViewModel(new ScriptedModelsLlm(() => []), new ModelProfileService(settings), new FakeToasts(), settings, new FakeSystemInfo(), NewServicesViewModel(settings), new ModelManifestStore(settings), hf, new ModelDownloadService());

        vm.HfSearchQuery = "test";
        await vm.SearchHuggingFaceCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.HfSearchResults.Count);
        Assert.Equal("org/repo-a", vm.HfSearchResults[0].RepoId);
    }

    [Fact]
    public async Task SelectHfRepo_lists_a_sharded_model_once_as_a_downloadable_set()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var cardJson = """{"id":"org/repo","sha":"abc","cardData":{"license":"mit"}}""";
        var treeJson = """
            [{"type":"file","oid":"x","size":10,"lfs":{"oid":"single-oid","size":10},"path":"model-Q4.gguf"},
             {"type":"file","oid":"y","size":20,"lfs":{"oid":"part1-oid","size":20},"path":"big-00001-of-00002.gguf"},
             {"type":"file","oid":"z","size":20,"lfs":{"oid":"part2-oid","size":20},"path":"big-00002-of-00002.gguf"}]
            """;
        var hf = new HuggingFaceClient(new HttpClient(new RoutedFakeHandler(url =>
            url.Contains("/tree/", StringComparison.Ordinal) ? (HttpStatusCode.OK, treeJson) : (HttpStatusCode.OK, cardJson))));
        var vm = new ModelManagementViewModel(new ScriptedModelsLlm(() => []), new ModelProfileService(settings), new FakeToasts(), settings, new FakeSystemInfo(), NewServicesViewModel(settings), new ModelManifestStore(settings), hf, new ModelDownloadService());

        await vm.SelectHfRepoCommand.ExecuteAsync(new HfRepoResultViewModel("org/repo", 100));

        // r27 04 4.1: a sharded model used to be hidden outright, because a
        // single shard is a model that will not load. It is now listed once, as
        // its first shard, and downloads as a complete set.
        Assert.Equal(2, vm.HfFiles.Count);
        Assert.Equal("model-Q4.gguf", vm.HfFiles[0].FileName);
        Assert.False(vm.HfFiles[0].IsSharded);

        var sharded = vm.HfFiles[1];
        Assert.Equal("big-00001-of-00002.gguf", sharded.FileName);
        Assert.True(sharded.IsSharded);
        Assert.Equal(2, sharded.SelectedEntries().Count);
        Assert.All(sharded.FileSet.Entries, e => Assert.True(e.Required));
        Assert.Equal("mit", vm.SelectedHfRepo!.License);
    }

    [Fact]
    public async Task DownloadHfFile_verifies_hash_writes_manifest_and_appears_after_refresh()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var modelsRoot = temp.PathFor("assets");
        Directory.CreateDirectory(Path.Combine(modelsRoot, "Models"));
        settings.Settings.DataManagement.LocalAiAssetsRoot = modelsRoot;

        var bytes = System.Text.Encoding.UTF8.GetBytes("gguf bytes");
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
        var downloader = new ModelDownloadService(new HttpClient(new BytesHandler(bytes)));
        var manifest = new ModelManifestStore(settings);
        var vm = new ModelManagementViewModel(new ScriptedModelsLlm(() => []), new ModelProfileService(settings), new FakeToasts(), settings, new FakeSystemInfo(), NewServicesViewModel(settings), manifest, new HuggingFaceClient(), downloader);
        var file = new HfFileResultViewModel("org/repo", "tiny.gguf", bytes.Length, hash, ModelFitTier.FitsGpu, "fits");

        await vm.DownloadHfFileCommand.ExecuteAsync(file);

        // r27 04 4.2: the destination carries the repository, so companion
        // filenames from different repositories can coexist.
        var destination = Path.Combine(modelsRoot, "Models", "llm", "org__repo", "tiny.gguf");
        Assert.True(File.Exists(destination));
        var entry = await manifest.FindAsync(destination);
        Assert.NotNull(entry);
        Assert.Equal("org/repo", entry!.RepoId);
        Assert.Equal("hf-browser", entry.Source);

        await vm.RefreshAsync();
        Assert.Contains(vm.Models, m => m.ModelId == destination);
    }

    [Fact]
    public async Task DownloadHfFile_refuses_a_name_collision_without_overwriting()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var modelsRoot = temp.PathFor("assets");
        var llmDir = Path.Combine(modelsRoot, "Models", "LLM", "org__repo");
        Directory.CreateDirectory(llmDir);
        File.WriteAllText(Path.Combine(llmDir, "tiny.gguf"), "already here");
        settings.Settings.DataManagement.LocalAiAssetsRoot = modelsRoot;

        var toasts = new FakeToasts();
        var vm = new ModelManagementViewModel(new ScriptedModelsLlm(() => []), new ModelProfileService(settings), toasts, settings, new FakeSystemInfo(), NewServicesViewModel(settings), new ModelManifestStore(settings), new HuggingFaceClient(), new ModelDownloadService());
        var file = new HfFileResultViewModel("org/repo", "tiny.gguf", 10, "hash", ModelFitTier.FitsGpu, "fits");

        await vm.DownloadHfFileCommand.ExecuteAsync(file);

        Assert.Equal("already here", File.ReadAllText(Path.Combine(llmDir, "tiny.gguf")));
        Assert.Contains("already exists", toasts.LastShown?.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Initial_download_model_only_records_known_companion_without_downloading_it()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var assets = temp.PathFor("assets");
        Directory.CreateDirectory(Path.Combine(assets, "Models"));
        settings.Settings.DataManagement.LocalAiAssetsRoot = assets;
        var modelBytes = System.Text.Encoding.UTF8.GetBytes("model");
        var companionBytes = System.Text.Encoding.UTF8.GetBytes("projector");
        var fixture = CompanionFixture(modelBytes, companionBytes);
        var manifest = new ModelManifestStore(settings);
        var vm = new ModelManagementViewModel(new ScriptedModelsLlm(() => []), new ModelProfileService(settings), new FakeToasts(), settings, new FakeSystemInfo(), NewServicesViewModel(settings), manifest, new HuggingFaceClient(), new ModelDownloadService(new HttpClient(new RoutedBytesHandler(_ => modelBytes))));
        var file = new HfFileResultViewModel("org/repo", "model.gguf", modelBytes.Length, fixture.ModelHash, ModelFitTier.FitsGpu, "fits",
            new ModelFileSet("org/repo", [
                new ModelFileSetEntry("model.gguf", modelBytes.Length, fixture.ModelHash, ModelFileRole.Model, true, true),
                new ModelFileSetEntry("mmproj.gguf", companionBytes.Length, fixture.CompanionHash, ModelFileRole.Projector, false, true)]));
        file.IncludeProjector = false;

        await vm.DownloadHfFileCommand.ExecuteAsync(file);

        var modelPath = Path.Combine(assets, "Models", "llm", "org__repo", "model.gguf");
        var primary = await manifest.FindAsync(modelPath);
        Assert.True(File.Exists(modelPath));
        Assert.False(File.Exists(Path.Combine(assets, "Models", "llm", "org__repo", "mmproj.gguf")));
        Assert.Single(primary!.Companions);
        Assert.False(settings.Settings.ModelProfiles.Single(p => p.ModelId == modelPath).AutoManageCompanionAssets);
    }

    [Fact]
    public async Task Enabled_companion_policy_updates_an_explicitly_mapped_projector_with_the_model()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var assets = temp.PathFor("assets");
        var modelDir = Path.Combine(assets, "Models", "llm", "org__repo");
        Directory.CreateDirectory(modelDir);
        settings.Settings.DataManagement.LocalAiAssetsRoot = assets;
        var oldModel = System.Text.Encoding.UTF8.GetBytes("old-model");
        var newModel = System.Text.Encoding.UTF8.GetBytes("new-model");
        var oldCompanion = System.Text.Encoding.UTF8.GetBytes("old-projector");
        var newCompanion = System.Text.Encoding.UTF8.GetBytes("new-projector");
        var fixture = CompanionFixture(newModel, newCompanion);
        var modelPath = Path.Combine(modelDir, "model.gguf");
        var companionPath = Path.Combine(modelDir, "mmproj.gguf");
        File.WriteAllBytes(modelPath, oldModel);
        File.WriteAllBytes(companionPath, oldCompanion);
        settings.Settings.ManagedServers[0].ModelPath = modelPath;
        settings.Settings.ManagedServers[0].MmprojPath = companionPath;
        settings.Settings.ManagedServers[0].UseProjector = false;
        var settingsService = new ModelProfileService(settings);
        settingsService.GetOrCreate(modelPath, "local GGUF").AutoManageCompanionAssets = true;
        var manifest = new ModelManifestStore(settings);
        await manifest.UpsertAsync(new ModelManifestEntry
        {
            FilePath = modelPath, RepoId = "org/repo", RepoFile = "model.gguf", Sha256 = "old-model-hash",
            PendingSha256 = fixture.ModelHash, PendingSizeBytes = newModel.Length, Source = "hf-browser",
            Companions = [new ModelCompanionManifestEntry { LocalFilePath = companionPath, RepoFile = "mmproj.gguf", Role = "projector", Sha256 = "old-companion-hash", SizeBytes = oldCompanion.Length }]
        });
        await manifest.UpsertAsync(new ModelManifestEntry
        {
            FilePath = companionPath, RepoId = "org/repo", RepoFile = "mmproj.gguf", Sha256 = "old-companion-hash",
            SizeBytes = oldCompanion.Length, Source = "hf-browser", ParentModelPath = modelPath, CompanionRole = "projector"
        });
        var treeJson = $"[{{\"type\":\"file\",\"size\":{newModel.Length},\"lfs\":{{\"oid\":\"{fixture.ModelHash}\"}},\"path\":\"model.gguf\"}},{{\"type\":\"file\",\"size\":{newCompanion.Length},\"lfs\":{{\"oid\":\"{fixture.CompanionHash}\"}},\"path\":\"mmproj.gguf\"}},{{\"type\":\"file\",\"size\":{fixture.Metadata.Length},\"lfs\":{{\"oid\":\"{fixture.MetadataHash}\"}},\"path\":\".hermaeus/companions.json\"}}]";
        var hf = new HuggingFaceClient(new HttpClient(new RoutedFakeHandler(url => url.Contains("/tree/", StringComparison.Ordinal) ? (HttpStatusCode.OK, treeJson) : (HttpStatusCode.OK, fixture.Metadata))));
        var downloader = new ModelDownloadService(new HttpClient(new RoutedBytesHandler(url => url.Contains("mmproj.gguf", StringComparison.Ordinal) ? newCompanion : newModel)));
        var vm = new ModelManagementViewModel(new ScriptedModelsLlm(() => []), settingsService, new FakeToasts(), settings, new FakeSystemInfo(), NewServicesViewModel(settings), manifest, hf, downloader);
        var item = new ModelProfileItemViewModel(new LlmModel { Id = modelPath, Name = "model", Provider = "local GGUF" }, new ModelProfile { ModelId = modelPath, AutoManageCompanionAssets = true }) { UpdateStatus = ModelUpdateStatus.UpdateAvailable };

        await vm.UpdateModelCommand.ExecuteAsync(item);

        Assert.Equal("new-model", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(modelPath)));
        Assert.Equal("new-projector", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(companionPath)));
        Assert.Equal(fixture.CompanionHash, (await manifest.FindAsync(companionPath))!.Sha256);
        Assert.False(settings.Settings.ManagedServers[0].UseProjector);
        Assert.Equal(companionPath, settings.Settings.ManagedServers[0].MmprojPath);
    }

    [Fact]
    public async Task Disabled_companion_policy_does_not_update_existing_companion()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var assets = temp.PathFor("assets");
        var modelDir = Path.Combine(assets, "Models", "llm", "org__repo");
        Directory.CreateDirectory(modelDir);
        settings.Settings.DataManagement.LocalAiAssetsRoot = assets;
        var modelPath = Path.Combine(modelDir, "model.gguf");
        var companionPath = Path.Combine(modelDir, "mmproj.gguf");
        File.WriteAllText(modelPath, "old");
        File.WriteAllText(companionPath, "keep");
        var newModel = System.Text.Encoding.UTF8.GetBytes("new");
        var newModelHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(newModel));
        var profileService = new ModelProfileService(settings);
        profileService.GetOrCreate(modelPath, "local GGUF").AutoManageCompanionAssets = false;
        var manifest = new ModelManifestStore(settings);
        await manifest.UpsertAsync(new ModelManifestEntry { FilePath = modelPath, RepoId = "org/repo", RepoFile = "model.gguf", Sha256 = "old", PendingSha256 = newModelHash, Source = "hf-browser", Companions = [new ModelCompanionManifestEntry { LocalFilePath = companionPath, RepoFile = "mmproj.gguf", Role = "projector", Sha256 = "old" }] });
        await manifest.UpsertAsync(new ModelManifestEntry { FilePath = companionPath, RepoId = "org/repo", RepoFile = "mmproj.gguf", Sha256 = "old", Source = "hf-browser", ParentModelPath = modelPath, CompanionRole = "projector" });
        var vm = new ModelManagementViewModel(new ScriptedModelsLlm(() => []), profileService, new FakeToasts(), settings, new FakeSystemInfo(), NewServicesViewModel(settings), manifest, new HuggingFaceClient(), new ModelDownloadService(new HttpClient(new RoutedBytesHandler(_ => newModel))));
        var item = new ModelProfileItemViewModel(new LlmModel { Id = modelPath, Name = "model", Provider = "local GGUF" }, new ModelProfile { ModelId = modelPath, AutoManageCompanionAssets = false }) { UpdateStatus = ModelUpdateStatus.UpdateAvailable };

        await vm.UpdateModelCommand.ExecuteAsync(item);

        Assert.Equal("keep", File.ReadAllText(companionPath));
    }

    [Fact]
    public async Task Missing_known_companion_can_be_reacquired_without_substitution()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var assets = temp.PathFor("assets");
        var modelDir = Path.Combine(assets, "Models", "llm", "org__repo");
        Directory.CreateDirectory(modelDir);
        settings.Settings.DataManagement.LocalAiAssetsRoot = assets;
        var modelPath = Path.Combine(modelDir, "model.gguf");
        File.WriteAllText(modelPath, "model");
        var companionBytes = System.Text.Encoding.UTF8.GetBytes("recovered-projector");
        var fixture = CompanionFixture(System.Text.Encoding.UTF8.GetBytes("model"), companionBytes);
        var companionPath = Path.Combine(modelDir, "mmproj.gguf");
        var manifest = new ModelManifestStore(settings);
        const string revision = "0123456789abcdef0123456789abcdef01234567";
        settings.Settings.ManagedServers[0].ModelPath = modelPath;
        settings.Settings.ManagedServers[0].MmprojPath = companionPath;
        settings.Settings.ManagedServers[0].UseProjector = false;
        await manifest.UpsertAsync(new ModelManifestEntry { FilePath = modelPath, RepoId = "org/repo", RepoFile = "model.gguf", RevisionSha = revision, Sha256 = fixture.ModelHash, Source = "hf-browser", Companions = [new ModelCompanionManifestEntry { LocalFilePath = companionPath, RepoFile = "mmproj.gguf", RevisionSha = revision, Role = "projector", Sha256 = fixture.CompanionHash, SizeBytes = companionBytes.Length }] });
        var treeJson = $"[{{\"type\":\"file\",\"size\":6,\"lfs\":{{\"oid\":\"{fixture.ModelHash}\"}},\"path\":\"model.gguf\"}},{{\"type\":\"file\",\"size\":{companionBytes.Length},\"lfs\":{{\"oid\":\"{fixture.CompanionHash}\"}},\"path\":\"mmproj.gguf\"}},{{\"type\":\"file\",\"size\":{fixture.Metadata.Length},\"lfs\":{{\"oid\":\"{fixture.MetadataHash}\"}},\"path\":\".hermaeus/companions.json\"}}]";
        var hf = new HuggingFaceClient(new HttpClient(new RoutedFakeHandler(url => url.Contains("/tree/", StringComparison.Ordinal) ? (HttpStatusCode.OK, treeJson) : (HttpStatusCode.OK, fixture.Metadata))));
        var vm = new ModelManagementViewModel(new ScriptedModelsLlm(() => []), new ModelProfileService(settings), new FakeToasts(), settings, new FakeSystemInfo(), NewServicesViewModel(settings), manifest, hf, new ModelDownloadService(new HttpClient(new RoutedBytesHandler(_ => companionBytes))));
        var item = new ModelProfileItemViewModel(new LlmModel { Id = modelPath, Name = "model", Provider = "local GGUF" }, new ModelProfile { ModelId = modelPath }) { HasMissingCompanions = true, HasVerifiedCompanionReplacement = true };

        await vm.ReacquireCompanionsCommand.ExecuteAsync(item);

        Assert.Equal("recovered-projector", File.ReadAllText(companionPath));
        Assert.False(settings.Settings.ManagedServers[0].UseProjector);
        Assert.Equal(companionPath, settings.Settings.ManagedServers[0].MmprojPath);
    }

    [Fact]
    public async Task Disabling_companion_policy_with_keep_choice_preserves_files()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var assets = temp.PathFor("assets");
        var modelDir = Path.Combine(assets, "Models", "llm", "org__repo");
        Directory.CreateDirectory(modelDir);
        settings.Settings.DataManagement.LocalAiAssetsRoot = assets;
        var modelPath = Path.Combine(modelDir, "model.gguf");
        var companionPath = Path.Combine(modelDir, "mmproj.gguf");
        File.WriteAllText(modelPath, "model");
        File.WriteAllText(companionPath, "projector");
        var profiles = new ModelProfileService(settings);
        profiles.GetOrCreate(modelPath, "local GGUF").AutoManageCompanionAssets = true;
        var manifest = new ModelManifestStore(settings);
        await manifest.UpsertAsync(new ModelManifestEntry { FilePath = modelPath, RepoId = "org/repo", Companions = [new ModelCompanionManifestEntry { LocalFilePath = companionPath, RepoFile = "mmproj.gguf", Role = "projector" }] });
        var vm = new ModelManagementViewModel(new ScriptedModelsLlm(() => []), profiles, new FakeToasts(), settings, new FakeSystemInfo(), NewServicesViewModel(settings), manifest, new HuggingFaceClient(), new ModelDownloadService());
        vm.RequestCompanionDisableConfirmation = _ => Task.FromResult(CompanionDisableChoice.KeepFiles);
        var item = new ModelProfileItemViewModel(new LlmModel { Id = modelPath, Name = "model", Provider = "local GGUF" }, new ModelProfile { ModelId = modelPath, AutoManageCompanionAssets = false });

        await vm.SaveProfileCommand.ExecuteAsync(item);

        Assert.True(File.Exists(companionPath));
        Assert.False(settings.Settings.ModelProfiles.Single(p => p.ModelId == modelPath).AutoManageCompanionAssets);
    }

    [Fact]
    public async Task Disabling_companion_policy_with_remove_choice_removes_only_manifested_files()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var assets = temp.PathFor("assets");
        var modelDir = Path.Combine(assets, "Models", "llm", "org__repo");
        Directory.CreateDirectory(modelDir);
        settings.Settings.DataManagement.LocalAiAssetsRoot = assets;
        var modelPath = Path.Combine(modelDir, "model.gguf");
        var companionPath = Path.Combine(modelDir, "mmproj.gguf");
        File.WriteAllText(modelPath, "model");
        File.WriteAllText(companionPath, "projector");
        var profiles = new ModelProfileService(settings);
        profiles.GetOrCreate(modelPath, "local GGUF").AutoManageCompanionAssets = true;
        var manifest = new ModelManifestStore(settings);
        await manifest.UpsertAsync(new ModelManifestEntry { FilePath = modelPath, RepoId = "org/repo", Companions = [new ModelCompanionManifestEntry { LocalFilePath = companionPath, RepoFile = "mmproj.gguf", Role = "projector" }] });
        await manifest.UpsertAsync(new ModelManifestEntry { FilePath = companionPath, RepoId = "org/repo", RepoFile = "mmproj.gguf", Source = "hf-browser", ParentModelPath = modelPath, CompanionRole = "projector" });
        var vm = new ModelManagementViewModel(new ScriptedModelsLlm(() => []), profiles, new FakeToasts(), settings, new FakeSystemInfo(), NewServicesViewModel(settings), manifest, new HuggingFaceClient(), new ModelDownloadService());
        vm.RequestCompanionDisableConfirmation = _ => Task.FromResult(CompanionDisableChoice.RemoveFiles);
        var item = new ModelProfileItemViewModel(new LlmModel { Id = modelPath, Name = "model", Provider = "local GGUF" }, new ModelProfile { ModelId = modelPath, AutoManageCompanionAssets = false });

        await vm.SaveProfileCommand.ExecuteAsync(item);

        Assert.False(File.Exists(companionPath));
        Assert.Null(await manifest.FindAsync(companionPath));
        Assert.True(File.Exists(modelPath));
    }

    [Fact]
    public async Task Bulk_companion_cleanup_removes_only_reviewable_files_and_preserves_present_assets()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var assets = temp.PathFor("assets");
        var modelDir = Path.Combine(assets, "Models", "llm", "org__repo");
        Directory.CreateDirectory(modelDir);
        settings.Settings.DataManagement.LocalAiAssetsRoot = assets;

        var modelPath = Path.Combine(modelDir, "model.gguf");
        var stalePath = Path.Combine(modelDir, "mmproj-stale.gguf");
        var presentPath = Path.Combine(modelDir, "mmproj-present.gguf");
        var unknownPath = Path.Combine(modelDir, "mmproj-unknown.gguf");
        File.WriteAllText(modelPath, "model");
        File.WriteAllText(stalePath, "stale");
        File.WriteAllText(presentPath, "present");

        var manifest = new ModelManifestStore(settings);
        await manifest.UpsertAsync(new ModelManifestEntry
        {
            FilePath = modelPath,
            Companions =
            [
                new ModelCompanionManifestEntry { LocalFilePath = stalePath, Role = "projector", SizeBytes = 99 },
                new ModelCompanionManifestEntry { LocalFilePath = presentPath, Role = "projector", SizeBytes = 7 },
                new ModelCompanionManifestEntry { LocalFilePath = unknownPath, Role = "draft_head", RequiresUserConfirmation = true }
            ]
        });

        var vm = new ModelManagementViewModel(new ScriptedModelsLlm(() => []), new ModelProfileService(settings), new FakeToasts(), settings,
            new FakeSystemInfo(), NewServicesViewModel(settings), manifest, new HuggingFaceClient(), new ModelDownloadService());
        var item = new ModelProfileItemViewModel(
            new LlmModel { Id = modelPath, Name = "model", Provider = "local GGUF" },
            new ModelProfile { ModelId = modelPath });
        var primary = await manifest.FindAsync(modelPath);
        item.ApplyCatalogClassification(ModelCatalogRole.ChatGeneration, null, primary);
        ModelDeletionPlan? confirmedPlan = null;
        vm.RequestCompanionRemovalConfirmation = plan =>
        {
            confirmedPlan = plan;
            return Task.FromResult(true);
        };

        await vm.ClearReviewableCompanionsCommand.ExecuteAsync(item);

        Assert.NotNull(confirmedPlan);
        Assert.Single(confirmedPlan!.Files);
        Assert.Contains("Present companions remain unchanged", confirmedPlan.Description, StringComparison.Ordinal);
        Assert.False(File.Exists(stalePath));
        Assert.True(File.Exists(presentPath));
        var remaining = await manifest.FindAsync(modelPath);
        Assert.NotNull(remaining);
        Assert.Single(remaining!.Companions);
        Assert.Equal(presentPath, remaining.Companions[0].LocalFilePath);
    }
}
