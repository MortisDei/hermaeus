using System.Text.Json;
using Hermaeus.Desktop.Views;
using Hermaeus.Agent.Models;
using Hermaeus.Agent.Services;
using Hermaeus.Core.Models;
using Hermaeus.Rag.Models;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;

namespace Hermaeus.Tests;

public sealed class R33UxAndPetTests
{
    [Fact]
    public void Primary_agent_states_use_actionable_labels()
    {
        Assert.Equal("Waiting for you", AgentPresentationText.TaskStatus(AgentTaskStatus.WaitingForUser));
        Assert.Equal("Recovered after interruption", AgentPresentationText.TaskStatus(AgentTaskStatus.Interrupted));
        Assert.Equal("Applied and verified", AgentPresentationText.PatchStatus(AgentDraftPatchStatus.Applied));
        Assert.Equal("Needs review", AgentPresentationText.PatchStatus(AgentDraftPatchStatus.Pending));
    }

    [Fact]
    public void Lab_and_rag_labels_keep_capability_and_scope_meaningful()
    {
        Assert.Equal("Unavailable", LabPresentationText.CapabilityState(CapabilityState.Unavailable));
        Assert.Contains("current model or runtime", LabPresentationText.CapabilityHint(CapabilityState.Unavailable), StringComparison.Ordinal);
        Assert.Equal("Completed with reservations", LabPresentationText.RunStatus("PartiallySucceeded"));
        Assert.Equal("Completed, effective configuration unverified", LabPresentationText.RunStatus("Inconclusive"));

        var local = new RagDataset { Name = "Notes" };
        var remote = new RagDataset { Name = "Web", Config = new RagDatasetConfig { EnableWebLoader = true } };
        Assert.Equal("Local files", new RagDatasetQueryOptionViewModel(local, true).ScopeLabel);
        Assert.Equal("Remote web", new RagDatasetQueryOptionViewModel(remote, true).ScopeLabel);
    }

    [Fact]
    public void Pet_is_off_by_default_and_position_stays_inside_the_window()
    {
        var settings = new UiSettingsViewModel();
        var pet = new DesktopPetViewModel();
        pet.BindSettings(settings);
        pet.UpdateViewport(800, 600, 96, 104);

        Assert.False(settings.PetEnabled);
        Assert.False(pet.IsVisible);

        pet.SetPosition(900, 900);

        Assert.Equal(704, pet.PositionX);
        Assert.Equal(496, pet.PositionY);
        Assert.Equal(704, settings.PetPositionX);
        Assert.Equal(496, settings.PetPositionY);
    }

    [Fact]
    public void Bundled_pet_is_a_valid_v2_data_package()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        var catalog = new PetPackageCatalog(settings);

