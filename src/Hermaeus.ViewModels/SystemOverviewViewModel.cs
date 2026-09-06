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
    public UiBoundCollection<ResourceReleaseReceiptViewModel> ResourceReleaseReceipts { get; } = [];

    [ObservableProperty] private string _privacyAuditSummary = string.Empty;
    [ObservableProperty] private string _status = "Ready.";
    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private SystemSnapshot? _snapshot;
    [ObservableProperty] private string _healthSummary = "No component health snapshot captured.";
    [ObservableProperty] private string _resourceStatus = "No workload resource snapshot captured.";
    [ObservableProperty] private bool _hasResourceReleaseReceipts;
    [ObservableProperty] private string _activeDetail = "overview";

    public bool IsOverviewDetailVisible => ActiveDetail == "overview";
    public bool IsStartupDetailVisible => ActiveDetail == "startup";
    public bool IsPrivacyDetailVisible => ActiveDetail == "privacy";

    partial void OnActiveDetailChanged(string value)
    {
        OnPropertyChanged(nameof(IsOverviewDetailVisible));
        OnPropertyChanged(nameof(IsStartupDetailVisible));
        OnPropertyChanged(nameof(IsPrivacyDetailVisible));
    }

    [RelayCommand]
    private void ShowOverviewDetail() => ActiveDetail = "overview";

    [RelayCommand]
    private void ShowStartupDetail() => ActiveDetail = "startup";

    [RelayCommand]
    private void ShowPrivacyDetail() => ActiveDetail = "privacy";

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
            HealthSummary = BuildHealthSummary(Snapshot.Components);

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
        ResourceReleaseReceipts.Clear();
        HasResourceReleaseReceipts = false;
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
            var components = allocations.SelectMany(allocation => allocation.Components).ToArray();
            var gpu = DescribeResourceMetric(resourceSnapshot, consumer.ConsumerId, allocations, components, ResourceKind.DeviceMemory);
            var system = DescribeResourceMetric(resourceSnapshot, consumer.ConsumerId, allocations, components, ResourceKind.SystemResidentMemory);
            var unknownCount = components
                .Count(component => !component.ObservedBytes.HasValue && !component.ReservedBytes.HasValue && !component.PredictedBytes.HasValue);
            var state = allocations.Length == 0 && consumer.Kind == ResourceConsumerKind.Reranker
                ? "Registered, lazy until a RAG query needs reranking"
                : allocations.Length == 0
                    ? "Registered, not resident"
                    : string.Join(", ", allocations.Select(a => a.LifecycleState));
            ResourceConsumers.Add(new ResourceConsumerReceiptViewModel(
                consumer.ConsumerId,
                consumer.Kind.ToString(),
                state,
                gpu.Display,
                system.Display,
                unknownCount == 0 ? "" : gpu.HasObservedEvidence || system.HasObservedEvidence
                    ? $"{unknownCount} component(s) attribution incomplete"
                    : $"{unknownCount} component(s) not observed"));
        }

        foreach (var device in resourceSnapshot.DeviceTotals)
            ResourceDevices.Add(new ResourceDeviceReceiptViewModel(
                device.DeviceId,
                FormatOptionalBytes(device.UsedBytes),
                FormatOptionalBytes(device.CapacityBytes)));

        foreach (var unknown in resourceSnapshot.Unknowns)
            ResourceUnknowns.Add(new ResourceUnknownViewModel(unknown.Code, unknown.Detail, unknown.ConsumerId));

        foreach (var release in _resourceCoordinator.RecentReleaseReceipts)
            ResourceReleaseReceipts.Add(new ResourceReleaseReceiptViewModel(
                release.ConsumerId,
                release.Reason,
                release.ReleasedAtUtc.ToLocalTime().ToString("T")));
        HasResourceReleaseReceipts = ResourceReleaseReceipts.Count > 0;

        ResourceStatus = $"Updated {resourceSnapshot.CapturedAtUtc.ToLocalTime():T}; {resourceSnapshot.Consumers.Count} consumer(s), {resourceSnapshot.Unknowns.Count} evidence gap(s), {ResourceReleaseReceipts.Count} recent release receipt(s).";
    }

    private static ResourceMetricSummary DescribeResourceMetric(
        ResourceSnapshot snapshot,
        string consumerId,
        IReadOnlyList<ResourceAllocation> allocations,
        IReadOnlyList<ResourceAllocationComponent> components,
        ResourceKind kind)
    {
        var parentObservations = snapshot.AuthoritativeObservations
            .Where(observation => string.Equals(observation.ConsumerId, consumerId, StringComparison.Ordinal)
                && observation.Scope is ResourceObservationScope.Consumer or ResourceObservationScope.Allocation
                && observation.ResourceKind == kind
                && observation.ValueBytes.HasValue
                && observation.TrustState != ResourceObservationTrustState.Unknown)
            .ToArray();
        var consumerObservations = parentObservations
            .Where(observation => observation.Scope == ResourceObservationScope.Consumer)
            .ToArray();
        var selectedParentObservations = consumerObservations.Length > 0
            ? consumerObservations
            : parentObservations.Where(observation => observation.Scope == ResourceObservationScope.Allocation).ToArray();
        if (selectedParentObservations.Length > 0)
        {
            var observedBytes = selectedParentObservations.Sum(observation => observation.ValueBytes!.Value);
            return new ResourceMetricSummary($"{FormatResourceBytes(observedBytes)} observed", HasObservedEvidence: true);
        }

        var observedComponents = components
            .Where(component => component.ResourceKind == kind && component.ObservedBytes.HasValue)
            .ToArray();
        if (observedComponents.Length > 0)
        {
            var observedBytes = observedComponents.Sum(component => component.ObservedBytes!.Value);
            return new ResourceMetricSummary($"{FormatResourceBytes(observedBytes)} observed", HasObservedEvidence: true);
        }

        var plannedComponents = components
            .Where(component => component.ResourceKind == kind && !component.ObservedBytes.HasValue
                && (component.ReservedBytes.HasValue || component.PredictedBytes.HasValue))
            .ToArray();
        if (plannedComponents.Length > 0)
        {
            var plannedBytes = plannedComponents.Sum(component => component.ReservedBytes ?? component.PredictedBytes ?? 0);
            return new ResourceMetricSummary($"{FormatResourceBytes(plannedBytes)} planned", HasObservedEvidence: false);
        }

        return allocations.Count == 0
            ? new ResourceMetricSummary("Not resident", HasObservedEvidence: false)
            : new ResourceMetricSummary("Not observed", HasObservedEvidence: false);
    }

    private static string FormatResourceBytes(long bytes) => bytes == 0 ? "0 B" : FormatBytes(bytes);

    private sealed record ResourceMetricSummary(string Display, bool HasObservedEvidence);

    private static string BuildHealthSummary(IReadOnlyList<ComponentStatus> components)
    {
        var healthy = components.Count(component => component.Status is "Ready" or "Present" or "OK");
        var attention = components.Count(component => component.Status is "Missing" or "Not set" or "Low");
        var unknown = components.Count - healthy - attention;
        return $"{healthy} healthy · {attention} need attention · {unknown} Unknown";
    }

    private static string FormatOptionalBytes(long? bytes) => bytes.HasValue ? FormatResourceBytes(bytes.Value) : "Unknown";

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

public sealed record ResourceReleaseReceiptViewModel(string ConsumerId, string Reason, string ReleasedAt);

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
