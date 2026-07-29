using Hermaeus.Core.Models;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class CommandRegistryTests
{
    [Fact]
    public void Register_throws_on_duplicate_id()
    {
        var registry = new CommandRegistry();
        registry.Register(new AppCommand("a.one", "One", "Area", "Desc", [], "", () => true, () => Task.CompletedTask));

        Helpers.Throws<InvalidOperationException>(() =>
            registry.Register(new AppCommand("a.one", "Two", "Area", "Desc", [], "", () => true, () => Task.CompletedTask)));
    }

    [Fact]
    public void ByArea_filters_to_the_requested_area_only()
    {
        var registry = new CommandRegistry();
        registry.Register(new AppCommand("a.one", "One", "Chat", "Desc", [], "", () => true, () => Task.CompletedTask));
        registry.Register(new AppCommand("a.two", "Two", "Agent", "Desc", [], "", () => true, () => Task.CompletedTask));

        var chatCommands = registry.ByArea("Chat");
        Assert.Single(chatCommands);
        Assert.Equal("a.one", chatCommands[0].Id);
    }

    /// <summary>doc 04 4.1: a command that cannot run right now must be renderable as
    /// disabled with a reason, not hidden - callers check CanExecute() then
    /// ReasonUnavailable() rather than dropping the entry from a list.</summary>
    [Fact]
    public void A_command_that_cannot_execute_reports_its_disabled_reason()
    {
        var command = new AppCommand(
            "agent.start-task", "Start", "Agent", "Desc", [], "",
            CanExecute: () => false,
            Execute: () => Task.CompletedTask,
            DisabledReason: () => "no workspace root selected");

        Assert.False(command.CanExecute());
        Assert.Equal("no workspace root selected", command.ReasonUnavailable());
    }

    [Fact]
    public void A_command_with_no_disabled_reason_falls_back_to_a_generic_message_when_unavailable()
    {
        var command = new AppCommand("x", "X", "Area", "Desc", [], "", () => false, () => Task.CompletedTask);
        Assert.NotEqual(string.Empty, command.ReasonUnavailable());
    }

    [Fact]
    public void An_available_command_reports_no_reason()
    {
        var command = new AppCommand("x", "X", "Area", "Desc", [], "", () => true, () => Task.CompletedTask);
        Assert.Equal(string.Empty, command.ReasonUnavailable());
    }
}
