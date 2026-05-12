using System.Collections.ObjectModel;
using Aether.Core.Services;
using Aether.Services;
using Aether.Services.ProcessManagement;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _svc;
    private readonly ITtsService _tts;
    private readonly IToastService _toasts;
    private readonly IBackupService _backups;
    private readonly ISecretStore _secrets;
    private readonly XttsProcessManager _xttsProcess;
    private readonly SynchronizationContext? _sync;

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
    [ObservableProperty] private string _dataMigrationPreview = string.Empty;
    [ObservableProperty] private string _localAiAssetsRoot = string.Empty;
    [ObservableProperty] private string _localAiAssetsStatus = "Choose a local AI assets folder first.";
    [ObservableProperty] private string _ragRerankerModelPath = string.Empty;
    [ObservableProperty] private string _backupDirectory = string.Empty;
    [ObservableProperty] private string _restoreBackupPath = string.Empty;
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
    [ObservableProperty] private bool   _enableTrayIcon = true;
    [ObservableProperty] private bool   _minimizeToTray = true;
    [ObservableProperty] private bool   _enableLocalHotkeys = true;
    [ObservableProperty] private bool   _enableGlobalHotkeys;
    [ObservableProperty] private string _globalHotkeyStatus = "System-wide hotkeys are off.";
    [ObservableProperty] private string _ttsStatus = "Stopped";
    [ObservableProperty] private string _settingsError = string.Empty;

    public string[] Themes { get; } = ["System", "Dark", "Light"];
    public string[] TtsDevices { get; } = ["cpu", "auto", "cuda"];
    public ObservableCollection<string> TtsVoices { get; } = ["default"];
    public Action? RequestDataRootPicker { get; set; }
    public Action? RequestLocalAiAssetsRootPicker { get; set; }
    public Action? RequestBackupDirectoryPicker { get; set; }
    public Action? RequestRestoreBackupPicker { get; set; }
    public Action? RequestTtsScriptPicker { get; set; }
    public Action? RequestTtsOutputPicker { get; set; }
    public Action? RequestTtsVoiceDirectoryPicker { get; set; }
    public Action? RequestTtsVoiceSamplePicker { get; set; }

    public bool IsTtsRunning => _xttsProcess.IsRunning;

    public SettingsViewModel(
        ISettingsService svc,
        ITtsService tts,
        IToastService toasts,
        IBackupService backups,
        ISecretStore secrets,
        XttsProcessManager xttsProcess)
    {
        _svc = svc;
        _tts = tts;
        _toasts = toasts;
        _backups = backups;
        _secrets = secrets;
        _xttsProcess = xttsProcess;
        _sync = SynchronizationContext.Current;
        _xttsProcess.StatusChanged += OnXttsStatusChanged;
        Reload();
    }

    private void OnXttsStatusChanged()
    {
        if (_sync is not null)
            _sync.Post(_ => ApplyXttsStatus(), null);
        else
            ApplyXttsStatus();
    }

    private void ApplyXttsStatus()
    {
        TtsStatus = _xttsProcess.StatusLabel;
        OnPropertyChanged(nameof(IsTtsRunning));
        StartTtsCommand.NotifyCanExecuteChanged();
        StopTtsCommand.NotifyCanExecuteChanged();
    }

    public void Reload()
    {
        var s = _svc.Settings;
        LlamaCppBaseUrl     = s.LlamaCppBaseUrl;
        LlamaCppEnabled     = s.LlamaCppEnabled;
        OpenAiBaseUrl       = s.OpenAiBaseUrl;
        OpenAiApiKey        = _secrets.IsReference(s.OpenAiApiKey) ? string.Empty : s.OpenAiApiKey;
        OpenAiEnabled       = s.OpenAiEnabled;
        EmbeddingModel      = s.EmbeddingModel;
        DefaultSystemPrompt = s.DefaultSystemPrompt;
        Temperature         = s.Temperature;
        MaxTokens           = s.MaxTokens;
        FontSize            = s.FontSize;
        SelectedTheme       = s.Theme;
        CtrlEnterToSend     = s.CtrlEnterToSend;
        DataRootDirectory   = s.DataRootDirectory;
        LocalAiAssetsRoot   = s.LocalAiAssetsRoot;
        RagRerankerModelPath = s.RagRerankerModelPath;
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
        EnableTrayIcon      = s.EnableTrayIcon;
        MinimizeToTray      = s.MinimizeToTray;
        EnableLocalHotkeys  = s.EnableLocalHotkeys;
        EnableGlobalHotkeys = s.EnableGlobalHotkeys;
        TtsStatus           = _xttsProcess.StatusLabel;
        OnPropertyChanged(nameof(IsTtsRunning));
        UpdateMigrationPreview();
        UpdateLocalAiAssetsStatus();
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
        if (!string.IsNullOrWhiteSpace(OpenAiApiKey))
            s.OpenAiApiKey = await _secrets.StoreAsync("openai-api-key", OpenAiApiKey.Trim());
        s.OpenAiEnabled       = OpenAiEnabled;
        s.EmbeddingModel      = EmbeddingModel;
        s.DefaultSystemPrompt = DefaultSystemPrompt;
        s.Temperature         = Temperature;
        s.MaxTokens           = MaxTokens;
        s.FontSize            = FontSize;
        s.Theme               = SelectedTheme;
        s.CtrlEnterToSend     = CtrlEnterToSend;
        s.DataRootDirectory   = DataRootDirectory.Trim();
        s.LocalAiAssetsRoot   = LocalAiAssetsRoot.Trim();
        s.RagRerankerModelPath = RagRerankerModelPath.Trim();
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
        s.EnableTrayIcon      = EnableTrayIcon;
        s.MinimizeToTray      = MinimizeToTray;
        s.EnableLocalHotkeys  = EnableLocalHotkeys;
        s.EnableGlobalHotkeys = EnableGlobalHotkeys;
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
    private void BrowseLocalAiAssetsRoot() => RequestLocalAiAssetsRootPicker?.Invoke();

    [RelayCommand]
    private void ApplyLocalAiAssets()
    {
        SettingsError = string.Empty;
        var layout = LocalAiAssetLocator.Detect(LocalAiAssetsRoot);
        if (string.IsNullOrWhiteSpace(layout.Root) || !Directory.Exists(layout.Root))
        {
            SettingsError = "Choose an existing local AI assets folder first.";
            _toasts.Show("AI assets not applied", SettingsError, ToastKind.Warning);
            return;
        }

        if (!string.IsNullOrWhiteSpace(layout.TtsScriptPath)) TtsScriptPath = layout.TtsScriptPath;
        if (!string.IsNullOrWhiteSpace(layout.TtsPythonPath)) TtsPythonPath = layout.TtsPythonPath;
        if (!string.IsNullOrWhiteSpace(layout.TtsVoiceDirectory)) TtsVoiceDirectory = layout.TtsVoiceDirectory;
        if (!string.IsNullOrWhiteSpace(layout.TtsOutputDirectory)) TtsOutputDirectory = layout.TtsOutputDirectory;
        if (!string.IsNullOrWhiteSpace(layout.RerankerDirectory)) RagRerankerModelPath = layout.RerankerDirectory;
        UpdateLocalAiAssetsStatus();
        _toasts.Show("AI assets applied", layout.Summary, ToastKind.Success, 5500);
    }

    [RelayCommand]
    private void BrowseBackupDirectory() => RequestBackupDirectoryPicker?.Invoke();

    [RelayCommand]
    private void BrowseRestoreBackup() => RequestRestoreBackupPicker?.Invoke();

    [RelayCommand]
    private async Task BackupDataAsync()
    {
        try
        {
            var target = string.IsNullOrWhiteSpace(BackupDirectory)
                ? ResolveDataRoot()
                : BackupDirectory.Trim();
            var result = await _backups.BackupAsync(target);
            _toasts.Show("Backup complete", $"{result.FilesIncluded} file(s) written to {result.Path}", ToastKind.Success, 7000);
        }
        catch (Exception ex)
        {
            SettingsError = ex.Message;
            _toasts.Show("Backup failed", ex.Message, ToastKind.Error, 7000);
        }
    }

    [RelayCommand]
    private async Task RestoreDataAsync()
    {
        try
        {
            await _backups.RestoreAsync(RestoreBackupPath.Trim());
            _toasts.Show("Restore complete", "Restart Aether to load restored data.", ToastKind.Success, 7000);
        }
        catch (Exception ex)
        {
            SettingsError = ex.Message;
            _toasts.Show("Restore refused", ex.Message, ToastKind.Error, 7000);
        }
    }

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
        if (string.IsNullOrWhiteSpace(TtsScriptPath))
        {
            RequestTtsScriptPicker?.Invoke();
            if (string.IsNullOrWhiteSpace(TtsScriptPath))
            {
                SettingsError = "Choose the XTTS API server script before starting XTTS.";
                _toasts.Show("XTTS path needed", SettingsError, ToastKind.Warning);
                return;
            }
        }

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

    public void Shutdown() => _xttsProcess.Stop();

    private bool CanStartTts() => !IsTtsRunning;
    private bool CanStopTts() => IsTtsRunning;

    partial void OnDataRootDirectoryChanged(string value) => UpdateMigrationPreview();
    partial void OnLocalAiAssetsRootChanged(string value) => UpdateLocalAiAssetsStatus();

    private void UpdateMigrationPreview()
    {
        var plan = _svc.PreviewDataRootMigration(_svc.Settings.DataRootDirectory, DataRootDirectory);
        DataMigrationPreview = plan.Conflicts.Count > 0
            ? $"Move blocked: {plan.Conflicts.Count} existing database file(s) in target."
            : plan.WillMove
                ? $"Save will move {plan.FilesToMove} database file(s) to {plan.CurrentDataRoot}."
                : "No data move needed.";
    }

    private string ResolveDataRoot()
    {
        var configured = _svc.Settings.DataRootDirectory?.Trim();
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aether")
            : Path.GetFullPath(configured);
    }

    private void UpdateLocalAiAssetsStatus()
    {
        LocalAiAssetsStatus = LocalAiAssetLocator.Detect(LocalAiAssetsRoot).Summary;
    }
}
