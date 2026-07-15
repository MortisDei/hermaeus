using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Rag.Embeddings;
using Aether.Rag.Storage;
using Aether.Rag.Retrieval;
using Aether.Voice;
using System.Diagnostics;
using System.Linq;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Aether.Services;

public sealed partial class DoctorService
{
    private async Task<DoctorCheck> CheckRagDbAsync(CancellationToken ct)
    {
        try
        {
            await _ragStore.InitializeAsync();
            var datasets = await _ragStore.GetDatasetsAsync(ct);
            return BuildCheck(
                "rag-db",
                "RAG database health",
                DoctorCheckStatus.Ready,
                "RAG database ready",
                $"Datasets: {datasets.Count}",
                "Open RAG",
                true,
                $"Datasets: {datasets.Count}",
                "RAG");
        }
        catch (Exception ex)
        {
            return BuildCheck(
                "rag-db",
                "RAG database health",
                DoctorCheckStatus.Error,
                "RAG database error",
                ex.Message,
                "Open RAG",
                true,
                ex.ToString(),
                "RAG");
        }
    }

    private async Task<DoctorCheck> CheckEmbeddingBackendAsync(CancellationToken ct)
    {
        try
        {
            var embedding = await _embeddings.EmbedAsync("doctor", ct);
            var ok = embedding.Length > 0;
            return BuildCheck(
                "embeddings",
                "Embedding backend health",
                ok ? DoctorCheckStatus.Ready : DoctorCheckStatus.Warning,
                ok ? "Embedding backend responded" : "Embedding backend returned empty response",
                ok ? $"Dimensions: {embedding.Length}" : "No embedding returned.",
                "Open Services",
                true,
                ok ? $"Dimensions: {embedding.Length}" : "No embedding returned.",
                "RAG");
        }
        catch (Exception ex)
        {
            return BuildCheck(
                "embeddings",
                "Embedding backend health",
                DoctorCheckStatus.Warning,
                "Embedding backend not reachable",
                ex.Message,
                "Open Services",
                true,
                ex.ToString(),
                "RAG");
        }
    }

    private DoctorCheck CheckEmbeddingModel()
    {
        var embeddingModel = _settings.Settings.Rag.EmbeddingModel.Trim();
        var search = FindInstalledEmbeddingModel(embeddingModel);
        if (search.Found)
        {
            return BuildCheck(
                "embedding-model",
                "Embedding model availability",
                DoctorCheckStatus.Ready,
                "Embedding model found",
                search.Path,
                "Open Services",
                true,
                search.Path,
                "RAG");
        }

        return BuildCheck(
            "embedding-model",
            "Embedding model availability",
            DoctorCheckStatus.Warning,
            "No embedding model found",
            "Download a dedicated embedding GGUF model for RAG indexing.",
            "Download embedding model",
            true,
            string.IsNullOrWhiteSpace(search.SearchedIn)
                ? "No candidate model directories were found."
                : $"Searched in: {search.SearchedIn}",
            "RAG");
    }

    private async Task<DoctorCheck> CheckEmbeddingModelVersionAsync(DoctorCheck modelCheck, CancellationToken ct)
    {
        var configured = _settings.Settings.Rag.EmbeddingModel.Trim();
        var search = FindInstalledEmbeddingModel(configured);
        if (!search.Found)
        {
            return BuildCheck(
                "embedding-model-update",
                "nomic embedding model version",
                DoctorCheckStatus.Info,
                "Embedding model version check skipped",
                "Install the pinned nomic embedding model before checking its file hash.",
                "Download embedding model",
                true,
                modelCheck.Diagnostics,
                "RAG");
        }

        if (!LooksLikeNomicEmbeddingName(configured) && !LooksLikeNomicEmbeddingName(Path.GetFileName(search.Path)))
        {
            return BuildCheck(
                "embedding-model-update",
                "nomic embedding model version",
                DoctorCheckStatus.Info,
                "Non-nomic embedding model selected",
                "Doctor only verifies the pinned nomic-embed-text-v1.5 model hash.",
                "Open Settings",
                true,
                search.Path,
                "RAG");
        }

        var hashOk = await _downloads.VerifyHashAsync(search.Path, _embeddingDownload.Sha256, null, ct);
        return BuildCheck(
            "embedding-model-update",
            "nomic embedding model version",
            hashOk ? DoctorCheckStatus.Ready : DoctorCheckStatus.Warning,
            hashOk ? "Pinned nomic embedding model verified" : "nomic embedding model should be refreshed",
            hashOk
                ? "Installed file matches the pinned nomic-embed-text-v1.5 GGUF."
                : "Download the pinned nomic-embed-text-v1.5 GGUF so RAG embeddings use the expected model.",
            "Download embedding model",
            true,
            search.Path,
            "RAG");
    }

