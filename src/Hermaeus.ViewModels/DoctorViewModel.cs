using System.Linq;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Hermaeus.ViewModels;

public partial class DoctorViewModel : ObservableObject
{
    private const string HermaeusReleasesPageUrl = "https://github.com/MortisDei/hermaeus/releases/latest";

    private readonly IDoctorService _doctor;
    private readonly IToastService _toasts;
    private readonly ISettingsService _settingsService;
    private readonly IVoiceOrchestrator? _voice;
    private readonly IActivityRecorder? _activity;
    private readonly IRecommendationStore? _recommendationStore;
    private readonly RecommendationApplicationService? _recommendationApplication;
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
    [ObservableProperty] private bool _isInstallingSpeechRecognition;
    [ObservableProperty] private string _speechRecognitionProgress = string.Empty;

    private const int MaxEmbeddingDiagnosticLines = 200;
    private readonly Queue<string> _embeddingLogLines = new();
    private readonly object _embeddingLogFileLock = new();
    private string _lastEmbeddingProgress = string.Empty;

    public UiBoundCollection<DoctorCheck> Checks { get; } = [];

    public Func<string, Task<bool>>? RequestCopyToClipboard { get; set; }
    public Action<string>? RequestNavigate { get; set; }

    /// <summary>
    /// Opens an external URL (e.g. the GitHub releases page) in the user's
    /// default browser. Delegate-injected, like the requests above, so this
    /// stays testable without launching a real browser during a test run.
    /// </summary>
    public Action<string>? RequestOpenUrl { get; set; }

    /// <summary>
    /// Asks the user to confirm a titled action (r14 3.2 prune). When unset,
    /// confirmation is treated as declined so nothing is ever deleted without
    /// an explicit yes.
    /// </summary>
    public Func<string, string, Task<bool>>? RequestConfirmAsync { get; set; }

    /// <summary>
    /// r19 2.2: set by MainWindowViewModel to bridge to ServicesViewModel
    /// (Doctor has no server-process knowledge of its own). Stops every
    /// running llama-server ahead of an update and returns the ids to
    /// restart afterward; restarting re-syncs each server's executable path
    /// from the just-updated config first.
    /// </summary>
    public Func<IReadOnlyList<string>>? RequestStopRunningLlamaServersForUpdate { get; set; }
    public Func<IReadOnlyList<string>, Task>? RequestRestartServers { get; set; }

    /// <summary>
    /// Re-syncs every Services row's displayed executable path after a
    /// successful llama.cpp update, since the update rewrites every managed
    /// server's path unconditionally, not just the ones that were running
    /// (and so get resynced anyway by <see cref="RequestRestartServers"/>).
    /// </summary>
    public Action? RequestSyncServerExecutablePaths { get; set; }

    public DoctorViewModel(IDoctorService doctor, IToastService toasts, ISettingsService settings, IVoiceOrchestrator? voice = null, IActivityRecorder? activity = null,
        IRecommendationStore? recommendationStore = null, RecommendationApplicationService? recommendationApplication = null)
    {
        _doctor = doctor;
        _toasts = toasts;
        _settingsService = settings;
        _voice = voice;
        _activity = activity;
        _recommendationStore = recommendationStore;
        _recommendationApplication = recommendationApplication;
    }

    public UiBoundCollection<RecommendationReviewViewModel> Recommendations { get; } = [];
    public bool HasRecommendations => Recommendations.Count > 0;

    public async Task RefreshRecommendationsAsync(CancellationToken ct = default)
    {
        if (_recommendationStore is null || _recommendationApplication is null)
            return;
        var rows = await _recommendationStore.QueryAsync(new RecommendationQuery { Limit = 32 }, ct);
        Recommendations.Clear();
        foreach (var row in rows.Where(value => value.Status is RecommendationStatus.Current or RecommendationStatus.Accepted
                     && value.Kind == RecommendationKind.ResourceConflict))
        {
            Recommendations.Add(new RecommendationReviewViewModel(
                row, _recommendationApplication, null, () => RefreshRecommendationsAsync(), RequestNavigate));
        }
        OnPropertyChanged(nameof(HasRecommendations));
    }

