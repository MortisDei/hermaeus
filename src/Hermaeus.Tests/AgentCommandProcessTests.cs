using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Xml.Linq;
using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Hermaeus.Core.Models;
using Xunit;

namespace Hermaeus.Tests;

public sealed class AgentCommandProcessTests
{
    [Fact]
    public async Task Already_cancelled_capture_does_not_launch_an_executable()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AgentProcessCapture.RunAsync(
            new ProcessStartInfo("nonexistent-hermaeus-fixture"), TimeSpan.FromSeconds(1), cancelled.Token));
    }

    [Fact]
    public async Task Capture_bounds_both_streams_including_giant_lines_and_keeps_final_diagnostics()
    {
        var start = Script(
            "[Console]::Out.Write(('o' * 400000)); [Console]::Out.WriteLine('FINAL_OUT'); "
            + "[Console]::Error.Write(('e' * 400000)); [Console]::Error.WriteLine('FINAL_ERROR'); exit 7",
            "printf '%0400000d' 0; printf 'FINAL_OUT\\n'; printf '%0400000d' 0 >&2; printf 'FINAL_ERROR\\n' >&2; exit 7");
        var result = await AgentProcessCapture.RunAsync(start, TimeSpan.FromSeconds(10), CancellationToken.None);
        Assert.Equal(AgentToolOutcomeSignal.Completed, result.Signal);
        Assert.Equal(7, result.ExitCode);
        Assert.EndsWith("FINAL_OUT" + Environment.NewLine, result.StandardOutput);
        Assert.EndsWith("FINAL_ERROR" + Environment.NewLine, result.StandardError);
        Assert.Contains("earlier character(s) omitted", result.StandardOutput);
        Assert.Contains("earlier character(s) omitted", result.StandardError);
        Assert.InRange(result.StandardOutput.Length, 1, AgentProcessCapture.RetainedCharactersPerStream + 128);
        Assert.InRange(result.StandardError.Length, 1, AgentProcessCapture.RetainedCharactersPerStream + 128);
    }

    [Fact]
    public async Task Execution_deadline_is_injectable_and_distinct_from_caller_cancellation()
    {
        var start = Script("[System.Threading.Thread]::Sleep([System.Threading.Timeout]::Infinite)", "while :; do read value; done");
        var result = await AgentProcessCapture.RunAsync(start, TimeSpan.FromMilliseconds(100), CancellationToken.None);
        Assert.Equal(AgentToolOutcomeSignal.TimedOut, result.Signal);
        Assert.NotNull(result.ExitCode);
        Assert.Contains("does not prove all descendants", result.CleanupDetail);
    }

    [WindowsOnlyFact]
    public async Task Exited_root_with_live_pipe_inheriting_child_reports_unverified_cleanup_not_containment()
    {
        var pipeName = $"hermaeus-exited-root-{Guid.NewGuid():N}";
        await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var childScript = $$"""
            $pipe = [System.IO.Pipes.NamedPipeClientStream]::new('.', '{{pipeName}}', [System.IO.Pipes.PipeDirection]::Out)
            $pipe.Connect(10000)
            $writer = [System.IO.StreamWriter]::new($pipe)
            $writer.WriteLine($PID)
            $writer.WriteLine($env:HERMAEUS_FIXTURE_PARENT_PID)
            $writer.Flush()
            $writer.Dispose()
            [System.Threading.Thread]::Sleep([System.Threading.Timeout]::Infinite)
            """;
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(childScript));
        var start = Script($"$env:HERMAEUS_FIXTURE_PARENT_PID=$PID; "
            + $"$child = Start-Process -FilePath '{powershell.Replace("'", "''")}' -NoNewWindow -PassThru "
            + $"-ArgumentList @('-NoLogo','-NoProfile','-NonInteractive','-EncodedCommand','{encoded}'); exit 0", "");
        using var cancellation = new CancellationTokenSource();
        var run = AgentProcessCapture.RunAsync(start, TimeSpan.FromSeconds(30), cancellation.Token);
        Process? descendant = null;
        try
        {
            using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await pipe.WaitForConnectionAsync(startup.Token);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            var childPid = int.Parse(Assert.IsType<string>(await reader.ReadLineAsync(startup.Token)),
                System.Globalization.CultureInfo.InvariantCulture);
            descendant = Process.GetProcessById(childPid);
            var rootPid = int.Parse(Assert.IsType<string>(await reader.ReadLineAsync(startup.Token)),
                System.Globalization.CultureInfo.InvariantCulture);
            Process? root = null;
            try { root = Process.GetProcessById(rootPid); }
            catch (ArgumentException) { /* The controlled root has already exited. */ }
            if (root is not null)
                using (root) await root.WaitForExitAsync(startup.Token);
            Assert.False(descendant.HasExited);
            Assert.False(run.IsCompleted); // The live child's inherited pipes still prevent drain.

            cancellation.Cancel();
            var result = await run.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(AgentToolOutcomeSignal.Cancelled, result.Signal);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Root had already exited; descendant ownership is unverified.", result.CleanupDetail);
            Assert.DoesNotContain("Tree termination requested", result.CleanupDetail);
            Assert.False(descendant.HasExited);
        }
        finally
        {
            cancellation.Cancel();
            if (descendant is not null)
            {
                try
                {
                    if (!descendant.HasExited) descendant.Kill(entireProcessTree: true);
                    await descendant.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
                finally { descendant.Dispose(); }
            }
            await run.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [WindowsOnlyFact]
    public async Task A_failed_output_decoder_terminates_the_writer_and_reports_failure()
    {
        var start = Script("[Console]::OpenStandardOutput().WriteByte(255); [System.Threading.Thread]::Sleep([System.Threading.Timeout]::Infinite)", "");
        start.StandardOutputEncoding = new UTF8Encoding(false, true);
        var result = await AgentProcessCapture.RunAsync(start, TimeSpan.FromSeconds(10), CancellationToken.None);
        Assert.Equal(AgentToolOutcomeSignal.Failed, result.Signal);
        Assert.NotNull(result.ExitCode);
        Assert.Contains("Output read failed", result.CleanupDetail);
    }

    [WindowsOnlyFact]
    public async Task Cancelling_an_approved_recipe_stops_its_known_descendant_before_returning()
    {
        using var temp = new TempDir();
        var pipeName = $"hermaeus-command-{Guid.NewGuid():N}";
        await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var scriptPath = temp.PathFor("child.ps1");
        await File.WriteAllTextAsync(scriptPath, $$"""
            $pipe = [System.IO.Pipes.NamedPipeClientStream]::new('.', '{{pipeName}}', [System.IO.Pipes.PipeDirection]::Out)
            $pipe.Connect(10000)
            $writer = [System.IO.StreamWriter]::new($pipe)
            $writer.WriteLine($PID)
            $writer.Flush()
            $writer.Dispose()
            [System.Threading.Thread]::Sleep([System.Threading.Timeout]::Infinite)
            """, Encoding.UTF8);
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        // A controlled workspace build script is the approved command's child.
        // This fixture requires no SDK restore or external package downloads.
        var project = new XDocument(new XElement("Project", new XAttribute("DefaultTargets", "Build"),
            new XElement("Target", new XAttribute("Name", "Restore")),
            new XElement("Target", new XAttribute("Name", "Build"),
                new XElement("Exec", new XAttribute("Command",
                    $"\"{powershell}\" -NoLogo -NoProfile -NonInteractive -File \"{scriptPath}\"")))));
        await File.WriteAllTextAsync(temp.PathFor("controlled.proj"), project.ToString());
        using var cancellation = new CancellationTokenSource();
        var executor = new AgentToolExecutor(new AgentWorkspaceTools());
        var run = executor.ExecuteAsync("run_command", new() { ["command"] = "dotnet build controlled.proj" },
            new AgentWorkspaceOptions(temp.PathFor("")), cancellation.Token);
        Process? descendant = null;
        try
        {
            using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await pipe.WaitForConnectionAsync(startup.Token);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            var pid = int.Parse(Assert.IsType<string>(await reader.ReadLineAsync(startup.Token)),
                System.Globalization.CultureInfo.InvariantCulture);
            descendant = Process.GetProcessById(pid);
            cancellation.Cancel();
            var result = await run.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(NormalizedOutcome.Cancelled, result.NormalizedOutcome.Outcome);
            await descendant.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(descendant.HasExited);
        }
        finally
        {
            cancellation.Cancel();
            if (descendant is not null)
            {
                try
                {
                    if (!descendant.HasExited) descendant.Kill(entireProcessTree: true);
                    await descendant.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
                finally { descendant.Dispose(); }
            }
            await run.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private static ProcessStartInfo Script(string windowsScript, string unixScript)
    {
        var start = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe")
                : "/bin/sh",
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        if (OperatingSystem.IsWindows())
        {
            foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand",
                Convert.ToBase64String(Encoding.Unicode.GetBytes(windowsScript)) })
                start.ArgumentList.Add(argument);
        }
        else
        {
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add(unixScript);
        }
        return start;
    }
}
