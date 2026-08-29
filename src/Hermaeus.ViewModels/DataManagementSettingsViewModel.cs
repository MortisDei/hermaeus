using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Security.Cryptography;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Hermaeus.ViewModels;

public partial class DataManagementSettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly BackupService _backups;
    private readonly IToastService _toasts;
    private readonly Func<string> _resolveDataRoot;
    private readonly IActivityRecorder? _activity;

    [ObservableProperty] private string _dataRootDirectory = string.Empty;
    [ObservableProperty] private string _dataMigrationPreview = string.Empty;
    [ObservableProperty] private string _localAiAssetsRoot = string.Empty;
    [ObservableProperty] private string _localAiAssetsStatus = "Choose a local AI assets folder first.";
    [ObservableProperty] private LlamaRuntimeVariant _llamaRuntimeVariant = LlamaRuntimeVariant.Auto;

    /// <summary>Selectable llama.cpp build variants for the Services/data settings (r14 1.1).</summary>
    public IReadOnlyList<LlamaRuntimeVariant> LlamaRuntimeVariantOptions { get; } =
        Enum.GetValues<LlamaRuntimeVariant>();
    [ObservableProperty] private string _backupDirectory = string.Empty;
    [ObservableProperty] private string _restoreBackupPath = string.Empty;
    [ObservableProperty] private string _settingsError = string.Empty;
    [ObservableProperty] private bool _dataRootMigrationPending;

    private readonly SemaphoreSlim _dataRootMigrationGate = new(1, 1);
    private int _dataRootEditVersion;

    public Action? RequestDataRootPicker { get; set; }
    public Action? RequestLocalAiAssetsRootPicker { get; set; }
    public Action? RequestBackupDirectoryPicker { get; set; }
    public Action? RequestRestoreBackupPicker { get; set; }
    public Func<Task<bool>>? RequestRestoreBackupConfirmation { get; set; }
    public Func<DataMigrationPlan, Task<bool>>? RequestDataRootMigrationConfirmation { get; set; }
    public Func<Task>? CommitDataRootMigration { get; set; }
    public event Action? LocalAiAssetsRootChanged;

    public DataManagementSettingsViewModel(
        ISettingsService settings,
        BackupService backups,
        IToastService toasts,
        Func<string> resolveDataRoot,
        IActivityRecorder? activity = null)
    {
        _settings = settings;
        _backups = backups;
        _toasts = toasts;
        _resolveDataRoot = resolveDataRoot;
        _activity = activity;
    }

    public void ReloadFrom(AppSettings settings)
    {
        _dataRootEditVersion++;
        DataRootDirectory = settings.DataManagement.DataRootDirectory;
        LocalAiAssetsRoot = settings.DataManagement.LocalAiAssetsRoot;
        LlamaRuntimeVariant = settings.DataManagement.LlamaRuntimeVariant;
        UpdateMigrationPreview();
        UpdateLocalAiAssetsStatus();
    }

    public void ApplyTo(AppSettings settings)
    {
        settings.DataManagement.DataRootDirectory = DataRootDirectory.Trim();
        settings.DataManagement.LocalAiAssetsRoot = LocalAiAssetsRoot.Trim();
        settings.DataManagement.LlamaRuntimeVariant = LlamaRuntimeVariant;
    }

    [RelayCommand] private void BrowseDataRoot() => RequestDataRootPicker?.Invoke();
    [RelayCommand] private void BrowseLocalAiAssetsRoot() => RequestLocalAiAssetsRootPicker?.Invoke();
    [RelayCommand] private void BrowseBackupDirectory() => RequestBackupDirectoryPicker?.Invoke();
    [RelayCommand] private void BrowseRestoreBackup() => RequestRestoreBackupPicker?.Invoke();

    /// <summary>
    /// Opens the live resolved data root (respecting a configured override,
    /// not the raw text box) in the OS file explorer, so "where is my data
    /// stored" (r6 01-first-five-minutes.md 1.2) has a one-click answer.
    /// </summary>
    [RelayCommand]
    private void OpenDataRoot() => OpenFolder(_resolveDataRoot());

    [RelayCommand]
    private void OpenLocalAiAssetsRoot() => OpenFolder(LocalAiAssetsRoot);

    private void OpenFolder(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                SettingsError = "That folder does not exist yet.";
                return;
            }

            Process.Start(new ProcessStartInfo { FileName = Path.GetFullPath(path), UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SettingsError = ex.Message;
        }
    }

    [RelayCommand]
    private async Task BackupDataAsync()
    {
        SettingsError = string.Empty;
        try
        {
            var target = string.IsNullOrWhiteSpace(BackupDirectory) ? _resolveDataRoot() : BackupDirectory.Trim();
            var result = await _backups.BackupAsync(target);
            // r28 doc 03 3.3. Both directions record, because "did the backup
            // actually run" is precisely the question this panel exists for.
            _activity.RecordSafe("backup.write", result.Path, ActivityOutcome.Succeeded,
                "Backup written", $"{result.FilesIncluded} file(s) to {result.Path}");
            _toasts.Show("Backup complete", $"{result.FilesIncluded} file(s) written to {result.Path}", ToastKind.Success, 7000);
        }
        catch (Exception ex)
        {
            SettingsError = ex.Message;
            _activity.RecordSafe("backup.write", string.Empty, ActivityOutcome.Failed, "Backup failed", ex.Message);
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
            _activity.RecordSafe("backup.restore", RestoreBackupPath.Trim(), ActivityOutcome.Succeeded,
                "Backup restored", RestoreBackupPath.Trim());
            _toasts.Show("Restore complete", "Restart Hermaeus to load restored data.", ToastKind.Success, 7000);
        }
        catch (Exception ex)
        {
            SettingsError = ex.Message;
            // BackupService's own refusal reason, not a rewritten one.
            _activity.RecordSafe("backup.restore", RestoreBackupPath.Trim(), ActivityOutcome.Failed,
                "Restore refused", ex.Message);
            _toasts.Show("Restore refused", ex.Message, ToastKind.Error, 7000);
        }
    }

    public void UpdateMigrationPreview()
    {
        var plan = _settings.PreviewDataRootMigration(_settings.Settings.DataManagement.DataRootDirectory, DataRootDirectory);
        var rootsDiffer = !string.Equals(plan.PreviousDataRoot, plan.CurrentDataRoot, StringComparison.OrdinalIgnoreCase);
        DataRootMigrationPending = rootsDiffer && plan.Conflicts.Count == 0;
        DataMigrationPreview = plan.Conflicts.Count > 0
            ? $"Move blocked: {plan.Conflicts.Count} existing database file(s) in target."
            : !rootsDiffer
                ? "The current data folder is active."
                : plan.FilesToMove > 0
                    ? $"Move {plan.FilesToMove} workspace file(s) to {plan.CurrentDataRoot} after confirmation."
                    : $"Use {plan.CurrentDataRoot} as the data folder after confirmation.";
    }

    [RelayCommand]
    private async Task ConfirmDataRootMigrationAsync()
    {
        var plan = _settings.PreviewDataRootMigration(_settings.Settings.DataManagement.DataRootDirectory, DataRootDirectory);
        if (!DataRootMigrationPending)
            return;

        var version = _dataRootEditVersion;
        await _dataRootMigrationGate.WaitAsync();
        try
        {
            if (version != _dataRootEditVersion)
                return;

            if (RequestDataRootMigrationConfirmation is null
                || !await RequestDataRootMigrationConfirmation(plan))
            {
                RevertDataRootEdit();
                return;
            }

            if (version != _dataRootEditVersion || CommitDataRootMigration is null)
            {
                RevertDataRootEdit();
                return;
            }

            await CommitDataRootMigration();
            var committedRoot = SettingsService.ResolveDataRoot(_settings.Settings);
            if (!string.Equals(committedRoot, plan.CurrentDataRoot, StringComparison.OrdinalIgnoreCase))
                RevertDataRootEdit();
            else
                UpdateMigrationPreview();
        }
        finally
        {
            _dataRootMigrationGate.Release();
        }
    }

    private void RevertDataRootEdit()
    {
        _dataRootEditVersion++;
        DataRootDirectory = _settings.Settings.DataManagement.DataRootDirectory;
        UpdateMigrationPreview();
    }

    public void UpdateLocalAiAssetsStatus()
    {
        LocalAiAssetsStatus = LocalAiAssetLocator.Detect(LocalAiAssetsRoot).Summary;
        LocalAiAssetsRootChanged?.Invoke();
    }

    partial void OnDataRootDirectoryChanged(string value)
    {
        _dataRootEditVersion++;
        UpdateMigrationPreview();
    }
    partial void OnLocalAiAssetsRootChanged(string value) => UpdateLocalAiAssetsStatus();
}
