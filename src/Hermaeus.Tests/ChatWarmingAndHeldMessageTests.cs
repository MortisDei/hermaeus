using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>
/// r27 01-startup-that-never-waits.md 1.3 and 1.4. At launch the model dropdown
/// is empty while a server loads a model, and a send in that window used to
/// return silently: the user typed a question, pressed send, and the app did
/// nothing and said nothing.
/// </summary>
public sealed class ChatWarmingAndHeldMessageTests
{
    private static (ChatViewModel Vm, FakeToasts Toasts) NewViewModel(TempDir temp, ILlmService? llm = null)
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new ThrowingSaveConversationStore();
        var memoryStore = new MemoryStore(settings);
        memoryStore.InitializeAsync().GetAwaiter().GetResult();
        var toasts = new FakeToasts();
        var vm = new ChatViewModel(
            llm ?? new FakeLlm(),
            store,
            memoryStore,
            settings,
            new FakeTts(),
            new ModelProfileService(settings),
            toasts,
            new FakeConversationMemoryService(),
            new RuntimeLogService(settings),
            new ConversationExportService());
        return (vm, toasts);
    }

    private static void Warm(ChatViewModel vm, TimeSpan elapsed, string name = "Chat")
    {
        vm.WarmingServerProvider = () => new ChatWarmingServer(name, elapsed);
        vm.RefreshWarmingState();
    }

    // ── 1.3 The warming state ───────────────────────────────────────────────

    [Fact]
    public void Warming_is_true_only_while_a_server_is_starting_and_no_models_are_listed()
    {
        using var temp = new TempDir();
        var (vm, _) = NewViewModel(temp);

        vm.RefreshWarmingState();
        Assert.False(vm.IsServerWarming);

        Warm(vm, TimeSpan.FromSeconds(12));
        Assert.True(vm.IsServerWarming);
        Assert.Contains("Chat is starting", vm.WarmingText);
        Assert.Contains("12s", vm.WarmingText);
        Assert.False(vm.WarmingIsSlow);
    }

    [Fact]
    public void Warming_clears_the_moment_a_model_lists()
    {
        using var temp = new TempDir();
        var (vm, _) = NewViewModel(temp);
        Warm(vm, TimeSpan.FromSeconds(12));
        Assert.True(vm.IsServerWarming);

        vm.AvailableModels.Add(new LlmModel { Id = "a", Name = "a", Provider = "Test" });
        vm.RefreshWarmingState();

        Assert.False(vm.IsServerWarming);
        Assert.Equal(string.Empty, vm.WarmingText);
    }

    [Fact]
    public void Past_ninety_seconds_the_line_says_it_is_longer_than_usual_and_points_at_services()
    {
        using var temp = new TempDir();
        var (vm, _) = NewViewModel(temp);

        Warm(vm, TimeSpan.FromSeconds(95));

        Assert.True(vm.WarmingIsSlow);
        Assert.Contains("longer than usual", vm.WarmingText);
        Assert.Contains("Services", vm.WarmingText);
    }

    [Fact]
    public void Elapsed_time_reads_as_minutes_and_seconds_past_a_minute()
    {
        Assert.Equal("41s", ChatWarmingState.FormatElapsed(TimeSpan.FromSeconds(41)));
        Assert.Equal("2m 5s", ChatWarmingState.FormatElapsed(TimeSpan.FromSeconds(125)));
        Assert.Equal("0s", ChatWarmingState.FormatElapsed(TimeSpan.FromSeconds(-3)));
    }

    // ── 1.4 The held message ────────────────────────────────────────────────

    [Fact]
    public async Task A_send_while_warming_holds_the_message_and_clears_the_composer()
    {
        using var temp = new TempDir();
        var (vm, _) = NewViewModel(temp);
        Warm(vm, TimeSpan.FromSeconds(10));

        vm.InputText = "what changed in r27?";
        await vm.SendCommand.ExecuteAsync(null);

        Assert.True(vm.HasHeldMessage);
        Assert.Equal(string.Empty, vm.InputText);
        var held = Assert.Single(vm.Messages);
        Assert.True(held.IsHeld);
        Assert.Equal("what changed in r27?", held.Content);
        Assert.False(string.IsNullOrWhiteSpace(held.HeldReason));
    }

    [Fact]
    public async Task A_held_message_sends_exactly_once_when_a_model_lists()
    {
        using var temp = new TempDir();
        var (vm, _) = NewViewModel(temp);
        Warm(vm, TimeSpan.FromSeconds(10));
        vm.InputText = "hello";
        await vm.SendCommand.ExecuteAsync(null);
        Assert.True(vm.HasHeldMessage);

        // A model listing is what releases the hold.
        vm.WarmingServerProvider = () => null;
        vm.AvailableModels.Add(new LlmModel { Id = "a", Name = "a", Provider = "Test" });
        vm.SelectedModel = vm.AvailableModels[0];
        vm.RefreshWarmingState();

        await WaitForAsync(() => !vm.HasHeldMessage && vm.Messages.Count >= 2, "the held message being released");

        Assert.False(vm.HasHeldMessage);
        Assert.Equal(1, vm.Messages.Count(m => m.Role == "user" && m.Content == "hello"));
        Assert.DoesNotContain(vm.Messages, m => m.IsHeld);
    }

    [Fact]
    public async Task A_second_send_while_one_is_held_is_refused_and_does_not_queue()
    {
        using var temp = new TempDir();
        var (vm, toasts) = NewViewModel(temp);
        Warm(vm, TimeSpan.FromSeconds(10));

        vm.InputText = "first";
        await vm.SendCommand.ExecuteAsync(null);
        Assert.True(vm.HasHeldMessage);

        // CanSend refuses a second send outright; calling the method directly
        // proves the refusal is in the send path, not only in the button state.
        Assert.False(vm.CanHoldMessage);
        vm.InputText = "second";
        await vm.SendCommand.ExecuteAsync(null);

        Assert.Equal(1, vm.Messages.Count(m => m.IsHeld));
        Assert.DoesNotContain(vm.Messages, m => m.Content == "second");
        Assert.Contains("Already waiting", toasts.LastShown!.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cancelling_a_hold_restores_the_text_to_the_composer_and_sends_nothing()
    {
        using var temp = new TempDir();
        var (vm, _) = NewViewModel(temp);
        Warm(vm, TimeSpan.FromSeconds(10));
        vm.InputText = "give it back";
        await vm.SendCommand.ExecuteAsync(null);

        vm.CancelHeldMessageCommand.Execute(null);

        Assert.False(vm.HasHeldMessage);
        Assert.Equal("give it back", vm.InputText);
        Assert.Empty(vm.Messages);
    }

    [Fact]
    public async Task A_send_with_no_model_and_nothing_warming_still_returns_without_holding()
    {
        using var temp = new TempDir();
        var (vm, _) = NewViewModel(temp);
        vm.WarmingServerProvider = () => null;
        vm.RefreshWarmingState();

        vm.InputText = "nobody is listening";
        await vm.SendCommand.ExecuteAsync(null);

        // No models configured at all, no server, a misconfigured runtime: those
        // are not warming, and a hold against them would wait forever.
        Assert.False(vm.HasHeldMessage);
        Assert.Empty(vm.Messages);
        Assert.Equal("nobody is listening", vm.InputText);
    }

    [Fact]
    public async Task A_hold_whose_server_stops_starting_sends_nothing_and_leaves_the_text_recoverable()
    {
        using var temp = new TempDir();
        var (vm, toasts) = NewViewModel(temp);
        Warm(vm, TimeSpan.FromSeconds(10));
        vm.InputText = "still mine";
        await vm.SendCommand.ExecuteAsync(null);
        Assert.True(vm.HasHeldMessage);

        // The server went to Error: it is no longer starting, and no model listed.
        vm.WarmingServerProvider = () => null;
        vm.RefreshWarmingState();

        Assert.False(vm.HasHeldMessage);
        Assert.Equal("still mine", vm.InputText);
        Assert.Empty(vm.Messages);
        Assert.Contains("not sent", toasts.LastShown!.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_hold_is_never_persisted_and_never_survives_a_restart()
    {
        // A held message lives in the view model only: it is not a conversation
        // state and is not written to the store until it actually sends. There is
        // no field on Conversation or Message that could carry one.
        Assert.Null(typeof(Conversation).GetProperty("HeldMessage"));
        Assert.Null(typeof(Message).GetProperty("IsHeld"));
    }
}
