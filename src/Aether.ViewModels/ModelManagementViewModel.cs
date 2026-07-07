using System.Collections.ObjectModel;
using Aether.Core.Models;
using System.Threading;
using Aether.Core.Services;
using Aether.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

public partial class ModelManagementViewModel : ObservableObject
{
    private readonly ILlmService _llm;
    private readonly ModelProfileService _profiles;
    private readonly IToastService _toasts;
    private long _lastRefreshUtcTicks = DateTime.MinValue.Ticks;
    private readonly List<LlmModel> _modelCache = [];
    private readonly object _modelCacheLock = new();

    public ObservableCollection<ModelProfileItemViewModel> Models { get; } = [];

    [ObservableProperty] private bool   _isLoading;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool   _isError;
    [ObservableProperty] private bool   _forceRefresh;

    public ModelManagementViewModel(ILlmService llm, ModelProfileService profiles, IToastService toasts)
    {
        _llm = llm;
        _profiles = profiles;
        _toasts = toasts;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsLoading = true; StatusMessage = string.Empty; IsError = false;
        try
        {
            List<LlmModel>? cachedModels;
            lock (_modelCacheLock)
            {
                var lastTicks = Interlocked.Read(ref _lastRefreshUtcTicks);
                var lastRefresh = new DateTime(lastTicks, DateTimeKind.Utc);
                var useCache = !ForceRefresh
                               && _modelCache.Count > 0
                               && DateTime.UtcNow - lastRefresh < TimeSpan.FromMinutes(2);
                cachedModels = useCache ? _modelCache.ToList() : null;
            }

            var models = cachedModels ?? await _llm.GetModelsAsync();
            if (cachedModels is null)
            {
                lock (_modelCacheLock)
                {
                    _modelCache.Clear();
                    _modelCache.AddRange(models);
                    Interlocked.Exchange(ref _lastRefreshUtcTicks, DateTime.UtcNow.Ticks);
                }
            }

            _profiles.ApplyProfiles(models);
            Models.Clear();
            foreach (var m in models)
            {
                var profile = _profiles.GetOrCreate(m.Id, m.Provider);
                Models.Add(new ModelProfileItemViewModel(m, profile));
            }

            StatusMessage = models.Count == 0
                ? "No models reported by the running backends"
                : $"{models.Count} model(s) loaded{(cachedModels is not null ? " from cache" : "")}";
            ForceRefresh = false;
        }
        catch (Exception ex) { StatusMessage = ex.Message; IsError = true; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task SaveProfileAsync(ModelProfileItemViewModel? item)
    {
        if (item is null) return;

        await _profiles.SaveAsync(item.ToProfile());
        item.ApplySavedState();
        lock (_modelCacheLock)
            _profiles.ApplyProfiles(_modelCache);
        _toasts.Show("Model profile saved", $"Updated metadata for {item.DisplayName}.", ToastKind.Success);
    }

    [RelayCommand]
    private async Task ResetProfileAsync(ModelProfileItemViewModel? item)
    {
        if (item is null) return;

        await _profiles.ResetAsync(item.ModelId);
        item.Reset();
        lock (_modelCacheLock)
            _profiles.ApplyProfiles(_modelCache);
        _toasts.Show("Model profile reset", $"Aether metadata for {item.RawName} was cleared.", ToastKind.Info);
    }

}

public partial class ModelProfileItemViewModel : ObservableObject
{
    private readonly string _originalDisplayName;
    private readonly string _originalDescription;
    private readonly string _originalTagsText;
    private readonly double? _originalTemperature;
    private readonly int? _originalContextSize;
    private readonly int? _originalMaxTokens;
    private readonly double? _originalTopP;
    private readonly int? _originalTopK;
    private readonly double? _originalMinP;
    private readonly double? _originalRepeatPenalty;
    private readonly double? _originalFrequencyPenalty;
    private readonly double? _originalPresencePenalty;
    private readonly bool _originalIsVisible;
    private readonly string _originalAvatar;

    public string ModelId { get; }
    public string RawName { get; }
    public string Provider { get; }
    public string SizeDisplay { get; }
    public string ModifiedDisplay { get; }
    public int? ProbedContextLength { get; }
    public string ContextWatermark => ProbedContextLength is { } n ? $"Detected: {n}" : "Default context";

    [ObservableProperty] private string _displayName;
    [ObservableProperty] private string _description;
    [ObservableProperty] private string _tagsText;
    [ObservableProperty] private double? _defaultTemperature;
    [ObservableProperty] private int? _defaultContextSize;
    [ObservableProperty] private int? _defaultMaxTokens;
    [ObservableProperty] private double? _defaultTopP;
    [ObservableProperty] private int? _defaultTopK;
    [ObservableProperty] private double? _defaultMinP;
    [ObservableProperty] private double? _defaultRepeatPenalty;
    [ObservableProperty] private double? _defaultFrequencyPenalty;
    [ObservableProperty] private double? _defaultPresencePenalty;
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private string _avatar;

    public ModelProfileItemViewModel(LlmModel model, ModelProfile profile)
    {
        ModelId = model.Id;
        RawName = model.Name;
        Provider = model.Provider;
        SizeDisplay = model.SizeDisplay;
        ModifiedDisplay = model.ModifiedAt?.ToString("d MMM yyyy") ?? string.Empty;
        ProbedContextLength = model.ProbedContextLength;
        _displayName = profile.DisplayName;
        _description = profile.Description;
        _tagsText = string.Join(", ", profile.Tags);
        _defaultTemperature = profile.DefaultTemperature;
        _defaultContextSize = profile.DefaultContextSize;
        _defaultMaxTokens = profile.DefaultMaxTokens;
        _defaultTopP = profile.DefaultTopP;
        _defaultTopK = profile.DefaultTopK;
        _defaultMinP = profile.DefaultMinP;
        _defaultRepeatPenalty = profile.DefaultRepeatPenalty;
        _defaultFrequencyPenalty = profile.DefaultFrequencyPenalty;
        _defaultPresencePenalty = profile.DefaultPresencePenalty;
        _isVisible = profile.IsVisible;
        _avatar = profile.Avatar;

        _originalDisplayName = DisplayName;
        _originalDescription = Description;
        _originalTagsText = TagsText;
        _originalTemperature = DefaultTemperature;
        _originalContextSize = DefaultContextSize;
        _originalMaxTokens = DefaultMaxTokens;
        _originalTopP = DefaultTopP;
        _originalTopK = DefaultTopK;
        _originalMinP = DefaultMinP;
        _originalRepeatPenalty = DefaultRepeatPenalty;
        _originalFrequencyPenalty = DefaultFrequencyPenalty;
        _originalPresencePenalty = DefaultPresencePenalty;
        _originalIsVisible = IsVisible;
        _originalAvatar = Avatar;
    }

    public string EffectiveName => string.IsNullOrWhiteSpace(DisplayName) ? RawName : DisplayName.Trim();
    public string TagsDisplay => string.Join("  ", Tags);

    public ModelProfile ToProfile() => new()
    {
        ModelId = ModelId,
        DisplayName = DisplayName,
        Description = Description,
        Tags = Tags,
        DefaultTemperature = DefaultTemperature,
        DefaultContextSize = DefaultContextSize,
        DefaultMaxTokens = DefaultMaxTokens,
        DefaultTopP = DefaultTopP,
        DefaultTopK = DefaultTopK,
        DefaultMinP = DefaultMinP,
        DefaultRepeatPenalty = DefaultRepeatPenalty,
        DefaultFrequencyPenalty = DefaultFrequencyPenalty,
        DefaultPresencePenalty = DefaultPresencePenalty,
        Backend = Provider,
        IsVisible = IsVisible,
        Avatar = Avatar
    };

    public void ApplySavedState()
    {
        OnPropertyChanged(nameof(EffectiveName));
        OnPropertyChanged(nameof(TagsDisplay));
    }

    public void Reset()
    {
        DisplayName = string.Empty;
        Description = string.Empty;
        TagsText = string.Empty;
        DefaultTemperature = null;
        DefaultContextSize = null;
        DefaultMaxTokens = null;
        DefaultTopP = null;
        DefaultTopK = null;
        DefaultMinP = null;
        DefaultRepeatPenalty = null;
        DefaultFrequencyPenalty = null;
        DefaultPresencePenalty = null;
        IsVisible = true;
        Avatar = string.Empty;
        ApplySavedState();
    }

    public void Revert()
    {
        DisplayName = _originalDisplayName;
        Description = _originalDescription;
        TagsText = _originalTagsText;
        DefaultTemperature = _originalTemperature;
        DefaultContextSize = _originalContextSize;
        DefaultMaxTokens = _originalMaxTokens;
        DefaultTopP = _originalTopP;
        DefaultTopK = _originalTopK;
        DefaultMinP = _originalMinP;
        DefaultRepeatPenalty = _originalRepeatPenalty;
        DefaultFrequencyPenalty = _originalFrequencyPenalty;
        DefaultPresencePenalty = _originalPresencePenalty;
        IsVisible = _originalIsVisible;
        Avatar = _originalAvatar;
        ApplySavedState();
    }

    private List<string> Tags => TagsText
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(t => t.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToList();

    partial void OnDisplayNameChanged(string value) => OnPropertyChanged(nameof(EffectiveName));
    partial void OnTagsTextChanged(string value) => OnPropertyChanged(nameof(TagsDisplay));
}
