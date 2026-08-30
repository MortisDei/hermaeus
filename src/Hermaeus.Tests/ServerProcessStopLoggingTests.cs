using Hermaeus.Core.Models;
using Hermaeus.Services.ProcessManagement;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>
/// Stop is invoked from several shutdown paths (window close, tray exit,
/// dispose). It must be idempotent at the logging level so the runtime log
/// shows one Stopping/Stopped pair per actual shutdown instead of three.
/// </summary>
public sealed class ServerProcessStopLoggingTests
{
    [Theory]
    [InlineData(true, 137, ServerStatus.Stopped)]
    [InlineData(false, 137, ServerStatus.Error)]
    [InlineData(false, 0, ServerStatus.Stopped)]
    public void Process_exit_status_distinguishes_intentional_stop_from_crash(
        bool stopRequested, int code, ServerStatus expected) =>
        Assert.Equal(expected, ServerProcessManager.GetProcessExitStatus(stopRequested, code));

    [Fact]
    public async Task Stop_logs_once_then_stays_silent_when_already_stopped()
    {
        var mgr = new ServerProcessManager();
        var stoppingLines = 0;
        mgr.LogLine += line =>
        {
            if (line.Contains("Stopping", StringComparison.Ordinal))
                Interlocked.Increment(ref stoppingLines);
        };

        // Drive to a non-running state cheaply: a bogus executable path fails to
        // resolve, leaving the manager in Error with no process.
        await mgr.StartAsync(new ServerConfig
        {
            ExecutablePath = "hermaeus-nonexistent-llama-server-binary",
            ModelPath = "hermaeus-nonexistent-model.gguf",
            Port = 59999
        });

        mgr.Stop(); // real transition out of Error: logs one Stopping
        mgr.Stop(); // already stopped: logs nothing
        mgr.Stop(); // still stopped: logs nothing

        Assert.Equal(1, Volatile.Read(ref stoppingLines));
    }

    [Fact]
    public void Stop_on_a_never_started_manager_logs_nothing()
    {
        var mgr = new ServerProcessManager();
        var stoppingLines = 0;
        mgr.LogLine += line =>
        {
            if (line.Contains("Stopping", StringComparison.Ordinal))
                Interlocked.Increment(ref stoppingLines);
        };

        mgr.Stop();
        mgr.Stop();

        Assert.Equal(0, Volatile.Read(ref stoppingLines));
    }
}
