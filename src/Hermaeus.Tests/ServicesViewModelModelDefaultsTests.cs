using Hermaeus.Core.Models;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>
/// r19 2.1 (model-card defaults flow into the Services server card) and 2.5
/// (the manual model-path text box is gone, so a path outside the detected
/// scan must still render selected in the ComboBox).
/// </summary>
public sealed class ServicesViewModelModelDefaultsTests
{
    private static (ServicesViewModel Vm, ServerProcessViewModel Server, SettingsService Settings, string ModelPath) Build(TempDir temp, int? cardDefaultContextSize = null)
    {
        var settings = NewSettings(temp);
        var modelsRoot = temp.PathFor("assets");
        var nested = Path.Combine(modelsRoot, "models");
        Directory.CreateDirectory(nested);
        var modelPath = Path.Combine(nested, "model-a.gguf");
        File.WriteAllText(modelPath, "fake");
        settings.Settings.DataManagement.LocalAiAssetsRoot = modelsRoot;

        settings.Settings.ManagedServers.Clear();
        settings.Settings.ManagedServers.Add(new ServerConfig { Name = "Chat", Port = 39201 });

        if (cardDefaultContextSize is not null)
        {
            settings.Settings.ModelProfiles.Add(new ModelProfile
            {
                ModelId = modelPath,
                DefaultContextSize = cardDefaultContextSize
            });
        }

        var vm = NewServicesViewModel(settings);
        return (vm, vm.Servers[0], settings, modelPath);
    }

    [Fact]
    public void Selecting_a_model_with_a_card_default_and_no_tune_profile_sets_context_size()
    {
        using var temp = new TempDir();
        var (_, server, _, modelPath) = Build(temp, cardDefaultContextSize: 24000);

        server.ModelPath = modelPath;

        Assert.Equal(24000, server.ContextSize);
        Assert.Equal("Context from model card", server.ContextSourceLabel);
    }

    [Fact]
    public void A_tune_profile_does_not_override_the_card_default_or_editor()
    {
        using var temp = new TempDir();
        var (_, server, settings, modelPath) = Build(temp, cardDefaultContextSize: 24000);
        LlamaTuneProfileStore.Upsert(settings.Settings, modelPath, contextSize: 9000, extraArgs: "", currentGpuLayers: -1, currentThreads: 4, result: null);

        server.ModelPath = modelPath;

        Assert.Equal(24000, server.ContextSize);
        Assert.Equal("Context from model card", server.ContextSourceLabel);
    }

    [Fact]
    public void Reselecting_the_same_path_does_not_re_apply_the_card_default_over_a_user_edit()
    {
        using var temp = new TempDir();
        var (_, server, _, modelPath) = Build(temp, cardDefaultContextSize: 24000);

        server.ModelPath = modelPath;
        Assert.Equal(24000, server.ContextSize);

        server.ContextSize = 12000;
        server.ModelPath = modelPath; // no-op reassignment, e.g. from RefreshDetectedModels' Reset repair

        Assert.Equal(12000, server.ContextSize);
    }

    [Fact]
    public void Browsing_to_a_model_outside_the_detected_root_still_renders_selected()
    {
        using var temp = new TempDir();
        var (_, server, _, _) = Build(temp);
        var outsidePath = temp.PathFor("elsewhere/model-b.gguf");
        Directory.CreateDirectory(Path.GetDirectoryName(outsidePath)!);
        File.WriteAllText(outsidePath, "fake");

        server.ModelPath = outsidePath;

        Assert.Contains(outsidePath, server.DetectedModelPaths);

        // A later rescan (RefreshDetectedModels) will not find this file
        // under the assets root and would clear it from the list; the
        // ComboBox-Reset repair path must put it back.
        server.RefreshDetectedModels();
        Assert.Equal(outsidePath, server.ModelPath);
        Assert.Contains(outsidePath, server.DetectedModelPaths);
    }

