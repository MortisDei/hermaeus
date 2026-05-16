using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Aether.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

public partial class DraftPatchDiffViewModel : ObservableObject
{
    private readonly IPatchDiffService _diffService;

    public ObservableCollection<DiffLine> Lines { get; } = new();

    public string RelativePath { get; private set; } = string.Empty;
    public string OldContent { get; private set; } = string.Empty;
    public string NewContent { get; private set; } = string.Empty;

    public Func<bool, Task>? DecisionCallback { get; set; }

    public DraftPatchDiffViewModel(IPatchDiffService diffService)
    {
        _diffService = diffService;
    }

    public Task LoadAsync(string relativePath, string oldText, string newText)
    {
        RelativePath = relativePath;
        OldContent = oldText ?? string.Empty;
        NewContent = newText ?? string.Empty;
        Lines.Clear();
        var diffs = _diffService.ComputeLineDiffs(OldContent, NewContent);
        foreach (var d in diffs)
            Lines.Add(d);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (DecisionCallback is not null)
            await DecisionCallback(true);
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        if (DecisionCallback is not null)
            await DecisionCallback(false);
    }
}
