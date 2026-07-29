using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Security.Cryptography;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.Services.Recall;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Hermaeus.ViewModels;

public partial class MemorySettingsViewModel : ObservableObject
{
    private readonly RecallIndexingService? _recallIndexing;
    private readonly IToastService? _toasts;

    [ObservableProperty] private bool _memoryFeatureEnabled;
    [ObservableProperty] private bool _memoryInjectIntoContext;
    [ObservableProperty] private double _memoryImportanceThreshold = 0.6;
    [ObservableProperty] private int _memoryInjectionTokenBudget = 500;
    [ObservableProperty] private int _memoryAutoArchiveDays = 90;
    [ObservableProperty] private bool _consumeAgentLessonsInChat;

    /// <summary>r24 doc 02 2.0: default on, but visible. Keeps a searchable copy of
    /// message and task text in recall.db, included in backups.</summary>
    [ObservableProperty] private bool _recallIndexingEnabled = true;

    /// <summary>r24 doc 02 2.6: opt-in, off by default, matching ConsumeAgentLessonsInChat's precedent.</summary>
    [ObservableProperty] private bool _recallInjectionEnabled;
    [ObservableProperty] private int _recallInjectionTokenBudget = 400;

    [ObservableProperty] private int _recallEntryCount;
    [ObservableProperty] private string _recallIndexSizeDisplay = "0 B";
    [ObservableProperty] private bool _isClearingRecallIndex;

    public Func<Task<bool>>? RequestConfirmClearIndex { get; set; }

    public MemorySettingsViewModel(RecallIndexingService? recallIndexing = null, IToastService? toasts = null)
    {
        _recallIndexing = recallIndexing;
        _toasts = toasts;
    }

    public void ReloadFrom(AppSettings settings)
    {
        MemoryFeatureEnabled = settings.Memory.Enabled;
        MemoryInjectIntoContext = settings.Memory.InjectMemoriesIntoContext;
        MemoryImportanceThreshold = settings.Memory.AutoSummarizeImportanceThreshold;
        MemoryInjectionTokenBudget = settings.Memory.InjectionTokenBudget;
        MemoryAutoArchiveDays = settings.Memory.AutoArchiveAfterDays;
        ConsumeAgentLessonsInChat = settings.Memory.ConsumeAgentLessonsInChat;
        RecallIndexingEnabled = settings.Memory.RecallIndexingEnabled;
        RecallInjectionEnabled = settings.Memory.RecallInjectionEnabled;
        RecallInjectionTokenBudget = settings.Memory.RecallInjectionTokenBudget;
        _ = RefreshRecallSizeAsync();
    }

    public void ApplyTo(AppSettings settings)
    {
        settings.Memory.Enabled = MemoryFeatureEnabled;
        settings.Memory.InjectMemoriesIntoContext = MemoryInjectIntoContext;
        settings.Memory.AutoSummarizeImportanceThreshold = MemoryImportanceThreshold;
        settings.Memory.InjectionTokenBudget = MemoryInjectionTokenBudget;
        settings.Memory.AutoArchiveAfterDays = MemoryAutoArchiveDays;
        settings.Memory.ConsumeAgentLessonsInChat = ConsumeAgentLessonsInChat;
        settings.Memory.RecallIndexingEnabled = RecallIndexingEnabled;
        settings.Memory.RecallInjectionEnabled = RecallInjectionEnabled;
        settings.Memory.RecallInjectionTokenBudget = RecallInjectionTokenBudget;
    }

    public async Task RefreshRecallSizeAsync()
    {
        if (_recallIndexing is null) return;
        try
        {
            var (count, bytes) = await _recallIndexing.GetSizeAsync();
            RecallEntryCount = count;
            RecallIndexSizeDisplay = FormatBytes(bytes);
        }
        catch { /* best-effort display only */ }
    }

    /// <summary>2.0's destructive control: genuinely deletes every row and vacuums.
    /// Touches recall.db only - the confirmation dialog states this plainly.</summary>
    [RelayCommand]
    public async Task ClearRecallIndexAsync()
    {
        if (_recallIndexing is null) return;
        var confirmed = RequestConfirmClearIndex is null || await RequestConfirmClearIndex();
        if (!confirmed) return;

        IsClearingRecallIndex = true;
        try
        {
            var removed = await _recallIndexing.ClearIndexAsync();
            await RefreshRecallSizeAsync();
            _toasts?.Show("Recall index cleared", $"Removed {removed} indexed row(s). No conversation, memory, task or dataset was touched.", ToastKind.Success);
        }
        finally
        {
            IsClearingRecallIndex = false;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }
}
