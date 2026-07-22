using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>r19 2.3: reuse a previously-downloaded CUDA runtime companion instead of re-downloading it on every llama.cpp update.</summary>
public sealed class CudaRuntimeReuseTests
{
    private const string AssetName = "cudart-llama-bin-win-cuda-12.4-x64.zip";

    [Fact]
    public void TryReuse_copies_files_from_a_sibling_with_a_matching_verified_marker()
    {
        using var temp = new TempDir();
        var root = temp.PathFor("llama-server");
        var previous = Path.Combine(root, "b100");
        Directory.CreateDirectory(previous);
        var dllPath = Path.Combine(previous, "cudart64_12.dll");
        File.WriteAllText(dllPath, "fake-dll-bytes");
        CudaRuntimeReuse.WriteMarker(previous, AssetName, [dllPath]);

        var destination = Path.Combine(root, "b101");

        var reusedFrom = CudaRuntimeReuse.TryReuse(root, destination, AssetName);

        Assert.Equal("b100", reusedFrom);
        Assert.True(File.Exists(Path.Combine(destination, "cudart64_12.dll")));
        Assert.Equal("fake-dll-bytes", File.ReadAllText(Path.Combine(destination, "cudart64_12.dll")));
        // The new directory gets its own marker so a THIRD update can chain-reuse from it too.
        Assert.True(File.Exists(Path.Combine(destination, "cudart.json")));
    }

    [Fact]
    public void TryReuse_refuses_a_mismatched_asset_name()
    {
        using var temp = new TempDir();
        var root = temp.PathFor("llama-server");
        var previous = Path.Combine(root, "b100");
        Directory.CreateDirectory(previous);
        var dllPath = Path.Combine(previous, "cudart64_12.dll");
        File.WriteAllText(dllPath, "fake-dll-bytes");
        CudaRuntimeReuse.WriteMarker(previous, "cudart-llama-bin-win-cuda-11.8-x64.zip", [dllPath]);

        var destination = Path.Combine(root, "b101");

        var reusedFrom = CudaRuntimeReuse.TryReuse(root, destination, AssetName);

        Assert.Null(reusedFrom);
        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public void TryReuse_refuses_when_a_recorded_file_is_missing()
    {
        using var temp = new TempDir();
        var root = temp.PathFor("llama-server");
        var previous = Path.Combine(root, "b100");
        Directory.CreateDirectory(previous);
        var dllPath = Path.Combine(previous, "cudart64_12.dll");
        File.WriteAllText(dllPath, "fake-dll-bytes");
        CudaRuntimeReuse.WriteMarker(previous, AssetName, [dllPath]);
        File.Delete(dllPath); // simulates a partially-cleaned-up previous install

        var destination = Path.Combine(root, "b101");

        var reusedFrom = CudaRuntimeReuse.TryReuse(root, destination, AssetName);

        Assert.Null(reusedFrom);
    }

    [Fact]
    public void TryReuse_refuses_when_a_recorded_file_size_no_longer_matches()
    {
        using var temp = new TempDir();
        var root = temp.PathFor("llama-server");
        var previous = Path.Combine(root, "b100");
        Directory.CreateDirectory(previous);
        var dllPath = Path.Combine(previous, "cudart64_12.dll");
        File.WriteAllText(dllPath, "fake-dll-bytes");
        CudaRuntimeReuse.WriteMarker(previous, AssetName, [dllPath]);
        File.WriteAllText(dllPath, "different length now"); // size no longer matches the marker

        var destination = Path.Combine(root, "b101");

        var reusedFrom = CudaRuntimeReuse.TryReuse(root, destination, AssetName);

        Assert.Null(reusedFrom);
    }

    [Fact]
    public void TryReuse_returns_null_when_no_sibling_directories_exist()
    {
        using var temp = new TempDir();
        var root = temp.PathFor("llama-server");
        Directory.CreateDirectory(root);

        var reusedFrom = CudaRuntimeReuse.TryReuse(root, Path.Combine(root, "b101"), AssetName);

        Assert.Null(reusedFrom);
    }
}
