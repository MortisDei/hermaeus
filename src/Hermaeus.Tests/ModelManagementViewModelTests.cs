using System.Net;
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
    public async Task SelectHfRepo_hides_multipart_files_and_shows_single_file_ggufs()
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

        var file = Assert.Single(vm.HfFiles);
        Assert.Equal("model-Q4.gguf", file.FileName);
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

        var destination = Path.Combine(modelsRoot, "Models", "llm", "tiny.gguf");
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
        var llmDir = Path.Combine(modelsRoot, "Models", "LLM");
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
}
