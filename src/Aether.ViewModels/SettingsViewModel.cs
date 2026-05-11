using System.Collections.ObjectModel;
using Aether.Core.Services;
using Aether.Services.ProcessManagement;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _svc;
    private readonly ITtsService _tts;
    private readonly IToastService _toasts;
    private readonly XttsProcessManager _xttsProcess;

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
    [ObservableProperty] private string _ttsPythonPath = string.Empty;
    [ObservableProperty] private string _ttsScriptPath = string.Empty;
    [ObservableProperty] private string _ttsOutputDirectory = string.Empty;
    [ObservableProperty] private string _ttsVoiceDirectory = string.Empty;
    [ObservableProperty] private string _ttsDevice = "cpu";
    [ObservableProperty] private string _ttsModelVersion = "2.0.3";
    [ObservableProperty] private bool   _ttsPreload;
    [ObservableProperty] private string _ttsPreviewText = "Aether voice preview is ready.";
    [ObservableProperty] private string _ttsCloneDisplayName = string.Empty;
    [ObservableProperty] private bool   _startMinimized;
    [ObservableProperty] private bool   _showQuickChat;
    [ObservableProperty] private string _ttsStatus = "Stopped";
    [ObservableProperty] private string _settingsError = string.Empty;

    public string[] Themes { get; } = ["System", "Dark", "Light"];
    public string[] TtsDevices { get; } = ["cpu", "auto", "cuda"];
    public ObservableCollection<string> TtsVoices { get; } = ["default"];
    public Action? RequestDataRootPicker { get; set; }
    public Action? RequestTtsScriptPicker { get; set; }
    public Action? RequestTtsOutputPicker { get; set; }
    public Action? RequestTtsVoiceDirectoryPicker { get; set; }
    public Action? RequestTtsVoiceSamplePicker { get; set; }

    public bool IsTtsRunning => _xttsProcess.IsRunning;

    public SettingsViewModel(ISettingsService svc, ITtsService tts, IToastService toasts, XttsProcessManager xttsProcess)
    {
        _svc = svc;
        _tts = tts;
        _toasts = toasts;
        _xttsProcess = xttsProcess;
        _xttsProcess.StatusChanged += () =>
        {
            TtsStatus = _xttsProcess.StatusLabel;
            OnPropertyChanged(nameof(IsTtsRunning));
            StartTtsCommand.NotifyCanExecuteChanged();
            StopTtsCommand.NotifyCanExecuteChanged();
        };
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
        TtsPythonPath       = s.TtsPythonPath;
        TtsScriptPath       = s.TtsScriptPath;
        TtsOutputDirectory  = s.TtsOutputDirectory;
        TtsVoiceDirectory   = s.TtsVoiceDirectory;
        TtsDevice           = s.TtsDevice;
        TtsModelVersion     = s.TtsModelVersion;
        TtsPreload          = s.TtsPreload;
        StartMinimized      = s.StartMinimized;
        ShowQuickChat       = s.ShowQuickChat;
        TtsStatus           = _xttsProcess.StatusLabel;
        OnPropertyChanged(nameof(IsTtsRunning));
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
        s.TtsPythonPath       = TtsPythonPath.Trim();
        s.TtsScriptPath       = TtsScriptPath.Trim();
        s.TtsOutputDirectory  = TtsOutputDirectory.Trim();
        s.TtsVoiceDirectory   = TtsVoiceDirectory.Trim();
        s.TtsDevice           = TtsDevice;
        s.TtsModelVersion     = TtsModelVersion.Trim();
        s.TtsPreload          = TtsPreload;
        s.StartMinimized      = StartMinimized;
        s.ShowQuickChat       = ShowQuickChat;
        try
        {
            var result = await _svc.SaveAsync(previousDataRoot);
            if (result.DataMigrated)
            {
                var message = $"Moved {result.FilesMoved} database file(s) to {result.CurrentDataRoot}. Backup: {result.BackupDirectory}";
                _toasts.Show("Aether data moved", message, ToastKind.Success, 7000);
            }
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
    private void BrowseTtsScript() => RequestTtsScriptPicker?.Invoke();

    [RelayCommand]
    private void BrowseTtsOutput() => RequestTtsOutputPicker?.Invoke();

    [RelayCommand]
    private void BrowseTtsVoiceDirectory() => RequestTtsVoiceDirectoryPicker?.Invoke();

    [RelayCommand]
    private void ImportTtsVoiceSample() => RequestTtsVoiceSamplePicker?.Invoke();

    [RelayCommand(CanExecute = nameof(CanStartTts))]
    private async Task StartTtsAsync()
    {
        await SaveAsync();
        if (!string.IsNullOrWhiteSpace(SettingsError)) return;

        try
        {
            await _xttsProcess.StartAsync(_svc.Settings);
            _toasts.Show("XTTS v2 started", $"Listening at {TtsServiceUrl}", ToastKind.Success);
            await RefreshTtsVoicesAsync();
        }
        catch (Exception ex)
        {
            SettingsError = ex.Message;
            _toasts.Show("XTTS v2 failed", ex.Message, ToastKind.Error, 7000);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStopTts))]
    private void StopTts()
    {
        _xttsProcess.Stop();
        _toasts.Show("XTTS v2 stopped", "The local voice server was stopped.", ToastKind.Info);
    }

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

    [RelayCommand]
    private async Task PreviewTtsVoiceAsync()
    {
        await SaveAsync();
        if (!string.IsNullOrWhiteSpace(SettingsError)) return;

        try
        {
            await _tts.PreviewVoiceAsync(TtsSpeaker, TtsPreviewText);
            _toasts.Show("Voice preview played", string.IsNullOrWhiteSpace(TtsSpeaker) ? "default" : TtsSpeaker, ToastKind.Success);
        }
        catch (Exception ex)
        {
            SettingsError = ex.Message;
            _toasts.Show("Voice preview failed", ex.Message, ToastKind.Error, 7000);
        }
    }

    public async Task ImportTtsVoiceSampleAsync(string sourcePath)
    {
        try
        {
            var imported = await _tts.ImportVoiceSampleAsync(sourcePath, TtsCloneDisplayName);
            TtsSpeaker = imported;
            await RefreshTtsVoicesAsync();
            _toasts.Show("Voice imported", Path.GetFileName(imported), ToastKind.Success);
        }
        catch (Exception ex)
        {
            SettingsError = ex.Message;
            _toasts.Show("Voice import failed", ex.Message, ToastKind.Error, 7000);
        }
    }

    [RelayCommand] private void Reset() => Reload();

    private bool CanStartTts() => !IsTtsRunning;
    private bool CanStopTts() => IsTtsRunning;
}