    [Fact]
    public void Switching_models_clears_incompatible_runtime_fields_and_restores_each_model_draft()
    {
        using var temp = new TempDir();
        var (_, server, _, modelA) = Build(temp);
        var modelB = temp.PathFor("assets/models/model-b.gguf");
        var draftA = temp.PathFor("assets/models/mtp-model-a.gguf");
        var mmprojA = temp.PathFor("assets/models/mmproj-model-a.gguf");
        File.WriteAllText(modelB, "fake b");
        File.WriteAllText(draftA, "fake draft");
        File.WriteAllText(mmprojA, "fake projector");

        server.ModelPath = modelA;
        server.ContextSize = 131072;
        server.GpuPlacementSelection = "All";
        server.Threads = 4;
        server.PromptThreads = 3;
        server.Slots = 2;
        server.ExtraArgs = "--draft-max 8";
        server.KvCacheType = "q8_0";
        server.FlashAttention = "on";
        server.ContextShift = true;
        server.MemoryLock = true;
        server.NoMemoryMap = true;
        server.CpuMoeLayersText = "12";
        server.SpeculativeTypes = "draft-mtp";
        server.DraftModelPath = draftA;
        server.DraftGpuLayersText = "10";
        server.SpeculativeNMaxText = "8";
        server.SpeculativeNMinText = "2";
        server.SpeculativePMinText = "0.2";
        server.MmprojPath = mmprojA;
        server.UseProjector = true;
        server.PreserveReasoning = false;

        server.ModelPath = modelB;

        Assert.Equal(4096, server.ContextSize);
        Assert.Equal("CPU", server.GpuPlacementSelection);
        Assert.Equal(0, server.GpuLayers);
        Assert.Equal(0, server.Threads);
        Assert.Equal(0, server.PromptThreads);
        Assert.Equal(1, server.Slots);
        Assert.Equal(string.Empty, server.ExtraArgs);
        Assert.Equal("f16", server.KvCacheType);
        Assert.Equal("auto", server.FlashAttention);
        Assert.False(server.ContextShift);
        Assert.False(server.MemoryLock);
        Assert.False(server.NoMemoryMap);
        Assert.Equal(string.Empty, server.CpuMoeLayersText);
        Assert.Equal(string.Empty, server.SpeculativeTypes);
        Assert.Equal(string.Empty, server.DraftModelPath);
        Assert.Equal(string.Empty, server.DraftGpuLayersText);
        Assert.Equal(string.Empty, server.SpeculativeNMaxText);
        Assert.Equal(string.Empty, server.SpeculativeNMinText);
        Assert.Equal(string.Empty, server.SpeculativePMinText);
        Assert.Equal(string.Empty, server.MmprojPath);
        Assert.False(server.UseProjector);
        Assert.True(server.PreserveReasoning);

        server.ContextSize = 8192;
        server.Threads = 2;
        server.SpeculativeTypes = "ngram-mod";
        server.ModelPath = modelA;

        Assert.Equal(131072, server.ContextSize);
        Assert.Equal("All", server.GpuPlacementSelection);
        Assert.Equal(-1, server.GpuLayers);
        Assert.Equal(4, server.Threads);
        Assert.Equal(3, server.PromptThreads);
        Assert.Equal(2, server.Slots);
        Assert.Equal("--draft-max 8", server.ExtraArgs);
        Assert.Equal("q8_0", server.KvCacheType);
        Assert.Equal("on", server.FlashAttention);
        Assert.True(server.ContextShift);
        Assert.True(server.MemoryLock);
        Assert.True(server.NoMemoryMap);
        Assert.Equal("12", server.CpuMoeLayersText);
        Assert.Equal("draft-mtp", server.SpeculativeTypes);
        Assert.Equal(draftA, server.DraftModelPath);
        Assert.Equal("10", server.DraftGpuLayersText);
        Assert.Equal("8", server.SpeculativeNMaxText);
        Assert.Equal("2", server.SpeculativeNMinText);
        Assert.Equal("0.2", server.SpeculativePMinText);
        Assert.Equal(mmprojA, server.MmprojPath);
        Assert.True(server.UseProjector);
        Assert.False(server.PreserveReasoning);

    }

    [Fact]
    public void Switching_to_a_model_uses_only_its_card_and_tune_profile()
    {
        using var temp = new TempDir();
        var (_, server, settings, modelA) = Build(temp);
        var modelB = temp.PathFor("assets/models/model-b.gguf");
        File.WriteAllText(modelB, "fake b");
        settings.Settings.ModelProfiles.Add(new ModelProfile
        {
            ModelId = modelB,
            DefaultContextSize = 20000,
            DefaultKvCacheType = "q8_0",
            DefaultPreserveReasoning = false
        });
        LlamaTuneProfileStore.Upsert(settings.Settings, modelB, contextSize: 15000,
            extraArgs: "--batch-size 256", currentGpuLayers: 12, currentThreads: 6, result: null);

        server.ModelPath = modelA;
        server.SpeculativeTypes = "draft-mtp";
        server.DraftModelPath = temp.PathFor("assets/models/mtp-model-a.gguf");
        server.ModelPath = modelB;

        Assert.Equal(20000, server.ContextSize);
        Assert.Equal("Exact", server.GpuPlacementSelection);
        Assert.Equal(12, server.GpuLayers);
        Assert.Equal(6, server.Threads);
        Assert.Equal("--batch-size 256", server.ExtraArgs);
        Assert.Equal("q8_0", server.KvCacheType);
        Assert.False(server.PreserveReasoning);
        Assert.Equal(string.Empty, server.SpeculativeTypes);
        Assert.Equal(string.Empty, server.DraftModelPath);
    }

    [Fact]
    public void Rebuild_resolves_an_existing_managed_llama_server_for_default_slots()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var assets = temp.PathFor("assets");
        var install = Path.Combine(assets, "llama-server", "b123");
        Directory.CreateDirectory(install);
        var executable = Path.Combine(install, OperatingSystem.IsWindows() ? "llama-server.exe" : "llama-server");
        WriteRunnableLlamaProbeFixture(executable);
        settings.Settings.DataManagement.LocalAiAssetsRoot = assets;
        settings.Settings.ManagedServers.Clear();

        var services = NewServicesViewModel(settings);

        Assert.Equal(executable, services.Servers[0].ExecutablePath);
        Assert.Equal(executable, services.Servers[1].ExecutablePath);
    }
}
