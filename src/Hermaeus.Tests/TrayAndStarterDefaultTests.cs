using Hermaeus.Core.Models;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>
/// Two owner-reported defaults, both cases of one setting quietly deciding two
/// things.
/// </summary>
public sealed class TrayAndStarterDefaultTests
{
    /// <summary>
    /// Closing and minimizing used to share `MinimizeToTray`, so anyone who
    /// wanted minimize-to-tray also lost the ability to close the app from its
    /// own window button, and the checkbox labelled "Minimize to tray" carried
    /// a tooltip describing what closing did.
    /// </summary>
    [Fact]
    public void Close_to_tray_is_its_own_setting_and_defaults_to_the_previous_behaviour()
    {
        var ui = new UiSettings();

        Assert.True(ui.CloseToTray, "an existing install must behave exactly as it did before the split");
        Assert.True(ui.MinimizeToTray);

        // They move independently: wanting minimize-to-tray must not force
        // close-to-tray, which was the whole complaint.
        ui.CloseToTray = false;
        Assert.True(ui.MinimizeToTray);
        Assert.False(ui.CloseToTray);
    }

    /// <summary>
    /// The low tier is recommended to machines with no GPU, which are the least
    /// likely to have a user reading model licences. Recommending a research
    /// and non-commercial model there was the wrong default.
    /// </summary>
    [Fact]
    public void The_recommended_starter_model_is_permissively_licensed_at_every_tier()
    {
        var tiers = new[]
        {
            new SystemSnapshot { Gpus = [] },
            new SystemSnapshot { Gpus = [new GpuInfo { Name = "GPU", Status = "ok", MemoryTotalBytes = 8L * 1024 * 1024 * 1024 }] },
            new SystemSnapshot { Gpus = [new GpuInfo { Name = "GPU", Status = "ok", MemoryTotalBytes = 24L * 1024 * 1024 * 1024 }] },
        };

        foreach (var snapshot in tiers)
        {
            var entry = StarterModelCatalog.Recommend(snapshot);
            Assert.True(entry.License is "MIT" or "Apache-2.0",
                $"'{entry.Id}' is recommended by default under '{entry.License}'. A default should not carry "
                + "use restrictions; offer restricted models, do not recommend them.");
        }
    }

    [Fact]
    public void The_no_gpu_recommendation_is_phi_4_mini_and_permissively_licensed()
    {
        var recommended = StarterModelCatalog.Recommend(new SystemSnapshot { Gpus = [] });

        Assert.Equal(StarterModelCatalog.Phi4Mini.Id, recommended.Id);
        Assert.Equal(StarterModelCatalog.Small.Id, recommended.Id);
        Assert.Equal("MIT", recommended.License);
        Assert.DoesNotContain(
            StarterModelCatalog.All,
            entry => entry.LicenseNote.Contains("research", StringComparison.OrdinalIgnoreCase)
                     || entry.LicenseNote.Contains("non-commercial", StringComparison.OrdinalIgnoreCase));
    }
}
