using System.Xml.Linq;
using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Hermaeus.Core.Models;
using Xunit;

namespace Hermaeus.Tests;

public sealed class AgentRecipeExposureTests
{
    [Fact]
    public async Task Approved_build_recipe_can_read_a_synthetic_file_outside_the_workspace()
    {
        using var temp = new TempDir();
        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);
        var marker = "r34-canary-" + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(temp.PathFor("outside-canary.txt"), marker);
        var project = new XDocument(new XElement("Project", new XAttribute("DefaultTargets", "Build"),
            new XElement("Target", new XAttribute("Name", "Restore")),
            new XElement("Target", new XAttribute("Name", "Build"),
                new XElement("ReadLinesFromFile", new XAttribute("File", "../outside-canary.txt"),
                    new XElement("Output", new XAttribute("TaskParameter", "Lines"), new XAttribute("ItemName", "Canary"))),
                new XElement("Message", new XAttribute("Importance", "High"), new XAttribute("Text", "@(Canary)")))));
        await File.WriteAllTextAsync(Path.Combine(workspace, "controlled.proj"), project.ToString());

        // The approved recipe runs with the host user's authority. Workspace
        // path checks on direct tools do not sandbox transitive build tasks.
        var executor = new AgentToolExecutor(new AgentWorkspaceTools());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var result = await executor.ExecuteAsync("run_command", new() { ["command"] = "dotnet build controlled.proj" },
            new AgentWorkspaceOptions(workspace), timeout.Token);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(NormalizedOutcome.Succeeded, result.NormalizedOutcome.Outcome);
        Assert.Contains(marker, result.ResultSummary);
        Assert.Equal(marker, await File.ReadAllTextAsync(temp.PathFor("outside-canary.txt")));
    }
}
