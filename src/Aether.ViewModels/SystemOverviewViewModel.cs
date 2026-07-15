using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

public partial class SystemOverviewViewModel : ObservableObject
{
    private readonly ISystemInfoService _system;
    private readonly IToastService _toasts;
    private readonly PrivacyAuditService _privacyAudit;

    public UiBoundCollection<SystemMetricViewModel> Metrics { get; } = [];
    public UiBoundCollection<GpuInfoViewModel> Gpus { get; } = [];
    public UiBoundCollection<ComponentStatusViewModel> Components { get; } = [];
    public UiBoundCollection<PrivacyAuditItemViewModel> PrivacyAuditItems { get; } = [];

    [ObservableProperty] private string _privacyAuditSummary = string.Empty;
    [ObservableProperty] private string _status = "Ready.";
    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private SystemSnapshot? _snapshot;

    public SystemOverviewViewModel(ISystemInfoService system, IToastService toasts, PrivacyAuditService privacyAudit)
    {
        _system = system;
        _toasts = toasts;
        _privacyAudit = privacyAudit;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsRefreshing = true;
        try
        {
            Snapshot = await _system.CaptureAsync();
            Metrics.Clear();
            Metrics.Add(new("App", Snapshot.AppVersion));
            Metrics.Add(new("OS", $"{Snapshot.OSDescription} ({Snapshot.Architecture})"));
            Metrics.Add(new("CPU", $"{Snapshot.CpuName} · {Snapshot.ProcessorCount} threads"));
            Metrics.Add(new("RAM", $"{FormatBytes(Snapshot.AvailableMemoryBytes)} available / {FormatBytes(Snapshot.TotalMemoryBytes)} total"));
            Metrics.Add(new("Process", $"{FormatBytes(Snapshot.ProcessMemoryBytes)} RSS · {FormatBytes(Snapshot.ManagedMemoryBytes)} managed"));
            Metrics.Add(new("Data root", Snapshot.DataRoot));
            Metrics.Add(new("Storage", $"{FormatBytes(Snapshot.DataRootFreeBytes)} free / {FormatBytes(Snapshot.DataRootTotalBytes)} total"));
            Metrics.Add(new("Databases", FormatBytes(Snapshot.DatabaseBytes)));

            Gpus.Clear();
            foreach (var gpu in Snapshot.Gpus)
                Gpus.Add(new GpuInfoViewModel(gpu));

            Components.Clear();
            foreach (var component in Snapshot.Components)
                Components.Add(new ComponentStatusViewModel(component));

            await RefreshPrivacyAuditAsync();

            Status = $"Updated {Snapshot.CapturedAt.ToLocalTime():T}.";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            _toasts.Show("System overview failed", ex.Message, ToastKind.Warning, 7000);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private async Task RefreshPrivacyAuditAsync()
    {
        var items = await _privacyAudit.ScanAsync();
        PrivacyAuditItems.Clear();
        foreach (var item in items)
            PrivacyAuditItems.Add(new PrivacyAuditItemViewModel(item.Name, item.Status, item.Detail));

        var count = await _privacyAudit.CountOutboundDestinationsAsync();
        PrivacyAuditSummary = count == 0
            ? "0 configured outbound destinations. Nothing is currently set up to leave this machine."
            : $"{count} configured outbound destination{(count == 1 ? "" : "s")}.";
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "unavailable";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }
}

public sealed record SystemMetricViewModel(string Name, string Value);

public sealed record PrivacyAuditItemViewModel(string Name, string Status, string Detail);

public sealed class GpuInfoViewModel
{
    private readonly GpuInfo _gpu;
    public string Name => _gpu.Name;
    public string Provider => _gpu.Provider;
    public string Status => _gpu.Status;
    public string Memory => _gpu.MemoryUsedBytes.HasValue && _gpu.MemoryTotalBytes.HasValue
        ? $"{SystemOverviewViewModel.FormatBytes(_gpu.MemoryUsedBytes.Value)} / {SystemOverviewViewModel.FormatBytes(_gpu.MemoryTotalBytes.Value)}"
        : "VRAM unavailable";
    public GpuInfoViewModel(GpuInfo gpu) => _gpu = gpu;
}

public sealed class ComponentStatusViewModel
{
    private readonly ComponentStatus _component;
    public string Name => _component.Name;
    public string Status => _component.Status;
    public string Detail => _component.Detail;
    public ComponentStatusViewModel(ComponentStatus component) => _component = component;
}
