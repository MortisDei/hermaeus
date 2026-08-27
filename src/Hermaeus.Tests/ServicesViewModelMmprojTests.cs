using Hermaeus.Core.Models;
using Hermaeus.Services;
using Hermaeus.Services.ProcessManagement;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>The projector picker lists local candidates for explicit selection. A filename
/// alone is not compatibility evidence, so it never auto-selects or substitutes one.</summary>
public sealed class ServicesViewModelMmprojTests
{
    private static (ServicesViewModel Vm, ServerProcessViewModel Server, string ModelPath, string ModelsDir) Build(TempDir temp)
    {
        var settings = NewSettings(temp);
        var modelsRoot = temp.PathFor("assets");
        var nested = Path.Combine(modelsRoot, "models");
        Directory.CreateDirectory(nested);
        var modelPath = Path.Combine(nested, "model-a.gguf");
        File.WriteAllText(modelPath, "fake");
        settings.Settings.DataManagement.LocalAiAssetsRoot = modelsRoot;

        settings.Settings.ManagedServers.Clear();
        settings.Settings.ManagedServers.Add(new ServerConfig { Name = "Chat", Port = 39301 });

        var vm = NewServicesViewModel(settings);
        return (vm, vm.Servers[0], modelPath, nested);
    }

    [Fact]
    public void A_sole_mmproj_file_beside_the_model_is_listed_but_not_auto_selected()
    {
        using var temp = new TempDir();
        var (_, server, modelPath, dir) = Build(temp);
        File.WriteAllText(Path.Combine(dir, "mmproj-model-a.gguf"), "fake");

        server.ModelPath = modelPath;

        Assert.Empty(server.MmprojPath);
        Assert.Contains(Path.Combine(dir, "mmproj-model-a.gguf"), server.DetectedMmprojPaths);
    }

    [Fact]
    public void No_mmproj_file_beside_the_model_leaves_the_field_empty()
    {
        using var temp = new TempDir();
        var (_, server, modelPath, _) = Build(temp);

        server.ModelPath = modelPath;

        Assert.Empty(server.MmprojPath);
        Assert.Empty(server.DetectedMmprojPaths);
    }

    [Fact]
    public void An_explicit_mmproj_choice_is_not_overwritten_by_a_later_model_path_reassignment()
    {
        using var temp = new TempDir();
        var (_, server, modelPath, dir) = Build(temp);
        File.WriteAllText(Path.Combine(dir, "mmproj-model-a.gguf"), "fake");
        server.ModelPath = modelPath;
        Assert.Empty(server.MmprojPath);

        var manualChoice = temp.PathFor("elsewhere/mmproj-custom.gguf");
        Directory.CreateDirectory(Path.GetDirectoryName(manualChoice)!);
        File.WriteAllText(manualChoice, "fake");
        server.MmprojPath = manualChoice;

        server.ModelPath = modelPath; // no-op reassignment, e.g. from RefreshDetectedModels' Reset repair

        Assert.Equal(manualChoice, server.MmprojPath);
    }

    [Fact]
    public void Switching_models_does_not_substitute_a_projector()
    {
        using var temp = new TempDir();
        var (_, server, firstModel, dir) = Build(temp);
        var secondModel = Path.Combine(dir, "model-b.gguf");
        var firstProjector = Path.Combine(dir, "mmproj-model-a.gguf");
        var secondProjector = Path.Combine(dir, "mmproj-model-b.gguf");
        File.WriteAllText(firstProjector, "fake");
        server.ModelPath = firstModel;
        File.Delete(firstProjector);
        File.WriteAllText(secondModel, "fake");
        File.WriteAllText(secondProjector, "fake");

        server.ModelPath = secondModel;

        Assert.Empty(server.MmprojPath);
        Assert.Contains(secondProjector, server.DetectedMmprojPaths);
    }

    [Fact]
    public void Projector_use_preference_preserves_the_configured_path_and_round_trips_through_server_config()
    {
        using var temp = new TempDir();
        var (_, server, _, _) = Build(temp);
        var projectorPath = temp.PathFor("projector/mmproj-verified.gguf");
        Directory.CreateDirectory(Path.GetDirectoryName(projectorPath)!);
        File.WriteAllText(projectorPath, "projector");

        server.MmprojPath = projectorPath;
        server.UseProjector = false;
        var disabled = server.BuildConfig();

        Assert.False(disabled.UseProjector);
        Assert.Equal(projectorPath, disabled.MmprojPath);
        Assert.DoesNotContain("--mmproj", ServerProcessManager.BuildLaunchArguments(disabled));

        server.UseProjector = true;
        var enabled = server.BuildConfig();
        var args = ServerProcessManager.BuildLaunchArguments(enabled).ToList();
        var index = args.IndexOf("--mmproj");

        Assert.True(index >= 0);
        Assert.Equal(projectorPath, args[index + 1]);
    }
}