    /// <summary>doc 04 4.1: registered next to the ViewModel that owns the action.</summary>
    public void RegisterCommands(ICommandRegistry registry)
    {
        registry.Register(new AppCommand(
            Id: "doctor.run-scan", Title: "Run Doctor scan", Area: "Doctor",
            Description: "Check the local environment for problems.",
            Keywords: ["doctor", "scan", "diagnose", "check"], Shortcut: "",
            CanExecute: () => !IsScanning,
            DisabledReason: () => "A scan is already running.",
            Execute: () => ScanCommand.ExecuteAsync(null)));

        registry.Register(new AppCommand(
            Id: "doctor.copy-all-diagnostics", Title: "Copy all diagnostics", Area: "Doctor",
            Description: "Copy every Doctor check result to the clipboard.",
            Keywords: ["doctor", "copy", "diagnostics"], Shortcut: "",
            CanExecute: () => Checks.Count > 0,
            DisabledReason: () => "Run a scan first.",
            Execute: () => CopyAllDiagnosticsCommand.ExecuteAsync(null)));
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
            LastScanned = $"Last scan: {LocalTimeFormat.DateTimeMinutes(report.ScannedAt)}";
            if (showIssueToast)
                ShowStartupIssueToast(report);
            NarrateCriticalIssues(report);
            AlertOnNewUntunedModels(report);

            var errors = report.Checks.Count(c => c.Status == DoctorCheckStatus.Error);
            var warnings = report.Checks.Count(c => c.Status == DoctorCheckStatus.Warning);
            _ = _activity?.RecordAsync("doctor.scan", string.Empty,
                errors > 0 ? ActivityOutcome.Failed : warnings > 0 ? ActivityOutcome.Partial : ActivityOutcome.Succeeded,
                "Doctor scan completed",
                errors + warnings > 0 ? $"{errors} error(s), {warnings} warning(s)" : string.Empty);
        }
        catch (Exception ex)
        {
            Summary = "Doctor scan failed.";
            _toasts.Show("Doctor scan failed", ex.Message, ToastKind.Error, 7000);
            _ = _activity?.RecordAsync("doctor.scan", string.Empty, ActivityOutcome.Failed, "Doctor scan failed", ex.Message);
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
            _toasts.Show("Hermaeus Doctor found problems", report.Summary, ToastKind.Error, 9000);
            return;
        }

