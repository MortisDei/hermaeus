using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Aether.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Aether.ViewModels;

public partial class DraftPatchDiffViewModel : ObservableObject
{
    private readonly IPatchDiffService _diffService;

    public ObservableCollection<DiffLine> Lines { get; } = new();

    public DraftPatchDiffViewModel(IPatchDiffService diffService)
    {
        _diffService = diffService;
    }

    public Task LoadAsync(string oldText, string newText)
    {
        Lines.Clear();
        var diffs = _diffService.ComputeLineDiffs(oldText ?? string.Empty, newText ?? string.Empty);
        foreach (var d in diffs)
            Lines.Add(d);
        return Task.CompletedTask;
    }
}
