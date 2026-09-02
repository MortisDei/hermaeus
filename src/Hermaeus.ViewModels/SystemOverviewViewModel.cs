using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Hermaeus.ViewModels;

public partial class SystemOverviewViewModel : ObservableObject
{
    private readonly ISystemInfoService _system;
    private readonly IToastService _toasts;
    private readonly PrivacyAuditService _privacyAudit;
    private readonly IStartupTimingService? _startupTiming;
    private readonly IResourceCoordinator? _resourceCoordinator;

    public UiBoundCollection<SystemMetricViewModel> Metrics { get; } = [];
    public UiBoundCollection<GpuInfoViewModel> Gpus { get; } = [];
    public UiBoundCollection<ComponentStatusViewModel> Components { get; } = [];
    public UiBoundCollection<PrivacyAuditItemViewModel> PrivacyAuditItems { get; } = [];
    public UiBoundCollection<ResourceConsumerReceiptViewModel> ResourceConsumers { get; } = [];
    public UiBoundCollection<ResourceDeviceReceiptViewModel> ResourceDevices { get; } = [];
    public UiBoundCollection<ResourceUnknownViewModel> ResourceUnknowns { get; } = [];

    [ObservableProperty] private string _privacyAuditSummary = string.Empty;
    [ObservableProperty] private string _status = "Ready.";
    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private SystemSnapshot? _snapshot;
    [ObservableProperty] private string _resourceStatus = "No workload resource snapshot captured.";

    public SystemOverviewViewModel(
        ISystemInfoService system,
        IToastService toasts,
        PrivacyAuditService privacyAudit,
        IStartupTimingService? startupTiming = null,
        IResourceCoordinator? resourceCoordinator = null)
    {
        _system = system;
        _toasts = toasts;
        _privacyAudit = privacyAudit;
        _startupTiming = startupTiming;
        _resourceCoordinator = resourceCoordinator;
        if (_startupTiming is not null)
            _startupTiming.Changed += RefreshStartupBreakdown;
        RefreshStartupBreakdown();
    }

    /// <summary>
    /// r27 01-startup-that-never-waits.md 1.5: the last startup's phases, in
    /// order, with their milliseconds. No target, no rating, no judgement about
    /// whether the number is good; it is the number.
    /// </summary>
    public UiBoundCollection<SystemMetricViewModel> StartupPhases { get; } = [];

    /// <summary>Elapsed-to-healthy per auto-started server, which is no longer part of the startup total.</summary>
    public UiBoundCollection<SystemMetricViewModel> StartupServerStarts { get; } = [];

    [ObservableProperty] private string _startupTotal = string.Empty;
    [ObservableProperty] private bool _hasStartupBreakdown;
    [ObservableProperty] private bool _hasStartupServerStarts;

    public void RefreshStartupBreakdown()
    {
        var last = _startupTiming?.Last;
        StartupPhases.Clear();
        StartupServerStarts.Clear();

        if (last is null)
        {
            StartupTotal = string.Empty;
            HasStartupBreakdown = false;
            HasStartupServerStarts = false;
            return;
        }

        foreach (var phase in last.Phases.Where(p => p.Name != "total"))
            AddPhase(phase, depth: 0);

        foreach (var start in last.ServerStarts)
            StartupServerStarts.Add(new SystemMetricViewModel(
                start.ServerName,
                start.ReachedHealthy ? $"{start.ElapsedMs} ms to healthy" : $"{start.ElapsedMs} ms, did not reach healthy"));

        StartupTotal = $"{last.TotalMs} ms total";
        HasStartupBreakdown = StartupPhases.Count > 0;
        HasStartupServerStarts = StartupServerStarts.Count > 0;
    }

    private void AddPhase(StartupPhase phase, int depth)
    {
        var indent = new string(' ', depth * 4);
        // 5.3: concurrent parts overlap and do not sum to their parent, so the
        // label says so rather than leaving a reader to add them up.
        var suffix = phase.HasChildren && phase.ChildrenRanConcurrently ? " (concurrent)" : string.Empty;
        StartupPhases.Add(new SystemMetricViewModel($"{indent}{phase.Name}{suffix}", $"{phase.Ms} ms"));
        foreach (var child in phase.Children)
            AddPhase(child, depth + 1);
    }

