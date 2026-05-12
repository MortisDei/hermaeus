using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Aether.Core.Models;
using Aether.Core.Services;

namespace Aether.Services;

public sealed class SystemInfoService : ISystemInfoService
{
    private readonly ISettingsService _settings;
    private readonly ISecretStore? _secrets;

    public SystemInfoService(ISettingsService settings, ISecretStore? secrets = null)
    {
        _settings = settings;
        _secrets = secrets;
    }

    public async Task<SystemSnapshot> CaptureAsync(CancellationToken ct = default)
    {
        var dataRoot = SettingsService.ResolveDataRoot(_settings.Settings);
        Directory.CreateDirectory(dataRoot);
        var drive = new DriveInfo(Path.GetPathRoot(dataRoot)!);
        var process = Process.GetCurrentProcess();
        var snapshot = new SystemSnapshot
        {
            CapturedAt = DateTime.UtcNow,
            AppVersion = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? typeof(SystemInfoService).Assembly.GetName().Version?.ToString()
                ?? "unknown",
            OSDescription = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.OSArchitecture.ToString(),
            ProcessorCount = Environment.ProcessorCount,
            CpuName = await GetCpuNameAsync(ct),
            TotalMemoryBytes = GetTotalMemoryBytes(),
            AvailableMemoryBytes = GetAvailableMemoryBytes(),
            ProcessMemoryBytes = process.WorkingSet64,
            ManagedMemoryBytes = GC.GetTotalMemory(forceFullCollection: false),
            DataRoot = dataRoot,
            DataRootTotalBytes = drive.TotalSize,
            DataRootFreeBytes = drive.AvailableFreeSpace,
            DatabaseBytes = Directory.EnumerateFiles(dataRoot, "*.db*", SearchOption.TopDirectoryOnly)
                .Sum(f => new FileInfo(f).Length)
        };

        snapshot.Gpus.AddRange(await GetGpusAsync(ct));
        if (snapshot.Gpus.Count == 0)
            snapshot.Gpus.Add(new GpuInfo { Name = "GPU probe unavailable", Provider = "best-effort", Status = "unavailable" });

        snapshot.Components.Add(new ComponentStatus { Name = "Data root", Status = "Ready", Detail = dataRoot });
        snapshot.Components.Add(new ComponentStatus
        {
            Name = "Local AI assets",
            Status = string.IsNullOrWhiteSpace(_settings.Settings.LocalAiAssetsRoot)
                ? "Not set"
                : Directory.Exists(_settings.Settings.LocalAiAssetsRoot) ? "Ready" : "Missing",
            Detail = string.IsNullOrWhiteSpace(_settings.Settings.LocalAiAssetsRoot)
                ? "Choose an assets folder in Settings"
                : Path.GetFullPath(_settings.Settings.LocalAiAssetsRoot)
        });
        if (_secrets is not null)
        {
            var backend = await _secrets.BackendLabelAsync(ct);
            snapshot.Components.Add(new ComponentStatus { Name = "Secrets", Status = "Ready", Detail = backend });
        }
        snapshot.Components.Add(new ComponentStatus { Name = "Chat database", Status = File.Exists(Path.Combine(dataRoot, "conversations.db")) ? "Present" : "Not created", Detail = FormatBytes(snapshot.DatabaseBytes) });
        snapshot.Components.Add(new ComponentStatus { Name = "Benchmark database", Status = File.Exists(Path.Combine(dataRoot, "benchmarks.db")) ? "Present" : "Not created", Detail = Path.Combine(dataRoot, "benchmarks.db") });
        snapshot.Components.Add(new ComponentStatus { Name = "Free storage", Status = drive.AvailableFreeSpace > 10L * 1024 * 1024 * 1024 ? "OK" : "Low", Detail = FormatBytes(drive.AvailableFreeSpace) });
        return snapshot;
    }

    private static async Task<List<GpuInfo>> GetGpusAsync(CancellationToken ct)
    {
        var nvidia = await TryNvidiaSmiAsync(ct);
        if (nvidia.Count > 0)
            return nvidia;

        if (OperatingSystem.IsLinux())
            return TryLinuxDrmGpus();

        return [];
    }

