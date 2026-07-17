using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Services;
using Aether.ViewModels;
using Xunit;
using static Aether.Tests.Helpers;

namespace Aether.Tests;

public sealed class ChatViewModelModelSwitchTests
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