        var package = Assert.Single(catalog.GetAvailablePackages(), candidate => candidate.Manifest.Id == "moss");
        Assert.True(package.IsBundled);
        Assert.Equal(2, package.Manifest.SpriteVersionNumber);
        Assert.EndsWith("spritesheet.webp", package.SpritesheetPath, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(package.SpritesheetPath));
    }

    [Fact]
    public async Task Pet_import_rejects_traversal_and_executable_content()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        var catalog = new PetPackageCatalog(settings);
        var packageRoot = temp.PathFor("incoming/pet");
        Directory.CreateDirectory(packageRoot);

        await File.WriteAllTextAsync(
            Path.Combine(packageRoot, "pet.json"),
            JsonSerializer.Serialize(new PetPackageManifest
            {
                Id = "unsafe-pet",
                DisplayName = "Unsafe",
                Description = "test",
                SpriteVersionNumber = 2,
                SpritesheetPath = "../outside.webp"
            }));

        var traversal = await catalog.ImportAsync(Path.Combine(packageRoot, "pet.json"));
        Assert.False(traversal.Succeeded);
        Assert.Contains("traversal", traversal.Error, StringComparison.OrdinalIgnoreCase);

        await File.WriteAllTextAsync(Path.Combine(packageRoot, "hook.js"), "alert(1);");
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "Pets", "moss", "spritesheet.webp"),
            Path.Combine(packageRoot, "spritesheet.webp"));
        await File.WriteAllTextAsync(
            Path.Combine(packageRoot, "pet.json"),
            JsonSerializer.Serialize(new PetPackageManifest
            {
                Id = "unsafe-pet",
                DisplayName = "Unsafe",
                Description = "test",
                SpriteVersionNumber = 2,
                SpritesheetPath = "spritesheet.webp"
            }));
        var executable = await catalog.ImportAsync(Path.Combine(packageRoot, "pet.json"));

        Assert.False(executable.Succeeded);
        Assert.Contains("unsupported file type", executable.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Monaco_bundle_exposes_only_the_local_editor_contract()
    {
        var indexPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Monaco", "index.html");
        Assert.True(File.Exists(indexPath));
        var html = File.ReadAllText(indexPath);

        Assert.Contains("hermaeusEditorApi", html, StringComparison.Ordinal);
        Assert.Contains("setDocument", html, StringComparison.Ordinal);
        Assert.Contains("getText", html, StringComparison.Ordinal);
        Assert.Contains("setTheme", html, StringComparison.Ordinal);
        Assert.DoesNotContain("http://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Workspace_editor_keeps_local_lifecycle_and_fallback_contract()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Hermaeus.Desktop",
            "Views",
            "WorkspaceEditorView.axaml.cs"));

        Assert.Contains("TryCreateMonaco", source, StringComparison.Ordinal);
        Assert.Contains("BuildFallbackEditor", source, StringComparison.Ordinal);
        Assert.Contains("OnDetachedFromVisualTree", source, StringComparison.Ordinal);
        Assert.Contains("_monacoAttempted = false", source, StringComparison.Ordinal);
        Assert.Contains("DisposeWebViewAsync", source, StringComparison.Ordinal);
        Assert.Contains("ActualThemeVariantChanged", source, StringComparison.Ordinal);
        Assert.Contains("MaxMonacoCharacters", source, StringComparison.Ordinal);
        Assert.Contains("MonacoReadyTimeout", source, StringComparison.Ordinal);
        Assert.Contains("FallbackIfMonacoDoesNotBecomeReadyAsync", source, StringComparison.Ordinal);
        Assert.Contains("3 seconds", source, StringComparison.Ordinal);
        Assert.Contains("new Uri(Path.GetFullPath(indexPath))", source, StringComparison.Ordinal);
        Assert.Contains("layout()", source, StringComparison.Ordinal);
        Assert.Contains("AttachFallbackEditor", source, StringComparison.Ordinal);
        Assert.Contains("AvaloniaEdit fallback", source, StringComparison.Ordinal);
        Assert.Contains("_attachedToVisualTree", source, StringComparison.Ordinal);
        Assert.Contains("EditorHost.SizeChanged", source, StringComparison.Ordinal);
        Assert.Contains("webView.ZIndex = 1", source, StringComparison.Ordinal);
        Assert.Contains("webView.IsVisible = false", source, StringComparison.Ordinal);
        Assert.Contains("_fallbackEditor.IsVisible = true", source, StringComparison.Ordinal);
        Assert.Contains("IsReadOnly = false", source, StringComparison.Ordinal);
        Assert.Contains("ToolTip.SetTip", source, StringComparison.Ordinal);

        var appStyles = File.ReadAllText(Path.Combine(root, "src", "Hermaeus.Desktop", "App.axaml"));
        Assert.Contains("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml", appStyles, StringComparison.Ordinal);

        var editorHost = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Hermaeus.Desktop",
            "Views",
            "AgentView.axaml"));
        Assert.Contains("Height=\"320\"", editorHost, StringComparison.Ordinal);
        Assert.Contains("MaxHeight=\"600\"", editorHost, StringComparison.Ordinal);
    }

    [Fact]
    public void Owned_modals_use_the_shared_dpi_safe_placement_correction()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var views = Path.Combine(root, "src", "Hermaeus.Desktop", "Views");
        var helper = File.ReadAllText(Path.Combine(views, "ModalWindowPlacement.cs"));

        Assert.Contains("PixelSize.FromSize(owner.ClientSize, scaling)", helper, StringComparison.Ordinal);
        Assert.Contains("screen.WorkingArea", helper, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp", helper, StringComparison.Ordinal);

        foreach (var axamlPath in Directory.EnumerateFiles(views, "*.axaml"))
        {
            var axaml = File.ReadAllText(axamlPath);
            if (!axaml.Contains("WindowStartupLocation=\"CenterOwner\"", StringComparison.Ordinal))
                continue;

            var codeBehind = Path.ChangeExtension(axamlPath, ".axaml.cs");
            Assert.True(File.Exists(codeBehind), $"Missing code-behind for {Path.GetFileName(axamlPath)}");
            Assert.Contains("ModalWindowPlacement.ScheduleCenterOnOwner", File.ReadAllText(codeBehind),
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Model_identity_fallback_is_never_empty_for_a_local_file()
    {
        var path = Path.Combine(Path.GetTempPath(), "gemma-4-E2B.gguf");
        var item = new ModelProfileItemViewModel(
            new LlmModel { Id = path, Name = string.Empty },
            new ModelProfile { ModelId = path });

        Assert.Equal("gemma-4-E2B", item.EffectiveName);

        var unnamed = new ModelProfileItemViewModel(
            new LlmModel { Id = string.Empty, Name = string.Empty },
            new ModelProfile());
        Assert.Equal("selected model", unnamed.EffectiveName);
    }

    [Fact]
    public void Benchmark_unverified_detail_keeps_reasons_and_reconciliation_bounded()
    {
        var run = new BenchmarkRun
        {
            SuiteName = "RAG Answer Style",
            ModelName = "Gemma",
            RuntimeEvidence = new RuntimeEvidenceEnvelope
            {
                Status = RuntimeEvidenceStatus.Unverified,
                RequestedConfigurationStableId = "requested-configuration",
                ResolvedConfigurationStableId = "resolved-configuration",
                LaunchedConfigurationStableId = "launched-configuration",
                EffectiveConfigurationStableId = string.Empty,
                EffectiveFields = new Dictionary<string, string>
                {
                    ["context"] = "131072",
                    ["gpu_layers"] = "999"
                },
                TelemetryProcessInstanceIds = ["189803:2026-09-13T00:00:00Z"],
                Reasons = Enumerable.Range(0, 20).Select(index => $"reason-{index}").ToArray()
            }
        };

        var viewModel = new BenchmarkRunInfoViewModel(run);

        Assert.Equal(RuntimeEvidenceStatus.Unverified.ToString(), viewModel.EvidenceStatusLabel);
        Assert.Equal(12, viewModel.EvidenceReasonsLabel.Split(Environment.NewLine).Length);
        Assert.Contains("requested-config", viewModel.ReconciliationSummary, StringComparison.Ordinal);
        Assert.Contains("context=131072", viewModel.ReconciliationSummary, StringComparison.Ordinal);
    }
}
