using System.Linq;
using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Services;
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
    private readonly object _embeddingLogFileLock = new();

    public UiBoundCollection<DoctorCheck> Checks { get; } = [];

    public Action<string>? RequestCopyToClipboard { get; set; }
    public Action<string>? RequestNavigate { get; set; }

    /// <summary>
    /// Asks the user to confirm a titled action (r14 3.2 prune). When unset,
    /// confirmation is treated as declined so nothing is ever deleted without
    /// an explicit yes.
    /// </summary>
    public Func<string, string, Task<bool>>? RequestConfirmAsync { get; set; }

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

        // r12 03-runtime-vm-correctness.md 3.9: one fire-and-forget Task.Run
        // per progress line let concurrent writes interleave in the log
        // file. A progress callback fires often enough, but not on a hot
        // per-render path, so a lock-serialized synchronous append is fine.
        try
        {
            var root = Aether.Services.SettingsService.ResolveDataRoot(_settingsService.Settings);
            var dir = Path.Combine(root, "logs");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "embedding_downloads.log");
            var entry = $"{DateTime.UtcNow:O} {s}{Environment.NewLine}";
            lock (_embeddingLogFileLock)
            {
                File.AppendAllText(path, entry);
            }
        }
        catch { }
    }

    /// <summary>
    /// r12 03-runtime-vm-correctness.md 3.9: the four install actions below
    /// were copy-pasted, differing only in the busy flag, the progress
    /// setter, the installer call, and a couple of strings. One helper now
    /// owns the shared shape (busy guard, CTS lifecycle including disposal,
    /// success/failure/cancellation toasts, post-install rescan).
    /// </summary>
    private async Task RunInstallAsync(
        Func<bool> isBusy,
        Action<bool> setBusy,
        Action<string> setProgress,
        Func<IProgress<string>, CancellationToken, Task<bool>> installAsync,
        string successTitle,
        string successBody,
        string failureTitle,
        string cancelledTitle)
    {
        if (isBusy()) return;
        setBusy(true);
        _installCts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<string>(setProgress);
            var ok = await installAsync(progress, _installCts.Token);
            _toasts.Show(ok ? successTitle : failureTitle,
                ok ? successBody : "See diagnostics for details.",
                ok ? ToastKind.Success : ToastKind.Error,
                7000);
            await ScanAsync();
        }
        catch (Exception ex)
        {
            _toasts.Show(ex is OperationCanceledException ? cancelledTitle : failureTitle,
                ex is OperationCanceledException ? "Installation was cancelled." : ex.Message,
                ex is OperationCanceledException ? ToastKind.Info : ToastKind.Error,
                7000);
        }
        finally
        {
            setProgress(string.Empty);
            setBusy(false);
            _installCts?.Dispose();
            _installCts = null;
        }
    }

    /// <summary>
    /// Updates llama.cpp via the detailed path so the flow can offer to prune
    /// superseded version directories (r14 3.2) and hint that running servers
    /// need a restart to pick up the new binary (r14 3.3).
    /// </summary>
    private async Task RunLlamaUpdateAsync()
    {
        if (IsInstallingLlamaServer) return;
        IsInstallingLlamaServer = true;
        _installCts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<string>(s => LlamaServerProgress = s);
            var outcome = await _doctor.InstallLlamaServerUpdateDetailedAsync(progress, _installCts.Token);
            if (!outcome.Success)
            {
                _toasts.Show("llama.cpp update failed", "See diagnostics for details.", ToastKind.Error, 7000);
                return;
            }

            _toasts.Show("llama.cpp updated",
                "Running servers keep the old build until you restart them from Services.",
                ToastKind.Success, 8000);

            if (outcome.PrunableVersionDirectories.Count > 0 && RequestConfirmAsync is not null)
            {
                var reclaimable = SystemInfoService.FormatBytes(
                    outcome.PrunableVersionDirectories.Sum(LlamaServerSetupService.DirectorySizeBytes));
                var confirmed = await RequestConfirmAsync(
                    "Remove old llama.cpp versions?",
                    $"{outcome.PrunableVersionDirectories.Count} superseded version(s) can be removed to reclaim about {reclaimable}. The current and previous versions are kept.");
                if (confirmed)
                {
                    var freed = _doctor.PruneLlamaServerVersions(outcome.PrunableVersionDirectories);
                    _toasts.Show("Old versions removed", $"Reclaimed {SystemInfoService.FormatBytes(freed)}.", ToastKind.Info, 5000);
                }
            }

            await ScanAsync();
        }
        catch (Exception ex)
        {
            _toasts.Show(ex is OperationCanceledException ? "llama.cpp update cancelled" : "llama.cpp update failed",
                ex is OperationCanceledException ? "Installation was cancelled." : ex.Message,
                ex is OperationCanceledException ? ToastKind.Info : ToastKind.Error,
                7000);
        }
        finally
        {
            LlamaServerProgress = string.Empty;
            IsInstallingLlamaServer = false;
            _installCts?.Dispose();
            _installCts = null;
        }
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
            await RunInstallAsync(
                () => IsInstallingReranker,
                v => IsInstallingReranker = v,
                s => RerankerProgress = s,
                (p, ct) => _doctor.InstallRerankerAssetsAsync(p, ct),
                "Reranker installed", "Reranker assets installed.",
                "Reranker install failed", "Reranker install cancelled");
            return;
        }

        if (check.Key == "kokoro-native")
        {
            await RunInstallAsync(
                () => IsInstallingNativeKokoro,
                v => IsInstallingNativeKokoro = v,
                s => NativeKokoroProgress = s,
                (p, ct) => _doctor.InstallNativeKokoroAssetsAsync(p, ct),
                "Kokoro (native) installed", "Kokoro native ONNX model and voices installed.",
                "Kokoro (native) install failed", "Kokoro (native) install cancelled");
            return;
        }

        if (check.Key is "embedding-model" or "embedding-model-update")
        {
            await RunInstallAsync(
                () => IsInstallingEmbeddingModel,
                v => IsInstallingEmbeddingModel = v,
                HandleEmbeddingProgress,
                (p, ct) => _doctor.InstallEmbeddingModelAsync(p, ct),
                "Embedding model installed", "Embedding model downloaded and configured.",
                "Embedding model install failed", "Embedding install cancelled");
            return;
        }

        var wantsLlamaDownload = check.Key == "llama-server" && check.FixLabel.StartsWith("Download", StringComparison.OrdinalIgnoreCase);
        if (check.Key == "llama-server-update" || wantsLlamaDownload)
        {
            await RunLlamaUpdateAsync();
            return;
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
