using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Services;
using Aether.ViewModels;
using Xunit;
using static Aether.Tests.Helpers;

namespace Aether.Tests;

// r13 04-chat-sampling.md 4.1: the sampling flyout completes the orphan Temp spinner into a
// full editor for all eight parameters, all still VM-local (never written to settings).
public sealed class ChatSamplingFlyoutTests
{
    private static ChatViewModel NewChatViewModel(SettingsService settings, ILlmService llm)
    {
        var memoryStore = new MemoryStore(settings);
        memoryStore.InitializeAsync().GetAwaiter().GetResult();
        return new ChatViewModel(
            llm, new ThrowingSaveConversationStore(), memoryStore, settings,
            new FakeTts(), new ModelProfileService(settings), new FakeToasts(),
            new FakeConversationMemoryService(), new RuntimeLogService(settings), new ConversationExportService());
    }

    // ── r19 6.1: memory pill flyout "Open in Memories" ──────────────────────────

    [Fact]
    public void OpenMemoryInMemories_navigates_with_the_memory_title()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var vm = NewChatViewModel(settings, new CapturingLlm());

        string? navigatedTitle = null;
        vm.RequestNavigateToMemory = title => navigatedTitle = title;

        var source = new SourceReference(ProvenanceKind.Memory, "Owner prefers dark mode", Snippet: "The owner said they prefer dark mode in every app.");
        vm.OpenMemoryInMemoriesCommand.Execute(source);

        Assert.Equal("Owner prefers dark mode", navigatedTitle);
    }

    [Fact]
    public void OpenMemoryInMemories_does_nothing_for_a_null_source()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var vm = NewChatViewModel(settings, new CapturingLlm());

        var navigated = false;
        vm.RequestNavigateToMemory = _ => navigated = true;

        vm.OpenMemoryInMemoriesCommand.Execute(null);

        Assert.False(navigated);
    }

    [Fact]
    public async Task Send_after_editing_TopK_and_MinP_carries_the_edited_values_through()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var capturing = new CapturingLlm();
        var vm = NewChatViewModel(settings, capturing);
        await vm.LoadModelsAsync(force: true);

        vm.TopK = 77;
        vm.MinP = 0.03;
        vm.InputText = "hello";
        await vm.SendCommand.ExecuteAsync(null);

        Assert.Equal(77, capturing.LastOptions?.TopK);
        Assert.Equal(0.03, capturing.LastOptions?.MinP);
    }

    [Fact]
    public async Task Send_after_editing_all_eight_parameters_carries_every_one_through()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var capturing = new CapturingLlm();
        var vm = NewChatViewModel(settings, capturing);
        await vm.LoadModelsAsync(force: true);

        vm.Temperature = 1.1;
        vm.TopP = 0.42;
        vm.TopK = 12;
        vm.MinP = 0.07;
        vm.RepeatPenalty = 1.3;
        vm.FrequencyPenalty = 0.4;
        vm.PresencePenalty = 0.6;
        vm.MaxTokens = 999;
        vm.InputText = "hello";
        await vm.SendCommand.ExecuteAsync(null);

        var options = capturing.LastOptions!;
        Assert.Equal(1.1, options.Temperature);
        Assert.Equal(0.42, options.TopP);
        Assert.Equal(12, options.TopK);
        Assert.Equal(0.07, options.MinP);
        Assert.Equal(1.3, options.RepeatPenalty);
        Assert.Equal(0.4, options.FrequencyPenalty);
        Assert.Equal(0.6, options.PresencePenalty);
        Assert.Equal(999, options.MaxTokens);
    }

    [Fact]
    public async Task Reset_restores_global_defaults_when_no_model_profile_override_is_set()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Llm.Temperature = 0.65;
        settings.Settings.Llm.TopK = 40;
        var capturing = new CapturingLlm();
        var vm = NewChatViewModel(settings, capturing);
        await vm.LoadModelsAsync(force: true);

        vm.Temperature = 1.9;
        vm.TopK = 3;

        vm.ResetSamplingToModelDefaultsCommand.Execute(null);

        Assert.Equal(0.65, vm.Temperature);
        Assert.Equal(40, vm.TopK);
    }

    [Fact]
    public async Task Reset_prefers_the_selected_models_profile_override_over_the_global_default()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Llm.Temperature = 0.65;
        var capturing = new CapturingLlm();
        var vm = NewChatViewModel(settings, capturing);
        await vm.LoadModelsAsync(force: true);
        vm.SelectedModel!.DefaultTemperature = 0.2;

        vm.Temperature = 1.9;
        vm.ResetSamplingToModelDefaultsCommand.Execute(null);

        Assert.Equal(0.2, vm.Temperature);
    }

    [Fact]
    public async Task Opening_editing_and_resetting_the_flyout_never_writes_to_ISettingsService_Settings()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        settings.Settings.Llm.Temperature = 0.7;
        settings.Settings.Llm.TopK = 40;
        settings.Settings.Llm.MinP = 0.05;
        var capturing = new CapturingLlm();
        var vm = NewChatViewModel(settings, capturing);
        await vm.LoadModelsAsync(force: true);

        vm.Temperature = 1.5;
        vm.TopK = 99;
        vm.MinP = 0.9;
        vm.ResetSamplingToModelDefaultsCommand.Execute(null);

        Assert.Equal(0.7, settings.Settings.Llm.Temperature);
        Assert.Equal(40, settings.Settings.Llm.TopK);
        Assert.Equal(0.05, settings.Settings.Llm.MinP);
    }
}
