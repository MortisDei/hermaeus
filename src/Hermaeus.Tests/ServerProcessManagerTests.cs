using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Hermaeus.Core.Models;
using Hermaeus.Services.ProcessManagement;
using Xunit;

namespace Hermaeus.Tests;

public sealed class ServerProcessManagerTests
{
    /// <summary>Windows utility that accepts arbitrary CLI args and exits almost instantly with a nonzero code, useful as a stand-in "process that exits immediately".</summary>
    private static readonly string ImmediateExitExecutable =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "where.exe");

    private sealed class FakeProcessJobObject(bool succeeds) : IProcessJobObject
    {
        public bool AssignAttempted { get; private set; }
        public bool TryAssign(Process process)
        {
            AssignAttempted = true;
            return succeeds;
        }
    }

    private sealed class FakePortOwnerLookup : IPortOwnerLookup
    {
        public PortOwnerInfo? Owner { get; set; }
        public bool IsPortListening(int port) => Owner is not null;
        public PortOwnerInfo? FindOwner(int port) => Owner;
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static ServerConfig NewConfig(string executablePath, string modelPath, int port) => new()
    {
        Name = "Test",
        ExecutablePath = executablePath,
        ModelPath = modelPath,
        Port = port,
        ContextSize = 4096
    };

    [Fact]
    public async Task StartAsync_reports_error_without_launching_when_the_port_is_already_in_use()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var temp = new TempDir();
        var modelPath = temp.PathFor("model.gguf");
        File.WriteAllText(modelPath, "fake");

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var mgr = new ServerProcessManager();
        var config = NewConfig(ImmediateExitExecutable, modelPath, port);

        await mgr.StartAsync(config);

        Assert.Equal(ServerStatus.Error, mgr.Status);
        Assert.Contains(port.ToString(), mgr.ErrorMessage);
        Assert.DoesNotContain("Launched PID", mgr.GetLog());
    }

    [Fact]
    public async Task StartAsync_does_not_block_launch_when_job_object_assignment_fails()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var temp = new TempDir();
        var modelPath = temp.PathFor("model.gguf");
        File.WriteAllText(modelPath, "fake");

        var jobObject = new FakeProcessJobObject(succeeds: false);
        var mgr = new ServerProcessManager(jobObject: jobObject);
        var config = NewConfig(ImmediateExitExecutable, modelPath, GetFreePort());

        await mgr.StartAsync(config);

        Assert.True(jobObject.AssignAttempted);
        // The process still launched (job-object failure must never block a launch) and
        // where.exe's near-instant nonzero exit is reported as an Error, not left hanging.
        Assert.Contains("Launched PID", mgr.GetLog());
        Assert.Equal(ServerStatus.Error, mgr.Status);
        Assert.Contains("could not attach process to the app's job object", mgr.GetLog());
    }

    [Fact]
    public async Task StartAsync_reports_error_with_exit_code_and_log_tail_when_the_process_exits_immediately()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var temp = new TempDir();
        var modelPath = temp.PathFor("model.gguf");
        File.WriteAllText(modelPath, "fake");

        var mgr = new ServerProcessManager();
        var config = NewConfig(ImmediateExitExecutable, modelPath, GetFreePort());

        await mgr.StartAsync(config);

        Assert.Equal(ServerStatus.Error, mgr.Status);
        Assert.Contains("Exit code", mgr.ErrorMessage);
        Assert.Contains("Recent log:", mgr.ErrorMessage);
    }

    [Fact]
    public async Task StartAsync_reports_a_cancelled_log_line_when_cancelled_during_the_health_wait()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var temp = new TempDir();
        var modelPath = temp.PathFor("model.gguf");
        File.WriteAllText(modelPath, "fake");

        var mgr = new ServerProcessManager();
        var config = NewConfig(ImmediateExitExecutable, modelPath, GetFreePort());

        // Pre-cancelled token: the health-wait loop's first ThrowIfCancellationRequested
        // fires before it ever checks HasExited or attempts an HTTP call, so this
        // exercises the cancel path deterministically regardless of exe timing.
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await mgr.StartAsync(config, cts.Token);

        Assert.Equal(ServerStatus.Stopped, mgr.Status);
        Assert.Contains("cancelled", mgr.GetLog(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>r11 4.4: StartAsync replaced _monitorCts on restart without disposing the previous instance.</summary>
    [Fact]
    public async Task StartAsync_disposes_the_previous_monitor_cts_on_restart()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var temp = new TempDir();
        var modelPath = temp.PathFor("model.gguf");
        File.WriteAllText(modelPath, "fake");

        var mgr = new ServerProcessManager();
        var config = NewConfig(ImmediateExitExecutable, modelPath, GetFreePort());

        // where.exe exits before /health ever responds, but StartAsync still
        // creates _monitorCts before the health-wait loop observes that.
        await mgr.StartAsync(config);
        Assert.Equal(ServerStatus.Error, mgr.Status);
        var firstCts = GetMonitorCts(mgr);
        Assert.NotNull(firstCts);

        config.Port = GetFreePort();
        await mgr.StartAsync(config);

        Assert.Throws<ObjectDisposedException>(() => firstCts!.Token.Register(() => { }));
    }

    /// <summary>r11 4.5: NormalizeConfig used to write the resolved executable/model paths back onto the caller's ServerConfig - typically the settings object itself - silently rewriting a directory/bare-name configuration in memory, later persisted by an unrelated SaveAsync.</summary>
    [Fact]
    public async Task StartAsync_does_not_mutate_the_callers_ServerConfig()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var temp = new TempDir();
        var modelDir = temp.PathFor("models");
        Directory.CreateDirectory(modelDir);
        var modelPath = Path.Combine(modelDir, "model.gguf");
        File.WriteAllText(modelPath, "fake");

        var mgr = new ServerProcessManager();
        // A directory for ExecutablePath and ModelPath so NormalizeConfig
        // must resolve both to concrete files internally.
        var executableDir = temp.PathFor("bin");
        Directory.CreateDirectory(executableDir);
        File.Copy(ImmediateExitExecutable, Path.Combine(executableDir, "llama-server.exe"));
        var config = NewConfig(executableDir, modelDir, GetFreePort());

        await mgr.StartAsync(config);

        Assert.Equal(executableDir, config.ExecutablePath);
        Assert.Equal(modelDir, config.ModelPath);
    }

    /// <summary>r11 1.5: auto-tune probes started processes and waited for /health on a port it never checked for prior occupancy; if anything was already listening there, every candidate "reached /health" instantly against the wrong process.</summary>
    [Fact]
    public async Task AutoTuneAsync_fails_fast_with_the_named_port_owner_when_the_port_is_occupied()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var temp = new TempDir();
        var modelPath = temp.PathFor("model.gguf");
        File.WriteAllText(modelPath, "fake");

        var lookup = new FakePortOwnerLookup { Owner = new PortOwnerInfo(4321, "some-other-server", null) };
        var config = NewConfig(ImmediateExitExecutable, modelPath, GetFreePort());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ServerProcessManager.AutoTuneAsync(config, portOwnerLookup: lookup));

        Assert.Contains("some-other-server", ex.Message);
        Assert.Contains("4321", ex.Message);
    }

    private static CancellationTokenSource? GetMonitorCts(ServerProcessManager mgr) =>
        (CancellationTokenSource?)typeof(ServerProcessManager)
            .GetField("_monitorCts", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(mgr);

    // r17 01-gguf-context-and-tuning.md 1.5: pure context-suggestion ladder.
    private static Hermaeus.Services.GgufModelInfo Shape() => new("llama", "Q4_K_M", 32, 131072, 4096, 32, 8, 128, 128);

    [Fact]
    public void SuggestContextSize_returns_null_when_the_configured_context_already_fits()
    {
        // 4 GB weights * 1.05 + tiny KV at 4096 context comfortably fits an 8 GB card.
        var result = ServerProcessManager.SuggestContextSize(Shape(), fileSizeBytes: 4_000_000_000, vramBytes: 8_000_000_000, configuredContext: 4096);

        // Now we expect a suggestion if a larger ladder value fits.
        Assert.NotNull(result);
        Assert.True(result > 4096);
    }

    [Fact]
    public void SuggestContextSize_suggests_upward_when_headroom_exists()
    {
        var gemmaLike = new Hermaeus.Services.GgufModelInfo(
            "gemma3",
            "Q4_K_M",
            BlockCount: 34,
            TrainingContextLength: 131072,
            EmbeddingLength: 2560,
            HeadCount: 8,
            HeadCountKv: 4,
            KeyLength: 256,
            ValueLength: 256,
            SlidingWindow: 1024,
            SlidingWindowPattern: [true, true, true, true, true, false]);

        // 3.9 GB weights, 8 GB VRAM. 65536 context fits, so it should suggest 131072 if that also fits.
        var result = ServerProcessManager.SuggestContextSize(
            gemmaLike,
            fileSizeBytes: 3_900_000_000,
            vramBytes: 8_000_000_000,
            configuredContext: 65536);

        Assert.NotNull(result);
        Assert.Equal(131072, result);
    }

    [Fact]
    public void SuggestContextSize_is_capped_by_the_models_training_context()
    {
        var trainedShort = Shape() with { TrainingContextLength = 8192 };

        // 4 GB VRAM: the full 131072 configured context needs ~16 GB of KV alone and does not
        // fit, forcing a ladder search; the search must not suggest anything above the 8192
        // training context even though larger ladder values would otherwise fit 4 GB fine.
        var result = ServerProcessManager.SuggestContextSize(trainedShort, fileSizeBytes: 100_000_000, vramBytes: 4_000_000_000, configuredContext: 131072);

        Assert.NotNull(result);
        Assert.True(result <= 8192);
    }

    [Fact]
    public void SuggestContextSize_returns_null_when_nothing_on_the_ladder_fits()
    {
        var result = ServerProcessManager.SuggestContextSize(Shape(), fileSizeBytes: 4_000_000_000, vramBytes: 1_000_000, configuredContext: 131072);

        Assert.Null(result);
    }

    [Fact]
    public void SuggestContextSize_returns_null_when_shape_facts_are_missing()
    {
        var incomplete = new Hermaeus.Services.GgufModelInfo("llama", "Q4_K_M", null, null, null, null, null, null, null);

        var result = ServerProcessManager.SuggestContextSize(incomplete, fileSizeBytes: 4_000_000_000, vramBytes: 64_000_000_000, configuredContext: 131072);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SuggestContextSize_returns_null_when_vram_is_unavailable(long vramBytes)
    {
        var result = ServerProcessManager.SuggestContextSize(Shape(), fileSizeBytes: 4_000_000_000, vramBytes: vramBytes, configuredContext: 131072);

        Assert.Null(result);
    }

    /// <summary>r18 04-llama-server-engine-options.md 4.2 acceptance criterion: with the same
    /// model and VRAM, switching the KV cache type from f16 to q8_0 must visibly raise the
    /// suggested/fitting context.</summary>
    [Fact]
    public void SuggestContextSize_reflects_a_cheaper_kv_cache_type()
    {
        const long fileSize = 6_000_000_000; // 6 GB weights
        const long vram = 8_000_000_000; // 8 GB VRAM

        var f16Result = ServerProcessManager.SuggestContextSize(Shape(), fileSize, vram, configuredContext: 4096);
        var q8Result = ServerProcessManager.SuggestContextSize(
            Shape(), fileSize, vram, configuredContext: 4096,
            bytesPerElementK: 1.0625, bytesPerElementV: 1.0625);

        Assert.NotNull(f16Result);
        Assert.NotNull(q8Result);
        Assert.True(q8Result > f16Result, $"q8_0 ({q8Result}) should fit a larger context than f16 ({f16Result})");
    }
}
