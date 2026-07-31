using System.Diagnostics;
using Hermaeus.Core.Models;
using Hermaeus.Services.ProcessManagement;
using Xunit;

namespace Hermaeus.Tests;

public sealed class LocalApiProcessManagerJobObjectTests
{
    /// <summary>Windows utility that accepts arbitrary CLI args and exits almost instantly, useful as a stand-in "process that exits immediately".</summary>
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

    /// <summary>r11 4.1: unlike ServerProcessManager/KokoroProcessManager/XttsProcessManager, LocalApiProcessManager never joined the app's job object, so an app crash orphaned the LocalApi process holding its port and per-app tokens alive in memory.</summary>
    [WindowsOnlyFact]
    public async Task StartAsync_assigns_the_child_process_to_the_job_object()
    {
        var jobObject = new FakeProcessJobObject(succeeds: true);
        var manager = new LocalApiProcessManager(jobObject, launchTargetResolver: () => (ImmediateExitExecutable, []));

        var settings = new AppSettings();
        settings.LocalApi.Enabled = true;
        settings.LocalApi.Tokens.Add(new LocalApiTokenEntry { Name = "Default", SecretRef = "secret:local-api" });

        using var cts = new CancellationTokenSource();
        // The synchronous prefix of StartAsync (process Start + job-object
        // assignment) runs before the method's first await, so by the time
        // this call returns a Task, TryAssign has already been attempted.
        var task = manager.StartAsync(settings, cts.Token);

        Assert.True(jobObject.AssignAttempted);

        cts.Cancel();
        try { await task; }
        catch (OperationCanceledException) { }
        finally { manager.Stop(); }
    }
}
