using System.Xml.Linq;
using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Hermaeus.Core.Models;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class AgentDiagnosticRedactionTests
{
    [Fact]
    public async Task Real_command_output_and_secret_bearing_exception_do_not_enter_persisted_or_copied_diagnostics()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var redactor = new RedactionService();
        var logs = new RuntimeLogService(settings, redactor);
        var executor = new AgentToolExecutor(new AgentWorkspaceTools(), logs: logs);
        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);
        const string providerToken = "sk-synthetic-provider-secret-123456789";
        const string queryToken = "synthetic-query-secret-123456789";
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var payload = $"SYNTHETIC_CHILD_OUTPUT {providerToken} {home}/private https://example.invalid/?token={queryToken}";
        // A real approved MSBuild recipe emits the synthetic diagnostic. No
        // SDK/package restore or nested compiler build is needed by this project.
        var project = new XDocument(new XElement("Project", new XAttribute("DefaultTargets", "Build"),
            new XElement("Target", new XAttribute("Name", "Build"),
                new XElement("Message", new XAttribute("Importance", "high"), new XAttribute("Text", payload)))));
        await File.WriteAllTextAsync(Path.Combine(workspace, "controlled.proj"), project.ToString());

        var output = await executor.ExecuteAsync("run_command", new() { ["command"] = "dotnet build controlled.proj" },
            new AgentWorkspaceOptions(workspace)).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(NormalizedOutcome.Succeeded, output.NormalizedOutcome.Outcome);
        Assert.Contains("SYNTHETIC_CHILD_OUTPUT", output.ResultSummary);
        Assert.Contains(providerToken, output.ResultSummary);
        // Tool results are task evidence. This test asserts that diagnostics
        // do not silently copy that raw content into their separate sinks.
        var absent = Path.Combine(home, "hermaeus-missing-" + Guid.NewGuid().ToString("N") + "-api_key=" + providerToken);
        // The real executor converts this known filesystem exception into an
        // unavailable tool result. Its exception-derived detail remains task
        // evidence, while diagnostic sinks still receive only safe metadata.
        var unavailable = await executor.ExecuteAsync("read_file",
            new() { ["relative_path"] = "note.txt" }, new AgentWorkspaceOptions(absent));
        Assert.Equal(NormalizedOutcome.Unavailable, unavailable.NormalizedOutcome.Outcome);
        Assert.Contains(providerToken, unavailable.ResultSummary);

        var entries = logs.GetEntries();
        Assert.Contains(entries, entry => entry.Message.Contains("Agent tool completed", StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.Message.Contains("outcome=Unavailable", StringComparison.Ordinal));
        var persisted = await File.ReadAllTextAsync(logs.GetLogFilePath());
        AssertSafe(persisted, providerToken, queryToken, home);
        AssertSafe(string.Join('\n', entries.Select(entry => entry.Message)), providerToken, queryToken, home);

        var view = new LogsViewModel(logs, redactor);
        string? exported = null;
        view.RequestCopyToClipboard = value => { exported = value; return Task.FromResult(true); };
        await view.CopyVisibleLogsCommand.ExecuteAsync(null);
        Assert.NotNull(exported);
        Assert.Contains("Agent tool completed", exported);
        AssertSafe(exported, providerToken, queryToken, home);
        exported = null;
        await view.CopyRedactedDiagnosticsCommand.ExecuteAsync(null);
        Assert.NotNull(exported);
        Assert.Contains("outcome=Unavailable", exported);
        AssertSafe(exported, providerToken, queryToken, home);
        Assert.Equal("Copied redacted diagnostics", view.StatusText);
    }

    private static void AssertSafe(string text, string providerToken, string queryToken, string home)
    {
        Assert.DoesNotContain(providerToken, text);
        Assert.DoesNotContain(queryToken, text);
        Assert.DoesNotContain("SYNTHETIC_CHILD_OUTPUT", text);
        if (!string.IsNullOrWhiteSpace(home)) Assert.DoesNotContain(home, text);
    }
}
