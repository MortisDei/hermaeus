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

        var vm = new ServicesViewModel(settings, new RuntimeProfileService(settings), new FakeToasts(), new RedactionService(), new TrustService(), new RuntimeLogService(settings));
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
    public void A_tune_profile_wins_over_the_card_default()
    {
        using var temp = new TempDir();
        var (_, server, settings, modelPath) = Build(temp, cardDefaultContextSize: 24000);
        LlamaTuneProfileStore.Upsert(settings.Settings, modelPath, contextSize: 9000, extraArgs: "", currentGpuLayers: -1, currentThreads: 4, result: null);

        server.ModelPath = modelPath;

        Assert.Equal(9000, server.ContextSize);
        Assert.Equal("Context from Auto Tune", server.ContextSourceLabel);
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
}