    /// <summary>doc 04 4.1: registered next to the ViewModel that owns the action.</summary>
    public void RegisterCommands(ICommandRegistry registry)
    {
        registry.Register(new AppCommand(
            Id: "system.refresh-snapshot", Title: "Refresh system overview", Area: "System",
            Description: "Recapture hardware, storage and privacy audit info.",
            Keywords: ["system", "refresh", "snapshot", "hardware"], Shortcut: "",
            CanExecute: () => !IsRefreshing,
            DisabledReason: () => "Already refreshing.",
            Execute: () => RefreshCommand.ExecuteAsync(null)));
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
            var ramRatio = Snapshot.TotalMemoryBytes > 0
                ? Math.Clamp(1d - (double)Snapshot.AvailableMemoryBytes / Snapshot.TotalMemoryBytes, 0d, 1d)
                : (double?)null;
            Metrics.Add(new("RAM", $"{FormatBytes(Snapshot.AvailableMemoryBytes)} available / {FormatBytes(Snapshot.TotalMemoryBytes)} total", ramRatio));
            Metrics.Add(new("Process", $"{FormatBytes(Snapshot.ProcessMemoryBytes)} RSS · {FormatBytes(Snapshot.ManagedMemoryBytes)} managed"));
            Metrics.Add(new("Data root", Snapshot.DataRoot));
            var storageRatio = Snapshot.DataRootTotalBytes > 0
                ? Math.Clamp(1d - (double)Snapshot.DataRootFreeBytes / Snapshot.DataRootTotalBytes, 0d, 1d)
                : (double?)null;
            Metrics.Add(new("Storage", $"{FormatBytes(Snapshot.DataRootFreeBytes)} free / {FormatBytes(Snapshot.DataRootTotalBytes)} total", storageRatio));
            Metrics.Add(new("Databases", FormatBytes(Snapshot.DatabaseBytes)));

            Gpus.Clear();
            foreach (var gpu in Snapshot.Gpus)
                Gpus.Add(new GpuInfoViewModel(gpu));

            Components.Clear();
            foreach (var component in Snapshot.Components)
                Components.Add(new ComponentStatusViewModel(component));

            await RefreshResourceSnapshotAsync();

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

    private async Task RefreshResourceSnapshotAsync()
    {
        ResourceConsumers.Clear();
        ResourceDevices.Clear();
        ResourceUnknowns.Clear();
        if (_resourceCoordinator is null)
        {
            ResourceStatus = "Workload resource admission is unavailable.";
            return;
        }

        var resourceSnapshot = await _resourceCoordinator.CaptureSnapshotAsync();
        foreach (var consumer in resourceSnapshot.Consumers)
        {
            var allocations = resourceSnapshot.Allocations
                .Where(allocation => string.Equals(allocation.ConsumerId, consumer.ConsumerId, StringComparison.Ordinal))
                .ToArray();
            var knownComponents = allocations.SelectMany(allocation => allocation.Components)
                .Where(component => component.ObservedBytes.HasValue || component.ReservedBytes.HasValue || component.PredictedBytes.HasValue)
                .ToArray();
            var gpuBytes = knownComponents.Where(component => component.ResourceKind == ResourceKind.DeviceMemory)
                .Sum(ComponentBytes);
            var systemBytes = knownComponents.Where(component => component.ResourceKind == ResourceKind.SystemResidentMemory)
                .Sum(ComponentBytes);
            var unknownCount = allocations.SelectMany(allocation => allocation.Components)
                .Count(component => !component.ObservedBytes.HasValue && !component.ReservedBytes.HasValue && !component.PredictedBytes.HasValue);
            ResourceConsumers.Add(new ResourceConsumerReceiptViewModel(
                consumer.ConsumerId,
                consumer.Kind.ToString(),
                allocations.Length == 0 ? "Registered, not resident" : string.Join(", ", allocations.Select(a => a.LifecycleState)),
                gpuBytes == 0 && !knownComponents.Any(component => component.ResourceKind == ResourceKind.DeviceMemory) ? "Unknown" : FormatBytes(gpuBytes),
                systemBytes == 0 && !knownComponents.Any(component => component.ResourceKind == ResourceKind.SystemResidentMemory) ? "Unknown" : FormatBytes(systemBytes),
                unknownCount == 0 ? "" : $"{unknownCount} component(s) Unknown"));
        }

        foreach (var device in resourceSnapshot.DeviceTotals)
            ResourceDevices.Add(new ResourceDeviceReceiptViewModel(
                device.DeviceId,
                FormatOptionalBytes(device.UsedBytes),
                FormatOptionalBytes(device.CapacityBytes)));

        foreach (var unknown in resourceSnapshot.Unknowns)
            ResourceUnknowns.Add(new ResourceUnknownViewModel(unknown.Code, unknown.Detail, unknown.ConsumerId));

        ResourceStatus = $"Updated {resourceSnapshot.CapturedAtUtc.ToLocalTime():T}; {resourceSnapshot.Consumers.Count} consumer(s), {resourceSnapshot.Unknowns.Count} Unknown observation(s).";
    }

    private static long ComponentBytes(ResourceAllocationComponent component) =>
        component.ObservedBytes ?? component.ReservedBytes ?? component.PredictedBytes ?? 0;

    private static string FormatOptionalBytes(long? bytes) => bytes.HasValue ? FormatBytes(bytes.Value) : "Unknown";

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

public sealed record SystemMetricViewModel(string Name, string Value, double? Ratio = null)
{
    public bool HasRatio => Ratio.HasValue;
    public double ProgressValue => Math.Clamp(Ratio ?? 0, 0, 1) * 100;
}

public sealed record PrivacyAuditItemViewModel(string Name, string Status, string Detail);

public sealed record ResourceConsumerReceiptViewModel(
    string ConsumerId,
    string Kind,
    string State,
    string DeviceMemory,
    string SystemMemory,
    string Unknown);

public sealed record ResourceDeviceReceiptViewModel(string DeviceId, string Used, string Capacity);

public sealed record ResourceUnknownViewModel(string Code, string Detail, string? ConsumerId);

public sealed class GpuInfoViewModel
{
    private readonly GpuInfo _gpu;
    public string Name => _gpu.Name;
    public string Provider => _gpu.Provider;
    public string Status => _gpu.Status;
    public string Memory => _gpu.MemoryUsedBytes.HasValue && _gpu.MemoryTotalBytes.HasValue
        ? $"{SystemOverviewViewModel.FormatBytes(_gpu.MemoryUsedBytes.Value)} / {SystemOverviewViewModel.FormatBytes(_gpu.MemoryTotalBytes.Value)}"
        : _gpu.MemoryTotalBytes.HasValue
            ? $"{SystemOverviewViewModel.FormatBytes(_gpu.MemoryTotalBytes.Value)} total"
            : "VRAM unavailable";
    public bool HasMemoryRatio => _gpu.MemoryUsedBytes.HasValue && _gpu.MemoryTotalBytes is > 0;
    public double MemoryProgressValue => HasMemoryRatio
        ? Math.Clamp((double)_gpu.MemoryUsedBytes!.Value / _gpu.MemoryTotalBytes!.Value, 0, 1) * 100
        : 0;
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
