using System.Diagnostics;
using System.Security.Cryptography;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Hermaeus.ViewModels;

public partial class LocalAiSetupSettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly LocalAiSetupService _localAiSetup;
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

    public UiBoundCollection<LocalAiReadinessItem> LocalAiReadinessItems { get; } = [];
    public UiBoundCollection<LocalAiSetupAction> LocalAiSetupActions { get; } = [];
    public UiBoundCollection<string> LocalAiInstallPlanCreates { get; } = [];
    public UiBoundCollection<string> LocalAiInstallPlanInstalls { get; } = [];

    public Action<string>? RequestCopyToClipboard { get; set; }

    public LocalAiSetupSettingsViewModel(
        ISettingsService settings,
        LocalAiSetupService localAiSetup,
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
        var scanSettings = BuildScanScopedSettings();
        LocalAiSetupBusy = true;
        LocalAiSetupLog = string.Empty;
        try
        {
            var report = await _localAiSetup.ScanAsync(scanSettings);
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

        if (action.RequiresApproval
            && (!action.PlanReviewed
                || !string.Equals(LocalAiInstallPlanActionId, action.Id, StringComparison.Ordinal)))
        {
            PreviewLocalAiInstallPlan(action);
            _toasts.Show("Review install plan", "Review the install plan before approving this action.", ToastKind.Info, 6000);
            return;
        }

        // r12 01-settings-lifecycle.md 1.5: an action that is about to write
        // files derived from these paths genuinely needs them persisted
        // first, but that persistence is the full apply/save (every tab),
        // not a partial side-channel write of just these fields.
        await _saveSettings();
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

        for (var i = 0; i < LocalAiSetupActions.Count; i++)
        {
            var current = LocalAiSetupActions[i];
            var reviewed = string.Equals(current.Id, action.Id, StringComparison.Ordinal);
            if (current.PlanReviewed != reviewed)
                LocalAiSetupActions[i] = current with { PlanReviewed = reviewed };
        }

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
        _toasts.Show("Setup commands copied", "Review commands before running them outside Hermaeus.", ToastKind.Info);
    }

    /// <summary>
    /// r12 01-settings-lifecycle.md 1.5: a scan is read-only with respect to
    /// settings, so it works from a clone carrying the current edit-box
    /// values instead of writing them into the live
    /// <see cref="ISettingsService.Settings"/> (the old behavior lingered
    /// there, unsaved, until an unrelated save persisted it).
    /// </summary>
    private AppSettings BuildScanScopedSettings()
    {
        var settings = _settings.Settings.Clone();
        settings.DataManagement.LocalAiAssetsRoot = _data.LocalAiAssetsRoot.Trim();
        settings.Tts.PythonPath = _tts.TtsPythonPath.Trim();
        settings.Tts.ScriptPath = _tts.TtsScriptPath.Trim();
        settings.Tts.ModelDirectory = _tts.TtsModelDirectory.Trim();
        settings.Tts.OutputDirectory = _tts.TtsOutputDirectory.Trim();
        settings.Tts.VoiceDirectory = _tts.TtsVoiceDirectory.Trim();
        settings.Rag.RerankerModelPath = _rag.RagRerankerModelPath.Trim();
        return settings;
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
