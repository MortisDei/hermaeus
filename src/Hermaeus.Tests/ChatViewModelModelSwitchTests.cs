using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

[Collection(AvaloniaTestHost.CollectionName)]
public sealed class ChatViewModelModelSwitchTests(AvaloniaTestHost avalonia)
{
    private static (ChatViewModel vm, ThrowingSaveConversationStore store, ISettingsService settings) NewViewModel(
        TempDir temp, ILlmService? llm = null)
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new ThrowingSaveConversationStore();
        var memoryStore = new MemoryStore(settings);
        memoryStore.InitializeAsync().GetAwaiter().GetResult();
        var vm = new ChatViewModel(
            llm ?? new FakeLlm(),
            store,
            memoryStore,
            settings,
            new FakeTts(),
            new ModelProfileService(settings),
            new FakeToasts(),
            new FakeConversationMemoryService(),
            new RuntimeLogService(settings),
            new ConversationExportService());
        return (vm, store, settings);
    }

    private static LlmModel Model(string id, double? temp = null, int? maxTokens = null, double? topP = null) => new()
    {
        Id = id,
        Name = id,
        Provider = "Test",
        DefaultTemperature = temp,
        DefaultMaxTokens = maxTokens,
        DefaultTopP = topP
    };

    // ── 1.3: selecting a model never dirties Settings.Llm.MaxTokens ──

    [Fact]
    public async Task Selecting_a_model_with_a_profile_max_tokens_never_mutates_the_global_setting()
    {
        using var temp = new TempDir();
        var llm = new ScriptedModelsLlm(() => [Model("a", maxTokens: 8192)]);
        var (vm, _, settings) = NewViewModel(temp, llm);
        var originalGlobalMaxTokens = settings.Settings.Llm.MaxTokens;

        await vm.LoadModelsAsync();

        Assert.Equal(8192, vm.MaxTokens);
        Assert.Equal(originalGlobalMaxTokens, settings.Settings.Llm.MaxTokens);
    }

    // ── 3.3: background model refresh must not reset user-tuned sampling params ──

    [Fact]
    public async Task Refreshing_models_with_an_equal_id_instance_preserves_user_tuned_temperature()
    {
        using var temp = new TempDir();
        var llm = new ScriptedModelsLlm(() => [Model("a", temp: 0.9)]);
        var (vm, _, _) = NewViewModel(temp, llm);
        await vm.LoadModelsAsync();
        Assert.Equal(0.9, vm.Temperature);

        vm.Temperature = 0.2;

        await vm.LoadModelsAsync(force: true);

        Assert.Equal(0.2, vm.Temperature);
        Assert.Equal("a", vm.SelectedModel?.Id);
    }

    [Fact]
    public Task Bound_model_picker_refresh_preserves_sampling_and_provider_usage() => avalonia.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var (vm, _, _) = NewViewModel(temp, new UsageLlm());
        await vm.LoadModelsAsync();
        var (window, picker) = CreateBoundPicker(vm);
        try
        {
            vm.Temperature = 0.2;
            vm.TopP = 0.4;
            vm.MaxTokens = 71;
            vm.TopK = 23;
            vm.MinP = 0.05;
            vm.RepeatPenalty = 1.2;
            vm.FrequencyPenalty = 0.3;
            vm.PresencePenalty = 0.6;
            vm.InputText = "Synthetic picker refresh check";
            await vm.SendCommand.ExecuteAsync(null);
            Assert.Equal("Reported by provider", vm.ContextUsageKind);
            var reportedLabel = vm.ContextUsageLabel;
            var previousModel = vm.SelectedModel;

            await vm.LoadModelsAsync(force: true);

            Assert.NotSame(previousModel, vm.SelectedModel);
            Assert.Same(vm.SelectedModel, picker.SelectedItem);
            Assert.Equal(0.2, vm.Temperature);
            Assert.Equal(0.4, vm.TopP);
            Assert.Equal(71, vm.MaxTokens);
            Assert.Equal(23, vm.TopK);
            Assert.Equal(0.05, vm.MinP);
            Assert.Equal(1.2, vm.RepeatPenalty);
            Assert.Equal(0.3, vm.FrequencyPenalty);
            Assert.Equal(0.6, vm.PresencePenalty);
            await Task.Delay(200); // A transient picker selection must not schedule the 150 ms estimate refresh.
            Assert.Equal("Reported by provider", vm.ContextUsageKind);
            Assert.Equal(reportedLabel, vm.ContextUsageLabel);

            vm.InputText = "A changed draft does need a fresh estimate";
            await WaitForAsync(() => vm.ContextUsageKind == "Estimated", "draft token estimate");
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Bound_model_picker_refresh_applies_defaults_when_the_selected_model_disappears(bool noModels) => avalonia.RunAsync(async () =>
    {
        using var temp = new TempDir();
        var refreshed = false;
        var llm = new ScriptedModelsLlm(() => !refreshed ? [Model("a", temp: 0.9)]
            : noModels ? [] : [Model("b", temp: 0.5)]);
        var (vm, _, settings) = NewViewModel(temp, llm);
        await vm.LoadModelsAsync();
        var (window, picker) = CreateBoundPicker(vm);
        try
        {
            vm.Temperature = 0.2;
            refreshed = true;
            await vm.LoadModelsAsync(force: true);

            Assert.Equal(noModels ? null : "b", vm.SelectedModel?.Id);
            Assert.Same(vm.SelectedModel, picker.SelectedItem);
            Assert.Equal(noModels ? settings.Settings.Llm.Temperature : 0.5, vm.Temperature);
            Assert.Equal(!noModels, vm.HasSelectedModel);
        }
        finally { window.Close(); }
    });

    private static (Window Window, ComboBox Picker) CreateBoundPicker(ChatViewModel vm)
    {
        var picker = new ComboBox { DataContext = vm };
        picker.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(vm.AvailableModels)));
        picker.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(vm.SelectedModel)) { Mode = BindingMode.TwoWay });
        var window = new Window { Content = picker, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        window.Hide();
        return (window, picker);
    }

    [Fact]
    public async Task Switching_to_a_model_without_a_profile_value_resets_to_the_settings_default_instead_of_leaking()
    {
        using var temp = new TempDir();
        var llm = new ScriptedModelsLlm(() => [Model("a", temp: 0.9), Model("b")]);
        var (vm, _, settings) = NewViewModel(temp, llm);
        settings.Settings.Llm.Temperature = 0.7;
        await vm.LoadModelsAsync();
        Assert.Equal(0.9, vm.Temperature);

        vm.SelectedModel = vm.AvailableModels.Single(m => m.Id == "b");

        Assert.Equal(0.7, vm.Temperature);
    }

    // ── 2.2: SendAsync must not leave a stuck streaming bubble on failure ──

    [Fact]
    public async Task SendAsync_marks_the_message_as_error_and_toasts_when_persistence_throws()
    {
        using var temp = new TempDir();
        var (vm, store, _) = NewViewModel(temp);
        await vm.LoadModelsAsync();
        store.ThrowOnSave = true;

        vm.InputText = "hello";
        await vm.SendCommand.ExecuteAsync(null);

        Assert.DoesNotContain(vm.Messages, m => m.IsStreaming);
        Assert.False(vm.IsGenerating);
    }

    // ── 2.5: concurrent LoadModelsAsync calls must not duplicate models ──

    [Fact]
    public async Task Concurrent_LoadModelsAsync_calls_share_the_in_flight_load_and_never_duplicate_models()
    {
        using var temp = new TempDir();
        var gate = new TaskCompletionSource();
        var llm = new ScriptedModelsLlm(() => [Model("a")]) { DelayGate = gate };
        var (vm, _, _) = NewViewModel(temp, llm);

        var first = vm.LoadModelsAsync();
        var second = vm.LoadModelsAsync();
        gate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Single(vm.AvailableModels);
        Assert.Equal(1, llm.GetModelsCallCount);
    }

    [Fact]
    public async Task Refreshing_models_preserves_a_selection_changed_while_discovery_was_pending()
    {
        using var temp = new TempDir();
        var llm = new ScriptedModelsLlm(() => [Model("a", temp: 0.9), Model("b", temp: 0.5)]);
        var (vm, _, _) = NewViewModel(temp, llm);
        await vm.LoadModelsAsync();
        llm.DelayGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var refresh = vm.LoadModelsAsync(force: true);
        vm.SelectedModel = vm.AvailableModels.Single(m => m.Id == "b");
        vm.Temperature = 0.3;
        llm.DelayGate.SetResult();
        await refresh;

        Assert.Equal("b", vm.SelectedModel?.Id);
        Assert.Equal(0.3, vm.Temperature);
    }

    // ── 3.9: ClearChat resets the system prompt; RemoveContextAttachment recomputes status ──

    [Fact]
    public void ClearChat_resets_system_prompt_to_the_configured_default()
    {
        using var temp = new TempDir();
        var (vm, _, settings) = NewViewModel(temp);
        settings.Settings.Llm.DefaultSystemPrompt = "be nice";
        vm.SystemPrompt = "something else entirely";

        vm.ClearChatCommand.Execute(null);

        Assert.Equal("be nice", vm.SystemPrompt);
    }

    [Fact]
    public async Task RemoveContextAttachment_recomputes_the_status_label_instead_of_leaving_it_stale()
    {
        using var temp = new TempDir();
        var (vm, _, _) = NewViewModel(temp);
        var skippedFile = temp.PathFor("skipped.exe");
        await File.WriteAllTextAsync(skippedFile, "binary-ish");
        var readyFile = temp.PathFor("ready.txt");
        await File.WriteAllTextAsync(readyFile, "hello world");

        await vm.AddContextFilesAsync([readyFile, skippedFile]);
        Assert.Contains("skipped", vm.AttachmentStatus);

        var skipped = vm.ContextAttachments.First(a => !a.IsReady);
        vm.RemoveContextAttachmentCommand.Execute(skipped);

        Assert.DoesNotContain("skipped", vm.AttachmentStatus);
        Assert.Contains("1 file(s) ready", vm.AttachmentStatus);
    }
}
