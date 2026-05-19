using System.Collections.ObjectModel;
using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

public partial class LlmDefaultsSettingsViewModel : ObservableObject
{
    private readonly ISecretStore _secrets;

    [ObservableProperty] private string _llamaCppBaseUrl = "http://localhost:8080";
    [ObservableProperty] private bool _llamaCppEnabled = true;
    [ObservableProperty] private string _openAiBaseUrl = "https://api.openai.com";
    [ObservableProperty] private string _openAiApiKey = string.Empty;
    [ObservableProperty] private bool _openAiEnabled;
    [ObservableProperty] private string _defaultSystemPrompt = string.Empty;
    [ObservableProperty] private double _temperature = 0.7;
    [ObservableProperty] private int _maxTokens = 4096;

    public LlmDefaultsSettingsViewModel(ISecretStore secrets) => _secrets = secrets;

    public void ReloadFrom(AppSettings settings)
    {
        LlamaCppBaseUrl = settings.Llm.LlamaCppBaseUrl;
        LlamaCppEnabled = settings.Llm.LlamaCppEnabled;
        OpenAiBaseUrl = settings.Llm.OpenAiBaseUrl;
        OpenAiApiKey = _secrets.IsReference(settings.Llm.OpenAiApiKey) ? string.Empty : settings.Llm.OpenAiApiKey;
        OpenAiEnabled = settings.Llm.OpenAiEnabled;
        DefaultSystemPrompt = settings.Llm.DefaultSystemPrompt;
        Temperature = settings.Llm.Temperature;
        MaxTokens = settings.Llm.MaxTokens;
    }

    public async Task ApplyToAsync(AppSettings settings)
    {
        settings.Llm.LlamaCppBaseUrl = LlamaCppBaseUrl;
        settings.Llm.LlamaCppEnabled = LlamaCppEnabled;
        settings.Llm.OpenAiBaseUrl = OpenAiBaseUrl;
        if (!string.IsNullOrWhiteSpace(OpenAiApiKey))
            settings.Llm.OpenAiApiKey = await _secrets.StoreAsync("openai-api-key", OpenAiApiKey.Trim());
        settings.Llm.OpenAiEnabled = OpenAiEnabled;
        settings.Llm.DefaultSystemPrompt = DefaultSystemPrompt;
        settings.Llm.Temperature = Temperature;
        settings.Llm.MaxTokens = MaxTokens;
    }
}

public partial class RagSettingsViewModel : ObservableObject
{
    private readonly Func<string> _fallbackRoot;

    [ObservableProperty] private string _embeddingModel = "nomic-embed-text";
    [ObservableProperty] private string _ragRerankerModelPath = string.Empty;

    public ObservableCollection<string> EmbeddingModelOptions { get; } = [];
    public ObservableCollection<string> RerankerModelPathOptions { get; } = [];

    public RagSettingsViewModel(Func<string> fallbackRoot) => _fallbackRoot = fallbackRoot;

    public void ReloadFrom(AppSettings settings, string localAiAssetsRoot)
    {
        EmbeddingModel = settings.Rag.EmbeddingModel;
        RagRerankerModelPath = settings.Rag.RerankerModelPath;
        RefreshLocalAiAssetOptions(localAiAssetsRoot);
    }

    public void ApplyTo(AppSettings settings)
    {
        settings.Rag.EmbeddingModel = EmbeddingModel;
        settings.Rag.RerankerModelPath = RagRerankerModelPath.Trim();
    }

