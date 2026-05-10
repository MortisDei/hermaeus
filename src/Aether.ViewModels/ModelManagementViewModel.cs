using System.Collections.ObjectModel;
using Aether.Core.Models;
using Aether.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

public partial class ModelManagementViewModel : ObservableObject
{
    private readonly ILlmService _llm;

    public ObservableCollection<LlmModel> Models { get; } = [];

    [ObservableProperty] private bool   _isLoading;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool   _isError;

    public ModelManagementViewModel(ILlmService llm) => _llm = llm;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsLoading = true; StatusMessage = string.Empty; IsError = false;
        try
        {
            var models = await _llm.GetModelsAsync();
            Models.Clear();
            foreach (var m in models) Models.Add(m);
            StatusMessage = models.Count == 0
                ? "No models reported by the running backends"
                : $"{models.Count} model(s) loaded";
        }
        catch (Exception ex) { StatusMessage = ex.Message; IsError = true; }
        finally { IsLoading = false; }
    }
}
