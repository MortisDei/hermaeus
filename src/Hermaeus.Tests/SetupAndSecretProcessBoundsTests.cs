using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;

namespace Hermaeus.Tests;

public sealed class SetupAndSecretProcessBoundsTests
{
    [WindowsOnlyFact]
    public async Task Voice_unbroken_output_is_bounded_and_preserves_final_diagnostics()
    {
        var result = await VoiceProviderProcessRunner.RunProcessAsync(PowerShellExecutable,
            Arguments("[Console]::Out.Write(('v' * 1048576)); [Console]::Out.Write('FINAL-VOICE')"),
            Path.GetTempPath(), null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.InRange(result.Log.Length, 1, 132000);
        Assert.Contains("truncated", result.Log, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FINAL-VOICE", result.Log);
    }

    [WindowsOnlyFact]
    public async Task Setup_bounds_retained_output_and_progress_delivery()
    {
        var progress = new CountingProgress();
        var result = await LocalAiSetupService.RunProcessAsync(PowerShellExecutable,
            Arguments("1..1024 | ForEach-Object { [Console]::Error.WriteLine(('e' * 4096)) }; [Console]::Out.WriteLine('FINAL-SETUP')"),
            Path.GetTempPath(), progress, CancellationToken.None);

        Assert.False(result.Success);
        Assert.InRange(result.Log.Length, 1, 132000);
        Assert.Contains("truncated", result.Log, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FINAL-SETUP", result.Log);
        Assert.InRange(progress.Characters, 1, 132000);
        Assert.InRange(progress.MaximumFragment, 1, 8192);
    }

    [Fact]
    public void Setup_view_retains_a_bounded_tail_across_progress_and_final_result_appends()
    {
        var view = new LocalAiSetupSettingsViewModel(null!, null!, null!, null!, null!, null!, () => Task.CompletedTask);
        var append = typeof(LocalAiSetupSettingsViewModel).GetMethod("AppendSetupLog", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(append);
        for (var index = 0; index < 256; index++)
            append.Invoke(view, [new string('p', 4096)]);
        append.Invoke(view, ["FINAL-RESULT"]);

        Assert.InRange(view.LocalAiSetupLog.Length, 1, 132000);
        Assert.Contains("truncated", view.LocalAiSetupLog, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("FINAL-RESULT", view.LocalAiSetupLog);
    }

    [WindowsOnlyFact]
    public async Task Secret_command_drains_stderr_while_writing_standard_input()
    {
        var result = await SecretStore.RunOsSecretCommandAsync(PowerShellExecutable,
            Arguments("[Console]::Error.Write(('e' * 1048576)); $inputText = [Console]::In.ReadToEnd(); [Console]::Out.Write('synthetic-secret')").ToArray(),
            new string('i', 1048576), CancellationToken.None, TimeSpan.FromSeconds(3));

        Assert.True(result.Success);
        Assert.Equal("synthetic-secret", result.Output);
    }

    [WindowsOnlyFact]
    public async Task Secret_command_refuses_a_truncated_secret_instead_of_returning_its_prefix()
    {
        var result = await SecretStore.RunOsSecretCommandAsync(PowerShellExecutable,
            Arguments("[Console]::Out.Write(('s' * 1048576))").ToArray(), null,
            CancellationToken.None, TimeSpan.FromSeconds(3));

        Assert.False(result.Success);
        Assert.Empty(result.Output);
    }

    [WindowsOnlyFact]
    public Task Voice_cancellation_stops_the_signalled_root_before_returning() =>
        AssertRootStops(async (arguments, ct) =>
            await VoiceProviderProcessRunner.RunProcessAsync(PowerShellExecutable, arguments, Path.GetTempPath(), null, ct), true);

    [WindowsOnlyFact]
    public Task Setup_cancellation_stops_the_signalled_root_before_returning() =>
        AssertRootStops(async (arguments, ct) =>
            await LocalAiSetupService.RunProcessAsync(PowerShellExecutable, arguments, Path.GetTempPath(), null, ct), true);

    [WindowsOnlyFact]
    public Task Secret_cancellation_stops_the_signalled_root_and_returns_failure() =>
        AssertRootStops(async (arguments, ct) =>
            Assert.False((await SecretStore.RunOsSecretCommandAsync(PowerShellExecutable, arguments.ToArray(), null, ct)).Success), false);

    private static string PowerShellExecutable => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
        "WindowsPowerShell", "v1.0", "powershell.exe");

    private static IReadOnlyList<string> Arguments(string script) =>
        ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))];

    private static async Task AssertRootStops(Func<IReadOnlyList<string>, CancellationToken, Task> start, bool throwsCancellation)
    {
        var name = $"hermaeus-setup-probe-{Guid.NewGuid():N}";
        await using var pipe = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var cancellation = new CancellationTokenSource();
        var script = $$"""
            $pipe = [System.IO.Pipes.NamedPipeClientStream]::new('.', '{{name}}', [System.IO.Pipes.PipeDirection]::Out)
            $pipe.Connect(5000)
            $writer = [System.IO.StreamWriter]::new($pipe)
            $writer.WriteLine($PID)
            $writer.Flush()
            $writer.Dispose()
            [System.Threading.Thread]::Sleep(30000)
            """;
        var run = start(Arguments(script), cancellation.Token);
        Process? owned = null;
        try
        {
            using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await pipe.WaitForConnectionAsync(startup.Token);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            owned = Process.GetProcessById(int.Parse(Assert.IsType<string>(await reader.ReadLineAsync(startup.Token)),
                System.Globalization.CultureInfo.InvariantCulture));
            Assert.False(owned.HasExited);
            cancellation.Cancel();
            if (throwsCancellation)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await run.WaitAsync(TimeSpan.FromSeconds(5)));
            else
                await run.WaitAsync(TimeSpan.FromSeconds(5));
            owned.Refresh();
            Assert.True(owned.HasExited, "The operation returned while its signalled root was alive.");
        }
        finally
        {
            cancellation.Cancel();
            if (owned is not null)
            {
                try
                {
                    if (!owned.HasExited) owned.Kill(entireProcessTree: true);
                    await owned.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
                finally { owned.Dispose(); }
            }
            try { await run.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { }
        }
    }

    private sealed class CountingProgress : IProgress<string>
    {
        private readonly object _gate = new();
        public int Characters { get; private set; }
        public int MaximumFragment { get; private set; }
        public void Report(string value)
        {
            lock (_gate)
            {
                Characters += value.Length;
                MaximumFragment = Math.Max(MaximumFragment, value.Length);
            }
        }
    }
}
