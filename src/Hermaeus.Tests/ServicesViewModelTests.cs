using Hermaeus.Core.Models;
using Hermaeus.Core.Services;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class ServicesViewModelTests
{
    private static ServerProcessViewModel NewServerVm(TempDir temp, int contextSize)
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var config = new ServerConfig { Name = "Chat", ContextSize = contextSize };
        return new ServerProcessViewModel(config, settings, new RedactionService(), new TrustService(), new FakeToasts(), new RuntimeLogService(settings));
    }

    /// <summary>A tiny but structurally valid llama-shaped GGUF header (block_count 32,
    /// kv heads 8, key/value 128 dims each): 131072 bytes of KV per token, so 8192 context
    /// costs ~1 GiB of KV and 65536 context costs ~8 GiB - enough to cross an 8 GB VRAM budget
    /// without needing an actual multi-gigabyte weights file on disk.</summary>
    private static string WriteLlamaGgufFixture(TempDir temp, string name = "model.gguf")
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write(System.Text.Encoding.ASCII.GetBytes("GGUF"));
            w.Write((uint)3);
            w.Write((ulong)0);
            w.Write((ulong)9);

            void WriteKey(string key)
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(key);
                w.Write((ulong)bytes.Length);
                w.Write(bytes);
            }
            void WriteString(string key, string value)
            {
                WriteKey(key);
                w.Write((uint)8);
                var bytes = System.Text.Encoding.UTF8.GetBytes(value);
                w.Write((ulong)bytes.Length);
                w.Write(bytes);
            }
            void WriteU32(string key, uint value)
            {
                WriteKey(key);
                w.Write((uint)4);
                w.Write(value);
            }

            WriteString("general.architecture", "llama");
            WriteU32("general.file_type", 15);
            WriteU32("llama.block_count", 32);
            WriteU32("llama.context_length", 131072);
            WriteU32("llama.embedding_length", 4096);
            WriteU32("llama.attention.head_count", 32);
            WriteU32("llama.attention.head_count_kv", 8);
            WriteU32("llama.attention.key_length", 128);
            WriteU32("llama.attention.value_length", 128);
        }

        var path = temp.PathFor(name);
        File.WriteAllBytes(path, ms.ToArray());
        return path;
    }

    // ── r17 01-gguf-context-and-tuning.md 1.4/1.6: hardware-aware context-fit warning ──

    [Fact]
    public async Task Context_fit_warning_is_silent_at_small_context_and_present_at_huge_context()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var modelPath = WriteLlamaGgufFixture(temp);
        var hw = new HardwareProfile(TotalRamBytes: 64L * 1024 * 1024 * 1024, MaxGpuVramBytes: 8L * 1024 * 1024 * 1024, GpuName: "Test GPU");
        var config = new ServerConfig { Name = "Chat", ModelPath = modelPath, ContextSize = 8192, GpuLayers = -1 };
        var vm = new ServerProcessViewModel(config, settings, new RedactionService(), new TrustService(), new FakeToasts(), new RuntimeLogService(settings), hardwareProfile: hw);
        await Task.Delay(200);

        Assert.False(vm.HasContextFitWarning, vm.ContextFitNote);

        vm.ContextSize = 65536;

        Assert.True(vm.HasContextFitWarning);
        Assert.Contains("GB", vm.ContextFitNote, StringComparison.Ordinal);
        Assert.Contains("KV cache", vm.ContextFitNote, StringComparison.Ordinal);
        Assert.Contains("weights", vm.ContextFitNote, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gpu_fit_breakdown_recomputes_for_unsaved_what_if_controls()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var modelPath = WriteLlamaGgufFixture(temp);
        var hw = new HardwareProfile(64L * 1024 * 1024 * 1024, 8L * 1024 * 1024 * 1024, "Test GPU");
        var config = new ServerConfig { Name = "Chat", ModelPath = modelPath, ContextSize = 8192, GpuLayers = -1 };
        var vm = new ServerProcessViewModel(
            config,
            settings, new RedactionService(), new TrustService(), new FakeToasts(), new RuntimeLogService(settings),
            hardwareProfile: hw);
        await Task.Delay(200);

        Assert.True(vm.HasGpuFitBreakdown);
        Assert.Contains("KV key cache", vm.GpuFitBreakdown, StringComparison.Ordinal);
        var before = vm.GpuFitBreakdown;

        vm.ContextSize = 65536;

        Assert.NotEqual(before, vm.GpuFitBreakdown);
        Assert.Contains("65,536", vm.GpuFitBreakdown, StringComparison.Ordinal);
        Assert.Equal(8192, config.ContextSize);
    }

    [Fact]
    public async Task Context_fit_falls_back_to_the_flat_threshold_when_metadata_is_unavailable()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var hw = new HardwareProfile(TotalRamBytes: 64L * 1024 * 1024 * 1024, MaxGpuVramBytes: 8L * 1024 * 1024 * 1024, GpuName: "Test GPU");
        // No ModelPath at all, so no GGUF header can be read - the flat 16384 rule must still fire.
        var config = new ServerConfig { Name = "Chat", ContextSize = 32768, GpuLayers = -1 };
        var vm = new ServerProcessViewModel(config, settings, new RedactionService(), new TrustService(), new FakeToasts(), new RuntimeLogService(settings), hardwareProfile: hw);
        await Task.Delay(200);

        Assert.True(vm.HasContextFitWarning);
        Assert.Contains("32,768", vm.ContextFitNote, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Training_context_advisory_is_appended_independent_of_the_vram_verdict()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var modelPath = WriteLlamaGgufFixture(temp);
        // Plenty of VRAM so the KV/weights verdict itself never warns; only the
        // training-context advisory (info.TrainingContextLength = 131072) should fire.
        var hw = new HardwareProfile(TotalRamBytes: 256L * 1024 * 1024 * 1024, MaxGpuVramBytes: 256L * 1024 * 1024 * 1024, GpuName: "Huge GPU");
        var config = new ServerConfig { Name = "Chat", ModelPath = modelPath, ContextSize = 200000, GpuLayers = -1 };
        var vm = new ServerProcessViewModel(config, settings, new RedactionService(), new TrustService(), new FakeToasts(), new RuntimeLogService(settings), hardwareProfile: hw);
        await Task.Delay(200);

        Assert.True(vm.HasContextFitWarning);
        Assert.Contains("trained at 131,072 context", vm.ContextFitNote, StringComparison.Ordinal);
    }

    [Fact]
    public void Oversized_context_note_visible_above_threshold()
    {
        using var temp = new TempDir();
        var vm = NewServerVm(temp, 32768);

        Assert.True(vm.HasContextFitWarning);
        Assert.Contains("32,768", vm.ContextFitNote);
    }

    [Fact]
    public void Oversized_context_note_absent_below_threshold()
    {
        using var temp = new TempDir();
        var vm = NewServerVm(temp, 8192);

        Assert.False(vm.HasContextFitWarning);
    }

    [Fact]
    public void Oversized_context_note_updates_when_the_field_changes()
    {
        using var temp = new TempDir();
        var vm = NewServerVm(temp, 8192);
        Assert.False(vm.HasContextFitWarning);

        vm.ContextSize = 32768;

        Assert.True(vm.HasContextFitWarning);
        Assert.Contains("32,768", vm.ContextFitNote);
    }

    [Fact]
    public void Missing_configured_draft_is_not_presented_as_a_detected_candidate()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var modelPath = WriteLlamaGgufFixture(temp);
        var missingDraft = temp.PathFor("missing/mtp-draft.gguf");
        var config = new ServerConfig
        {
            Name = "Chat",
            ModelPath = modelPath,
            Speculative = new SpeculativeDecodingConfig { DraftModelPath = missingDraft }
        };
        var server = new ServerProcessViewModel(config, settings, new RedactionService(), new TrustService(),
            new FakeToasts(), new RuntimeLogService(settings));

        Assert.True(server.HasMissingDraftModel);
        Assert.DoesNotContain(missingDraft, server.DetectedDraftModelPaths);
        Assert.Contains("missing", server.DraftModelHint, StringComparison.OrdinalIgnoreCase);
        server.ClearDraftModelCommand.Execute(null);
        Assert.Empty(server.DraftModelPath);
    }

    private static ServicesViewModel NewServicesVm(TempDir temp, out ISettingsService settings)
    {
        settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        return NewServicesViewModel(settings);
    }

    // ── r12 01-settings-lifecycle.md 1.4: Rebuild must diff, not churn ──

    [Fact]
    public async Task Saving_an_unrelated_setting_does_not_fire_ServerAvailabilityChanged()
    {
        using var temp = new TempDir();
        var vm = NewServicesVm(temp, out var settings);
        var fired = 0;
        vm.ServerAvailabilityChanged += (_, _) => fired++;

        // Simulate an unrelated save (e.g. a UI font-size tweak): nothing
        // about the managed servers changed, so this should not touch the
        // Services panel at all.
        settings.Settings.DataManagement.LocalAiAssetsRoot = temp.PathFor("assets");
        await settings.SaveAsync();
        await Task.Delay(100);

        Assert.Equal(0, fired);
    }

    [Fact]
    public async Task Changing_a_managed_server_port_fires_ServerAvailabilityChanged()
    {
        using var temp = new TempDir();
        var vm = NewServicesVm(temp, out var settings);
        var fired = 0;
        vm.ServerAvailabilityChanged += (_, _) => fired++;

        settings.Settings.ManagedServers[0].Port = 50000;
        await settings.SaveAsync();
        await WaitForAsync(() => fired > 0, "the availability-changed event firing");

        Assert.True(fired > 0, "changing a managed server's port must fire ServerAvailabilityChanged");
    }

    /// <summary>r16 03-workbench-and-desktop.md 3.2: the nav dot's old converter only re-evaluated on a full Servers rebuild, not a per-item Status transition.</summary>
    [Fact]
    public void AnyServerRunning_reflects_a_per_item_status_change_without_a_settings_save()
    {
        using var temp = new TempDir();
        var vm = NewServicesVm(temp, out _);
        Assert.False(vm.AnyServerRunning);

        var raisedForAnyServerRunning = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ServicesViewModel.AnyServerRunning))
                raisedForAnyServerRunning++;
        };

        vm.Servers[0].Status = ServerStatus.Running;
        Assert.True(vm.AnyServerRunning, "starting one server should light AnyServerRunning without a settings save/rebuild");
        Assert.True(raisedForAnyServerRunning > 0, "AnyServerRunning should raise PropertyChanged on a per-server Status transition");

        vm.Servers[0].Status = ServerStatus.Stopped;
        Assert.False(vm.AnyServerRunning, "stopping the last running server should clear AnyServerRunning");
    }

    [Fact]
    public async Task Rebuild_reuses_the_existing_row_instead_of_replacing_it()
    {
        using var temp = new TempDir();
        var vm = NewServicesVm(temp, out var settings);
        var originalChatRow = vm.Servers.First(s => !s.EmbeddingsMode);
        originalChatRow.LogExpanded = true;

        settings.Settings.DataManagement.LocalAiAssetsRoot = temp.PathFor("assets");
        await settings.SaveAsync();
        await Task.Delay(100);

        var afterRebuild = vm.Servers.First(s => !s.EmbeddingsMode);
        Assert.Same(originalChatRow, afterRebuild);
        Assert.True(afterRebuild.LogExpanded, "reused rows must keep their UI state (e.g. expanded logs)");
    }

    [Fact]
    public async Task Removing_a_managed_server_disposes_its_view_model()
    {
        using var temp = new TempDir();

        // Rebuild reaches this view model through RunOnUi, which is
        // fire-and-forget by design in production, so waiting on a timeout made
        // this assertion probable rather than certain: under full-suite load the
        // wait expired and the suite went red for a reason that was not a bug.
        // Installing the queueing context for construction captures those posts
        // instead, so the test drains the work it is waiting on. Widening the
        // timeout would only have converted an occasional red into a slower one.
        var sync = new QueueingSynchronizationContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(sync);
        ServicesViewModel vm;
        ISettingsService settings;
        try
        {
            vm = NewServicesVm(temp, out settings);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        var chatRow = vm.Servers.First(s => !s.EmbeddingsMode);

        settings.Settings.ManagedServers.RemoveAll(s => s.Id == chatRow.Id);
        settings.Settings.ManagedServers.Add(new ServerConfig { Name = "Chat", Port = 39201 });
        await settings.SaveAsync();
        sync.DrainAll();

        Assert.True(chatRow.IsDisposed, "a row whose config was removed must be disposed, not just dropped");
        Assert.DoesNotContain(vm.Servers, s => s.Id == chatRow.Id);
    }

    /// <summary>
    /// The Settings tab's SaveAsync(AppSettings, ...) overload swaps
    /// ISettingsService.Settings to a brand new clone (same content, new
    /// object identity) on every save, from anywhere else in the app. Before
    /// this fix, an existing ServerProcessViewModel row kept its readonly
    /// _config pointed at the pre-swap object forever, so any edit made on
    /// that row afterward silently mutated an orphaned object instead of the
    /// live settings tree: SyncToConfig() "succeeded" and _settings.SaveAsync()
    /// returned normally, but the edit never actually reached settings.json.
    /// </summary>
    [Fact]
    public async Task An_unrelated_settings_swap_does_not_silently_drop_the_next_edit_on_an_existing_row()
    {
        using var temp = new TempDir();
        var vm = NewServicesVm(temp, out var settings);
        var chatRow = vm.Servers.First(s => !s.EmbeddingsMode);

        // Simulate an unrelated Settings-tab save (e.g. toggling a UI
        // preference): same ManagedServers content, new AppSettings identity.
        await settings.SaveAsync(settings.Settings.Clone());
        await WaitForAsync(() => ReferenceEquals(vm.Servers.First(s => s.Id == chatRow.Id), chatRow), "the server row instance being preserved across rebuild");

        chatRow.AutoStart = true;
        chatRow.GpuLayers = 999;
        await chatRow.SaveConfigCommand.ExecuteAsync(null);

        var persisted = settings.Settings.ManagedServers.First(s => s.Id == chatRow.Id);
        Assert.True(persisted.AutoStart, "AutoStart edited after an unrelated settings swap must actually persist");
        Assert.Equal(999, persisted.GpuLayers);
    }
}
