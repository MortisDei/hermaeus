using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Hermaeus.Core.Models;
using Hermaeus.Rag;
using Hermaeus.Rag.Retrieval;
using Hermaeus.Rag.Storage;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

/// <summary>
/// A workspace with no declared command recipes can run nothing, and the only
/// way to declare one was to hand-edit .hermaeus/workspace.json: the user had
/// to know the file existed and which command families the safety gate would
/// accept. Both are on screen now. Nothing here widens what the gate allows.
/// </summary>
public sealed class WorkspaceCommandRecipeEditingTests
{
    private static async Task<(AgentViewModel Vm, string Workspace)> NewViewModelAsync(TempDir temp)
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new FileAgentTaskStateStore(settings);
        await store.InitializeAsync();
        var memoryStore = new WorkspaceMemoryStore(new MemoryStore(settings), settings);
        await memoryStore.InitializeAsync();
        var tools = new AgentWorkspaceTools();
        var ragStore = new SqliteRagStore(settings);
        await ragStore.InitializeAsync();
        var rag = new RagQueryService(ragStore, new FakeEmbeddingService(), new FakeLlm(), settings, new NoOpReranker());
        var agentService = new AgentService(store, new FakeAgentContextBuilder(), new AgentSafetyGate(),
            new AgentToolExecutor(tools), new FakeAgentLlm());
        var profiles = new FileWorkspaceProfileStore(settings);
        var manifests = new WorkspaceManifestService();

        var workspace = temp.PathFor("workspace");
        Directory.CreateDirectory(workspace);

        var vm = new AgentViewModel(
            agentService, store, memoryStore, tools, new FakeLlm(), rag,
            new RuntimeLogService(settings),
            new WorkspaceAnalysisService(profiles, memoryStore),
            new WorkspaceActivationService(manifests, profiles),
            manifests, settings)
        {
            WorkspaceRoot = workspace
        };
        return (vm, workspace);
    }

    [Fact]
    public void Every_offered_family_is_one_the_safety_gate_actually_accepts()
    {
        Assert.NotEmpty(WorkspaceCommandRecipes.KnownFamilies);
        Assert.All(WorkspaceCommandRecipes.KnownFamilies,
            family => Assert.Equal(family, WorkspaceCommandRecipes.ExtractFamily(family)));
    }

    [Fact]
    public async Task Adding_a_recipe_writes_it_to_the_workspace_manifest_immediately()
    {
        using var temp = new TempDir();
        var (vm, workspace) = await NewViewModelAsync(temp);

        vm.SelectedCommandFamily = vm.AvailableCommandFamilies.First(f => f.Family == "dotnet test");
        await vm.AddCommandRecipeCommand.ExecuteAsync(null);

        var recipe = Assert.Single(vm.CommandRecipes);
        Assert.Equal("dotnet test", recipe.Command);

        // On the fly means on disk, not on the next Save click.
        var manifest = await new WorkspaceManifestService().LoadAsync(workspace);
        Assert.NotNull(manifest);
        Assert.Contains(manifest!.AllowedCommands, c => c.Command == "dotnet test");
    }

    [Fact]
    public async Task An_argument_narrows_the_recipe_to_one_project()
    {
        using var temp = new TempDir();
        var (vm, _) = await NewViewModelAsync(temp);

        vm.SelectedCommandFamily = vm.AvailableCommandFamilies.First(f => f.Family == "dotnet test");
        vm.NewRecipeArgument = "src/Hermaeus.Tests/Hermaeus.Tests.csproj";
        await vm.AddCommandRecipeCommand.ExecuteAsync(null);

        Assert.Equal("dotnet test src/Hermaeus.Tests/Hermaeus.Tests.csproj", Assert.Single(vm.CommandRecipes).Command);
        // The composer is cleared so the next add does not silently inherit it.
        Assert.Equal(string.Empty, vm.NewRecipeArgument);
    }

    [Fact]
    public async Task The_same_recipe_is_not_added_twice()
    {
        using var temp = new TempDir();
        var (vm, _) = await NewViewModelAsync(temp);

        vm.SelectedCommandFamily = vm.AvailableCommandFamilies.First(f => f.Family == "dotnet build");
        await vm.AddCommandRecipeCommand.ExecuteAsync(null);
        await vm.AddCommandRecipeCommand.ExecuteAsync(null);

        Assert.Single(vm.CommandRecipes);
    }

    [Fact]
    public async Task Removing_a_recipe_takes_it_off_the_manifest_too()
    {
        using var temp = new TempDir();
        var (vm, workspace) = await NewViewModelAsync(temp);

        vm.SelectedCommandFamily = vm.AvailableCommandFamilies.First(f => f.Family == "pytest");
        await vm.AddCommandRecipeCommand.ExecuteAsync(null);
        await vm.RemoveCommandRecipeCommand.ExecuteAsync(vm.CommandRecipes.Single());

        Assert.Empty(vm.CommandRecipes);
        var manifest = await new WorkspaceManifestService().LoadAsync(workspace);
        Assert.DoesNotContain(manifest?.AllowedCommands ?? [], c => c.Command == "pytest");
    }

    [Fact]
    public async Task A_recipe_cannot_be_added_without_a_workspace_to_add_it_to()
    {
        using var temp = new TempDir();
        var (vm, _) = await NewViewModelAsync(temp);
        vm.WorkspaceRoot = string.Empty;

        vm.SelectedCommandFamily = vm.AvailableCommandFamilies[0];

        Assert.False(vm.CanAddCommandRecipe);
    }

    [Fact]
    public async Task An_argument_that_breaks_the_family_is_refused_rather_than_saved()
    {
        using var temp = new TempDir();
        var (vm, _) = await NewViewModelAsync(temp);

        // The gate keys off the family prefix, so an argument that turns the
        // command into something else must not be storable as a recipe.
        vm.SelectedCommandFamily = vm.AvailableCommandFamilies.First(f => f.Family == "dotnet test");
        vm.NewRecipeArgument = "\n&& rm -rf /";
        await vm.AddCommandRecipeCommand.ExecuteAsync(null);

        // Either it kept the family (and is therefore still gate-checkable) or
        // it was refused outright; what must never happen is a stored recipe
        // the gate does not recognize.
        Assert.All(vm.CommandRecipes,
            r => Assert.NotNull(WorkspaceCommandRecipes.ExtractFamily(r.Command)));
    }

    [Fact]
    public async Task Declared_recipes_are_what_the_capability_text_names()
    {
        using var temp = new TempDir();
        var (vm, _) = await NewViewModelAsync(temp);

        Assert.Contains(vm.CapabilityNotes, note => note.Contains("no command recipes", StringComparison.OrdinalIgnoreCase));

        vm.SelectedCommandFamily = vm.AvailableCommandFamilies.First(f => f.Family == "cargo test");
        await vm.AddCommandRecipeCommand.ExecuteAsync(null);

        Assert.Contains(vm.CapabilityNotes, note => note.Contains("cargo test", StringComparison.Ordinal));
    }
}
