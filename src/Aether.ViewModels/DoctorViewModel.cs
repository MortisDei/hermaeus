using System.Linq;
using Aether.Core.Models;
using Aether.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

public partial class DoctorViewModel : ObservableObject
{
    private readonly IDoctorService _doctor;
    private readonly IToastService _toasts;
    private readonly ISettingsService _settingsService;
    private readonly IVoiceOrchestrator? _voice;
    private CancellationTokenSource? _installCts;

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _summary = "Run Doctor to scan your environment.";
    [ObservableProperty] private string _lastScanned = string.Empty;
    [ObservableProperty] private bool _isInstallingReranker;
    [ObservableProperty] private string _rerankerProgress = string.Empty;
    [ObservableProperty] private bool _isInstallingLlamaServer;
    [ObservableProperty] private string _llamaServerProgress = string.Empty;
    [ObservableProperty] private bool _isInstallingEmbeddingModel;
    [ObservableProperty] private string _embeddingModelProgress = string.Empty;
    [ObservableProperty] private double _embeddingModelProgressPercent;
    [ObservableProperty] private bool _embeddingModelProgressIsIndeterminate = true;
    [ObservableProperty] private bool _isInstallingNativeKokoro;
    [ObservableProperty] private string _nativeKokoroProgress = string.Empty;

    private readonly System.Text.StringBuilder _embeddingLogBuffer = new();

    public UiBoundCollection<DoctorCheck> Checks { get; } = [];

    public Action<string>? RequestCopyToClipboard { get; set; }
    public Action<string>? RequestNavigate { get; set; }

    public DoctorViewModel(IDoctorService doctor, IToastService toasts, ISettingsService settings, IVoiceOrchestrator? voice = null)
    {
        _doctor = doctor;
        _toasts = toasts;
        _settingsService = settings;
        _voice = voice;
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        await ScanCoreAsync(showIssueToast: false);
    }

    public async Task RunStartupScanAsync()
    {
        await ScanCoreAsync(showIssueToast: true);
    }

    private async Task ScanCoreAsync(bool showIssueToast)
    {
        if (IsScanning) return;
        IsScanning = true;
        try
        {
            var report = await _doctor.ScanAsync();
            Checks.Clear();
            foreach (var check in report.Checks)
                Checks.Add(check);
            Summary = report.Summary;
            LastScanned = $"Last scan: {report.ScannedAt:yyyy-MM-dd HH:mm} UTC";
            if (showIssueToast)
                ShowStartupIssueToast(report);
            NarrateCriticalIssues(report);
            AlertOnNewUntunedModels(report);
        }
        catch (Exception ex)
        {
            Summary = "Doctor scan failed.";
            _toasts.Show("Doctor scan failed", ex.Message, ToastKind.Error, 7000);
        }
        finally
        {
            IsScanning = false;
        }
    }

    /// <summary>
    /// One utterance per scan, only when at least one check is an Error;
    /// never per check. Off by default (Doctor channel disabled) so a clean
    /// scan stays silent regardless.
    /// </summary>
    private void NarrateCriticalIssues(DoctorReport report)
    {
        if (_voice is null || report.ErrorCount == 0)
            return;

        var first = report.Checks.First(c => c.Status == DoctorCheckStatus.Error).Title;
        var text = report.ErrorCount == 1
            ? $"Doctor found 1 critical issue: {first}."
            : $"Doctor found {report.ErrorCount} critical issues: {first} and others.";
        _ = _voice.EnqueueAsync(new VoiceUtterance(text, VoiceChannel.Doctor, VoicePriority.Critical, DedupeKey: $"doctor:{report.ScannedAt:O}"));
    }

    private readonly HashSet<string> _knownUntunedModels = new(StringComparer.OrdinalIgnoreCase);
    private bool _hasScannedOnce;

    /// <summary>
    /// Seeds the known-untuned set on the first scan (so a fresh install with many
    /// untuned models does not immediately fire a toast for all of them, which the
    /// Warning summary already covers); every scan after that compares against the
    /// known set and alerts only about models that newly showed up as untuned, e.g.
    /// a GGUF file the user just dropped into the assets root.
    /// </summary>
    private void AlertOnNewUntunedModels(DoctorReport report)
    {
        var check = report.Checks.FirstOrDefault(c => c.Key == "llama-tune-profiles" && c.Status == DoctorCheckStatus.Warning);
        var untuned = check?.Diagnostics
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList() ?? [];

        var newModels = untuned.Where(m => !_knownUntunedModels.Contains(m)).ToList();
        foreach (var m in untuned)
            _knownUntunedModels.Add(m);

        if (_hasScannedOnce && newModels.Count > 0)
        {
            var name = Path.GetFileNameWithoutExtension(newModels[0]);
            var text = newModels.Count == 1
                ? $"{name} needs a tuned launch profile. Run auto-tune in Services before benchmarking or chatting."
                : $"{newModels.Count} new models need tuned launch profiles. Run auto-tune in Services before benchmarking or chatting.";
            _toasts.Show("New model detected", text, ToastKind.Info, 8000);
        }

        _hasScannedOnce = true;
    }

