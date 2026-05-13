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

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _summary = "Run Doctor to scan your environment.";
    [ObservableProperty] private string _lastScanned = string.Empty;

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
    private void RunFix(DoctorCheck? check)
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

        RequestNavigate(target);
    }
}
