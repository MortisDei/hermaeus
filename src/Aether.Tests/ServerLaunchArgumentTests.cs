using Aether.Core.Models;
using Aether.Services;
using Aether.Services.ProcessManagement;
using Xunit;

namespace Aether.Tests;

/// <summary>
/// llama-server launch defaults that make a single-user chat app fast: GPU
/// offload semantics, a single request slot, prompt-cache reuse, the embeddings
/// batch clamp, and per-slot context truth.
/// </summary>
public sealed class ServerLaunchArgumentTests
{
    [Fact]
    public void GpuLayers_renders_by_semantics()
    {
        Assert.DoesNotContain("--n-gpu-layers", Args(new ServerConfig { GpuLayers = 0 }));
        Assert.Equal("999", ArgValue(Args(new ServerConfig { GpuLayers = -1 }), "--n-gpu-layers"));
        Assert.Equal("20", ArgValue(Args(new ServerConfig { GpuLayers = 20 }), "--n-gpu-layers"));
    }

    [Fact]
    public void Default_launch_pins_single_slot_and_cache_reuse()
    {
        var args = Args(new ServerConfig());
        Assert.Equal("1", ArgValue(args, "--parallel"));
        Assert.Equal("256", ArgValue(args, "--cache-reuse"));
    }

    [Fact]
    public void User_parallel_and_cache_reuse_override_defaults()
    {
        var args = Args(new ServerConfig { Slots = 4, ExtraArgs = "--parallel 8 --cache-reuse 32" });
        // A user-supplied value wins; the default is not also emitted.
        Assert.Equal(1, args.Count(a => a == "--parallel"));
        Assert.Equal(1, args.Count(a => a == "--cache-reuse"));
        Assert.Equal("8", ArgValue(args, "--parallel"));
        Assert.Equal("32", ArgValue(args, "--cache-reuse"));
    }

    [Fact]
    public void Embeddings_pin_a_coherent_batch_pair()
    {
        var args = Args(new ServerConfig { EmbeddingsMode = true });
        Assert.Equal("512", ArgValue(args, "-b"));
        Assert.Equal("512", ArgValue(args, "-ub"));
    }

    [Fact]
    public void ParsePerSlotContextLength_prefers_per_slot_then_divides_total()
    {
        // default_generation_settings.n_ctx is already per-slot.
        Assert.Equal(16128, LlamaCppService.ParsePerSlotContextLength(
            """{"n_ctx": 64512, "default_generation_settings": {"n_ctx": 16128}}""", 4));
        // Only the total is exposed: divide by slots.
        Assert.Equal(16128, LlamaCppService.ParsePerSlotContextLength("""{"n_ctx": 64512}""", 4));
        // Single slot: total is the per-slot ceiling.
        Assert.Equal(8192, LlamaCppService.ParsePerSlotContextLength("""{"n_ctx": 8192}""", 1));
    }

    private static List<string> Args(ServerConfig cfg) => ServerProcessManager.BuildLaunchArguments(cfg).ToList();

    private static string? ArgValue(List<string> args, string flag)
    {
        var i = args.IndexOf(flag);
        return i >= 0 && i + 1 < args.Count ? args[i + 1] : null;
    }
}