    private void ShowStartupIssueToast(DoctorReport report)
    {
        if (report.ErrorCount > 0)
        {
            _toasts.Show("Aether Doctor found problems", report.Summary, ToastKind.Error, 9000);
            return;
        }

        if (report.WarningCount > 0)
            _toasts.Show("Aether Doctor found warnings", report.Summary, ToastKind.Warning, 9000);
    }

    [RelayCommand]
    private async Task CopyDiagnosticsAsync(DoctorCheck? check)
    {
        if (check is null) return;
        if (RequestCopyToClipboard is null) return;
        RequestCopyToClipboard(check.Diagnostics);
        _toasts.Show("Diagnostics copied", check.Title, ToastKind.Success, 3000);
    }

    [RelayCommand]
    private async Task CopyAllDiagnosticsAsync()
    {
        if (RequestCopyToClipboard is null) return;
        var payload = string.Join("\n\n", Checks.Select(c =>
            $"[{c.StatusLabel}] {c.Title}\n{c.Summary}\n{c.Detail}\n{c.Diagnostics}"));
        RequestCopyToClipboard(payload);
        _toasts.Show("Diagnostics copied", "Doctor summary copied to clipboard.", ToastKind.Success, 3000);
    }

    [RelayCommand]
    private void CancelInstall()
    {
        if (_installCts is null) return;
        try
        {
            _installCts.Cancel();
        }
        catch { }
    }

    private void HandleEmbeddingProgress(string s)
    {
        EmbeddingModelProgress = s;
        try
        {
            // parse percent like '... 42.3%'
            var m = System.Text.RegularExpressions.Regex.Match(s ?? string.Empty, "(\\d+(?:\\.\\d+)?)%", System.Text.RegularExpressions.RegexOptions.Compiled);
            if (m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var pct))
            {
                EmbeddingModelProgressPercent = pct;
                EmbeddingModelProgressIsIndeterminate = false;
            }
            else
            {
                EmbeddingModelProgressIsIndeterminate = true;
            }
        }
        catch { EmbeddingModelProgressIsIndeterminate = true; }

        // append to in-memory buffer
        try
        {
            _embeddingLogBuffer.AppendLine($"{DateTime.UtcNow:O} {s}");
        }
        catch { }

        // update the corresponding check diagnostics (if present)
        try
        {
            var idx = Checks.ToList().FindIndex(c => c.Key == "embedding-model");
            if (idx >= 0)
            {
                var existing = Checks[idx];
                var updated = new DoctorCheck(existing.Key, existing.Title, existing.Status, existing.Summary, existing.Detail, existing.FixLabel, existing.CanFix, _embeddingLogBuffer.ToString(), existing.Category);
                // replace item to notify UI
                Checks[idx] = updated;
            }
        }
        catch { }