    public void RefreshEmbeddingModelOptions(string localAiAssetsRoot)
    {
        EmbeddingModelOptions.Clear();
        AddEmbeddingModelOption(EmbeddingModel);
        try
        {
            var root = string.IsNullOrWhiteSpace(localAiAssetsRoot) ? _fallbackRoot() : Path.GetFullPath(localAiAssetsRoot);
            if (!Directory.Exists(root)) return;
            var ggufs = LocalAiAssetLocator.FindEmbeddingModels(root)
                .Select(Path.GetFileNameWithoutExtension)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x);
            foreach (var name in ggufs.Where(n => !string.IsNullOrWhiteSpace(n)))
                AddEmbeddingModelOption(name!);
        }
        catch { }
    }

    public void RefreshLocalAiAssetOptions(string localAiAssetsRoot)
    {
        RefreshEmbeddingModelOptions(localAiAssetsRoot);
        RefreshRerankerModelPathOptions(localAiAssetsRoot);
    }

    public void RefreshRerankerModelPathOptions(string localAiAssetsRoot)
    {
        RerankerModelPathOptions.Clear();
        AddRerankerModelPathOption(RagRerankerModelPath);
        try
        {
            var root = string.IsNullOrWhiteSpace(localAiAssetsRoot) ? _fallbackRoot() : Path.GetFullPath(localAiAssetsRoot);
            if (!Directory.Exists(root)) return;

            foreach (var path in LocalAiAssetLocator.FindRerankerDirectories(root))
                AddRerankerModelPathOption(path);

            if (string.IsNullOrWhiteSpace(RagRerankerModelPath) && RerankerModelPathOptions.Count > 0)
                RagRerankerModelPath = RerankerModelPathOptions[0];
        }
        catch { }
    }

    private void AddEmbeddingModelOption(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        if (EmbeddingModelOptions.Any(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)))
            return;

        EmbeddingModelOptions.Add(name.Trim());
    }

    private void AddRerankerModelPathOption(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        path = Path.GetFullPath(path.Trim());
        if (RerankerModelPathOptions.Any(existing => string.Equals(existing, path, StringComparison.OrdinalIgnoreCase)))
            return;

        RerankerModelPathOptions.Add(path);
    }
}

public partial class DataManagementSettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IBackupService _backups;
    private readonly IToastService _toasts;
    private readonly Func<string> _resolveDataRoot;

    [ObservableProperty] private string _dataRootDirectory = string.Empty;
    [ObservableProperty] private string _dataMigrationPreview = string.Empty;
    [ObservableProperty] private string _localAiAssetsRoot = string.Empty;
    [ObservableProperty] private string _localAiAssetsStatus = "Choose a local AI assets folder first.";
    [ObservableProperty] private string _backupDirectory = string.Empty;
    [ObservableProperty] private string _restoreBackupPath = string.Empty;
    [ObservableProperty] private string _settingsError = string.Empty;

    public Action? RequestDataRootPicker { get; set; }
    public Action? RequestLocalAiAssetsRootPicker { get; set; }
    public Action? RequestBackupDirectoryPicker { get; set; }
    public Action? RequestRestoreBackupPicker { get; set; }
    public Func<Task<bool>>? RequestRestoreBackupConfirmation { get; set; }
    public event Action? LocalAiAssetsRootChanged;

    public DataManagementSettingsViewModel(
        ISettingsService settings,
        IBackupService backups,
        IToastService toasts,
        Func<string> resolveDataRoot)
    {
        _settings = settings;
        _backups = backups;
        _toasts = toasts;
        _resolveDataRoot = resolveDataRoot;
    }

    public void ReloadFrom(AppSettings settings)
    {
        DataRootDirectory = settings.DataManagement.DataRootDirectory;
        LocalAiAssetsRoot = settings.DataManagement.LocalAiAssetsRoot;
        UpdateMigrationPreview();
        UpdateLocalAiAssetsStatus();
    }

    public void ApplyTo(AppSettings settings)
    {
        settings.DataManagement.DataRootDirectory = DataRootDirectory.Trim();
        settings.DataManagement.LocalAiAssetsRoot = LocalAiAssetsRoot.Trim();
    }

    [RelayCommand] private void BrowseDataRoot() => RequestDataRootPicker?.Invoke();
    [RelayCommand] private void BrowseLocalAiAssetsRoot() => RequestLocalAiAssetsRootPicker?.Invoke();
    [RelayCommand] private void BrowseBackupDirectory() => RequestBackupDirectoryPicker?.Invoke();
    [RelayCommand] private void BrowseRestoreBackup() => RequestRestoreBackupPicker?.Invoke();

    [RelayCommand]
    private async Task BackupDataAsync()
    {
        SettingsError = string.Empty;
        try
        {
            var target = string.IsNullOrWhiteSpace(BackupDirectory) ? _resolveDataRoot() : BackupDirectory.Trim();
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
            if (RequestRestoreBackupConfirmation is not null
                && !await RequestRestoreBackupConfirmation())
                return;
            await _backups.RestoreAsync(RestoreBackupPath.Trim());
            _toasts.Show("Restore complete", "Restart Aether to load restored data.", ToastKind.Success, 7000);
        }
        catch (Exception ex)
        {
            SettingsError = ex.Message;
            _toasts.Show("Restore refused", ex.Message, ToastKind.Error, 7000);
        }
    }

    public void UpdateMigrationPreview()
    {
        var plan = _settings.PreviewDataRootMigration(_settings.Settings.DataManagement.DataRootDirectory, DataRootDirectory);
        DataMigrationPreview = plan.Conflicts.Count > 0
            ? $"Move blocked: {plan.Conflicts.Count} existing database file(s) in target."
            : plan.WillMove
                ? $"Save will move {plan.FilesToMove} database file(s) to {plan.CurrentDataRoot}."
                : "No data move needed.";
    }

    public void UpdateLocalAiAssetsStatus()
    {
        LocalAiAssetsStatus = LocalAiAssetLocator.Detect(LocalAiAssetsRoot).Summary;
        LocalAiAssetsRootChanged?.Invoke();
    }

    partial void OnDataRootDirectoryChanged(string value) => UpdateMigrationPreview();
    partial void OnLocalAiAssetsRootChanged(string value) => UpdateLocalAiAssetsStatus();
}

