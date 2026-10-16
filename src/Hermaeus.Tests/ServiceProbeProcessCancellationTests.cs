using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class ServiceProbeProcessCancellationTests
{
    [WindowsOnlyFact]
    public Task Doctor_caller_cancellation_stops_the_started_probe_before_returning()
        => AssertCancellationStopsProbe(async (executable, arguments, ct) =>
            await DoctorService.RunVersionCommandAsync(executable, arguments, TimeSpan.FromSeconds(30), ct));

    [WindowsOnlyFact]
    public Task Nvidia_command_cancellation_stops_the_started_probe_before_returning()
        => AssertCancellationStopsProbe(async (executable, arguments, ct) =>
            await ProcessRuntimeTelemetrySource.RunNvidiaSmiCommandAsync(
                executable, arguments, 123, TimeSpan.FromSeconds(30), ct));

    [WindowsOnlyFact]
    public async Task Nvidia_command_drains_stderr_concurrently_with_stdout()
    {
        var script = "[Console]::Error.Write(('e' * 1048576)); [Console]::Out.WriteLine('123, 42')";
        var result = await ProcessRuntimeTelemetrySource.RunNvidiaSmiCommandAsync(
            PowerShellExecutable, PowerShellArguments(script), 123, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal(42L * 1024 * 1024, result.Bytes);
        Assert.Equal("nvidia-smi-process-gpu-memory", result.Source);
    }

    [WindowsOnlyFact]
    public async Task Nvidia_truncated_stdout_is_unknown_even_when_the_prefix_contains_a_valid_counter()
    {
        var script = "[Console]::Out.WriteLine('123, 42'); [Console]::Out.Write(('x' * 1048576))";
        var result = await ProcessRuntimeTelemetrySource.RunNvidiaSmiCommandAsync(
            PowerShellExecutable, PowerShellArguments(script), 123, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Null(result.Bytes);
        Assert.Equal("process-gpu-unavailable", result.Source);
    }

    [WindowsOnlyFact]
    public async Task Nvidia_command_deadline_returns_unknown()
    {
        var result = await ProcessRuntimeTelemetrySource.RunNvidiaSmiCommandAsync(
            PowerShellExecutable, PowerShellArguments("[System.Threading.Thread]::Sleep(30000)"),
            123, TimeSpan.FromMilliseconds(100), CancellationToken.None);

        Assert.Null(result.Bytes);
        Assert.Equal("process-gpu-unavailable", result.Source);
    }

    [WindowsOnlyFact]
    public async Task Doctor_flooded_streams_remain_bounded_and_the_probe_finishes()
    {
        var script = "[Console]::Error.Write(('e' * 1048576)); [Console]::Out.Write(('o' * 1048576))";
        var result = await DoctorService.RunVersionCommandAsync(
            PowerShellExecutable, PowerShellArguments(script), TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.InRange(result.Stdout.Length, 8000, 8020);
        Assert.InRange(result.Stderr.Length, 8000, 8020);
        Assert.EndsWith(" [truncated]", result.Stdout);
        Assert.EndsWith(" [truncated]", result.Stderr);
    }

    private static string PowerShellExecutable => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");

    private static IReadOnlyList<string> PowerShellArguments(string script) =>
        ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand",
            Convert.ToBase64String(Encoding.Unicode.GetBytes(script))];

    [WindowsOnlyFact]
    public async Task Help_probe_drains_stderr_before_accepting_complete_help()
    {
        var result = await LocalModelCapabilityService.RunHelpCommandAsync(PowerShellExecutable,
            PowerShellArguments("[Console]::Error.Write(('e' * 65536)); [Console]::Out.Write('--synthetic-help')"),
            CancellationToken.None);
        Assert.NotNull(result);
        Assert.Contains("--synthetic-help", result);
    }

    [WindowsOnlyFact]
    public async Task Help_probe_refuses_truncated_capability_evidence()
    {
        var result = await LocalModelCapabilityService.RunHelpCommandAsync(PowerShellExecutable,
            PowerShellArguments("[Console]::Out.Write('--synthetic-help'); [Console]::Out.Write(('x' * 1048576))"),
            CancellationToken.None);
        Assert.Null(result);
    }

    [WindowsOnlyFact]
    public Task Help_probe_cancellation_preserves_unavailable_contract_and_stops_root() =>
        AssertCancellationStopsProbe(async (executable, arguments, ct) =>
            Assert.Null(await LocalModelCapabilityService.RunHelpCommandAsync(executable, arguments, ct)), false);

    [WindowsOnlyFact]
    public async Task System_probe_drains_stderr_concurrently()
    {
        var result = await SystemInfoService.RunCommandAsync(PowerShellExecutable,
            PowerShellArguments("[Console]::Error.Write(('e' * 1048576)); [Console]::Out.Write('synthetic-info')").ToArray(),
            CancellationToken.None, TimeSpan.FromSeconds(5));
        Assert.Equal("synthetic-info", result);
    }

    [WindowsOnlyFact]
    public async Task System_probe_does_not_parse_truncated_stdout()
    {
        var result = await SystemInfoService.RunCommandAsync(PowerShellExecutable,
            PowerShellArguments("[Console]::Out.Write(('x' * 1048576))").ToArray(), CancellationToken.None);
        Assert.Empty(result);
    }

    [WindowsOnlyFact]
    public Task System_probe_cancellation_preserves_unavailable_contract_and_stops_root() =>
        AssertCancellationStopsProbe(async (executable, arguments, ct) =>
            Assert.Empty(await SystemInfoService.RunCommandAsync(executable, arguments.ToArray(), ct)), false);

    private static async Task AssertCancellationStopsProbe(
        Func<string, IReadOnlyList<string>, CancellationToken, Task> start, bool throwsCancellation = true)
    {
        var pipeName = $"hermaeus-probe-{Guid.NewGuid():N}";
        await using var pipe = new NamedPipeServerStream(
            pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var cancellation = new CancellationTokenSource();
        var script = $$"""
            $pipe = [System.IO.Pipes.NamedPipeClientStream]::new('.', '{{pipeName}}', [System.IO.Pipes.PipeDirection]::Out)
            $pipe.Connect(5000)
            $writer = [System.IO.StreamWriter]::new($pipe)
            $writer.WriteLine($PID)
            $writer.Flush()
            $writer.Dispose()
            [System.Threading.Thread]::Sleep([System.Threading.Timeout]::Infinite)
            """;
        var run = start(PowerShellExecutable, PowerShellArguments(script), cancellation.Token);
        Process? ownedProcess = null;
        try
        {
            using var startupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await pipe.WaitForConnectionAsync(startupTimeout.Token);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            var pidLine = await reader.ReadLineAsync(startupTimeout.Token);
            ownedProcess = Process.GetProcessById(int.Parse(Assert.IsType<string>(pidLine),
                System.Globalization.CultureInfo.InvariantCulture));
            Assert.False(ownedProcess.HasExited);

            cancellation.Cancel();
            if (throwsCancellation)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                    await run.WaitAsync(TimeSpan.FromSeconds(5)));
            else
                await run.WaitAsync(TimeSpan.FromSeconds(5));

            // Only the signalled root identity is proven here. This is not
            // evidence that every possible descendant has been contained.
            ownedProcess.Refresh();
            Assert.True(ownedProcess.HasExited, "The cancelled probe returned while its owned root was still alive.");
        }
        finally
        {
            cancellation.Cancel();
            if (ownedProcess is not null)
            {
                try
                {
                    if (!ownedProcess.HasExited)
                        ownedProcess.Kill(entireProcessTree: true);
                    await ownedProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
                finally { ownedProcess.Dispose(); }
            }
            try { await run.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { }
        }
    }
}
