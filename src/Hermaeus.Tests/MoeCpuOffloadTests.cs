using Hermaeus.Core.Models;
using Hermaeus.Services.ProcessManagement;
using Hermaeus.ViewModels;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>
/// Mixture-of-Experts CPU offload (0.36.0-alpha).
///
/// A different knob from GPU layers, and the right one for a MoE model: the
/// expert weights are most of the file but only a few are active per token, so
/// the useful trade is "attention on the GPU, experts in RAM". Cutting GPU
/// layers to make a MoE model fit gives up the part that actually wants the
/// GPU.
///
/// Flag names read from llama-server b10215's own --help, per the r27 rule
/// that only flags the installed binary lists may be emitted:
///   -cmoe,  --cpu-moe      keep all MoE weights in the CPU
///   -ncmoe, --n-cpu-moe N  keep the MoE weights of the first N layers in the CPU
/// </summary>
public sealed class MoeCpuOffloadTests
{
    private static ServerConfig NewConfig(int cpuMoeLayers, string extraArgs = "") => new()
    {
        Name = "Chat",
        ModelPath = "model.gguf",
        Port = 8080,
        ContextSize = 4096,
        CpuMoeLayers = cpuMoeLayers,
        ExtraArgs = extraArgs
    };

    [Fact]
    public void Zero_emits_nothing_so_an_older_config_launches_exactly_as_before()
    {
        var args = ServerProcessManager.BuildLaunchArguments(NewConfig(0));

        Assert.DoesNotContain("--cpu-moe", args);
        Assert.DoesNotContain("--n-cpu-moe", args);
    }

    [Fact]
    public void A_positive_count_keeps_that_many_layers_of_experts_on_the_cpu()
    {
        var args = ServerProcessManager.BuildLaunchArguments(NewConfig(24));

        var index = args.ToList().IndexOf("--n-cpu-moe");
        Assert.True(index >= 0, "--n-cpu-moe should be emitted for a positive count");
        Assert.Equal("24", args[index + 1]);
        Assert.DoesNotContain("--cpu-moe", args);
    }

    [Fact]
    public void Minus_one_keeps_every_expert_on_the_cpu_and_takes_no_value()
    {
        var args = ServerProcessManager.BuildLaunchArguments(NewConfig(-1));

        Assert.Contains("--cpu-moe", args);
        Assert.DoesNotContain("--n-cpu-moe", args);
    }

    /// <summary>
    /// ExtraArgs always wins, the same rule --parallel and --cache-reuse follow.
    /// ExtraArgs are appended to the command line, so what this asserts is that
    /// the flag appears exactly once (the user's) rather than twice, which is
    /// what a duplicated MoE flag would look like on the real command line.
    /// </summary>
    [Theory]
    [InlineData("--cpu-moe")]
    [InlineData("-cmoe")]
    [InlineData("--n-cpu-moe 8")]
    [InlineData("-ncmoe 8")]
    public void Extra_args_win_over_the_configured_option(string extraArgs)
    {
        var args = ServerProcessManager.BuildLaunchArguments(NewConfig(24, extraArgs));

        var moeFlags = args.Count(a =>
            a is "--cpu-moe" or "-cmoe" or "--n-cpu-moe" or "-ncmoe");
        Assert.Equal(1, moeFlags);

        // And it is the user's value, not the configured 24.
        var configuredIndex = args.ToList().IndexOf("--n-cpu-moe");
        if (configuredIndex >= 0)
            Assert.NotEqual("24", args[configuredIndex + 1]);
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("0", 0)]
    [InlineData("24", 24)]
    [InlineData("all", -1)]
    [InlineData("ALL", -1)]
    [InlineData("-1", -1)]
    [InlineData("-5", -1)]
    [InlineData("nonsense", 0)]
    public void The_editor_text_round_trips_through_the_stored_value(string text, int expected)
    {
        Assert.Equal(expected, ServerProcessViewModel.ParseCpuMoeLayers(text));
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(24, "24")]
    [InlineData(-1, "all")]
    public void The_stored_value_round_trips_back_to_editor_text(int layers, string expected)
    {
        Assert.Equal(expected, ServerProcessViewModel.FormatCpuMoeLayers(layers));
    }
}
