using System.Diagnostics;
using System.Security.Cryptography;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Hermaeus.ViewModels;

public partial class RagSettingsViewModel : ObservableObject
{
    private readonly Func<string> _fallbackRoot;

    [ObservableProperty] private string _ragRerankerModelPath = string.Empty;
    [ObservableProperty] private int _chatInjectionTokenBudget = 2000;

    public UiBoundCollection<string> RerankerModelPathOptions { get; } = [];

    public RagSettingsViewModel(Func<string> fallbackRoot) => _fallbackRoot = fallbackRoot;

    public void ReloadFrom(AppSettings settings, string localAiAssetsRoot)
    {
        RagRerankerModelPath = settings.Rag.RerankerModelPath;
        ChatInjectionTokenBudget = settings.Rag.ChatInjectionTokenBudget;
        RefreshLocalAiAssetOptions(localAiAssetsRoot);
    }

    public void ApplyTo(AppSettings settings)
    {
        settings.Rag.RerankerModelPath = RagRerankerModelPath.Trim();
        settings.Rag.ChatInjectionTokenBudget = ChatInjectionTokenBudget;
    }

    // Embed URL/Embed model used to be edited here too, but Services > Embeddings
    // already owns that server's port and model file (which this section's fields
    // were always overwritten by on save); it is now the only place to set them -
    // see ServicesViewModel.SyncToConfig.
    public void RefreshLocalAiAssetOptions(string localAiAssetsRoot)
    {
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
