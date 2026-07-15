using System.Diagnostics;
using System.Security.Cryptography;
using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

public partial class TrustSettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly TrustService _trust;
    private readonly IToastService _toasts;
    private readonly TtsSettingsViewModel _tts;
    private readonly DataManagementSettingsViewModel _data;
    private readonly RagSettingsViewModel _rag;

    [ObservableProperty] private bool _trustScanBusy;
    [ObservableProperty] private string _trustSummary = "Run a trust scan to review configured local tools.";
    [ObservableProperty] private string _trustLastScanned = string.Empty;
    [ObservableProperty] private string _settingsError = string.Empty;

    public UiBoundCollection<TrustItem> TrustItems { get; } = [];

    public TrustSettingsViewModel(
        ISettingsService settings,
        TrustService trust,
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
