using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Security.Cryptography;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Hermaeus.ViewModels;

public partial class MemorySettingsViewModel : ObservableObject
{
    [ObservableProperty] private bool _memoryFeatureEnabled;
    [ObservableProperty] private bool _memoryInjectIntoContext;
    [ObservableProperty] private double _memoryImportanceThreshold = 0.6;
    [ObservableProperty] private int _memoryInjectionTokenBudget = 500;
    [ObservableProperty] private int _memoryAutoArchiveDays = 90;
    [ObservableProperty] private bool _consumeAgentLessonsInChat;

    public void ReloadFrom(AppSettings settings)
    {
        MemoryFeatureEnabled = settings.Memory.Enabled;
        MemoryInjectIntoContext = settings.Memory.InjectMemoriesIntoContext;
        MemoryImportanceThreshold = settings.Memory.AutoSummarizeImportanceThreshold;
        MemoryInjectionTokenBudget = settings.Memory.InjectionTokenBudget;
        MemoryAutoArchiveDays = settings.Memory.AutoArchiveAfterDays;
        ConsumeAgentLessonsInChat = settings.Memory.ConsumeAgentLessonsInChat;
    }

    public void ApplyTo(AppSettings settings)
    {
        settings.Memory.Enabled = MemoryFeatureEnabled;
        settings.Memory.InjectMemoriesIntoContext = MemoryInjectIntoContext;
        settings.Memory.AutoSummarizeImportanceThreshold = MemoryImportanceThreshold;
        settings.Memory.InjectionTokenBudget = MemoryInjectionTokenBudget;
        settings.Memory.AutoArchiveAfterDays = MemoryAutoArchiveDays;
        settings.Memory.ConsumeAgentLessonsInChat = ConsumeAgentLessonsInChat;
    }
}
