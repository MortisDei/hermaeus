using Hermaeus.Core.Models;
using Hermaeus.Services;
using Hermaeus.Services.ProcessManagement;
using Xunit;

namespace Hermaeus.Tests;

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

    /// <summary>
    /// The pair stays coherent (equal), which is what stops llama-server
    /// logging its clamp warning, but it is no longer the hardcoded 512 this
    /// test used to assert. 512 is also the largest input the server will embed
    /// at all, and RAG chunks routinely exceed it, so that value was silently
    /// refusing a large share of every ingest. See EmbeddingBatchSizeTests.
    /// </summary>
    [Fact]
    public void Embeddings_pin_a_coherent_batch_pair_sized_to_the_context()
    {
        var args = Args(new ServerConfig { EmbeddingsMode = true, ContextSize = 4096 });
        Assert.Equal("4096", ArgValue(args, "-b"));
        Assert.Equal("4096", ArgValue(args, "-ub"));
    }

    // r18 04-llama-server-engine-options.md 4.1: first-class engine options, defaults
    // byte-identical to today's command line.

    [Fact]
    public void Default_engine_options_emit_nothing()
    {
        var args = Args(new ServerConfig());

        Assert.DoesNotContain("--cache-type-k", args);
        Assert.DoesNotContain("--cache-type-v", args);
        Assert.DoesNotContain("--flash-attn", args);
        Assert.DoesNotContain("--context-shift", args);
        Assert.DoesNotContain("--mlock", args);
        Assert.DoesNotContain("--no-mmap", args);
        Assert.DoesNotContain("--spec-type", args);
    }

    [Fact]
    public void KvCacheType_emits_only_when_not_f16()
    {
        var q8 = Args(new ServerConfig { KvCacheTypeK = "q8_0", KvCacheTypeV = "q8_0" });
        Assert.Equal("q8_0", ArgValue(q8, "--cache-type-k"));
        Assert.Equal("q8_0", ArgValue(q8, "--cache-type-v"));

        var f16 = Args(new ServerConfig { KvCacheTypeK = "f16", KvCacheTypeV = "f16" });
        Assert.DoesNotContain("--cache-type-k", f16);
        Assert.DoesNotContain("--cache-type-v", f16);
    }

    [Fact]
    public void FlashAttention_auto_emits_nothing_on_off_emit_the_value()
    {
        Assert.DoesNotContain("--flash-attn", Args(new ServerConfig { FlashAttention = "auto" }));
        Assert.Equal("on", ArgValue(Args(new ServerConfig { FlashAttention = "on" }), "--flash-attn"));
        Assert.Equal("off", ArgValue(Args(new ServerConfig { FlashAttention = "off" }), "--flash-attn"));
    }

    [Fact]
    public void ContextShift_memory_lock_and_no_memory_map_are_opt_in_flags()
    {
        Assert.Contains("--context-shift", Args(new ServerConfig { ContextShift = true }));
        Assert.Contains("--mlock", Args(new ServerConfig { MemoryLock = true }));
        Assert.Contains("--no-mmap", Args(new ServerConfig { NoMemoryMap = true }));
    }

    [Fact]
    public void Speculative_types_emit_spec_type_only_when_configured()
    {
        // r27 03 3.1 replaced r18 4.4's NgramSpeculative bool with a composable
        // section, because --spec-type is a list flag. The legacy bool no longer
        // reaches the launch path at all: SettingsService.NormalizeManagedServers
        // upgrades it into Types before a server is ever started.
        Assert.DoesNotContain("--spec-type", Args(new ServerConfig()));
        Assert.DoesNotContain("--spec-type", Args(new ServerConfig { NgramSpeculative = true }));
        Assert.Equal("ngram-mod", ArgValue(
            Args(new ServerConfig { Speculative = new SpeculativeDecodingConfig { Types = ["ngram-mod"] } }),
            "--spec-type"));
    }

    [Fact]
    public void ExtraArgs_always_wins_over_first_class_engine_options()
    {
        var cfg = new ServerConfig
        {
            KvCacheTypeK = "q8_0",
            FlashAttention = "on",
            ContextShift = true,
            MemoryLock = true,
            NoMemoryMap = true,
            NgramSpeculative = true,
            ExtraArgs = "--cache-type-k q4_0 --flash-attn off --no-context-shift --spec-type none"
        };
        var args = Args(cfg);

        // Each flag must appear exactly once (the ExtraArgs value), never doubled.
        Assert.Equal(1, args.Count(a => a == "--cache-type-k"));
        Assert.Equal("q4_0", ArgValue(args, "--cache-type-k"));
        Assert.Equal(1, args.Count(a => a == "--flash-attn"));
        Assert.Equal("off", ArgValue(args, "--flash-attn"));
        Assert.DoesNotContain("--context-shift", args);
        Assert.Contains("--no-context-shift", args);
        Assert.Equal(1, args.Count(a => a == "--spec-type"));
        Assert.Equal("none", ArgValue(args, "--spec-type"));
        // --mlock/--no-mmap have no ExtraArgs counterpart typed here, so those still emit.
        Assert.Contains("--mlock", args);
        Assert.Contains("--no-mmap", args);
    }

    [Fact]
    public void A_fresh_default_config_produces_a_byte_identical_command_line_to_v0_22_0_alpha()
    {
        var cfg = new ServerConfig { ModelPath = "model.gguf" };
        var args = Args(cfg);

        Assert.Equal(
            ["-m", "model.gguf", "--port", "8080", "--host", "127.0.0.1", "--ctx-size", "4096",
             "--threads", "4", "--parallel", "1", "--cache-reuse", "256"],
            args);
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
