using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Security.Cryptography;
using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

/// <summary>
/// Manages named per-app tokens (docs/review/03-next-level-roadmap.md Phase 2)
/// instead of one shared token. Add/revoke apply and save immediately,
/// independent of the page's main Save button: a pending revocation that
/// silently reverted if the user navigated away without saving would be a
/// real security footgun for a credential list.
/// </summary>
public partial class LocalApiSettingsViewModel : ObservableObject
{
    private readonly ISecretStore _secrets;
    private readonly ISettingsService? _settings;

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private int _port = 39300;
    [ObservableProperty] private string _newTokenName = string.Empty;
    [ObservableProperty] private string _newTokenValue = string.Empty;
    [ObservableProperty] private string _tokenStatus = "No tokens generated yet. The local API refuses every request until one is saved.";
    [ObservableProperty] private string _processStatusLabel = "Stopped";

    public ObservableCollection<LocalApiTokenRowViewModel> Tokens { get; } = [];

    public LocalApiSettingsViewModel(ISecretStore secrets, ISettingsService? settings = null)
    {
        _secrets = secrets;
        _settings = settings;
    }

    public void ReloadFrom(AppSettings settings)
    {
        Enabled = settings.LocalApi.Enabled;
        Port = settings.LocalApi.Port;
        NewTokenName = string.Empty;
        NewTokenValue = string.Empty;
        Tokens.Clear();
        foreach (var t in settings.LocalApi.Tokens)
            Tokens.Add(new LocalApiTokenRowViewModel { Id = t.Id, Name = t.Name, CreatedAtDisplay = t.CreatedAt.ToLocalTime().ToString("g") });
        TokenStatus = Tokens.Count == 0
            ? "No tokens generated yet. The local API refuses every request until one is saved."
            : $"{Tokens.Count} token(s) configured.";
    }

    public Task ApplyToAsync(AppSettings settings)
    {
        settings.LocalApi.Enabled = Enabled;
        settings.LocalApi.Port = Port;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void GenerateToken()
    {
        NewTokenValue = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        TokenStatus = "New token generated below. Copy it now and click Add; it will not be shown again.";
    }

    [RelayCommand]
    private async Task AddTokenAsync()
    {
        if (_settings is null || string.IsNullOrWhiteSpace(NewTokenName) || string.IsNullOrWhiteSpace(NewTokenValue))
            return;

        var name = NewTokenName.Trim();
        var secretRef = await _secrets.StoreAsync($"local-api-token-{Guid.NewGuid():N}", NewTokenValue.Trim());
        var entry = new LocalApiTokenEntry { Name = name, SecretRef = secretRef };
        _settings.Settings.LocalApi.Tokens.Add(entry);
        await _settings.SaveAsync();

        Tokens.Add(new LocalApiTokenRowViewModel { Id = entry.Id, Name = entry.Name, CreatedAtDisplay = entry.CreatedAt.ToLocalTime().ToString("g") });
        NewTokenName = string.Empty;
        NewTokenValue = string.Empty;
        TokenStatus = $"{Tokens.Count} token(s) configured.";
    }

    [RelayCommand]
    private async Task RevokeTokenAsync(LocalApiTokenRowViewModel? row)
    {
        if (_settings is null || row is null)
            return;

        _settings.Settings.LocalApi.Tokens.RemoveAll(t => t.Id == row.Id);
        await _settings.SaveAsync();

        var existing = Tokens.FirstOrDefault(t => t.Id == row.Id);
        if (existing is not null)
            Tokens.Remove(existing);
        TokenStatus = Tokens.Count == 0
            ? "No tokens configured. The local API refuses every request until one is saved."
            : $"{Tokens.Count} token(s) configured.";
    }
}