public partial class UiSettingsViewModel : ObservableObject
{
    [ObservableProperty] private double _fontSize = 14;
    [ObservableProperty] private string _selectedTheme = "System";
    [ObservableProperty] private bool _ctrlEnterToSend;
    [ObservableProperty] private bool _startMinimized;
    [ObservableProperty] private bool _showQuickChat;
    [ObservableProperty] private bool _enableTrayIcon = true;
    [ObservableProperty] private bool _minimizeToTray = true;
    [ObservableProperty] private bool _enableLocalHotkeys = true;
    [ObservableProperty] private bool _enableGlobalHotkeys;
    [ObservableProperty] private string _globalHotkeyStatus = "System-wide hotkeys are off.";

    public string[] Themes { get; } = ["System", "Dark", "Light"];

    public void ReloadFrom(AppSettings settings)
    {
        FontSize = settings.Ui.FontSize;
        SelectedTheme = settings.Ui.Theme;
        CtrlEnterToSend = settings.Ui.CtrlEnterToSend;
        StartMinimized = settings.Ui.StartMinimized;
        ShowQuickChat = settings.Ui.ShowQuickChat;
        EnableTrayIcon = settings.Ui.EnableTrayIcon;
        MinimizeToTray = settings.Ui.MinimizeToTray;
        EnableLocalHotkeys = settings.Ui.EnableLocalHotkeys;
        EnableGlobalHotkeys = settings.Ui.EnableGlobalHotkeys;
    }

    public void ApplyTo(AppSettings settings)
    {
        settings.Ui.FontSize = FontSize;
        settings.Ui.Theme = SelectedTheme;
        settings.Ui.CtrlEnterToSend = CtrlEnterToSend;
        settings.Ui.StartMinimized = StartMinimized;
        settings.Ui.ShowQuickChat = ShowQuickChat;
        settings.Ui.EnableTrayIcon = EnableTrayIcon;
        settings.Ui.MinimizeToTray = MinimizeToTray;
        settings.Ui.EnableLocalHotkeys = EnableLocalHotkeys;
        settings.Ui.EnableGlobalHotkeys = EnableGlobalHotkeys;
    }
}

public partial class MemorySettingsViewModel : ObservableObject
{
    [ObservableProperty] private bool _memoryFeatureEnabled;
    [ObservableProperty] private bool _memoryInjectIntoContext;
    [ObservableProperty] private double _memoryImportanceThreshold = 0.6;
    [ObservableProperty] private int _memoryInjectionTokenBudget = 500;
    [ObservableProperty] private bool _memoryEncryptionEnabled;
    [ObservableProperty] private int _memoryAutoArchiveDays = 90;

    public void ReloadFrom(AppSettings settings)
    {
        MemoryFeatureEnabled = settings.Memory.Enabled;
        MemoryInjectIntoContext = settings.Memory.InjectMemoriesIntoContext;
        MemoryImportanceThreshold = settings.Memory.AutoSummarizeImportanceThreshold;
        MemoryInjectionTokenBudget = settings.Memory.InjectionTokenBudget;
        MemoryEncryptionEnabled = settings.Memory.EncryptMemoriesAtRest;
        MemoryAutoArchiveDays = settings.Memory.AutoArchiveAfterDays;
    }

