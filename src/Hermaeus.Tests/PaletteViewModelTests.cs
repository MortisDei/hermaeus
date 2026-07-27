using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.Services.Recall;
using Hermaeus.ViewModels;
using Xunit;

namespace Hermaeus.Tests;

public sealed class PaletteViewModelTests
{
    private static AppCommand Cmd(string id, string area, string title, string[]? keywords = null) =>
        new(id, title, area, "desc", keywords ?? [], "", () => true, () => Task.CompletedTask);

    private static PaletteViewModel New(CommandRegistry? registry = null)
    {
        var reg = registry ?? new CommandRegistry();
        var recall = new RecallService([], new FakeEmbeddingService());
        return new PaletteViewModel(reg, recall);
    }

    [Fact]
    public void Opening_with_an_empty_query_lists_every_command_grouped_by_area()
    {
        var registry = new CommandRegistry();
        registry.Register(Cmd("chat.new", "Chat", "New conversation"));
        registry.Register(Cmd("agent.start", "Agent", "Start task"));
        registry.Register(Cmd("chat.export", "Chat", "Export"));
        var vm = New(registry);

        vm.OpenCommand.Execute(null);

        Assert.True(vm.IsOpen);
        Assert.Equal(2, vm.CommandGroups.Count);
        var chatGroup = vm.CommandGroups.Single(g => g.Area == "Chat");
        Assert.Equal(2, chatGroup.Commands.Count);
    }

    [Fact]
    public async Task Typing_a_query_matches_commands_by_title_area_or_keyword_instantly()
    {
        var registry = new CommandRegistry();
        registry.Register(Cmd("chat.new", "Chat", "New conversation", ["fresh"]));
        registry.Register(Cmd("agent.start", "Agent", "Start task"));
        var vm = New(registry);

        vm.QueryText = "fresh";
        await Task.Delay(20); // matched commands are synchronous within OnQueryTextChanged, before the debounce

        Assert.Single(vm.MatchedCommands);
        Assert.Equal("chat.new", vm.MatchedCommands[0].Id);
    }

    [Fact]
    public void Closing_clears_the_open_flag()
    {
        var vm = New();
        vm.OpenCommand.Execute(null);
        vm.CloseCommand.Execute(null);
        Assert.False(vm.IsOpen);
    }

    [Fact]
    public void SetActiveProject_defaults_the_scope_chip_to_the_current_project()
    {
        var vm = New();
        Assert.False(vm.HasActiveProject);

        vm.SetActiveProject("p1", "My Project");

        Assert.True(vm.HasActiveProject);
        Assert.True(vm.ScopeToActiveProject);
        Assert.Equal("My Project", vm.ActiveProjectName);

        vm.SetActiveProject("", "");
        Assert.False(vm.HasActiveProject);
        Assert.False(vm.ScopeToActiveProject);
    }

    [Fact]
    public async Task Executing_a_disabled_command_does_nothing()
    {
        var registry = new CommandRegistry();
        var executed = false;
        registry.Register(new AppCommand("x", "X", "Area", "desc", [], "", () => false, () => { executed = true; return Task.CompletedTask; }));
        var vm = New(registry);
        var command = registry.All[0];

        await vm.ExecuteCommandCommand.ExecuteAsync(command);

        Assert.False(executed);
    }

    [Fact]
    public async Task Executing_an_available_command_runs_it_and_closes_the_palette()
    {
        var registry = new CommandRegistry();
        var executed = false;
        registry.Register(new AppCommand("x", "X", "Area", "desc", [], "", () => true, () => { executed = true; return Task.CompletedTask; }));
        var vm = New(registry);
        vm.OpenCommand.Execute(null);
        var command = registry.All[0];

        await vm.ExecuteCommandCommand.ExecuteAsync(command);

        Assert.True(executed);
        Assert.False(vm.IsOpen);
    }
}