    private DoctorCheck CheckEmbeddingBackendSkipped(DoctorCheck modelCheck) =>
        BuildCheck(
            "embeddings",
            "Embedding backend health",
            DoctorCheckStatus.Info,
            "Embedding backend check skipped",
            "Install or select a dedicated embedding model before checking backend health.",
            "Details",
            false,
            modelCheck.Diagnostics,
            "RAG");

    /// <summary>
    /// Blank EmbeddingBaseUrl silently falls back to the chat server, queuing
    /// embed calls behind generation on a single-slot llama-server (r9
    /// 01-send-path-latency.md 1.4). Advisory only; the fallback stays the
    /// zero-config default.
    /// </summary>
    private DoctorCheck? CheckEmbeddingEndpointFallbackAdvisory()
    {
        var configured = _settings.Settings.Rag.EmbeddingBaseUrl?.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
            return null;
        if (!_settings.Settings.Memory.Enabled && !_settings.Settings.Rag.Enabled)
            return null;

        var chatUrl = _settings.Settings.Llm.LlamaCppBaseUrl.TrimEnd('/');
        return BuildCheck(
            "embedding-endpoint-fallback",
            "Embedding endpoint configuration",
            DoctorCheckStatus.Info,
            "Embedding requests fall back to the chat server",
            $"Rag.EmbeddingBaseUrl is not set; embedding requests fall back to {chatUrl}, queuing behind chat generation. Configure a dedicated embeddings server for best latency.",
            "Open Settings",
            true,
            $"Fallback endpoint: {chatUrl}",
            "RAG");
    }

    private DoctorCheck CheckRerankerAssets()
    {
        if (!_settings.Settings.Rag.RerankerEnabled)
        {
            return BuildCheck(
                "reranker",
                "Reranker assets",
                DoctorCheckStatus.Info,
                "Reranker disabled",
                "Enable reranker in Settings to improve RAG quality.",
                "Open Settings",
                true,
                "Reranker disabled.",
                "RAG");
        }

        var modelDir = ResolveRerankerDirectory();
        var vocabPath = Path.Combine(modelDir, "vocab.txt");
        // Accept any ONNX file in the reranker directory (some mirrors/name variants use different filenames)
        var ok = File.Exists(vocabPath) && Directory.Exists(modelDir) && Directory.EnumerateFiles(modelDir, "*.onnx", SearchOption.TopDirectoryOnly).Any();
        return BuildCheck(
            "reranker",
            "Reranker assets",
            ok ? DoctorCheckStatus.Ready : DoctorCheckStatus.Warning,
            ok ? "Reranker assets present" : "Reranker assets missing",
            ok ? modelDir : "Download or point Aether at the reranker model folder.",
            ok ? "Open Settings" : "Install Reranker",
            true,
            modelDir,
            "RAG");
    }

    public async Task<bool> InstallRerankerAssetsAsync(CancellationToken ct = default)
    {
        if (_reranker is Aether.Rag.Retrieval.OnnxCrossEncoderReranker onnx)
        {
            return await onnx.InstallAssetsAsync(null, ct);
        }

        return false;
    }

    public async Task<bool> InstallRerankerAssetsAsync(IProgress<string> progress, CancellationToken ct = default)
    {
        if (_reranker is Aether.Rag.Retrieval.OnnxCrossEncoderReranker onnx)
        {
            var result = await onnx.InstallAssetsAsync(progress, ct);
            if (!result)
            {
                _runtimeLogs?.Add(new RuntimeLogEntry(
                    DateTime.UtcNow,
                    RuntimeLogLevel.Error,
                    RuntimeLogCategory.Service,
                    "Reranker asset installation failed"));
            }
            else
            {
                _runtimeLogs?.Add(new RuntimeLogEntry(
                    DateTime.UtcNow,
                    RuntimeLogLevel.Info,
                    RuntimeLogCategory.Service,
                    "Reranker assets installed successfully"));
            }
            return result;
        }

        _runtimeLogs?.Add(new RuntimeLogEntry(
            DateTime.UtcNow,
            RuntimeLogLevel.Warning,
            RuntimeLogCategory.Service,
            "Reranker installation skipped: reranker not available"));
        return false;
    }

