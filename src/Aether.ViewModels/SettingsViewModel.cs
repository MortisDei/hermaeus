using System.Collections.ObjectModel;
using Aether.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _svc;
    private readonly ITtsService _tts;
    private readonly IToastService _toasts;

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
    [ObservableProperty] private string _dataRootDirectory = string.Empty;
    [ObservableProperty] private bool   _ttsEnabled = true;
    [ObservableProperty] private string _ttsServiceUrl = "http://127.0.0.1:8020";
    [ObservableProperty] private string _ttsSpeaker = string.Empty;
    [ObservableProperty] private string _settingsError = string.Empty;

    public string[] Themes { get; } = ["System", "Dark", "Light"];
    public ObservableCollection<string> TtsVoices { get; } = ["default"];
    public Action? RequestDataRootPicker { get; set; }

    public SettingsViewModel(ISettingsService svc, ITtsService tts, IToastService toasts)
    {
        _svc = svc;
        _tts = tts;
        _toasts = toasts;
        Reload();
    }

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
        DataRootDirectory   = s.DataRootDirectory;
        TtsEnabled          = s.TtsEnabled;
        TtsServiceUrl       = s.TtsServiceUrl;
        TtsSpeaker          = s.TtsSpeaker;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var s = _svc.Settings;
        var previousDataRoot = s.DataRootDirectory;
        SettingsError = string.Empty;
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
        s.DataRootDirectory   = DataRootDirectory.Trim();
        s.TtsEnabled          = TtsEnabled;
        s.TtsServiceUrl       = TtsServiceUrl;
        s.TtsSpeaker          = TtsSpeaker;
        try
        {
            await _svc.SaveAsync(previousDataRoot);
        }
        catch (Exception ex)
        {
            s.DataRootDirectory = previousDataRoot;
            DataRootDirectory = previousDataRoot;
            SettingsError = ex.Message;
            _toasts.Show("Settings not saved", ex.Message, ToastKind.Error);
            return;
        }

        IsSaved = true;
        _toasts.Show("Settings saved", "Aether settings were updated.", ToastKind.Success);
        await Task.Delay(2000);
        IsSaved = false;
    }

    [RelayCommand]
    private void BrowseDataRoot() => RequestDataRootPicker?.Invoke();

    [RelayCommand]
    private async Task RefreshTtsVoicesAsync()
    {
        SettingsError = string.Empty;
        try
        {
            var voices = await _tts.GetVoicesAsync();
            TtsVoices.Clear();
            foreach (var voice in voices)
                TtsVoices.Add(voice);

            if (!string.IsNullOrWhiteSpace(TtsSpeaker) && !TtsVoices.Contains(TtsSpeaker))
                TtsVoices.Add(TtsSpeaker);
        }
        catch (Exception ex)
        {
            SettingsError = $"Could not load XTTS voices: {ex.Message}";
            _toasts.Show("XTTS voices unavailable", ex.Message, ToastKind.Warning);
        }
    }

    [RelayCommand] private void Reset() => Reload();
}
