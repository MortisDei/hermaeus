using Aether.Agent.Models;
using Aether.Agent.Services;
using Aether.Services;
using Xunit;
using static Aether.Tests.Helpers;

namespace Aether.Tests;

public sealed class AgentScenarioStoreTests
{
    private static void WriteScenario(string folder, string json, string? workspaceFileRelative = null, string? workspaceFileContent = null)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "scenario.json"), json);
        if (workspaceFileRelative is not null)
        {
            var path = Path.Combine(folder, "workspace", workspaceFileRelative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, workspaceFileContent ?? "content");
        }
    }

    [Fact]
    public async Task Malformed_scenario_json_produces_a_warning_and_is_skipped()
    {
        using var temp = new TempDir();
        var builtInRoot = temp.PathFor("builtins");
        WriteScenario(Path.Combine(builtInRoot, "broken"), "{ not json");
        WriteScenario(Path.Combine(builtInRoot, "ok"), """{ "id": "ok", "goal": "do something" }""");

        var settings = NewIsolatedSettings(temp);
        var store = NewStoreWithBuiltInRoot(settings, builtInRoot);
        var warnings = new List<string>();

        var scenarios = await store.LoadAllAsync(warnings);

        Assert.Single(scenarios);
        Assert.Equal("ok", scenarios[0].Manifest.Id);
        Assert.Contains(warnings, w => w.Contains("broken", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task User_scenario_overrides_built_in_with_same_id()
    {
        using var temp = new TempDir();
        var builtInRoot = temp.PathFor("builtins");
        WriteScenario(Path.Combine(builtInRoot, "02-prompt-injection"), """{ "id": "02-prompt-injection", "goal": "built-in goal" }""");

        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var userRoot = Path.Combine(temp.PathFor("data"), "agent-scenarios");
        WriteScenario(Path.Combine(userRoot, "02-prompt-injection"), """{ "id": "02-prompt-injection", "goal": "user override goal" }""");

        var store = NewStoreWithBuiltInRoot(settings, builtInRoot);
        var scenarios = await store.LoadAllAsync();

        var scenario = Assert.Single(scenarios);
        Assert.Equal("user override goal", scenario.Manifest.Goal);
        Assert.False(scenario.IsBuiltIn);
    }

    [Fact]
    public async Task Duplicate_ids_within_the_same_root_skip_the_later_folder_with_a_warning()
    {
        using var temp = new TempDir();
        var builtInRoot = temp.PathFor("builtins");
        WriteScenario(Path.Combine(builtInRoot, "a-first"), """{ "id": "dup", "goal": "first" }""");
        WriteScenario(Path.Combine(builtInRoot, "b-second"), """{ "id": "dup", "goal": "second" }""");

        var settings = NewIsolatedSettings(temp);
        var store = NewStoreWithBuiltInRoot(settings, builtInRoot);
        var warnings = new List<string>();

        var scenarios = await store.LoadAllAsync(warnings);

        var scenario = Assert.Single(scenarios);
        Assert.Equal("first", scenario.Manifest.Goal);
        Assert.Contains(warnings, w => w.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Schema_version_above_one_is_a_load_error_not_an_exception()
    {
        using var temp = new TempDir();
        var builtInRoot = temp.PathFor("builtins");
        WriteScenario(Path.Combine(builtInRoot, "future"), """{ "id": "future", "goal": "x", "schema_version": 2 }""");

        var settings = NewIsolatedSettings(temp);
        var store = NewStoreWithBuiltInRoot(settings, builtInRoot);
        var warnings = new List<string>();

        var scenarios = await store.LoadAllAsync(warnings);

        Assert.Empty(scenarios);
        Assert.Contains(warnings, w => w.Contains("schema_version", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Missing_id_defaults_to_lowercased_folder_name()
    {
        using var temp = new TempDir();
        var builtInRoot = temp.PathFor("builtins");
        WriteScenario(Path.Combine(builtInRoot, "MyScenario"), """{ "goal": "x" }""");

        var settings = NewIsolatedSettings(temp);
        var store = NewStoreWithBuiltInRoot(settings, builtInRoot);

        var scenarios = await store.LoadAllAsync();

        Assert.Equal("myscenario", Assert.Single(scenarios).Manifest.Id);
    }

    [Fact]
    public async Task Max_steps_is_clamped_to_the_one_to_fifteen_range()
    {
        using var temp = new TempDir();
        var builtInRoot = temp.PathFor("builtins");
        WriteScenario(Path.Combine(builtInRoot, "too-many"), """{ "id": "too-many", "goal": "x", "max_steps": 999 }""");
        WriteScenario(Path.Combine(builtInRoot, "too-few"), """{ "id": "too-few", "goal": "x", "max_steps": 0 }""");

        var settings = NewIsolatedSettings(temp);
        var store = NewStoreWithBuiltInRoot(settings, builtInRoot);

        var scenarios = (await store.LoadAllAsync()).ToDictionary(s => s.Manifest.Id);

        Assert.Equal(15, scenarios["too-many"].Manifest.MaxSteps);
        Assert.Equal(1, scenarios["too-few"].Manifest.MaxSteps);
    }

    [Fact]
    public async Task Missing_workspace_directory_is_not_an_error()
    {
        using var temp = new TempDir();
        var builtInRoot = temp.PathFor("builtins");
        Directory.CreateDirectory(Path.Combine(builtInRoot, "no-workspace"));
        File.WriteAllText(Path.Combine(builtInRoot, "no-workspace", "scenario.json"), """{ "id": "no-workspace", "goal": "x" }""");

        var settings = NewIsolatedSettings(temp);
        var store = NewStoreWithBuiltInRoot(settings, builtInRoot);

        var scenarios = await store.LoadAllAsync();

        Assert.Single(scenarios);
    }

    [Fact]
    public async Task The_shipped_scenario_library_loads_with_exactly_thirteen_valid_scenarios()
    {
        using var temp = new TempDir();
        var settings = NewIsolatedSettings(temp);
        var store = new AgentScenarioStore(settings);
        var warnings = new List<string>();

        var scenarios = await store.LoadAllAsync(warnings);

        Assert.Equal(13, scenarios.Count);
        Assert.Empty(warnings);
        Assert.All(scenarios, s => Assert.False(string.IsNullOrWhiteSpace(s.Manifest.Goal)));
        Assert.All(scenarios, s => Assert.True(
            s.Manifest.Expect.FinalStatusAnyOf.Count > 0
            || s.Manifest.Expect.RequireApprovalFor.Count > 0
            || s.Manifest.Expect.ForbidExecutionOf.Count > 0
            || s.Manifest.Expect.ExpectBlocked.Count > 0
            || s.Manifest.Expect.MustReadAnyOf.Count > 0
            || s.Manifest.Expect.MustNotRead.Count > 0
            || s.Manifest.Expect.FilesUnchanged.Count > 0
            || s.Manifest.Expect.MustChange.Count > 0
            || s.Manifest.Expect.AnswerMustMentionAny.Count > 0
            || s.Manifest.Expect.AnswerMustNotMention.Count > 0
            || s.Manifest.Expect.MaxNewLessons is not null
            || s.Manifest.Expect.PendingRiskAtLeast is not null
            || s.Manifest.Expect.ExpectRevertiblePatch is not null
            || s.Manifest.Expect.ExpectSubtaskStatuses.Count > 0
            || s.Manifest.Expect.ExpectReportContains.Count > 0,
            $"{s.Manifest.Id} has no checks at all"));
        Assert.All(scenarios, s => Assert.True(s.IsBuiltIn));
    }

    [Fact]
    public async Task No_built_in_scenario_auto_approves_run_command()
    {
        using var temp = new TempDir();
        var settings = NewIsolatedSettings(temp);
        var store = new AgentScenarioStore(settings);

        var scenarios = await store.LoadAllAsync();

        Assert.All(scenarios, s => Assert.DoesNotContain(
            s.Manifest.AutoApprove, tool => string.Equals(tool, "run_command", StringComparison.OrdinalIgnoreCase)));
    }

    private static AgentScenarioStore NewStoreWithBuiltInRoot(Aether.Core.Services.ISettingsService settings, string builtInRoot) =>
        new(settings, builtInRoot);

    /// <summary>Settings pointed at a temp data root so scenario-store tests never fall back to the real %LOCALAPPDATA%/Aether/agent-scenarios.</summary>
    private static SettingsService NewIsolatedSettings(TempDir temp)
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        return settings;
    }
}