    private static async Task<List<GpuInfo>> TryNvidiaSmiAsync(CancellationToken ct)
    {
        var output = await RunCommandAsync("nvidia-smi",
            ["--query-gpu=name,memory.total,memory.used", "--format=csv,noheader,nounits"],
            ct);
        if (string.IsNullOrWhiteSpace(output))
            return [];

        var gpus = new List<GpuInfo>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 3) continue;
            long.TryParse(parts[1], out var totalMiB);
            long.TryParse(parts[2], out var usedMiB);
            gpus.Add(new GpuInfo
            {
                Name = parts[0],
                Provider = "nvidia-smi",
                MemoryTotalBytes = totalMiB * 1024 * 1024,
                MemoryUsedBytes = usedMiB * 1024 * 1024,
                Status = "OK"
            });
        }

        return gpus;
    }

    private static List<GpuInfo> TryLinuxDrmGpus()
    {
        const string drm = "/sys/class/drm";
        if (!Directory.Exists(drm))
            return [];

        var gpus = new List<GpuInfo>();
        foreach (var card in Directory.EnumerateDirectories(drm, "card*").Where(d => !Path.GetFileName(d).Contains('-')))
        {
            var device = Path.Combine(card, "device");
            var vendor = ReadFirstLine(Path.Combine(device, "vendor"));
            var model = ReadFirstLine(Path.Combine(device, "product")) ?? Path.GetFileName(card);
            gpus.Add(new GpuInfo
            {
                Name = string.IsNullOrWhiteSpace(model) ? Path.GetFileName(card) : model,
                Provider = string.IsNullOrWhiteSpace(vendor) ? "drm" : $"drm {vendor}",
                Status = "VRAM unavailable"
            });
        }
        return gpus;
    }

    private static async Task<string> GetCpuNameAsync(CancellationToken ct)
    {
        if (OperatingSystem.IsLinux() && File.Exists("/proc/cpuinfo"))
        {
            var lines = await File.ReadAllLinesAsync("/proc/cpuinfo", ct);
            var model = lines.FirstOrDefault(l => l.StartsWith("model name", StringComparison.OrdinalIgnoreCase));
            if (model is not null)
                return model.Split(':', 2).Last().Trim();
        }

        return RuntimeInformation.ProcessArchitecture.ToString();
    }

    private static long GetTotalMemoryBytes()
    {
        if (OperatingSystem.IsLinux())
        {
            var memTotal = ReadMemInfoBytes("MemTotal");
            if (memTotal > 0)
                return memTotal;
        }

        try
        {
            var info = GC.GetGCMemoryInfo();
            if (info.TotalAvailableMemoryBytes > 0)
                return info.TotalAvailableMemoryBytes;
        }
        catch { }

        return 0;
    }

    private static long GetAvailableMemoryBytes()
    {
        if (OperatingSystem.IsLinux())
            return ReadMemInfoBytes("MemAvailable");

        return 0;
    }

    private static long ReadMemInfoBytes(string key)
    {
        try
        {
            var line = File.ReadLines("/proc/meminfo").FirstOrDefault(l => l.StartsWith(key, StringComparison.OrdinalIgnoreCase));
            var value = line?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).FirstOrDefault();
            return long.TryParse(value, out var kb) ? kb * 1024 : 0;
        }
        catch { return 0; }
    }

    private static async Task<string> RunCommandAsync(string file, string[] args, CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = file,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        foreach (var arg in args)
            process.StartInfo.ArgumentList.Add(arg);

        try
        {
            if (!process.Start())
                return string.Empty;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return process.ExitCode == 0 ? output : string.Empty;
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch { }
            return string.Empty;
        }
    }

    private static string? ReadFirstLine(string path)
    {
        try { return File.Exists(path) ? File.ReadLines(path).FirstOrDefault()?.Trim() : null; }
        catch { return null; }
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
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