        // persist to a log file asynchronously
        _ = Task.Run(async () =>
        {
            try
            {
                var root = Aether.Services.SettingsService.ResolveDataRoot(_settingsService.Settings);
                var dir = Path.Combine(root, "logs");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "embedding_downloads.log");
                var entry = $"{DateTime.UtcNow:O} {s}{Environment.NewLine}";
                await File.AppendAllTextAsync(path, entry);
            }
            catch { }
        });
    }

    [RelayCommand]
    private async Task RunFix(DoctorCheck? check)
    {
        if (check is null || !check.CanFix)
        {
            _toasts.Show("No fix available", "This check does not provide an automated fix yet.", ToastKind.Info, 4000);
            return;
        }

        // Special-case reranker installation: perform install action via doctor service
        if (check.Key == "reranker")
        {
            try
            {
                if (IsInstallingReranker) return;
                IsInstallingReranker = true;
                _installCts = new CancellationTokenSource();
                var progress = new Progress<string>(s => RerankerProgress = s);
                var ok = await _doctor.InstallRerankerAssetsAsync(progress, _installCts.Token);
                _toasts.Show(ok ? "Reranker installed" : "Reranker install failed",
                    ok ? "Reranker assets installed." : "See diagnostics for details.",
                    ok ? ToastKind.Success : ToastKind.Error,
                    7000);
                // refresh doctor checks after attempt
                await ScanAsync();
                RerankerProgress = string.Empty;
                IsInstallingReranker = false;
                _installCts = null;
                return;
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException)
                {
                    _toasts.Show("Reranker install cancelled", "Installation was cancelled.", ToastKind.Info, 4000);
                }
                else
                {
                    _toasts.Show("Reranker install failed", ex.Message, ToastKind.Error, 7000);
                }
                IsInstallingReranker = false;
                RerankerProgress = string.Empty;
                _installCts = null;
                return;
            }
        }

        if (check.Key == "kokoro-native")
        {
            try
            {
                if (IsInstallingNativeKokoro) return;
                IsInstallingNativeKokoro = true;
                _installCts = new CancellationTokenSource();
                var progress = new Progress<string>(s => NativeKokoroProgress = s);
                var ok = await _doctor.InstallNativeKokoroAssetsAsync(progress, _installCts.Token);
                _toasts.Show(ok ? "Kokoro (native) installed" : "Kokoro (native) install failed",
                    ok ? "Kokoro native ONNX model and voices installed." : "See diagnostics for details.",
                    ok ? ToastKind.Success : ToastKind.Error,
                    7000);
                await ScanAsync();
                NativeKokoroProgress = string.Empty;
                IsInstallingNativeKokoro = false;
                _installCts = null;
                return;
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException)
                {
                    _toasts.Show("Kokoro (native) install cancelled", "Installation was cancelled.", ToastKind.Info, 4000);
                }
                else
                {
                    _toasts.Show("Kokoro (native) install failed", ex.Message, ToastKind.Error, 7000);
                }
                IsInstallingNativeKokoro = false;
                NativeKokoroProgress = string.Empty;
                _installCts = null;
                return;
            }
        }

        if (check.Key is "embedding-model" or "embedding-model-update")
        {
            try
            {
                if (IsInstallingEmbeddingModel) return;
                IsInstallingEmbeddingModel = true;
                _installCts = new CancellationTokenSource();
                var progress = new Progress<string>(s => HandleEmbeddingProgress(s));
                var ok = await _doctor.InstallEmbeddingModelAsync(progress, _installCts.Token);
                _toasts.Show(ok ? "Embedding model installed" : "Embedding model install failed",
                    ok ? "Embedding model downloaded and configured." : "See diagnostics for details.",
                    ok ? ToastKind.Success : ToastKind.Error,
                    7000);
                await ScanAsync();
                EmbeddingModelProgress = string.Empty;
                IsInstallingEmbeddingModel = false;
                _installCts = null;
                return;
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException)
                {
                    _toasts.Show("Embedding install cancelled", "Installation was cancelled.", ToastKind.Info, 4000);
                }
                else
                {
                    _toasts.Show("Embedding model install failed", ex.Message, ToastKind.Error, 7000);
                }
                IsInstallingEmbeddingModel = false;
                EmbeddingModelProgress = string.Empty;
                _installCts = null;
                return;
            }
        }

        var wantsLlamaDownload = check.Key == "llama-server" && check.FixLabel.StartsWith("Download", StringComparison.OrdinalIgnoreCase);
        if (check.Key == "llama-server-update" || wantsLlamaDownload)
        {
            try
            {
                if (IsInstallingLlamaServer) return;
                IsInstallingLlamaServer = true;
                _installCts = new CancellationTokenSource();
                var progress = new Progress<string>(s => LlamaServerProgress = s);
                var ok = await _doctor.InstallLlamaServerUpdateAsync(progress, _installCts.Token);
                _toasts.Show(ok ? "llama.cpp updated" : "llama.cpp update failed",
                    ok ? "llama-server downloaded and configured." : "See diagnostics for details.",
                    ok ? ToastKind.Success : ToastKind.Error,
                    7000);
                await ScanAsync();
                LlamaServerProgress = string.Empty;
                IsInstallingLlamaServer = false;
                _installCts = null;
                return;
            }
            catch (Exception ex)
            {
                _toasts.Show(ex is OperationCanceledException ? "llama.cpp update cancelled" : "llama.cpp update failed",
                    ex is OperationCanceledException ? "Update was cancelled." : ex.Message,
                    ex is OperationCanceledException ? ToastKind.Info : ToastKind.Error,
                    7000);
                LlamaServerProgress = string.Empty;
                IsInstallingLlamaServer = false;
                _installCts = null;
                return;
            }
        }

        if (RequestNavigate is null)
        {
            _toasts.Show("Navigation unavailable", "Doctor navigation is not configured.", ToastKind.Warning, 4000);
            return;
        }

        var target = check.Category switch
        {
            "Runtime" => "services",
            "RAG" => "rag",
            "System" => "system",
            "Voice" => "settings",
            "Security" => "settings",
            "Storage" => "settings",
            _ => "settings"
        };

        RequestNavigate(target);
    }
}
