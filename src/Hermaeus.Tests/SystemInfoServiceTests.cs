using Hermaeus.Core.Models;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;

namespace Hermaeus.Tests;

// r13 01-system-truth.md: Windows RAM/OS/CPU/GPU truth fixes.
public sealed class SystemInfoServiceTests
{
    // ── 1.2 OsNameFormatter (pure mapper) ────────────────────────────────
    [Fact]
    public void OsNameFormatter_maps_high_builds_to_windows_11()
    {
        var result = OsNameFormatter.Format("Microsoft Windows 10.0.26200", new Version(10, 0, 26200, 0));
        Assert.Equal("Windows 11 (build 26200)", result);
    }

    [Fact]
    public void OsNameFormatter_maps_low_builds_to_windows_10()
    {
        var result = OsNameFormatter.Format("Microsoft Windows 10.0.19045", new Version(10, 0, 19045, 0));
        Assert.Equal("Windows 10 (build 19045)", result);
    }

    [Fact]
    public void OsNameFormatter_appends_display_version_when_known()
    {
        var result = OsNameFormatter.Format("Microsoft Windows 10.0.26200", new Version(10, 0, 26200, 0), "24H2");
        Assert.Equal("Windows 11 24H2 (build 26200)", result);
    }

    [Fact]
    public void OsNameFormatter_leaves_non_windows_descriptions_untouched()
    {
        const string linuxDescription = "Linux 6.8.0-generic #1 SMP";
        var result = OsNameFormatter.Format(linuxDescription, new Version(6, 8, 0, 0));
        Assert.Equal(linuxDescription, result);
    }

    // ── 1.4 registry GPU parser (pure seam) ──────────────────────────────
    [Fact]
    public void ParseRegistryGpus_dedupes_keeping_largest_memory()
    {
        var gpus = SystemInfoService.ParseRegistryGpus(
        [
            ("NVIDIA GeForce RTX 4090", 8_000_000_000),
            ("NVIDIA GeForce RTX 4090", 24_000_000_000),
        ]);

        var gpu = Assert.Single(gpus);
        Assert.Equal(24_000_000_000, gpu.MemoryTotalBytes);
    }

    [Fact]
    public void ParseRegistryGpus_skips_software_and_zero_memory_adapters()
    {
        var gpus = SystemInfoService.ParseRegistryGpus(
        [
            (null, 4_000_000_000),
            ("Microsoft Basic Render Driver", 0),
            ("AMD Radeon RX 7900 XTX", 20_000_000_000),
        ]);

        var gpu = Assert.Single(gpus);
        Assert.Equal("AMD Radeon RX 7900 XTX", gpu.Name);
        Assert.Equal("registry", gpu.Provider);
        Assert.Null(gpu.MemoryUsedBytes);
    }

    [Fact]
    public void ParseRegistryGpus_returns_empty_for_no_real_adapters()
    {
        var gpus = SystemInfoService.ParseRegistryGpus([(null, 0), ("Basic Display Adapter", 0)]);
        Assert.Empty(gpus);
    }

    // ── 1.4 registry memory value conversion (found live: Intel's driver stores
    // HardwareInformation.MemorySize as 4-byte REG_BINARY, not a plain integer) ───────────
    [Fact]
    public void ConvertRegistryMemoryValue_reads_a_plain_qword_like_NVIDIA_reports()
    {
        // Observed live on an RTX 4060 Laptop GPU: HardwareInformation.qwMemorySize.
        Assert.Equal(8_585_740_288L, SystemInfoService.ConvertRegistryMemoryValue(8_585_740_288L));
    }

    [Fact]
    public void ConvertRegistryMemoryValue_reads_a_4_byte_little_endian_REG_BINARY_value()
    {
        // Observed live on Intel UHD Graphics: HardwareInformation.MemorySize = {0,240,255,127}.
        var bytes = new byte[] { 0, 240, 255, 127 };
        var expected = BitConverter.ToUInt32(bytes, 0);

        Assert.Equal((long)expected, SystemInfoService.ConvertRegistryMemoryValue(bytes));
    }

    [Fact]
    public void ConvertRegistryMemoryValue_reads_an_8_byte_little_endian_REG_BINARY_value()
    {
        var bytes = BitConverter.GetBytes(24_000_000_000UL);

        Assert.Equal(24_000_000_000L, SystemInfoService.ConvertRegistryMemoryValue(bytes));
    }

    [Fact]
    public void ConvertRegistryMemoryValue_returns_null_for_null_or_unexpected_shapes()
    {
        Assert.Null(SystemInfoService.ConvertRegistryMemoryValue(null));
        Assert.Null(SystemInfoService.ConvertRegistryMemoryValue(new byte[] { 1, 2, 3 }));
        Assert.Null(SystemInfoService.ConvertRegistryMemoryValue("not a number"));
    }

    // ── 1.5 HardwareProfile caching ──────────────────────────────────────
    [Fact]
    public async Task GetHardwareProfileAsync_caches_across_calls()
    {
        using var temp = new TempDir();
        var service = new SystemInfoService(Helpers.NewSettings(temp));

        var first = service.GetHardwareProfileAsync();
        var second = service.GetHardwareProfileAsync();

        // Lazy<Task<T>> guarantees the underlying probe factory runs at most
        // once; reference-equal tasks proves the second call reused it
        // instead of re-spawning nvidia-smi / re-walking the registry.
        Assert.Same(first, second);
        await first;
    }

    // ── 1.1 RAM rendering ─────────────────────────────────────────────────
    [Fact]
    public void FormatBytes_renders_nonzero_values_and_keeps_zero_as_unavailable()
    {
        var rendered = SystemOverviewViewModel.FormatBytes(4_200_000_000);
        Assert.NotEqual("unavailable", rendered);
        Assert.Contains("GB", rendered, StringComparison.Ordinal);
        Assert.Equal("unavailable", SystemOverviewViewModel.FormatBytes(0));
    }

    // ── 1.4 GPU tile: total-only VRAM display ────────────────────────────
    [Fact]
    public void GpuInfoViewModel_shows_total_only_when_used_is_unknown()
    {
        var vm = new GpuInfoViewModel(new GpuInfo { Name = "AMD Radeon RX 7900 XTX", MemoryTotalBytes = 20_000_000_000, Provider = "registry", Status = "OK" });
        Assert.EndsWith("total", vm.Memory, StringComparison.Ordinal);
        Assert.False(vm.HasMemoryRatio);
    }

    [Fact]
    public void Resource_bars_are_available_only_for_complete_observed_values()
    {
        var gpu = new GpuInfoViewModel(new GpuInfo
        {
            Name = "NVIDIA test GPU",
            MemoryUsedBytes = 4_000_000_000,
            MemoryTotalBytes = 8_000_000_000,
            Provider = "nvml",
            Status = "OK"
        });
        var metric = new SystemMetricViewModel("RAM", "4 GB available / 8 GB total", 0.5);

        Assert.True(gpu.HasMemoryRatio);
        Assert.Equal(50, gpu.MemoryProgressValue);
        Assert.True(metric.HasRatio);
        Assert.Equal(50, metric.ProgressValue);
    }
}
