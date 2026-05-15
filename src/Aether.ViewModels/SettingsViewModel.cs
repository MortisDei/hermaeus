using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Aether.Core.Models;
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
    private readonly IVoiceProviderRegistry _voiceProviderRegistry;
    private readonly IToastService _toasts;
    private readonly IBackupService _backups;
    private readonly ISecretStore _secrets;
    private readonly XttsProcessManager _xttsProcess;
    private readonly KokoroProcessManager _kokoroProcess;
    private readonly ILocalAiSetupService _localAiSetup;
    private readonly ITrustService _trust;

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
    [ObservableProperty] private bool   _startMinimized;
    [ObservableProperty] private bool   _showQuickChat;
    [ObservableProperty] private bool   _enableTrayIcon = true;
    [ObservableProperty] private bool   _minimizeToTray = true;
    [ObservableProperty] private bool   _enableLocalHotkeys = true;
    [ObservableProperty] private bool   _enableGlobalHotkeys;
    [ObservableProperty] private string _globalHotkeyStatus = "System-wide hotkeys are off.";
    [ObservableProperty] private string _settingsError = string.Empty;    [ObservableProperty] private bool _memoryFeatureEnabled;
    [ObservableProperty] private bool _memoryInjectIntoContext;
    [ObservableProperty] private double _memoryImportanceThreshold = 0.6;
    [ObservableProperty] private int _memoryInjectionTokenBudget = 500;
    [ObservableProperty] private bool _memoryEncryptionEnabled;
    [ObservableProperty] private int _memoryAutoArchiveDays = 90;    [ObservableProperty] private bool _localAiSetupBusy;
    [ObservableProperty] private string _localAiSetupLog = string.Empty;
    [ObservableProperty] private string _localAiSetupSummary = "Scan a local AI folder to see readiness.";
    [ObservableProperty] private bool _localAiInstallPlanVisible;
    [ObservableProperty] private string _localAiInstallPlanTitle = "Install plan";
    [ObservableProperty] private string _localAiInstallPlanSummary = string.Empty;
    [ObservableProperty] private string _localAiInstallPlanRisk = string.Empty;
    [ObservableProperty] private string _localAiInstallPlanRiskNotes = string.Empty;
    [ObservableProperty] private string _localAiInstallPlanActionId = string.Empty;
    [ObservableProperty] private bool _trustScanBusy;
    [ObservableProperty] private string _trustSummary = "Run a trust scan to review configured local tools.";
    [ObservableProperty] private string _trustLastScanned = string.Empty;

    public TtsSettingsViewModel Tts { get; }

    public string[] Themes { get; } = ["System", "Dark", "Light"];
    public ObservableCollection<LocalAiReadinessItem> LocalAiReadinessItems { get; } = [];
    public ObservableCollection<LocalAiSetupAction> LocalAiSetupActions { get; } = [];
    public ObservableCollection<string> LocalAiInstallPlanCreates { get; } = [];
    public ObservableCollection<string> LocalAiInstallPlanInstalls { get; } = [];
    public ObservableCollection<TrustItem> TrustItems { get; } = [];

    public Action? RequestDataRootPicker { get; set; }
    public Action? RequestLocalAiAssetsRootPicker { get; set; }
    public Action? RequestBackupDirectoryPicker { get; set; }
    public Action? RequestRestoreBackupPicker { get; set; }
    public Action? RequestTtsPythonPicker { get; set; }
    public Action? RequestTtsScriptPicker { get; set; }
    public Action? RequestTtsModelDirectoryPicker { get; set; }
    public Action? RequestTtsOutputPicker { get; set; }
    public Action? RequestTtsVoiceDirectoryPicker { get; set; }
    public Action? RequestTtsVoiceSamplePicker { get; set; }
    public Action<string>? RequestCopyToClipboard { get; set; }

    public SettingsViewModel(
        ISettingsService svc,
        ITtsService tts,
        IVoiceProviderRegistry voiceProviderRegistry,
        IToastService toasts,
        IBackupService backups,
        ISecretStore secrets,
        XttsProcessManager xttsProcess,
        KokoroProcessManager kokoroProcess,
        ILocalAiSetupService localAiSetup,
        ITrustService trust)
    {
        _svc = svc;
        _tts = tts;
        _voiceProviderRegistry = voiceProviderRegistry;
        _toasts = toasts;
        _backups = backups;
        _secrets = secrets;
        _xttsProcess = xttsProcess;
        _kokoroProcess = kokoroProcess;
        _localAiSetup = localAiSetup;
        _trust = trust;

        Tts = new TtsSettingsViewModel(_tts, _voiceProviderRegistry, _toasts, _xttsProcess, _kokoroProcess, _secrets, _svc);
        Reload();
    }

    // When the app wants the settings view to re-run the setup wizard, this action will be invoked
    public Action? RequestShowSetupWizard { get; set; }

    [RelayCommand]
    private async Task ReRunSetupWizardAsync()
    {
        // Mark wizard as not completed and persist
        _svc.Settings.SetupWizardCompleted = false;
        await _svc.SaveAsync();
        RequestShowSetupWizard?.Invoke();
    }

    public void Reload()
    {
        var s = _svc.Settings;
        LlamaCppBaseUrl     = s.Llm.LlamaCppBaseUrl;
        LlamaCppEnabled     = s.Llm.LlamaCppEnabled;
        OpenAiBaseUrl       = s.Llm.OpenAiBaseUrl;
        OpenAiApiKey        = _secrets.IsReference(s.Llm.OpenAiApiKey) ? string.Empty : s.Llm.OpenAiApiKey;
        OpenAiEnabled       = s.Llm.OpenAiEnabled;
        EmbeddingModel      = s.Rag.EmbeddingModel;
        DefaultSystemPrompt = s.Llm.DefaultSystemPrompt;
        Temperature         = s.Llm.Temperature;
        MaxTokens           = s.Llm.MaxTokens;
        FontSize            = s.Ui.FontSize;
        SelectedTheme       = s.Ui.Theme;
        CtrlEnterToSend     = s.Ui.CtrlEnterToSend;
        DataRootDirectory   = s.DataManagement.DataRootDirectory;
        LocalAiAssetsRoot   = s.DataManagement.LocalAiAssetsRoot;
        RagRerankerModelPath = s.Rag.RerankerModelPath;

        Tts.ReloadFrom(s);

        StartMinimized      = s.Ui.StartMinimized;
        ShowQuickChat       = s.Ui.ShowQuickChat;
        EnableTrayIcon      = s.Ui.EnableTrayIcon;
        MinimizeToTray      = s.Ui.MinimizeToTray;
        EnableLocalHotkeys  = s.Ui.EnableLocalHotkeys;
        EnableGlobalHotkeys = s.Ui.EnableGlobalHotkeys;
        MemoryFeatureEnabled = s.Memory.Enabled;
        MemoryInjectIntoContext = s.Memory.InjectMemoriesIntoContext;
        MemoryImportanceThreshold = s.Memory.AutoSummarizeImportanceThreshold;
        MemoryInjectionTokenBudget = s.Memory.InjectionTokenBudget;
        MemoryEncryptionEnabled = s.Memory.EncryptMemoriesAtRest;
        MemoryAutoArchiveDays = s.Memory.AutoArchiveAfterDays;
        UpdateMigrationPreview();
        UpdateLocalAiAssetsStatus();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var s = _svc.Settings;
        var previousDataRoot = s.DataManagement.DataRootDirectory;
        SettingsError = string.Empty;

        s.Llm.LlamaCppBaseUrl     = LlamaCppBaseUrl;
        s.Llm.LlamaCppEnabled     = LlamaCppEnabled;
        s.Llm.OpenAiBaseUrl       = OpenAiBaseUrl;
        if (!string.IsNullOrWhiteSpace(OpenAiApiKey))
            s.Llm.OpenAiApiKey = await _secrets.StoreAsync("openai-api-key", OpenAiApiKey.Trim());
        s.Llm.OpenAiEnabled       = OpenAiEnabled;
        s.Rag.EmbeddingModel      = EmbeddingModel;
        s.Llm.DefaultSystemPrompt = DefaultSystemPrompt;
        s.Llm.Temperature         = Temperature;
        s.Llm.MaxTokens           = MaxTokens;
        s.Ui.FontSize            = FontSize;
        s.Ui.Theme               = SelectedTheme;
        s.Ui.CtrlEnterToSend     = CtrlEnterToSend;

        s.DataManagement.DataRootDirectory   = DataRootDirectory.Trim();
        s.DataManagement.LocalAiAssetsRoot   = LocalAiAssetsRoot.Trim();
        s.Rag.RerankerModelPath = RagRerankerModelPath.Trim();

        s.Tts.Enabled = Tts.TtsEnabled;
        s.Tts.ServiceUrl = Tts.TtsServiceUrl;
        s.Tts.Speaker = Tts.TtsSpeaker;
        s.Tts.PythonPath = Tts.TtsPythonPath.Trim();
        s.Tts.ScriptPath = Tts.TtsScriptPath.Trim();
        s.Tts.ModelDirectory = Tts.TtsModelDirectory.Trim();
        s.Tts.OutputDirectory = Tts.TtsOutputDirectory.Trim();
        s.Tts.VoiceDirectory = Tts.TtsVoiceDirectory.Trim();
        s.Tts.Device = Tts.TtsDevice;
        s.Tts.ModelVersion = Tts.TtsModelVersion.Trim();
        s.Tts.Speed = Tts.TtsSpeed;
        s.Tts.Preload = Tts.TtsPreload;
        s.Tts.VoiceProvider = Tts.SelectedVoiceProvider;

        s.Ui.StartMinimized      = StartMinimized;
        s.Ui.ShowQuickChat       = ShowQuickChat;
        s.Ui.EnableTrayIcon      = EnableTrayIcon;
        s.Ui.MinimizeToTray      = MinimizeToTray;
        s.Ui.EnableLocalHotkeys  = EnableLocalHotkeys;
        s.Ui.EnableGlobalHotkeys = EnableGlobalHotkeys;

        s.Memory.Enabled = MemoryFeatureEnabled;
        s.Memory.InjectMemoriesIntoContext = MemoryInjectIntoContext;
        s.Memory.AutoSummarizeImportanceThreshold = MemoryImportanceThreshold;
        s.Memory.InjectionTokenBudget = MemoryInjectionTokenBudget;
        s.Memory.EncryptMemoriesAtRest = MemoryEncryptionEnabled;
        s.Memory.AutoArchiveAfterDays = MemoryAutoArchiveDays;

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
            s.DataManagement.DataRootDirectory = previousDataRoot;
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
    private async Task ApplyLocalAiAssetsAsync()
    {
        SettingsError = string.Empty;
        var layout = LocalAiAssetLocator.Detect(LocalAiAssetsRoot);
        if (string.IsNullOrWhiteSpace(layout.Root) || !Directory.Exists(layout.Root))
        {
            SettingsError = "Choose an existing local AI assets folder first.";
            _toasts.Show("AI assets not applied", SettingsError, ToastKind.Warning);
            return;
        }

        if (!string.IsNullOrWhiteSpace(layout.TtsScriptPath)) Tts.TtsScriptPath = layout.TtsScriptPath;
        if (!string.IsNullOrWhiteSpace(layout.TtsPythonPath)) Tts.TtsPythonPath = layout.TtsPythonPath;
        if (!string.IsNullOrWhiteSpace(layout.TtsModelDirectory)) Tts.TtsModelDirectory = layout.TtsModelDirectory;
        if (!string.IsNullOrWhiteSpace(layout.TtsVoiceDirectory)) Tts.TtsVoiceDirectory = layout.TtsVoiceDirectory;
        if (!string.IsNullOrWhiteSpace(layout.TtsOutputDirectory)) Tts.TtsOutputDirectory = layout.TtsOutputDirectory;
        if (!string.IsNullOrWhiteSpace(layout.RerankerDirectory)) RagRerankerModelPath = layout.RerankerDirectory;
        UpdateLocalAiAssetsStatus();
        await SaveAsync();
        if (string.IsNullOrWhiteSpace(SettingsError))
            _toasts.Show("AI assets applied", layout.Summary, ToastKind.Success, 5500);
    }

    [RelayCommand]
    private void BrowseBackupDirectory() => RequestBackupDirectoryPicker?.Invoke();

    [RelayCommand]
    private void BrowseRestoreBackup() => RequestRestoreBackupPicker?.Invoke();

    [RelayCommand]
    private async Task BackupDataAsync()
    {
        SettingsError = string.Empty;
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
        SettingsError = string.Empty;
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
    private void BrowseTtsPython() => RequestTtsPythonPicker?.Invoke();

    [RelayCommand]
    private void BrowseTtsModelDirectory() => RequestTtsModelDirectoryPicker?.Invoke();

    [RelayCommand]
    private void BrowseTtsOutput() => RequestTtsOutputPicker?.Invoke();

    

    [RelayCommand]
    private async Task ScanLocalAiSetupAsync()
    {
        SettingsError = string.Empty;
        await SaveLocalAiPathsForSetupAsync();
        LocalAiSetupBusy = true;
        LocalAiSetupLog = string.Empty;
        try
        {
            var report = await _localAiSetup.ScanAsync(_svc.Settings);
            LocalAiReadinessItems.Clear();
            foreach (var item in report.Items)
                LocalAiReadinessItems.Add(item);
            LocalAiSetupActions.Clear();
            foreach (var action in report.Actions)
                LocalAiSetupActions.Add(action);
            LocalAiSetupSummary = report.Summary;
            LocalAiSetupLog = string.IsNullOrWhiteSpace(report.SetupCommands)
                ? "No setup actions are currently recommended."
                : report.SetupCommands;
            _toasts.Show("AI folder scanned", report.Summary, ToastKind.Info, 5500);
        }
        catch (Exception ex)
        {
            SettingsError = ex.Message;
            _toasts.Show("AI scan failed", ex.Message, ToastKind.Error, 7000);
        }
        finally
        {
            LocalAiSetupBusy = false;
        }
    }

    [RelayCommand]
    private async Task RunLocalAiSetupActionAsync(LocalAiSetupAction? action)
    {
        SettingsError = string.Empty;
        if (action is null) return;
        if (!action.CanRun)
        {
            _toasts.Show("Setup action not ready", action.ExpectedResult, ToastKind.Warning, 6000);
            return;
        }

        if (action.RequiresApproval && !string.Equals(LocalAiInstallPlanActionId, action.Id, StringComparison.Ordinal))
        {
            PreviewLocalAiInstallPlan(action);
            _toasts.Show("Review install plan", "Review the install plan before approving this action.", ToastKind.Info, 6000);
            return;
        }

        await SaveLocalAiPathsForSetupAsync();
        LocalAiSetupBusy = true;
        LocalAiSetupLog = $"Approved: {action.Title}{Environment.NewLine}{action.CommandPreviewText}{Environment.NewLine}";
        try
        {
            var progress = new Progress<string>(line =>
            {
                LocalAiSetupLog += line + Environment.NewLine;
            });
            var result = await _localAiSetup.RunActionAsync(action, _svc.Settings, allowOverwrite: false, progress: progress);
            LocalAiSetupLog += result.Log;
            if (!result.Success)
            {
                _toasts.Show("Setup action stopped", result.Log, ToastKind.Warning, 7000);
                return;
            }

            ApplySetupResult(action, result);
            await SaveAsync();
            await ScanLocalAiSetupAsync();
            _toasts.Show("Setup action complete", action.ExpectedResult, ToastKind.Success, 6000);
        }
        catch (Exception ex)
        {
            SettingsError = ex.Message;
            _toasts.Show("Setup action failed", ex.Message, ToastKind.Error, 7000);
        }
        finally
        {
            LocalAiSetupBusy = false;
        }
    }

    [RelayCommand]
    private void PreviewLocalAiInstallPlan(LocalAiSetupAction? action)
    {
        if (action is null) return;

        LocalAiInstallPlanCreates.Clear();
        LocalAiInstallPlanInstalls.Clear();
        LocalAiInstallPlanActionId = action.Id;
        LocalAiInstallPlanTitle = action.Title;
        LocalAiInstallPlanSummary = action.ExpectedResult;
        LocalAiInstallPlanRisk = action.RiskLabel;
        LocalAiInstallPlanRiskNotes = action.RequiresNetwork
            ? "Downloads packages from the internet and runs local setup steps."
            : "Runs local setup steps only.";

        switch (action.Kind)
        {
            case LocalAiSetupActionKind.CreateVenv:
            case LocalAiSetupActionKind.CreateXttsApiScript:
            case LocalAiSetupActionKind.CreateDirectory:
            case LocalAiSetupActionKind.DownloadGgufModel:
            case LocalAiSetupActionKind.DownloadTtsModel:
            case LocalAiSetupActionKind.DownloadLlamaServer:
                if (!string.IsNullOrWhiteSpace(action.TargetPath))
                    LocalAiInstallPlanCreates.Add(action.TargetPath);
                break;
            case LocalAiSetupActionKind.InstallXttsDependencies:
                var packages = ExtractPackages(action.CommandPreview);
                if (packages.Count == 0)
                    LocalAiInstallPlanInstalls.Add("Python packages (see command preview)");
                else
                    foreach (var pkg in packages)
                        LocalAiInstallPlanInstalls.Add(pkg);
                break;
        }

        if (LocalAiInstallPlanCreates.Count == 0)
            LocalAiInstallPlanCreates.Add("No new files are expected.");
        if (LocalAiInstallPlanInstalls.Count == 0)
            LocalAiInstallPlanInstalls.Add("No package installs expected.");

        LocalAiInstallPlanVisible = true;
    }

    private static List<string> ExtractPackages(IReadOnlyList<string> commandPreview)
    {
        var packages = new List<string>();
        if (commandPreview is null || commandPreview.Count == 0) return packages;

        var installIndex = commandPreview
            .Select((value, index) => new { value, index })
            .FirstOrDefault(item => string.Equals(item.value, "install", StringComparison.OrdinalIgnoreCase))
            ?.index ?? -1;

        if (installIndex < 0 || installIndex + 1 >= commandPreview.Count)
            return packages;

        for (var i = installIndex + 1; i < commandPreview.Count; i++)
        {
            var value = commandPreview[i];
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (value.StartsWith("-", StringComparison.Ordinal)) continue;
            packages.Add(value);
        }

        return packages;
    }

    [RelayCommand]
    private void CopyLocalAiSetupCommands()
    {
        var text = LocalAiSetupActions.Count == 0
            ? LocalAiSetupLog
            : string.Join(Environment.NewLine, LocalAiSetupActions.Select(action => action.CommandPreviewText));
        if (string.IsNullOrWhiteSpace(text))
            return;

        RequestCopyToClipboard?.Invoke(text);
        _toasts.Show("Setup commands copied", "Review commands before running them outside Aether.", ToastKind.Info);
    }

    [RelayCommand]
    private async Task RescanTrustAsync()
    {
        SettingsError = string.Empty;
        SyncSettingsForTrustScan();
        TrustScanBusy = true;
        try
        {
            var report = await _trust.ScanAsync(_svc.Settings);
            TrustItems.Clear();
            foreach (var item in report.Items)
                TrustItems.Add(item);
            TrustSummary = report.Summary;
            TrustLastScanned = $"Last scan: {report.ScannedAt.ToLocalTime():g}";
            if (report.WarningCount > 0 || report.MissingCount > 0)
                _toasts.Show("Trust scan warnings", report.Summary, ToastKind.Warning, 7000);
            else
                _toasts.Show("Trust scan complete", report.Summary, ToastKind.Success, 5000);
        }
        catch (Exception ex)
        {
            SettingsError = ex.Message;
            _toasts.Show("Trust scan failed", ex.Message, ToastKind.Error, 7000);
        }
        finally
        {
            TrustScanBusy = false;
        }
    }

    [RelayCommand]
    private void BrowseTtsVoiceDirectory() => RequestTtsVoiceDirectoryPicker?.Invoke();

    [RelayCommand]
    private void ImportTtsVoiceSample() => RequestTtsVoiceSamplePicker?.Invoke();

    [RelayCommand] private void Reset() => Reload();

    public void Shutdown() => _xttsProcess.Stop();

    private void UpdateMigrationPreview()
    {
        var plan = _svc.PreviewDataRootMigration(_svc.Settings.DataManagement.DataRootDirectory, DataRootDirectory);
        DataMigrationPreview = plan.Conflicts.Count > 0
            ? $"Move blocked: {plan.Conflicts.Count} existing database file(s) in target."
            : plan.WillMove
                ? $"Save will move {plan.FilesToMove} database file(s) to {plan.CurrentDataRoot}."
                : "No data move needed.";
    }

    private string ResolveDataRoot()
    {
        var configured = _svc.Settings.DataManagement.DataRootDirectory?.Trim();
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aether")
            : Path.GetFullPath(configured);
    }

    private void UpdateLocalAiAssetsStatus()
    {
        LocalAiAssetsStatus = LocalAiAssetLocator.Detect(LocalAiAssetsRoot).Summary;
    }

    private async Task SaveLocalAiPathsForSetupAsync()
    {
        var s = _svc.Settings;
        s.DataManagement.LocalAiAssetsRoot = LocalAiAssetsRoot.Trim();
        s.Tts.PythonPath = Tts.TtsPythonPath.Trim();
        s.Tts.ScriptPath = Tts.TtsScriptPath.Trim();
        s.Tts.ModelDirectory = Tts.TtsModelDirectory.Trim();
        s.Tts.OutputDirectory = Tts.TtsOutputDirectory.Trim();
        s.Tts.VoiceDirectory = Tts.TtsVoiceDirectory.Trim();
        s.Rag.RerankerModelPath = RagRerankerModelPath.Trim();
        await _svc.SaveAsync(s.DataManagement.DataRootDirectory);
    }

    private void SyncSettingsForTrustScan()
    {
        var s = _svc.Settings;
        s.DataManagement.LocalAiAssetsRoot = LocalAiAssetsRoot.Trim();
        s.Tts.PythonPath = Tts.TtsPythonPath.Trim();
        s.Tts.ScriptPath = Tts.TtsScriptPath.Trim();
        s.Tts.ModelDirectory = Tts.TtsModelDirectory.Trim();
        s.Tts.OutputDirectory = Tts.TtsOutputDirectory.Trim();
        s.Tts.VoiceDirectory = Tts.TtsVoiceDirectory.Trim();
    }

    private void ApplySetupResult(LocalAiSetupAction action, LocalAiSetupResult result)
    {
        if (string.IsNullOrWhiteSpace(result.UpdatedPath))
            return;

        switch (action.Kind)
        {
            case LocalAiSetupActionKind.CreateVenv:
                Tts.TtsPythonPath = result.UpdatedPath;
                break;
            case LocalAiSetupActionKind.CreateXttsApiScript:
                Tts.TtsScriptPath = result.UpdatedPath;
                break;
            case LocalAiSetupActionKind.CreateDirectory when action.Id == "create-voices":
                Tts.TtsVoiceDirectory = result.UpdatedPath;
                break;
            case LocalAiSetupActionKind.CreateDirectory when action.Id == "create-output":
                Tts.TtsOutputDirectory = result.UpdatedPath;
                break;
        }
    }
}
