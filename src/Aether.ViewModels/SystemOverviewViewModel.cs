using System.Collections.ObjectModel;
using Aether.Core.Models;
using Aether.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Aether.ViewModels;

public partial class SystemOverviewViewModel : ObservableObject
{
    private readonly ISystemInfoService _system;
    private readonly IToastService _toasts;
    private readonly ISettingsService _settings;
    private readonly ISecretStore _secrets;
    private readonly IRuntimeLogService _logs;

    public ObservableCollection<SystemMetricViewModel> Metrics { get; } = [];
    public ObservableCollection<GpuInfoViewModel> Gpus { get; } = [];
    public ObservableCollection<ComponentStatusViewModel> Components { get; } = [];
    public ObservableCollection<PrivacyAuditItemViewModel> PrivacyAuditItems { get; } = [];

    [ObservableProperty] private string _status = "Ready.";
    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private SystemSnapshot? _snapshot;

    public SystemOverviewViewModel(ISystemInfoService system, IToastService toasts, ISettingsService settings, ISecretStore secrets, IRuntimeLogService logs)
    {
        _system = system;
        _toasts = toasts;
        _settings = settings;
        _secrets = secrets;
        _logs = logs;
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
        PrivacyAuditItems.Clear();
        var settings = _settings.Settings;
        var openAiRemote = settings.Llm.OpenAiEnabled || settings.VoiceProviderConfigs.ContainsKey("OpenAI") || settings.Tts.VoiceProvider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase);
        PrivacyAuditItems.Add(new PrivacyAuditItemViewModel(
            "Remote providers",
            openAiRemote ? "Review" : "Local",
            openAiRemote
                ? $"OpenAI-compatible endpoint configured at {settings.Llm.OpenAiBaseUrl}. Prompts may leave the machine when selected."
                : "No enabled remote chat provider detected in settings."));

        PrivacyAuditItems.Add(new PrivacyAuditItemViewModel(
            "Local providers",
            "Local",
            $"llama.cpp {(settings.Llm.LlamaCppEnabled ? "enabled" : "disabled")}; TTS provider {settings.Tts.VoiceProvider}; RAG {(settings.Rag.Enabled ? "enabled" : "available")}."));

        var exposedServers = settings.ManagedServers
            .Where(HasNetworkExposureFlag)
            .ToList();
        PrivacyAuditItems.Add(new PrivacyAuditItemViewModel(
            "Exposed local servers",
            exposedServers.Count == 0 ? "Local only" : "Warning",
            exposedServers.Count == 0
                ? "Managed llama-server entries do not include network-facing host flags."
                : string.Join("; ", exposedServers.Select(s => $"{s.Name} port {s.Port}: {s.ExtraArgs.Trim()}"))));

        var secretBackend = await _secrets.BackendLabelAsync();
        PrivacyAuditItems.Add(new PrivacyAuditItemViewModel(
            "Secret health",
            secretBackend.Contains("fallback", StringComparison.OrdinalIgnoreCase) ? "Fallback" : "Protected",
            $"Secret backend: {secretBackend}."));

        PrivacyAuditItems.Add(new PrivacyAuditItemViewModel(
            "Log redaction",
            "Enabled",
            $"{_logs.GetEntries().Count} in-memory runtime log entries. Diagnostics export uses redaction services."));

        PrivacyAuditItems.Add(new PrivacyAuditItemViewModel(
            "Data root backup",
            Directory.Exists(settings.DataManagement.DataRootDirectory) ? "Configured" : "Needs setup",
            string.IsNullOrWhiteSpace(settings.DataManagement.DataRootDirectory)
                ? "Data root is using default resolution. Configure and back it up before relying on long-term history."
                : settings.DataManagement.DataRootDirectory));

        PrivacyAuditItems.Add(new PrivacyAuditItemViewModel(
            "Features that may send data remotely",
            openAiRemote ? "Review" : "Local",
            "Remote chat/voice providers can send prompt, document, or voice data outside the local machine when explicitly configured. RAG web ingest remains dataset-scoped and approval driven."));
    }

    private static bool HasNetworkExposureFlag(ServerConfig server)
    {
        var args = server.ExtraArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("--listen", StringComparison.OrdinalIgnoreCase))
                return true;

            if ((arg.Equals("--host", StringComparison.OrdinalIgnoreCase) || arg.Equals("--host-address", StringComparison.OrdinalIgnoreCase))
                && i + 1 < args.Length)
            {
                var value = args[i + 1];
                if (!value.Equals("127.0.0.1", StringComparison.Ordinal)
                    && !value.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                    && !value.Equals("::1", StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
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