    public void ApplyTo(AppSettings settings)
    {
        settings.Memory.Enabled = MemoryFeatureEnabled;
        settings.Memory.InjectMemoriesIntoContext = MemoryInjectIntoContext;
        settings.Memory.AutoSummarizeImportanceThreshold = MemoryImportanceThreshold;
        settings.Memory.InjectionTokenBudget = MemoryInjectionTokenBudget;
        settings.Memory.EncryptMemoriesAtRest = MemoryEncryptionEnabled;
        settings.Memory.AutoArchiveAfterDays = MemoryAutoArchiveDays;
    }
}

public partial class LocalAiSetupSettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly ILocalAiSetupService _localAiSetup;
    private readonly IToastService _toasts;
    private readonly TtsSettingsViewModel _tts;
    private readonly DataManagementSettingsViewModel _data;
    private readonly RagSettingsViewModel _rag;
    private readonly Func<Task> _saveSettings;

    [ObservableProperty] private bool _localAiSetupBusy;
    [ObservableProperty] private string _localAiSetupLog = string.Empty;
    [ObservableProperty] private string _localAiSetupSummary = "Scan a local AI folder to see readiness.";
    [ObservableProperty] private bool _localAiInstallPlanVisible;
    [ObservableProperty] private string _localAiInstallPlanTitle = "Install plan";
    [ObservableProperty] private string _localAiInstallPlanSummary = string.Empty;
    [ObservableProperty] private string _localAiInstallPlanRisk = string.Empty;
    [ObservableProperty] private string _localAiInstallPlanRiskNotes = string.Empty;
    [ObservableProperty] private string _localAiInstallPlanActionId = string.Empty;
    [ObservableProperty] private string _settingsError = string.Empty;

    public ObservableCollection<LocalAiReadinessItem> LocalAiReadinessItems { get; } = [];
    public ObservableCollection<LocalAiSetupAction> LocalAiSetupActions { get; } = [];
    public ObservableCollection<string> LocalAiInstallPlanCreates { get; } = [];
    public ObservableCollection<string> LocalAiInstallPlanInstalls { get; } = [];

    public Action<string>? RequestCopyToClipboard { get; set; }

    public LocalAiSetupSettingsViewModel(
        ISettingsService settings,
        ILocalAiSetupService localAiSetup,
        IToastService toasts,
        TtsSettingsViewModel tts,
        DataManagementSettingsViewModel data,
        RagSettingsViewModel rag,
        Func<Task> saveSettings)
    {
        _settings = settings;
        _localAiSetup = localAiSetup;
        _toasts = toasts;
        _tts = tts;
        _data = data;
        _rag = rag;
        _saveSettings = saveSettings;
    }

    [RelayCommand]
    private async Task ApplyLocalAiAssetsAsync()
    {
        SettingsError = string.Empty;
        var layout = LocalAiAssetLocator.Detect(_data.LocalAiAssetsRoot);
        if (string.IsNullOrWhiteSpace(layout.Root) || !Directory.Exists(layout.Root))
        {
            SettingsError = "Choose an existing local AI assets folder first.";
            _toasts.Show("AI assets not applied", SettingsError, ToastKind.Warning);
            return;
        }

        if (!string.IsNullOrWhiteSpace(layout.TtsScriptPath)) _tts.TtsScriptPath = layout.TtsScriptPath;
        if (!string.IsNullOrWhiteSpace(layout.TtsPythonPath)) _tts.TtsPythonPath = layout.TtsPythonPath;
        if (!string.IsNullOrWhiteSpace(layout.TtsModelDirectory)) _tts.TtsModelDirectory = layout.TtsModelDirectory;
        if (!string.IsNullOrWhiteSpace(layout.TtsVoiceDirectory)) _tts.TtsVoiceDirectory = layout.TtsVoiceDirectory;
        if (!string.IsNullOrWhiteSpace(layout.TtsOutputDirectory)) _tts.TtsOutputDirectory = layout.TtsOutputDirectory;
        if (!string.IsNullOrWhiteSpace(layout.RerankerDirectory)) _rag.RagRerankerModelPath = layout.RerankerDirectory;
        _data.UpdateLocalAiAssetsStatus();
        await _saveSettings();
        _toasts.Show("AI assets applied", layout.Summary, ToastKind.Success, 5500);
    }

    [RelayCommand]
    private async Task ScanLocalAiSetupAsync()
    {
        SettingsError = string.Empty;
        await SaveLocalAiPathsForSetupAsync();
        LocalAiSetupBusy = true;
        LocalAiSetupLog = string.Empty;
        try
        {
            var report = await _localAiSetup.ScanAsync(_settings.Settings);
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
            var progress = new Progress<string>(line => LocalAiSetupLog += line + Environment.NewLine);
            var result = await _localAiSetup.RunActionAsync(action, _settings.Settings, allowOverwrite: false, progress: progress);
            LocalAiSetupLog += result.Log;
            if (!result.Success)
            {
                _toasts.Show("Setup action stopped", result.Log, ToastKind.Warning, 7000);
                return;
            }

            ApplySetupResult(action, result);
            await _saveSettings();
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

    private async Task SaveLocalAiPathsForSetupAsync()
    {
        var settings = _settings.Settings;
        settings.DataManagement.LocalAiAssetsRoot = _data.LocalAiAssetsRoot.Trim();
        settings.Tts.PythonPath = _tts.TtsPythonPath.Trim();
        settings.Tts.ScriptPath = _tts.TtsScriptPath.Trim();
        settings.Tts.ModelDirectory = _tts.TtsModelDirectory.Trim();
        settings.Tts.OutputDirectory = _tts.TtsOutputDirectory.Trim();
        settings.Tts.VoiceDirectory = _tts.TtsVoiceDirectory.Trim();
        settings.Rag.RerankerModelPath = _rag.RagRerankerModelPath.Trim();
        await _settings.SaveAsync(settings.DataManagement.DataRootDirectory);
    }

    private void ApplySetupResult(LocalAiSetupAction action, LocalAiSetupResult result)
    {
        if (string.IsNullOrWhiteSpace(result.UpdatedPath))
            return;

        switch (action.Kind)
        {
            case LocalAiSetupActionKind.CreateVenv:
                _tts.TtsPythonPath = result.UpdatedPath;
                break;
            case LocalAiSetupActionKind.CreateXttsApiScript:
                _tts.TtsScriptPath = result.UpdatedPath;
                break;
            case LocalAiSetupActionKind.CreateDirectory when action.Id == "create-voices":
                _tts.TtsVoiceDirectory = result.UpdatedPath;
                break;
            case LocalAiSetupActionKind.CreateDirectory when action.Id == "create-output":
                _tts.TtsOutputDirectory = result.UpdatedPath;
                break;
        }
    }

    private static List<string> ExtractPackages(IReadOnlyList<string> commandPreview)
    {
        var packages = new List<string>();
        if (commandPreview.Count == 0) return packages;

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
}

public partial class TrustSettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly ITrustService _trust;
    private readonly IToastService _toasts;
    private readonly TtsSettingsViewModel _tts;
    private readonly DataManagementSettingsViewModel _data;
    private readonly RagSettingsViewModel _rag;

    [ObservableProperty] private bool _trustScanBusy;
    [ObservableProperty] private string _trustSummary = "Run a trust scan to review configured local tools.";
    [ObservableProperty] private string _trustLastScanned = string.Empty;
    [ObservableProperty] private string _settingsError = string.Empty;

    public ObservableCollection<TrustItem> TrustItems { get; } = [];

    public TrustSettingsViewModel(
        ISettingsService settings,
        ITrustService trust,
        IToastService toasts,
        TtsSettingsViewModel tts,
        DataManagementSettingsViewModel data,
        RagSettingsViewModel rag)
    {
        _settings = settings;
        _trust = trust;
        _toasts = toasts;
        _tts = tts;
        _data = data;
        _rag = rag;
    }

    [RelayCommand]
    private async Task RescanTrustAsync()
    {
        SettingsError = string.Empty;
        SyncSettingsForTrustScan();
        TrustScanBusy = true;
        try
        {
            var report = await _trust.ScanAsync(_settings.Settings);
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

    private void SyncSettingsForTrustScan()
    {
        var settings = _settings.Settings;
        settings.DataManagement.LocalAiAssetsRoot = _data.LocalAiAssetsRoot.Trim();
        settings.Tts.PythonPath = _tts.TtsPythonPath.Trim();
        settings.Tts.ScriptPath = _tts.TtsScriptPath.Trim();
        settings.Tts.ModelDirectory = _tts.TtsModelDirectory.Trim();
        settings.Tts.OutputDirectory = _tts.TtsOutputDirectory.Trim();
        settings.Tts.VoiceDirectory = _tts.TtsVoiceDirectory.Trim();
        settings.Rag.RerankerModelPath = _rag.RagRerankerModelPath.Trim();
    }
}
