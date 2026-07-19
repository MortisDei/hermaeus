using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Services;
using Aether.ViewModels;
using Xunit;
using static Aether.Tests.Helpers;

namespace Aether.Tests;

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

    private static ServicesViewModel NewServicesVm(TempDir temp, out ISettingsService settings)
    {
        settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        return new ServicesViewModel(settings, new RuntimeProfileService(settings), new FakeToasts(), new RedactionService(), new TrustService(), new RuntimeLogService(settings));
    }

    /// <summary>
    /// ServicesViewModel.Rebuild runs from ISettingsService.SettingsChanged
    /// via RunOnUi; under xUnit's AsyncTestSyncContext, RunOnUi's captured
    /// context does not always match the context active by the time the
    /// event fires deep inside SaveAsync's own await chain, so the posted
    /// Rebuild can land after the awaited SaveAsync call already returned.
    /// Poll briefly instead of asserting immediately.
    /// </summary>
    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
            await Task.Delay(10);
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
        await WaitForAsync(() => fired > 0);

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
        var vm = NewServicesVm(temp, out var settings);
        var chatRow = vm.Servers.First(s => !s.EmbeddingsMode);

        settings.Settings.ManagedServers.RemoveAll(s => s.Id == chatRow.Id);
        settings.Settings.ManagedServers.Add(new ServerConfig { Name = "Chat", Port = 39201 });
        await settings.SaveAsync();
        await WaitForAsync(() => chatRow.IsDisposed);

        Assert.True(chatRow.IsDisposed, "a row whose config was removed must be disposed, not just dropped");
        Assert.DoesNotContain(vm.Servers, s => s.Id == chatRow.Id);
    }
}
