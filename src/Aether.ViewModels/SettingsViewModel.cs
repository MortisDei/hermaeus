using Aether.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _svc;

    [ObservableProperty] private string _llamaCppBaseUrl      = "http://localhost:8080";
    [ObservableProperty] private bool   _llamaCppEnabled      = true;
    [ObservableProperty] private string _openAiBaseUrl        = "https://api.openai.com";
    [ObservableProperty] private string _openAiApiKey         = string.Empty;
    [ObservableProperty] private bool   _openAiEnabled;
    [ObservableProperty] private string _embeddingModel       = "nomic-embed-text";
    [ObservableProperty] private string _defaultSystemPrompt  = string.Empty;
    [ObservableProperty] private double _temperature          = 0.7;
    [ObservableProperty] private int    _maxTokens            = 4096;
    [ObservableProperty] private double _fontSize             = 14;
    [ObservableProperty] private string _selectedTheme        = "System";
    [ObservableProperty] private bool   _ctrlEnterToSend;
    [ObservableProperty] private bool   _isSaved;

    public string[] Themes { get; } = ["System", "Dark", "Light"];

    public SettingsViewModel(ISettingsService svc) { _svc = svc; Reload(); }

    public void Reload()
    {
        var s = _svc.Settings;
        LlamaCppBaseUrl     = s.LlamaCppBaseUrl;
        LlamaCppEnabled     = s.LlamaCppEnabled;
        OpenAiBaseUrl       = s.OpenAiBaseUrl;
        OpenAiApiKey        = s.OpenAiApiKey;
        OpenAiEnabled       = s.OpenAiEnabled;
        EmbeddingModel      = s.EmbeddingModel;
        DefaultSystemPrompt = s.DefaultSystemPrompt;
        Temperature         = s.Temperature;
        MaxTokens           = s.MaxTokens;
        FontSize            = s.FontSize;
        SelectedTheme       = s.Theme;
        CtrlEnterToSend     = s.CtrlEnterToSend;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var s = _svc.Settings;
        s.LlamaCppBaseUrl     = LlamaCppBaseUrl;
        s.LlamaCppEnabled     = LlamaCppEnabled;
        s.OpenAiBaseUrl       = OpenAiBaseUrl;
        s.OpenAiApiKey        = OpenAiApiKey;
        s.OpenAiEnabled       = OpenAiEnabled;
        s.EmbeddingModel      = EmbeddingModel;
        s.DefaultSystemPrompt = DefaultSystemPrompt;
        s.Temperature         = Temperature;
        s.MaxTokens           = MaxTokens;
        s.FontSize            = FontSize;
        s.Theme               = SelectedTheme;
        s.CtrlEnterToSend     = CtrlEnterToSend;
        await _svc.SaveAsync();
        IsSaved = true;
        await Task.Delay(2000);
        IsSaved = false;
    }

    [RelayCommand] private void Reset() => Reload();
}
