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
    [ObservableProperty] private string _pullModelName = string.Empty;
    [ObservableProperty] private bool   _isPulling;
    [ObservableProperty] private string _pullStatus    = string.Empty;
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
            StatusMessage = $"{models.Count} model(s) loaded";
        }
        catch (Exception ex) { StatusMessage = ex.Message; IsError = true; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task PullAsync()
    {
        if (string.IsNullOrWhiteSpace(PullModelName)) return;
        IsPulling = true; PullStatus = "Starting..."; StatusMessage = string.Empty; IsError = false;
        try
        {
            await _llm.PullModelAsync(PullModelName.Trim(), new Progress<string>(s => PullStatus = s));
            PullModelName = string.Empty;
            await RefreshAsync();
        }
        catch (Exception ex) { StatusMessage = ex.Message; IsError = true; }
        finally { IsPulling = false; PullStatus = string.Empty; }
    }

    [RelayCommand]
    private async Task DeleteAsync(LlmModel? model)
    {
        if (model is null) return;
        try { await _llm.DeleteModelAsync(model.Id); Models.Remove(model); }
        catch (Exception ex) { StatusMessage = ex.Message; IsError = true; }
    }
}