        if (report.WarningCount > 0)
            _toasts.Show("Hermaeus Doctor found warnings", report.Summary, ToastKind.Warning, 9000);
    }

    [RelayCommand]
    private async Task CopyDiagnosticsAsync(DoctorCheck? check)
    {
        if (check is null) return;
        if (RequestCopyToClipboard is null) return;
        if (await RequestCopyToClipboard(check.Diagnostics))
            _toasts.Show("Diagnostics copied", check.Title, ToastKind.Success, 3000);
        else
            _toasts.Show("Could not copy diagnostics", "The clipboard was unavailable.", ToastKind.Warning, 3000);
    }

    [RelayCommand]
    private async Task CopyAllDiagnosticsAsync()
    {
        if (RequestCopyToClipboard is null) return;
        var payload = string.Join("\n\n", Checks.Select(c =>
            $"[{c.StatusLabel}] {c.Title}\n{c.Summary}\n{c.Detail}\n{c.Diagnostics}"));
        if (await RequestCopyToClipboard(payload))
            _toasts.Show("Diagnostics copied", "Doctor summary copied to clipboard.", ToastKind.Success, 3000);
        else
            _toasts.Show("Could not copy diagnostics", "The clipboard was unavailable.", ToastKind.Warning, 3000);
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
        if (string.Equals(s, _lastEmbeddingProgress, StringComparison.Ordinal))
            return;
        _lastEmbeddingProgress = s;
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

        _embeddingLogLines.Enqueue($"{DateTime.UtcNow:O} {s}");
        while (_embeddingLogLines.Count > MaxEmbeddingDiagnosticLines)
            _embeddingLogLines.Dequeue();

        // update the corresponding check diagnostics (if present)
        try
        {
            var idx = Checks.ToList().FindIndex(c => c.Key == "embedding-model");
            if (idx >= 0)
            {
                var existing = Checks[idx];
                var updated = new DoctorCheck(existing.Key, existing.Title, existing.Status, existing.Summary, existing.Detail, existing.FixLabel, existing.CanFix, string.Join(Environment.NewLine, _embeddingLogLines), existing.Category);
                // replace item to notify UI
                Checks[idx] = updated;
            }
        }
        catch { }

        // ModelDownloadService limits progress reports to four per second and
        // the embedding installer further coalesces to whole percentages. A
        // lock-serialized append therefore preserves useful detail without
        // turning each 80 KB network chunk into synchronous UI-thread I/O.
        try
        {
            var root = Hermaeus.Services.SettingsService.ResolveDataRoot(_settingsService.Settings);
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
    /// superseded version directories (r14 3.2). r19 2.2: running servers are
    /// stopped before the update and restarted against the new binary
    /// afterward (or the unchanged one, on failure) rather than left on the
    /// old build until the user notices and restarts them manually.
    /// </summary>
    private async Task RunLlamaUpdateAsync()
    {
        if (IsInstallingLlamaServer) return;
        IsInstallingLlamaServer = true;
        _installCts = new CancellationTokenSource();
        var stoppedServerIds = Array.Empty<string>() as IReadOnlyList<string>;
        try
        {
            if (RequestStopRunningLlamaServersForUpdate is not null)
            {
                stoppedServerIds = RequestStopRunningLlamaServersForUpdate();
                if (stoppedServerIds.Count > 0)
                    LlamaServerProgress = $"Stopping {stoppedServerIds.Count} running server(s) for update...";
            }

            var progress = new Progress<string>(s => LlamaServerProgress = s);
            var outcome = await _doctor.InstallLlamaServerUpdateDetailedAsync(progress, _installCts.Token);
            if (!outcome.Success)
            {
                _toasts.Show("llama.cpp update failed", "See diagnostics for details.", ToastKind.Error, 7000);
                return;
            }

            _toasts.Show("llama.cpp updated",
                stoppedServerIds.Count > 0
                    ? "Restarting the servers that were stopped for the update."
                    : "No running servers needed to be stopped.",
                ToastKind.Success, 8000);

            // Every managed server's ExecutablePath was rewritten on disk,
            // including servers that were not running (and so are not in
            // stoppedServerIds); refresh all rows' displayed path now rather
            // than leaving the not-running ones stale until app restart.
            RequestSyncServerExecutablePaths?.Invoke();

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
            if (stoppedServerIds.Count > 0 && RequestRestartServers is not null)
            {
                try { await RequestRestartServers(stoppedServerIds); }
                catch (Exception ex)
                {
                    _toasts.Show("Could not restart a server", ex.Message, ToastKind.Warning, 7000);
                }
            }
            await ScanAsync();
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

        if (check.Key == "speech-recognition")
        {
            await RunInstallAsync(
                () => IsInstallingSpeechRecognition,
                v => IsInstallingSpeechRecognition = v,
                s => SpeechRecognitionProgress = s,
                (p, ct) => _doctor.InstallSpeechRecognitionAssetsAsync(p, ct),
                "Speech recognition installed", "Speech recognition ONNX model installed.",
                "Speech recognition install failed", "Speech recognition install cancelled");
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

        if (check.Key == "app-update")
        {
            if (RequestOpenUrl is null)
            {
                _toasts.Show("Cannot open browser", "Opening a URL is not configured.", ToastKind.Warning, 4000);
                return;
            }

            RequestOpenUrl(HermaeusReleasesPageUrl);
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
