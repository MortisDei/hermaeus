using Hermaeus.Services;
using Hermaeus.Desktop;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

public sealed class ReleaseReadinessRegressionTests
{
    [Fact]
    public async Task Download_progress_is_coalesced_and_still_reports_completion()
    {
        using var temp = new TempDir();
        var content = new string('x', 1024 * 1024);
        var service = new ModelDownloadService(
            new HttpClient(new CapturingRangeHttpHandler(content)));
        var reports = new List<DownloadProgress>();

        var result = await service.DownloadAsync(
            "https://example.test/model.bin",
            temp.PathFor("model.bin"),
            new InlineProgress<DownloadProgress>(reports.Add));

        Assert.True(result.Success, result.Message);
        Assert.InRange(reports.Count, 1, 5);
        Assert.Equal(100, reports[^1].PercentComplete, precision: 5);
    }

    [Fact]
    public void Different_release_identifier_schemes_are_incomparable()
    {
        Assert.Equal(
            LlamaVersionComparison.Incomparable,
            DoctorService.CompareLlamaBuilds(installedBuild: 10034, latestBuild: null));
    }

    [Fact]
    public void Comparable_llama_builds_can_be_classified()
    {
        Assert.Equal(LlamaVersionComparison.Outdated, DoctorService.CompareLlamaBuilds(10034, 10035));
        Assert.Equal(LlamaVersionComparison.Current, DoctorService.CompareLlamaBuilds(10034, 10034));
        Assert.Equal(LlamaVersionComparison.Current, DoctorService.CompareLlamaBuilds(10035, 10034));
    }

    [Fact]
    public void Linux_package_uses_a_native_launcher_and_consistent_desktop_identity()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var buildScript = File.ReadAllText(Path.Combine(repoRoot, "build.sh"));

        Assert.Contains("cp \"$ROOT_DIR/src/Hermaeus.Desktop/Assets/hermaeus-app.png\" \"$ICON_DIR/hermaeus-app.png\"", buildScript, StringComparison.Ordinal);
        Assert.Contains("PNG_ICON_DIR=\"$DATA_HOME/icons/hicolor/512x512/apps\"", buildScript, StringComparison.Ordinal);
        Assert.Contains("PNG_ICON_FILE=\"$PNG_ICON_DIR/hermaeus.png\"", buildScript, StringComparison.Ordinal);
        Assert.Contains("cp \"$INSTALL_DIR/icons/hermaeus-app.png\" \"$PNG_ICON_FILE\"", buildScript, StringComparison.Ordinal);
        Assert.Contains("PNG_ICON_FILE=\"$DATA_HOME/icons/hicolor/512x512/apps/hermaeus.png\"", buildScript, StringComparison.Ordinal);
        Assert.Contains("mv \"$APP_DIR/Hermaeus.Desktop\" \"$APP_DIR/hermaeus-app\"", buildScript, StringComparison.Ordinal);
        Assert.Contains("ln -s \"app/hermaeus-app\" \"$PACKAGE_DIR/Hermaeus\"", buildScript, StringComparison.Ordinal);
        Assert.Contains("cp \"$APP_DIR/hermaeus-app\" \"$APP_DIR/install-hermaeus\"", buildScript, StringComparison.Ordinal);
        Assert.Contains("cp \"$APP_DIR/hermaeus-app\" \"$APP_DIR/uninstall-hermaeus\"", buildScript, StringComparison.Ordinal);
        Assert.Contains("ln -s \"app/install-hermaeus\" \"$PACKAGE_DIR/Install Hermaeus\"", buildScript, StringComparison.Ordinal);
        Assert.Contains("ln -s \"app/uninstall-hermaeus\" \"$PACKAGE_DIR/Uninstall Hermaeus\"", buildScript, StringComparison.Ordinal);
        Assert.Contains("cat > \"$INTEGRATION_DIR/install-desktop.sh\"", buildScript, StringComparison.Ordinal);
        Assert.Contains("cat > \"$INTEGRATION_DIR/uninstall-desktop.sh\"", buildScript, StringComparison.Ordinal);
        Assert.DoesNotContain("$PACKAGE_DIR/install-desktop.sh", buildScript, StringComparison.Ordinal);
        Assert.DoesNotContain("$PACKAGE_DIR/uninstall-desktop.sh", buildScript, StringComparison.Ordinal);
        Assert.Contains("INTERNAL_EXEC=\"$SOURCE_DIR/app/hermaeus-app\"", buildScript, StringComparison.Ordinal);
        Assert.Contains("Exec=$INSTALL_DIR/Hermaeus", buildScript, StringComparison.Ordinal);
        Assert.DoesNotContain("cat > \"$PACKAGE_DIR/Hermaeus\"", buildScript, StringComparison.Ordinal);
        Assert.DoesNotContain("$PACKAGE_DIR/hermaeus.desktop", buildScript, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(buildScript, "Icon=hermaeus"));
        Assert.Equal(1, CountOccurrences(buildScript, "StartupWMClass=hermaeus"));
        Assert.Equal(1, CountOccurrences(buildScript, "StartupNotify=true"));
        Assert.Contains("rm -f \"$DESKTOP_FILE\" \"$PNG_ICON_FILE\"", buildScript, StringComparison.Ordinal);

        var program = File.ReadAllText(Path.Combine(repoRoot, "src/Hermaeus.Desktop/Program.cs"));
        var mainWindow = File.ReadAllText(Path.Combine(repoRoot, "src/Hermaeus.Desktop/Views/MainWindow.axaml"));
        Assert.Contains("WmClass = \"hermaeus\"", program, StringComparison.Ordinal);
        Assert.Contains("Icon=\"/Assets/hermaeus-app.png\"", mainWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void Linux_package_integration_actions_resolve_only_for_internal_native_launchers()
    {
        var packageRoot = Path.Combine(Path.GetTempPath(), "hermaeus-package");
        var appDirectory = Path.Combine(packageRoot, "app");

        var install = PackageIntegrationAction.Resolve(Path.Combine(appDirectory, "install-hermaeus"));
        var uninstall = PackageIntegrationAction.Resolve(Path.Combine(appDirectory, "uninstall-hermaeus"));

        Assert.NotNull(install);
        Assert.Equal(PackageIntegrationActionKind.Install, install.Action);
        Assert.Equal(packageRoot, install.PackageRoot);
        Assert.Equal(Path.Combine(appDirectory, "integration", "install-desktop.sh"), install.ScriptPath);
        Assert.NotNull(uninstall);
        Assert.Equal(PackageIntegrationActionKind.Uninstall, uninstall.Action);
        Assert.Null(PackageIntegrationAction.Resolve(Path.Combine(appDirectory, "hermaeus-app")));
        Assert.Null(PackageIntegrationAction.Resolve(Path.Combine(packageRoot, "install-hermaeus")));
    }

    private static int CountOccurrences(string value, string fragment) =>
        value.Split(fragment, StringSplitOptions.None).Length - 1;

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
