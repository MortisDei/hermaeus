using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Aether.Core.Models;
using Aether.Services.ProcessManagement;
using Xunit;

namespace Aether.Tests;

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
}
