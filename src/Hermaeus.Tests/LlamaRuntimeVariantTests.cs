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
    [Fact]
    public void Release_asset_digest_requires_a_well_formed_SHA256_value()
    {
        var asset = new GitHubReleaseAsset(
            "llama-b10034-bin-ubuntu-x64.tar.gz",
            "https://example.test/llama.tar.gz",
            $"sha256:{new string('a', 64)}");

        Assert.Equal(new string('a', 64), LlamaServerSetupService.RequireSha256Digest(asset));
        Assert.Throws<InvalidOperationException>(() => LlamaServerSetupService.RequireSha256Digest(asset with { Digest = null }));
        Assert.Throws<InvalidOperationException>(() => LlamaServerSetupService.RequireSha256Digest(asset with { Digest = "sha256:not-a-hash" }));
    }

    [Fact]
    public void Pinned_release_has_a_SHA256_for_every_supported_platform()
    {
        foreach (var platform in Enum.GetValues<LlamaPlatform>())
        {
            var hash = LlamaServerSetupService.PinnedSha256For(platform);
            Assert.Equal(64, hash.Length);
            Assert.All(hash, c => Assert.True(Uri.IsHexDigit(c)));
        }
    }

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
        new("llama-b10066-bin-ubuntu-cuda-12.4-x64.tar.gz", "u/ubuntu-cuda-12.4"),
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
    public void ResolveUpdateVariant_re_evaluates_auto_without_pinning_to_installed_backend()
    {
        var nvidia = new HardwareProfile(0, 8L * 1024 * 1024 * 1024, "NVIDIA GeForce GTX 1660");

        Assert.Equal(
            LlamaRuntimeVariant.Cuda,
            LlamaServerSetupService.ResolveUpdateVariant(LlamaRuntimeVariant.Auto, LlamaRuntimeVariant.Cuda, nvidia));
        Assert.Equal(
            LlamaRuntimeVariant.Cuda,
            LlamaServerSetupService.ResolveUpdateVariant(LlamaRuntimeVariant.Auto, LlamaRuntimeVariant.Cpu, nvidia));
        Assert.Equal(
            LlamaRuntimeVariant.Cuda,
            LlamaServerSetupService.ResolveUpdateVariant(LlamaRuntimeVariant.Auto, LlamaRuntimeVariant.Auto, nvidia));
    }

    [Fact]
    public async Task Configured_and_installed_variants_round_trip_independently_through_settings()
    {
        using var temp = new TempDir();
        var writer = Helpers.NewSettings(temp);
        writer.Settings.DataManagement.LlamaRuntimeVariant = LlamaRuntimeVariant.Auto;
        writer.Settings.DataManagement.InstalledLlamaRuntimeVariant = LlamaRuntimeVariant.Vulkan;
        await writer.SaveAsync();

        var reader = Helpers.NewSettings(temp);
        await reader.LoadAsync();

        Assert.Equal(LlamaRuntimeVariant.Auto, reader.Settings.DataManagement.LlamaRuntimeVariant);
        Assert.Equal(LlamaRuntimeVariant.Vulkan, reader.Settings.DataManagement.InstalledLlamaRuntimeVariant);
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
    public void SelectDownloadAsset_linux_matches_gpu_variant_and_does_not_alias_cuda_to_cpu()
    {
        Assert.Equal("u/ubuntu-cuda-12.4", Select(LlamaPlatform.LinuxX64, LlamaRuntimeVariant.Cuda));
        Assert.Equal("u/ubuntu-vulkan-x64", Select(LlamaPlatform.LinuxX64, LlamaRuntimeVariant.Vulkan));
    }

    [Fact]
    public void SelectDownloadAsset_returns_null_for_a_linux_cuda_request_when_release_has_no_cuda_asset()
    {
        var currentB10679Assets = new[]
        {
            new GitHubReleaseAsset("llama-b10679-bin-ubuntu-x64.tar.gz", "u/ubuntu-cpu"),
            new GitHubReleaseAsset("llama-b10679-bin-ubuntu-vulkan-x64.tar.gz", "u/ubuntu-vulkan")
        };

        Assert.Null(LlamaServerSetupService.SelectDownloadAsset(
            currentB10679Assets, LlamaPlatform.LinuxX64, LlamaRuntimeVariant.Cuda));
    }

    [Fact]
    public void SelectDownloadAsset_returns_null_when_variant_absent()
    {
        // Windows ARM64 ships no CUDA or Vulkan build in this fixture.
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
    public void Latest_compatible_release_ignores_semver_and_skips_builds_without_an_asset()
    {
        var releases = new[]
        {
            new GitHubRelease("v0.2.0", [new("llama-v0.2.0.zip", "u/semver")]),
            new GitHubRelease("b10070", [new("llama-b10070-bin-win-cpu-x64.zip", "u/win-only")]),
            new GitHubRelease("b10066", [new("llama-b10066-bin-ubuntu-x64.tar.gz", "u/linux")]),
            new GitHubRelease("b10034", [new("llama-b10034-bin-ubuntu-x64.tar.gz", "u/old")])
        };

        var selected = LlamaServerSetupService.SelectLatestCompatibleRelease(
            releases, LlamaPlatform.LinuxX64, LlamaRuntimeVariant.Cpu);

        Assert.NotNull(selected);
        Assert.Equal("b10066", selected.Release.TagName);
        Assert.Equal("u/linux", selected.Asset.BrowserDownloadUrl);
        Assert.Equal(10066, selected.BuildNumber);
    }

    [Fact]
    public void Latest_compatible_release_does_not_fall_back_to_cpu_for_a_missing_gpu_backend()
    {
        var releases = new[]
        {
            new GitHubRelease("b10679", [new("llama-b10679-bin-ubuntu-x64.tar.gz", "u/cpu")]),
            new GitHubRelease("b10678", [new("llama-b10678-bin-ubuntu-vulkan-x64.tar.gz", "u/vulkan")])
        };

        var selected = LlamaServerSetupService.SelectLatestCompatibleRelease(
            releases, LlamaPlatform.LinuxX64, LlamaRuntimeVariant.Cuda);

        Assert.Null(selected);
    }

    [Fact]
    public void Auto_nvidia_selection_uses_vulkan_when_linux_cuda_asset_is_unavailable()
    {
        var nvidia = new HardwareProfile(0, 8L * 1024 * 1024 * 1024, "NVIDIA GeForce GTX 1660");
        var preferred = LlamaServerSetupService.ResolveVariant(LlamaRuntimeVariant.Auto, nvidia);
        var releases = new[]
        {
            new GitHubRelease("b10679",
            [
                new GitHubReleaseAsset("llama-b10679-bin-ubuntu-x64.tar.gz", "u/ubuntu-cpu"),
                new GitHubReleaseAsset("llama-b10679-bin-ubuntu-vulkan-x64.tar.gz", "u/ubuntu-vulkan")
            ])
        };

        var selected = LlamaServerSetupService.SelectLatestCompatibleRelease(
            releases,
            LlamaPlatform.LinuxX64,
            preferred,
            allowAutoAcceleratedFallback: true);

        Assert.Equal(LlamaRuntimeVariant.Cuda, preferred);
        Assert.NotNull(selected);
        Assert.Equal(LlamaRuntimeVariant.Vulkan, selected.Variant);
        Assert.Equal("u/ubuntu-vulkan", selected.Asset.BrowserDownloadUrl);
    }

    [Fact]
    public void Auto_amd_selection_keeps_vulkan_as_the_accelerated_backend()
    {
        var amd = new HardwareProfile(0, 8L * 1024 * 1024 * 1024, "AMD Radeon RX 7900");
        var preferred = LlamaServerSetupService.ResolveVariant(LlamaRuntimeVariant.Auto, amd);
        var releases = new[]
        {
            new GitHubRelease("b10679",
            [
                new GitHubReleaseAsset("llama-b10679-bin-ubuntu-x64.tar.gz", "u/ubuntu-cpu"),
                new GitHubReleaseAsset("llama-b10679-bin-ubuntu-vulkan-x64.tar.gz", "u/ubuntu-vulkan")
            ])
        };

        var selected = LlamaServerSetupService.SelectLatestCompatibleRelease(
            releases,
            LlamaPlatform.LinuxX64,
            preferred,
            allowAutoAcceleratedFallback: true);

        Assert.Equal(LlamaRuntimeVariant.Vulkan, preferred);
        Assert.NotNull(selected);
        Assert.Equal(LlamaRuntimeVariant.Vulkan, selected.Variant);
        Assert.Equal("u/ubuntu-vulkan", selected.Asset.BrowserDownloadUrl);
    }

    [Fact]
    public void Managed_discovery_selects_the_highest_installed_b_build()
    {
        using var temp = new TempDir();
        var executableName = OperatingSystem.IsWindows() ? "llama-server.exe" : "llama-server";
        var installPath = temp.PathFor("llama-server");
        var oldPath = Path.Combine(installPath, "b10034", executableName);
        var currentPath = Path.Combine(installPath, "b10066", executableName);
        Directory.CreateDirectory(Path.GetDirectoryName(oldPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(currentPath)!);
        File.WriteAllText(oldPath, "old");
        File.WriteAllText(currentPath, "current");

        var resolved = LlamaServerSetupService.ResolveInstalledExecutable(installPath);

        Assert.Equal(currentPath, resolved);
    }

    [Fact]
    public void Managed_llama_install_path_is_derived_from_the_configured_AI_assets_root()
    {
        using var temp = new TempDir();
        var setup = new LlamaServerSetupService();
        var assetsRoot = temp.PathFor("ai-assets");

        Assert.Equal(
            Path.Combine(assetsRoot, "llama-server"),
            setup.GetDefaultInstallPath(assetsRoot));
        Assert.DoesNotContain("Data", setup.GetDefaultInstallPath(assetsRoot), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveInstallRoot_walks_up_nested_tag_directories()
        => Assert.Equal(
            NormPath(@"C:\AI\llama.cpp"),
            NormPath(LlamaServerSetupService.ResolveInstallRoot(NormPath(@"C:\AI\llama.cpp\b10064\b10066"))));

    [Fact]
    public void ResolveInstallRoot_walks_up_legacy_llama_tag_directories()
        => Assert.Equal(
            NormPath(@"/opt/hermaeus/llama-server"),
            NormPath(LlamaServerSetupService.ResolveInstallRoot(NormPath(@"/opt/hermaeus/llama-server/llama-b10669/b10679/llama-b10679"))));

    [Theory]
    [InlineData("b10679", 10679)]
    [InlineData("llama-b10679", 10679)]
    public void TryParseBuildTag_accepts_managed_directory_names(string tag, int expected)
        => Assert.Equal(expected, LlamaServerSetupService.TryParseBuildTag(tag));

    [Fact]
    public void ResolveInstallRoot_keeps_unversioned_layout()
        => Assert.Equal(
            NormPath(@"C:\AI\llama.cpp"),
            NormPath(LlamaServerSetupService.ResolveInstallRoot(NormPath(@"C:\AI\llama.cpp"))));

    [Fact]
    public void ResolveInstallRoot_does_not_walk_a_tag_named_root_into_the_drive()
        => Assert.Equal(
            NormPath(@"C:\b10050"),
            NormPath(LlamaServerSetupService.ResolveInstallRoot(NormPath(@"C:\b10050"))));

    [Fact]
    public void SelectPrunableVersionDirectories_keeps_current_and_previous_ignores_non_tags()
    {
        // Literal paths are normalized to forward slashes: Path.GetFileName /
        // GetDirectoryName only treat '\' as a separator on Windows, so a
        // hardcoded backslash literal silently fails to parse into path
        // components when this test runs on Linux CI. Forward slashes parse
        // identically on both (Windows accepts '/' as an alt separator).
        string[] siblings =
        [
            NormPath(@"C:\AI\llama.cpp\b10060"),
            NormPath(@"C:\AI\llama.cpp\b10064"),
            NormPath(@"C:\AI\llama.cpp\b10066"),
            NormPath(@"C:\AI\llama.cpp\models"),
            NormPath(@"C:\AI\llama.cpp\notes-b1"),
        ];

        var prunable = LlamaServerSetupService.SelectPrunableVersionDirectories(siblings, "b10066", "b10064");

        Assert.Contains(NormPath(@"C:\AI\llama.cpp\b10060"), prunable);
        Assert.DoesNotContain(NormPath(@"C:\AI\llama.cpp\b10066"), prunable); // current kept
        Assert.DoesNotContain(NormPath(@"C:\AI\llama.cpp\b10064"), prunable); // previous kept
        Assert.DoesNotContain(NormPath(@"C:\AI\llama.cpp\models"), prunable); // non-tag ignored
        Assert.DoesNotContain(NormPath(@"C:\AI\llama.cpp\notes-b1"), prunable);
    }

    [Fact]
    public void Nested_archive_layout_keeps_current_build_and_ignores_unowned_tag_directories()
    {
        using var temp = new TempDir();
        var root = temp.PathFor("llama-server");
        var oldPath = CreateNestedRuntime(root, "b10660");
        CreateNestedRuntime(root, "b10650");
        var currentPath = CreateNestedRuntime(root, "llama-b10679");
        var sameBuildLegacyPath = Path.Combine(root, "llama-b10679", "llama-b10679", OperatingSystem.IsWindows() ? "llama-server.exe" : "llama-server");
        Directory.CreateDirectory(Path.GetDirectoryName(sameBuildLegacyPath)!);
        File.WriteAllText(sameBuildLegacyPath, "same-build legacy layout");
        Directory.CreateDirectory(Path.Combine(root, "b99999"));

        var candidates = LlamaServerSetupService.SelectPrunableVersionDirectories(root, currentPath, oldPath);

        Assert.Contains(Path.Combine(root, "b10650"), candidates);
        Assert.DoesNotContain(Path.Combine(root, "b10660"), candidates);
        Assert.DoesNotContain(Path.Combine(root, "b10679"), candidates);
        Assert.DoesNotContain(Path.Combine(root, "llama-b10679"), candidates);
        Assert.DoesNotContain(Path.Combine(root, "b99999"), candidates);

        var reclaimed = LlamaServerSetupService.PruneVersionDirectories(
            root, candidates.Append(Path.Combine(root, "b10679")), currentPath, oldPath);

        Assert.True(reclaimed > 0);
        Assert.True(File.Exists(currentPath));
        Assert.True(File.Exists(sameBuildLegacyPath));
        Assert.True(File.Exists(oldPath));
        Assert.False(Directory.Exists(Path.Combine(root, "b10650")));
        Assert.True(Directory.Exists(Path.Combine(root, "b99999")));
    }

    [Fact]
    public void Recovery_install_with_no_previous_runtime_survives_prune()
    {
        using var temp = new TempDir();
        var root = temp.PathFor("llama-server");
        var recoveredPath = CreateNestedRuntime(root, "b10679");

        var candidates = LlamaServerSetupService.SelectPrunableVersionDirectories(root, recoveredPath, null);

        Assert.Empty(candidates);
        LlamaServerSetupService.PruneVersionDirectories(root, candidates, recoveredPath);
        Assert.True(File.Exists(recoveredPath));
    }

    [Fact]
    public void Sequential_updates_keep_C_remove_only_genuinely_superseded_A()
    {
        using var temp = new TempDir();
        var root = temp.PathFor("llama-server");
        var a = CreateNestedRuntime(root, "b10650");
        var b = CreateNestedRuntime(root, "b10660");

        var afterB = LlamaServerSetupService.SelectPrunableVersionDirectories(root, b, a);
        LlamaServerSetupService.PruneVersionDirectories(root, afterB, b, a);
        Assert.True(File.Exists(b));

        var c = CreateNestedRuntime(root, "b10679");
        var afterC = LlamaServerSetupService.SelectPrunableVersionDirectories(root, c, b);
        LlamaServerSetupService.PruneVersionDirectories(root, afterC, c, b);

        Assert.True(File.Exists(c));
        Assert.True(File.Exists(b));
        Assert.False(Directory.Exists(Path.Combine(root, "b10650")));
    }

    [Fact]
    public void NearestTagDirectoryName_finds_the_version_dir()
    {
        Assert.Equal("b10066", LlamaServerSetupService.NearestTagDirectoryName(NormPath(@"C:\AI\llama.cpp\b10064\b10066\llama-server.exe")));
        Assert.Null(LlamaServerSetupService.NearestTagDirectoryName(NormPath(@"C:\AI\llama.cpp\llama-server.exe")));
    }

    [Theory]
    [InlineData(LlamaRuntimeVariant.Cuda, false, true)]  // GPU build did not launch -> reject
    [InlineData(LlamaRuntimeVariant.Cuda, true, false)]  // GPU build launched -> keep
    [InlineData(LlamaRuntimeVariant.Cpu, false, false)]  // CPU is terminal
    public void ShouldRejectGpuRuntime_is_terminal_at_cpu(LlamaRuntimeVariant variant, bool probeOk, bool expected)
        => Assert.Equal(expected, DoctorService.ShouldRejectGpuRuntime(variant, probeOk));

    [Theory]
    [InlineData(true, true, 999, true)]   // GPU + CPU build -> advise
    [InlineData(true, false, 0, true)]    // GPU + zero offload -> advise
    [InlineData(true, false, 999, false)] // GPU build offloading all -> quiet
    [InlineData(false, true, 0, false)]   // no GPU -> quiet
    public void ShouldAdviseGpuInference_fires_only_when_gpu_wasted(bool gpu, bool cpuBuild, int layers, bool expected)
        => Assert.Equal(expected, DoctorService.ShouldAdviseGpuInference(gpu, cpuBuild, layers));

    [Fact]
    public void ShouldAdviseGpuInference_does_not_treat_typed_auto_as_cpu()
    {
        Assert.False(DoctorService.ShouldAdviseGpuInference(
            hasRealGpu: true,
            installedBuildIsCpu: false,
            GpuPlacementIntent.Auto()));
    }

    [Fact]
    public void Cpu_only_build_detection_recognizes_linux_gpu_shared_libraries()
    {
        using var temp = new TempDir();
        var executable = temp.PathFor("llama-server");
        File.WriteAllText(executable, "test executable");
        File.WriteAllText(temp.PathFor("libggml-cpu.so"), "cpu backend");
        File.WriteAllText(temp.PathFor("libggml-vulkan.so"), "vulkan backend");

        Assert.False(DoctorService.IsCpuOnlyBuild(executable));
    }

    [Fact]
    public void Cpu_only_build_detection_recognizes_windows_gpu_libraries()
    {
        using var temp = new TempDir();
        var executable = temp.PathFor("llama-server.exe");
        File.WriteAllText(executable, "test executable");
        File.WriteAllText(temp.PathFor("ggml-cuda.dll"), "cuda backend");

        Assert.False(DoctorService.IsCpuOnlyBuild(executable));
    }

    [Fact]
    public void Cpu_only_build_detection_does_not_treat_unrelated_files_as_gpu_proof()
    {
        using var temp = new TempDir();
        var executable = temp.PathFor("llama-server");
        File.WriteAllText(executable, "test executable");
        File.WriteAllText(temp.PathFor("model-cuda-not-a-library.gguf"), "model");
        File.WriteAllText(temp.PathFor("notes-vulkan.txt"), "notes");

        Assert.True(DoctorService.IsCpuOnlyBuild(executable));
    }

    private static string? Select(LlamaPlatform platform, LlamaRuntimeVariant variant)
        => LlamaServerSetupService.SelectDownloadAsset(B10066Assets, platform, variant)?.BrowserDownloadUrl;

    private static string CreateNestedRuntime(string root, string tag)
    {
        var versionTag = tag.StartsWith("llama-", StringComparison.OrdinalIgnoreCase) ? tag[6..] : tag;
        var versionDirectory = Path.Combine(root, versionTag);
        var executable = Path.Combine(versionDirectory, $"llama-{versionTag}", OperatingSystem.IsWindows() ? "llama-server.exe" : "llama-server");
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        File.WriteAllText(executable, tag);
        return executable;
    }

    private static string NormPath(string p) => p.Replace('\\', '/').TrimEnd('/');
}
