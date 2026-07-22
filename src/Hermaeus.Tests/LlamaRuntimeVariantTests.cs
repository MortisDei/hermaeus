using Hermaeus.Core.Models;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>
/// GPU-aware llama.cpp build selection: vendor classification, Auto variant
/// resolution, per-platform/variant asset matching, the CUDA runtime companion,
/// install-root resolution, superseded-version pruning, and the launch-verify
/// CPU fallback decision.
/// </summary>
public sealed class LlamaRuntimeVariantTests
{
    // Mirrors the live ggml-org/llama.cpp b10066 asset list (verified against
    // the GitHub releases API at implementation time, same discipline as r11).
    private static readonly GitHubReleaseAsset[] B10066Assets =
    [
        new("cudart-llama-bin-win-cuda-12.4-x64.zip", "u/cudart-12.4"),
        new("cudart-llama-bin-win-cuda-13.3-x64.zip", "u/cudart-13.3"),
        new("llama-b10066-bin-macos-arm64.tar.gz", "u/macos-arm64"),
        new("llama-b10066-bin-macos-x64.tar.gz", "u/macos-x64"),
        new("llama-b10066-bin-ubuntu-arm64.tar.gz", "u/ubuntu-arm64"),
        new("llama-b10066-bin-ubuntu-vulkan-x64.tar.gz", "u/ubuntu-vulkan-x64"),
        new("llama-b10066-bin-ubuntu-x64.tar.gz", "u/ubuntu-x64"),
        new("llama-b10066-bin-win-cpu-arm64.zip", "u/win-cpu-arm64"),
        new("llama-b10066-bin-win-cpu-x64.zip", "u/win-cpu-x64"),
        new("llama-b10066-bin-win-cuda-12.4-x64.zip", "u/win-cuda-12.4"),
        new("llama-b10066-bin-win-cuda-13.3-x64.zip", "u/win-cuda-13.3"),
        new("llama-b10066-bin-win-hip-radeon-x64.zip", "u/win-hip"),
        new("llama-b10066-bin-win-sycl-x64.zip", "u/win-sycl"),
        new("llama-b10066-bin-win-vulkan-x64.zip", "u/win-vulkan"),
    ];

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4070", GpuVendor.Nvidia)]
    [InlineData("Quadro P2000", GpuVendor.Nvidia)]
    [InlineData("AMD Radeon RX 7900 XT", GpuVendor.Amd)]
    [InlineData("Intel Arc A770", GpuVendor.Intel)]
    [InlineData("Intel(R) UHD Graphics 630", GpuVendor.Intel)]
    [InlineData("Some Unknown Accelerator", GpuVendor.Unknown)]
    [InlineData(null, GpuVendor.Unknown)]
    public void ClassifyGpuVendor_maps_representative_names(string? name, GpuVendor expected)
        => Assert.Equal(expected, LlamaServerSetupService.ClassifyGpuVendor(name));

    [Fact]
    public void ResolveVariant_auto_picks_by_hardware_and_explicit_wins()
    {
        var nvidia = new HardwareProfile(0, 8L * 1024 * 1024 * 1024, "NVIDIA GeForce RTX 4070");
        var amd = new HardwareProfile(0, 8L * 1024 * 1024 * 1024, "AMD Radeon RX 7900");
        var none = new HardwareProfile(0, 0, null);

        Assert.Equal(LlamaRuntimeVariant.Cuda, LlamaServerSetupService.ResolveVariant(LlamaRuntimeVariant.Auto, nvidia));
        Assert.Equal(LlamaRuntimeVariant.Vulkan, LlamaServerSetupService.ResolveVariant(LlamaRuntimeVariant.Auto, amd));
        Assert.Equal(LlamaRuntimeVariant.Cpu, LlamaServerSetupService.ResolveVariant(LlamaRuntimeVariant.Auto, none));
        // Explicit always wins over Auto's hardware read.
        Assert.Equal(LlamaRuntimeVariant.Cpu, LlamaServerSetupService.ResolveVariant(LlamaRuntimeVariant.Cpu, nvidia));
        Assert.Equal(LlamaRuntimeVariant.Vulkan, LlamaServerSetupService.ResolveVariant(LlamaRuntimeVariant.Vulkan, none));
    }

    [Fact]
    public void SelectDownloadAsset_windows_matches_variant_token()
    {
        Assert.Equal("u/win-cpu-x64", Select(LlamaPlatform.WinX64, LlamaRuntimeVariant.Cpu));
        Assert.Equal("u/win-vulkan", Select(LlamaPlatform.WinX64, LlamaRuntimeVariant.Vulkan));
        // Two CUDA builds ship; the lowest version wins for driver compatibility.
        Assert.Equal("u/win-cuda-12.4", Select(LlamaPlatform.WinX64, LlamaRuntimeVariant.Cuda));
    }

    [Fact]
    public void SelectDownloadAsset_defaults_to_cpu_build_for_every_platform()
    {
        Assert.Equal("u/win-cpu-x64", Select(LlamaPlatform.WinX64, LlamaRuntimeVariant.Cpu));
        Assert.Equal("u/win-cpu-arm64", Select(LlamaPlatform.WinArm64, LlamaRuntimeVariant.Cpu));
        Assert.Equal("u/ubuntu-x64", Select(LlamaPlatform.LinuxX64, LlamaRuntimeVariant.Cpu));
        Assert.Equal("u/macos-arm64", Select(LlamaPlatform.MacArm64, LlamaRuntimeVariant.Cpu));
    }