    public Task<bool> InstallEmbeddingModelAsync(CancellationToken ct = default)
        => InstallEmbeddingModelAsync(null, ct);

    public async Task<bool> InstallEmbeddingModelAsync(IProgress<string>? progress, CancellationToken ct = default)
    {
        var destinationDirectory = ResolveEmbeddingModelDirectory();
        Directory.CreateDirectory(destinationDirectory);

        var destinationPath = Path.Combine(destinationDirectory, _embeddingDownload.FileName);
        if (File.Exists(destinationPath))
        {
            progress?.Report($"Verifying existing embedding model at {destinationPath}...");
            if (await _downloads.VerifyHashAsync(destinationPath, _embeddingDownload.Sha256, progress, ct))
            {
                await ConfigureInstalledEmbeddingModelAsync(destinationPath, ct);
                progress?.Report($"Embedding model ready at {destinationPath}");
                return true;
            }

            TryDelete(destinationPath);
            progress?.Report("Existing embedding model failed verification and will be downloaded again.");
        }

        var existing = FindInstalledEmbeddingModel(_embeddingDownload.ModelName);
        if (existing.Found && !string.Equals(Path.GetFullPath(existing.Path), Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
        {
            progress?.Report($"Verifying existing embedding model at {existing.Path}...");
            if (await _downloads.VerifyHashAsync(existing.Path, _embeddingDownload.Sha256, progress, ct))
            {
                File.Move(existing.Path, destinationPath, true);
                await ConfigureInstalledEmbeddingModelAsync(destinationPath, ct);
                progress?.Report($"Embedding model moved to {destinationPath}");
                return true;
            }
        }

        progress?.Report($"Downloading embedding model to {destinationPath}...");

        var downloadProgress = progress is null
            ? null
            : new Progress<DownloadProgress>(p => progress.Report($"Downloading embedding model... {p.PercentComplete:F1}%"));

        var result = await _downloads.DownloadAsync(_embeddingDownload.Url, destinationPath, downloadProgress, ct);
        if (!result.Success)
        {
            var errorMsg = $"Embedding model download failed: {result.Message}";
            progress?.Report(result.Message);
            _runtimeLogs?.Add(new RuntimeLogEntry(
                DateTime.UtcNow,
                RuntimeLogLevel.Error,
                RuntimeLogCategory.Service,
                errorMsg));
            return false;
        }

        progress?.Report("Verifying embedding model SHA256...");
        if (!await _downloads.VerifyHashAsync(destinationPath, _embeddingDownload.Sha256, progress, ct))
        {
            TryDelete(destinationPath);
            var errorMsg = "Embedding model verification failed. The downloaded file was removed.";
            progress?.Report(errorMsg);
            _runtimeLogs?.Add(new RuntimeLogEntry(
                DateTime.UtcNow,
                RuntimeLogLevel.Error,
                RuntimeLogCategory.Service,
                errorMsg));
            return false;
        }

        await ConfigureInstalledEmbeddingModelAsync(destinationPath, ct);
        progress?.Report($"Embedding model ready at {destinationPath}");
        _runtimeLogs?.Add(new RuntimeLogEntry(
            DateTime.UtcNow,
            RuntimeLogLevel.Info,
            RuntimeLogCategory.Service,
            $"Embedding model installed successfully at {destinationPath}"));
        return true;
    }

    private async Task ConfigureInstalledEmbeddingModelAsync(string destinationPath, CancellationToken ct)
    {
        _settings.Settings.Rag.EmbeddingModel = _embeddingDownload.ModelName;

        var embeddingServer = _settings.Settings.ManagedServers.FirstOrDefault(s => s.EmbeddingsMode);
        if (embeddingServer is not null)
            embeddingServer.ModelPath = destinationPath;

        await _settings.SaveAsync();
    }

    private string ResolveRerankerDirectory()
    {
        return OnnxCrossEncoderReranker.ResolveModelDirectory(_settings.Settings);
    }

    private (bool Found, string Path, string SearchedIn) FindInstalledEmbeddingModel(string embeddingModel)
    {
        var directories = GetEmbeddingCandidateDirectories();
        var markers = BuildEmbeddingMarkers(embeddingModel);
        var dedicated = LocalAiAssetLocator.FindEmbeddingModels(_settings.Settings.DataManagement.LocalAiAssetsRoot)
            .Where(path => markers.Any(marker => path.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(path => path.Length)
            .ToList();
        if (dedicated.Count > 0)
            return (true, dedicated[0], string.Join(", ", directories));

        foreach (var dir in directories)
        {
            if (!Directory.Exists(dir))
                continue;

            try
            {
                // Prefer marker-matched files. Do not treat arbitrary chat GGUFs as embedding models.
                var all = Directory.EnumerateFiles(dir, "*.gguf", SearchOption.AllDirectories).ToList();
                var matches = all.Where(path => markers.Any(marker => path.Contains(marker, StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(path => path.Length)
                    .ToList();

                if (matches.Count > 0)
                    return (true, matches[0], string.Join(", ", directories));
            }
            catch
            {
                // Keep scanning other directories.
            }
        }

        return (false, string.Empty, string.Join(", ", directories));
    }

    private string ResolveEmbeddingModelDirectory()
    {
        var settings = _settings.Settings;
        var aiRoot = settings.DataManagement.LocalAiAssetsRoot.Trim();
        if (!string.IsNullOrWhiteSpace(aiRoot) && Directory.Exists(aiRoot))
        {
            var layout = LocalAiAssetLocator.Detect(aiRoot);
            var models = !string.IsNullOrWhiteSpace(layout.ModelsDirectory)
                ? layout.ModelsDirectory
                : Path.Combine(Path.GetFullPath(aiRoot), "Models");
            return Path.Combine(models, "embed");
        }

        var dataRoot = SettingsService.ResolveDataRoot(_settings.Settings);
        return Path.Combine(dataRoot, "models", "embed");
    }

    private List<string> GetEmbeddingCandidateDirectories()
    {
        var settings = _settings.Settings;
        var aiRoot = settings.DataManagement.LocalAiAssetsRoot.Trim();
        var layout = LocalAiAssetLocator.Detect(aiRoot);
        var dataRoot = SettingsService.ResolveDataRoot(settings);

        return new[]
        {
            string.IsNullOrWhiteSpace(layout.ModelsDirectory) ? string.Empty : Path.Combine(layout.ModelsDirectory, "embed"),
            string.IsNullOrWhiteSpace(layout.ModelsDirectory) ? string.Empty : Path.Combine(layout.ModelsDirectory, "embedding"),
            string.IsNullOrWhiteSpace(layout.ModelsDirectory) ? string.Empty : Path.Combine(layout.ModelsDirectory, "embeddings"),
            layout.ModelsDirectory,
            Path.Combine(dataRoot, "models", "embed"),
            Path.Combine(dataRoot, "models")
        }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
    }

    private static List<string> BuildEmbeddingMarkers(string embeddingModel)
    {
        var markers = new List<string>
        {
            "embed",
            "embedding",
            "nomic",
            "bge",
            "gte"
        };

        if (!string.IsNullOrWhiteSpace(embeddingModel) && LooksLikeEmbeddingModelName(embeddingModel))
        {
            markers.Add(embeddingModel.Trim());
            markers.Add(embeddingModel.Trim().Replace('/', '-'));
        }

        return markers.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool LooksLikeEmbeddingModelName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var text = value.Trim();
        return text.Contains("embed", StringComparison.OrdinalIgnoreCase)
               || text.Contains("embedding", StringComparison.OrdinalIgnoreCase)
               || text.Contains("nomic", StringComparison.OrdinalIgnoreCase)
               || text.Contains("bge", StringComparison.OrdinalIgnoreCase)
               || LooksLikeE5EmbeddingName(text)
               || text.Contains("gte", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeNomicEmbeddingName(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Contains("nomic", StringComparison.OrdinalIgnoreCase)
        && (value.Contains("embed", StringComparison.OrdinalIgnoreCase)
            || value.Contains("text", StringComparison.OrdinalIgnoreCase));

    private static bool LooksLikeE5EmbeddingName(string text) =>
        text.Equals("e5", StringComparison.OrdinalIgnoreCase)
        || text.Contains("e5-", StringComparison.OrdinalIgnoreCase)
        || text.Contains("e5_", StringComparison.OrdinalIgnoreCase)
        || text.Contains("/e5", StringComparison.OrdinalIgnoreCase)
        || text.Contains("e5/", StringComparison.OrdinalIgnoreCase);
}
