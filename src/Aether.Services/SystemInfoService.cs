using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Aether.Core.Models;
using Aether.Core.Services;
using Microsoft.Win32;

namespace Aether.Services;

public sealed class SystemInfoService : ISystemInfoService
{
    private readonly ISettingsService _settings;
    private readonly ISecretStore? _secrets;
    private readonly object _hardwareProfileGate = new();
    private Lazy<Task<HardwareProfile>>? _hardwareProfile;

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
            OSDescription = FormatOsDescription(),
            Architecture = RuntimeInformation.OSArchitecture.ToString(),
            ProcessorCount = Environment.ProcessorCount,
            CpuName = await GetCpuNameAsync(ct),
            ProcessMemoryBytes = process.WorkingSet64,
            ManagedMemoryBytes = GC.GetTotalMemory(forceFullCollection: false),
            DataRoot = dataRoot,
            DataRootTotalBytes = drive.TotalSize,
            DataRootFreeBytes = drive.AvailableFreeSpace,
            DatabaseBytes = Directory.EnumerateFiles(dataRoot, "*.db*", SearchOption.TopDirectoryOnly)
                .Sum(f => new FileInfo(f).Length)
        };

        (snapshot.TotalMemoryBytes, snapshot.AvailableMemoryBytes) = GetMemoryBytes();

        snapshot.Gpus.AddRange(await GetGpusAsync(ct));
        if (snapshot.Gpus.Count == 0)
            snapshot.Gpus.Add(new GpuInfo { Name = "GPU probe unavailable", Provider = "best-effort", Status = "unavailable" });

        snapshot.Components.Add(new ComponentStatus { Name = "Data root", Status = "Ready", Detail = dataRoot });
        snapshot.Components.Add(new ComponentStatus
        {
            Name = "Local AI assets",
            Status = string.IsNullOrWhiteSpace(_settings.Settings.DataManagement.LocalAiAssetsRoot)
                ? "Not set"
                : Directory.Exists(_settings.Settings.DataManagement.LocalAiAssetsRoot) ? "Ready" : "Missing",
            Detail = string.IsNullOrWhiteSpace(_settings.Settings.DataManagement.LocalAiAssetsRoot)
                ? "Choose an assets folder in Settings"
                : Path.GetFullPath(_settings.Settings.DataManagement.LocalAiAssetsRoot)
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

    /// <summary>
    /// Cheap, cached hardware facts for repeated per-model checks (fits-chip,
    /// HF browser). First call does the real probes; later calls reuse the
    /// same completed task for the process lifetime (r13 1.5).
    /// </summary>
    public Task<HardwareProfile> GetHardwareProfileAsync(CancellationToken ct = default)
    {
        if (_hardwareProfile is null)
        {
            lock (_hardwareProfileGate)
            {
                _hardwareProfile ??= new Lazy<Task<HardwareProfile>>(() => BuildHardwareProfileAsync(ct));
            }
        }
        return _hardwareProfile.Value;
    }

    private static async Task<HardwareProfile> BuildHardwareProfileAsync(CancellationToken ct)
    {
        var (totalRam, _) = GetMemoryBytes();
        var gpus = await GetGpusAsync(ct);
        long maxVram = 0;
        string? gpuName = null;
        foreach (var gpu in gpus)
        {
            if (gpu.MemoryTotalBytes is > 0 && gpu.MemoryTotalBytes > maxVram)
            {
                maxVram = gpu.MemoryTotalBytes.Value;
                gpuName = gpu.Name;
            }
        }
        return new HardwareProfile(totalRam, maxVram, gpuName);
    }

    private static string FormatOsDescription()
    {
        if (!OperatingSystem.IsWindows())
            return RuntimeInformation.OSDescription;

        return OsNameFormatter.Format(RuntimeInformation.OSDescription, Environment.OSVersion.Version, TryGetWindowsDisplayVersion());
    }

    [SupportedOSPlatform("windows")]
    private static string? TryGetWindowsDisplayVersion()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            return key?.GetValue("DisplayVersion") as string;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<List<GpuInfo>> GetGpusAsync(CancellationToken ct)
    {
        var nvidia = await TryNvidiaSmiAsync(ct);
        if (nvidia.Count > 0)
            return nvidia;

        if (OperatingSystem.IsWindows())
        {
            var registryGpus = TryWindowsRegistryGpus();
            if (registryGpus.Count > 0)
                return registryGpus;
        }

        if (OperatingSystem.IsLinux())
            return TryLinuxDrmGpus();

        return [];
    }

    [SupportedOSPlatform("windows")]
    private static List<GpuInfo> TryWindowsRegistryGpus()
    {
        const string displayClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        var entries = new List<(string? Name, long MemoryBytes)>();
        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(displayClassKey);
            if (classKey is null)
                return [];

            foreach (var subKeyName in classKey.GetSubKeyNames())
            {
                if (subKeyName.Length != 4 || !subKeyName.All(char.IsDigit))
                    continue; // skips "Properties" and other non-index subkeys

                using var adapterKey = classKey.OpenSubKey(subKeyName);
                if (adapterKey is null)
                    continue;

                var name = adapterKey.GetValue("DriverDesc") as string;
                var memory = ReadRegistryQuadOrDword(adapterKey, "HardwareInformation.qwMemorySize")
                    ?? ReadRegistryQuadOrDword(adapterKey, "HardwareInformation.MemorySize")
                    ?? 0;
                entries.Add((name, memory));
            }
        }
        catch
        {
            return [];
        }

        return ParseRegistryGpus(entries);
    }

    [SupportedOSPlatform("windows")]
    private static long? ReadRegistryQuadOrDword(RegistryKey key, string valueName) =>
        ConvertRegistryMemoryValue(key.GetValue(valueName));

    /// <summary>Display drivers do not agree on how they store these values: NVIDIA stores
    /// qwMemorySize as a plain REG_QWORD/REG_DWORD, but Intel's driver has been observed
    /// storing HardwareInformation.MemorySize as 4-byte REG_BINARY (little-endian), which
    /// Convert.ToInt64 cannot handle directly. Handle both shapes explicitly rather than
    /// silently dropping the adapter as "size 0". Pure (no registry access) so it is directly
    /// testable; the registry read itself stays untestable-thin.</summary>
    internal static long? ConvertRegistryMemoryValue(object? rawValue) => rawValue switch
    {
        null => null,
        byte[] { Length: 4 } le32 => BitConverter.ToUInt32(le32, 0),
        byte[] { Length: 8 } le64 => unchecked((long)BitConverter.ToUInt64(le64, 0)),
        byte[] => null,
        _ => TryConvertToInt64(rawValue)
    };

    private static long? TryConvertToInt64(object value)
    {
        try { return Convert.ToInt64(value); }
        catch { return null; }
    }

    /// <summary>
    /// Pure parser: dedupe identical adapter names keeping the largest memory
    /// value, and skip software adapters (no name or zero reported memory).
    /// The registry read itself stays untestable-thin per r13 1.4.
    /// </summary>
    internal static List<GpuInfo> ParseRegistryGpus(IEnumerable<(string? Name, long MemoryBytes)> entries)
    {
        var byName = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, memoryBytes) in entries)
        {
            if (string.IsNullOrWhiteSpace(name) || memoryBytes <= 0)
                continue;
            if (!byName.TryGetValue(name, out var existing) || memoryBytes > existing)
                byName[name] = memoryBytes;
        }

        return byName.Select(kv => new GpuInfo
        {
            Name = kv.Key,
            Provider = "registry",
            MemoryTotalBytes = kv.Value,
            MemoryUsedBytes = null,
            Status = "OK"
        }).ToList();
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

        if (OperatingSystem.IsWindows())
        {
            var name = TryGetWindowsCpuName();
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }

        return RuntimeInformation.ProcessArchitecture.ToString();
    }

    [SupportedOSPlatform("windows")]
    private static string? TryGetWindowsCpuName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return (key?.GetValue("ProcessorNameString") as string)?.Trim();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Returns (total, available) bytes. Windows uses GlobalMemoryStatusEx (the machine's
    /// view); Linux reads /proc/meminfo; everything else falls back to the GC's view of total memory,
    /// which has no "available" concept (r13 1.1).</summary>
    private static (long Total, long Available) GetMemoryBytes()
    {
        if (OperatingSystem.IsWindows() && WindowsMemoryStatus.TryGet(out var total, out var available))
            return (total, available);

        if (OperatingSystem.IsLinux())
        {
            var memTotal = ReadMemInfoBytes("MemTotal");
            if (memTotal > 0)
                return (memTotal, ReadMemInfoBytes("MemAvailable"));
        }

        try
        {
            var info = GC.GetGCMemoryInfo();
            if (info.TotalAvailableMemoryBytes > 0)
                return (info.TotalAvailableMemoryBytes, 0);
        }
        catch { }

        return (0, 0);
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

    /// <summary>P/Invoke for the machine's real RAM totals (repo precedent: ProcessJobObject
    /// does its own local P/Invoke). GC.GetGCMemoryInfo() only sees the GC's view, and
    /// /proc/meminfo does not exist on Windows, so this is the only accurate source.</summary>
    [SupportedOSPlatform("windows")]
    private static class WindowsMemoryStatus
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        public static bool TryGet(out long totalBytes, out long availableBytes)
        {
            totalBytes = 0;
            availableBytes = 0;
            try
            {
                var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
                if (!GlobalMemoryStatusEx(ref status))
                    return false;
                totalBytes = (long)status.ullTotalPhys;
                availableBytes = (long)status.ullAvailPhys;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