    [Fact]
    public void SelectDownloadAsset_non_windows_ignores_variant()
    {
        // Linux keeps the r11 default-build selection regardless of variant.
        Assert.Equal("u/ubuntu-x64", Select(LlamaPlatform.LinuxX64, LlamaRuntimeVariant.Cuda));
        Assert.Equal("u/ubuntu-x64", Select(LlamaPlatform.LinuxX64, LlamaRuntimeVariant.Vulkan));
    }

    [Fact]
    public void SelectDownloadAsset_returns_null_when_variant_absent()
    {
        // Windows ARM64 ships no CUDA or Vulkan build in this release.
        Assert.Null(LlamaServerSetupService.SelectDownloadAsset(B10066Assets, LlamaPlatform.WinArm64, LlamaRuntimeVariant.Cuda));
    }

    [Fact]
    public void SelectCudartAsset_matches_the_chosen_cuda_version()
    {
        var cuda = LlamaServerSetupService.SelectDownloadAsset(B10066Assets, LlamaPlatform.WinX64, LlamaRuntimeVariant.Cuda)!;
        var cudart = LlamaServerSetupService.SelectCudartAsset(B10066Assets, cuda.Name);
        Assert.Equal("u/cudart-12.4", cudart?.BrowserDownloadUrl);
    }

    [Fact]
    public void ResolveInstallRoot_walks_up_nested_tag_directories()
        => Assert.Equal(
            NormPath(@"C:\AI\llama.cpp"),
            NormPath(LlamaServerSetupService.ResolveInstallRoot(@"C:\AI\llama.cpp\b10064\b10066")));

    [Fact]
    public void ResolveInstallRoot_keeps_unversioned_layout()
        => Assert.Equal(
            NormPath(@"C:\AI\llama.cpp"),
            NormPath(LlamaServerSetupService.ResolveInstallRoot(@"C:\AI\llama.cpp")));

    [Fact]
    public void ResolveInstallRoot_does_not_walk_a_tag_named_root_into_the_drive()
        => Assert.Equal(
            NormPath(@"C:\b10050"),
            NormPath(LlamaServerSetupService.ResolveInstallRoot(@"C:\b10050")));

    [Fact]
    public void SelectPrunableVersionDirectories_keeps_current_and_previous_ignores_non_tags()
    {
        string[] siblings =
        [
            @"C:\AI\llama.cpp\b10060",
            @"C:\AI\llama.cpp\b10064",
            @"C:\AI\llama.cpp\b10066",
            @"C:\AI\llama.cpp\models",
            @"C:\AI\llama.cpp\notes-b1",
        ];

        var prunable = LlamaServerSetupService.SelectPrunableVersionDirectories(siblings, "b10066", "b10064");

        Assert.Contains(@"C:\AI\llama.cpp\b10060", prunable);
        Assert.DoesNotContain(@"C:\AI\llama.cpp\b10066", prunable); // current kept
        Assert.DoesNotContain(@"C:\AI\llama.cpp\b10064", prunable); // previous kept
        Assert.DoesNotContain(@"C:\AI\llama.cpp\models", prunable); // non-tag ignored
        Assert.DoesNotContain(@"C:\AI\llama.cpp\notes-b1", prunable);
    }

    [Fact]
    public void NearestTagDirectoryName_finds_the_version_dir()
    {
        Assert.Equal("b10066", LlamaServerSetupService.NearestTagDirectoryName(@"C:\AI\llama.cpp\b10064\b10066\llama-server.exe"));
        Assert.Null(LlamaServerSetupService.NearestTagDirectoryName(@"C:\AI\llama.cpp\llama-server.exe"));
    }

    [Theory]
    [InlineData(LlamaRuntimeVariant.Cuda, false, true)]  // GPU build did not launch -> fall back
    [InlineData(LlamaRuntimeVariant.Cuda, true, false)]  // GPU build launched -> keep
    [InlineData(LlamaRuntimeVariant.Cpu, false, false)]  // CPU is terminal, never falls back
    public void ShouldFallbackToCpu_is_terminal_at_cpu(LlamaRuntimeVariant variant, bool probeOk, bool expected)
        => Assert.Equal(expected, DoctorService.ShouldFallbackToCpu(variant, probeOk));

    [Theory]
    [InlineData(true, true, 999, true)]   // GPU + CPU build -> advise
    [InlineData(true, false, 0, true)]    // GPU + zero offload -> advise
    [InlineData(true, false, 999, false)] // GPU build offloading all -> quiet
    [InlineData(false, true, 0, false)]   // no GPU -> quiet
    public void ShouldAdviseGpuInference_fires_only_when_gpu_wasted(bool gpu, bool cpuBuild, int layers, bool expected)
        => Assert.Equal(expected, DoctorService.ShouldAdviseGpuInference(gpu, cpuBuild, layers));

    private static string? Select(LlamaPlatform platform, LlamaRuntimeVariant variant)
        => LlamaServerSetupService.SelectDownloadAsset(B10066Assets, platform, variant)?.BrowserDownloadUrl;

    private static string NormPath(string p) => p.Replace('\\', '/').TrimEnd('/');
}
