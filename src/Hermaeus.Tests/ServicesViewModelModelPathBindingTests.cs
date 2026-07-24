using System.Collections.Specialized;
using Hermaeus.Core.Models;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>
/// A real bug found live: the Services page's Model (.gguf) ComboBox
/// (SelectedItem="{Binding ModelPath}", TwoWay by default) is bound to
/// ServerProcessViewModel.DetectedModelPaths. RefreshDetectedModels() called
/// DetectedModelPaths.Clear() then repopulated it; Clear() fires a
/// CollectionChanged Reset, which a live ComboBox reacts to by nulling its own
/// selection - and because the binding is TwoWay, that null immediately writes
/// back into ModelPath, before the collection is repopulated. A plain
/// ViewModel-level test never catches this because nothing in the VM layer
/// itself subscribes to CollectionChanged the way a real ComboBox does; these
/// tests wire up a fake subscriber that reproduces exactly that reaction.
/// </summary>
public sealed class ServicesViewModelModelPathBindingTests
{
    /// <summary>Mimics Avalonia's SelectingItemsControl reacting to a bound
    /// ItemsSource Reset by clearing SelectedItem, then writing that back
    /// through a TwoWay SelectedItem="{Binding ModelPath}" binding.</summary>
    private static void AttachComboBoxLikeBehavior(ServerProcessViewModel server)
    {
        server.DetectedModelPaths.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
                server.ModelPath = string.Empty;
        };
    }

    [Fact]
    public void RefreshDetectedModels_survives_the_ComboBox_Reset_null_writeback()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var modelsRoot = temp.PathFor("assets");
        var nested = Path.Combine(modelsRoot, "models", "hub", "models--unsloth--gemma-4-E4B-it-qat-GGUF", "snapshots", "e4a9ed86");
        Directory.CreateDirectory(nested);
        var modelPath = Path.Combine(nested, "gemma-4-E4B-it-qat.gguf");
        File.WriteAllText(modelPath, "fake");
        settings.Settings.DataManagement.LocalAiAssetsRoot = modelsRoot;

        settings.Settings.ManagedServers.Clear();
        settings.Settings.ManagedServers.Add(new ServerConfig { Name = "Chat", ModelPath = modelPath, Port = 39201 });
        settings.Settings.ManagedServers.Add(new ServerConfig { Name = "Embeddings", Port = 39202, EmbeddingsMode = true });

        var vm = NewServicesViewModel(settings);
        var server = vm.Servers[0];
        AttachComboBoxLikeBehavior(server);

        server.RefreshDetectedModels();

        Assert.Equal(modelPath, server.ModelPath);
    }

    [Fact]
    public async Task SaveConfig_survives_the_settings_changed_rebuild_with_a_live_ComboBox_binding()
    {
        using var temp = new TempDir();
        var settings = NewSettings(temp);
        var modelsRoot = temp.PathFor("assets");
        var nested = Path.Combine(modelsRoot, "models", "hub", "models--unsloth--gemma-4-E4B-it-qat-GGUF", "snapshots", "e4a9ed86");
        Directory.CreateDirectory(nested);
        var modelPath = Path.Combine(nested, "gemma-4-E4B-it-qat.gguf");
        File.WriteAllText(modelPath, "fake");
        settings.Settings.DataManagement.LocalAiAssetsRoot = modelsRoot;

        settings.Settings.ManagedServers.Clear();
        settings.Settings.ManagedServers.Add(new ServerConfig { Name = "Chat", ExecutablePath = "llama-server.exe", ModelPath = modelPath, Port = 39201, ContextSize = 16000, GpuLayers = 999, Threads = 4 });
        settings.Settings.ManagedServers.Add(new ServerConfig { Name = "Embeddings", Port = 39202, EmbeddingsMode = true });

        var vm = NewServicesViewModel(settings);
        var server = vm.Servers[0];
        AttachComboBoxLikeBehavior(server);

        await server.SaveConfigCommand.ExecuteAsync(null);

        Assert.False(string.IsNullOrWhiteSpace(server.ModelPath), "ModelPath was cleared by Save Config's settings-changed rebuild.");
        Assert.Equal(modelPath, server.ModelPath);
        Assert.Equal(modelPath, settings.Settings.ManagedServers[0].ModelPath);
    }
}
