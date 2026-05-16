using System.Collections.ObjectModel;
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
    private CancellationTokenSource? _installCts;

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _summary = "Run Doctor to scan your environment.";
    [ObservableProperty] private string _lastScanned = string.Empty;
    [ObservableProperty] private bool _isInstallingReranker;
    [ObservableProperty] private string _rerankerProgress = string.Empty;
    [ObservableProperty] private bool _isInstallingEmbeddingModel;
    [ObservableProperty] private string _embeddingModelProgress = string.Empty;

    public ObservableCollection<DoctorCheck> Checks { get; } = [];

    public Action<string>? RequestCopyToClipboard { get; set; }
    public Action<string>? RequestNavigate { get; set; }

    public DoctorViewModel(IDoctorService doctor, IToastService toasts)
    {
        _doctor = doctor;
        _toasts = toasts;
    }

    [RelayCommand]
    private async Task ScanAsync()
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

    [RelayCommand]
    private async Task RunFix(DoctorCheck? check)
    {
        if (check is null || !check.CanFix)
        {
            _toasts.Show("No fix available", "This check does not provide an automated fix yet.", ToastKind.Info, 4000);
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

        if (check.Key == "embedding-model")
        {
            try
            {
                if (IsInstallingEmbeddingModel) return;
                IsInstallingEmbeddingModel = true;
                var progress = new Progress<string>(s => EmbeddingModelProgress = s);
                var ok = await _doctor.InstallEmbeddingModelAsync(progress);
                _toasts.Show(ok ? "Embedding model installed" : "Embedding model install failed",
                    ok ? "Embedding model downloaded and configured." : "See diagnostics for details.",
                    ok ? ToastKind.Success : ToastKind.Error,
                    7000);
                await ScanAsync();
                EmbeddingModelProgress = string.Empty;
                IsInstallingEmbeddingModel = false;
                return;
            }
            catch (Exception ex)
            {
                _toasts.Show("Embedding model install failed", ex.Message, ToastKind.Error, 7000);
                IsInstallingEmbeddingModel = false;
                EmbeddingModelProgress = string.Empty;
                return;
            }
        }

        RequestNavigate(target);
    }
}
