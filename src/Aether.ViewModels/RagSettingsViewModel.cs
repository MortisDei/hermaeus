using System.Diagnostics;
using System.Security.Cryptography;
using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

public partial class RagSettingsViewModel : ObservableObject
{
    private readonly Func<string> _fallbackRoot;

    [ObservableProperty] private string _embeddingBaseUrl = "http://localhost:39202";
    [ObservableProperty] private string _embeddingModel = "nomic-embed-text";
    [ObservableProperty] private string _ragRerankerModelPath = string.Empty;

    public UiBoundCollection<string> EmbeddingModelOptions { get; } = [];
    public UiBoundCollection<string> RerankerModelPathOptions { get; } = [];

    public RagSettingsViewModel(Func<string> fallbackRoot) => _fallbackRoot = fallbackRoot;

    public void ReloadFrom(AppSettings settings, string localAiAssetsRoot)
    {
        EmbeddingBaseUrl = settings.Rag.EmbeddingBaseUrl;
        EmbeddingModel = settings.Rag.EmbeddingModel;
        RagRerankerModelPath = settings.Rag.RerankerModelPath;
        RefreshLocalAiAssetOptions(localAiAssetsRoot);
    }

    public void ApplyTo(AppSettings settings)
    {
        settings.Rag.EmbeddingBaseUrl = EmbeddingBaseUrl.Trim();
        settings.Rag.EmbeddingModel = EmbeddingModel;
        settings.Rag.RerankerModelPath = RagRerankerModelPath.Trim();
    }

    public void RefreshEmbeddingModelOptions(string localAiAssetsRoot)
    {
        EmbeddingModelOptions.Clear();
        AddEmbeddingModelOption(EmbeddingModel);
        try
        {
            var root = string.IsNullOrWhiteSpace(localAiAssetsRoot) ? _fallbackRoot() : Path.GetFullPath(localAiAssetsRoot);
            if (!Directory.Exists(root)) return;
            var ggufs = LocalAiAssetLocator.FindEmbeddingModels(root)
                .Select(Path.GetFileNameWithoutExtension)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x);
            foreach (var name in ggufs.Where(n => !string.IsNullOrWhiteSpace(n)))
                AddEmbeddingModelOption(name!);
        }
        catch { }
    }

    public void RefreshLocalAiAssetOptions(string localAiAssetsRoot)
    {
        RefreshEmbeddingModelOptions(localAiAssetsRoot);
        RefreshRerankerModelPathOptions(localAiAssetsRoot);
    }

    public void RefreshRerankerModelPathOptions(string localAiAssetsRoot)
    {
        RerankerModelPathOptions.Clear();
        AddRerankerModelPathOption(RagRerankerModelPath);
        try
        {
            var root = string.IsNullOrWhiteSpace(localAiAssetsRoot) ? _fallbackRoot() : Path.GetFullPath(localAiAssetsRoot);
            if (!Directory.Exists(root)) return;

            foreach (var path in LocalAiAssetLocator.FindRerankerDirectories(root))
                AddRerankerModelPathOption(path);

            if (string.IsNullOrWhiteSpace(RagRerankerModelPath) && RerankerModelPathOptions.Count > 0)
                RagRerankerModelPath = RerankerModelPathOptions[0];
        }
        catch { }
    }

    private void AddEmbeddingModelOption(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        if (EmbeddingModelOptions.Any(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)))
            return;

        EmbeddingModelOptions.Add(name.Trim());
    }

    private void AddRerankerModelPathOption(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        path = Path.GetFullPath(path.Trim());
        if (RerankerModelPathOptions.Any(existing => string.Equals(existing, path, StringComparison.OrdinalIgnoreCase)))
            return;

        RerankerModelPathOptions.Add(path);
    }
}
