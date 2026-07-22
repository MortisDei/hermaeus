using Hermaeus.Core.Models;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>r19 5.3: the vision projector picker auto-suggests a sole mmproj-*.gguf file found
/// beside the selected model, without ever overwriting an explicit user choice.</summary>
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

        var vm = new ServicesViewModel(settings, new RuntimeProfileService(settings), new FakeToasts(), new RedactionService(), new TrustService(), new RuntimeLogService(settings));
        return (vm, vm.Servers[0], modelPath, nested);
    }

    [Fact]
    public void A_sole_mmproj_file_beside_the_model_is_auto_suggested()
    {
        using var temp = new TempDir();
        var (_, server, modelPath, dir) = Build(temp);
        File.WriteAllText(Path.Combine(dir, "mmproj-model-a.gguf"), "fake");

        server.ModelPath = modelPath;

        Assert.Equal(Path.Combine(dir, "mmproj-model-a.gguf"), server.MmprojPath);
        Assert.Contains(server.MmprojPath, server.DetectedMmprojPaths);
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
        Assert.Equal(Path.Combine(dir, "mmproj-model-a.gguf"), server.MmprojPath);

        var manualChoice = temp.PathFor("elsewhere/mmproj-custom.gguf");
        Directory.CreateDirectory(Path.GetDirectoryName(manualChoice)!);
        File.WriteAllText(manualChoice, "fake");
        server.MmprojPath = manualChoice;

        server.ModelPath = modelPath; // no-op reassignment, e.g. from RefreshDetectedModels' Reset repair

        Assert.Equal(manualChoice, server.MmprojPath);
    }
}
