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

    [Fact]
    public void Windows_package_uses_a_fixed_target_auditable_native_launcher()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var launcherSourcePath = Path.Combine(repoRoot, "src/Hermaeus.Launcher/launcher.c");
        var launcherResourcePath = Path.Combine(repoRoot, "src/Hermaeus.Launcher/launcher.rc");
        var launcherSource = File.ReadAllText(launcherSourcePath);
        var launcherResource = File.ReadAllText(launcherResourcePath);

        Assert.Contains("GetModuleFileNameW(NULL, self_path", launcherSource, StringComparison.Ordinal);
        Assert.Contains("L\"\\\\app\\\\Hermaeus.Desktop.exe\"", launcherSource, StringComparison.Ordinal);
        Assert.Contains("GetFileAttributesW(target_path)", launcherSource, StringComparison.Ordinal);
        Assert.Contains("CreateProcessW(", launcherSource, StringComparison.Ordinal);
        Assert.Contains("target_path,", launcherSource, StringComparison.Ordinal);
        Assert.Contains("working_directory,", launcherSource, StringComparison.Ordinal);
        Assert.Contains("GetCommandLineW()", launcherSource, StringComparison.Ordinal);
        Assert.Contains("MessageBoxW(NULL, failure_message", launcherSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ShellExecute", launcherSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WinExec", launcherSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("system(", launcherSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RegOpen", launcherSource, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("../Hermaeus.Desktop/Assets/hermaeus.ico", launcherResource, StringComparison.Ordinal);
    }

    [Fact]
    public void Windows_package_layout_and_launcher_are_guarded_and_documented()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var buildScript = File.ReadAllText(Path.Combine(repoRoot, "build.ps1"));
        var packaging = File.ReadAllText(Path.Combine(repoRoot, "docs/packaging.md"));
        var userGuide = File.ReadAllText(Path.Combine(repoRoot, "docs/user-guide.md"));

        Assert.Contains("$appDir = Join-Path $packageDir \"app\"", buildScript, StringComparison.Ordinal);
        Assert.Contains("$iconDir = Join-Path $packageDir \"icons\"", buildScript, StringComparison.Ordinal);
        Assert.Contains("$localApiDir = Join-Path $appDir \"LocalApi\"", buildScript, StringComparison.Ordinal);
        Assert.Contains("Build-NativeLauncher $Runtime $launcherPath", buildScript, StringComparison.Ordinal);
        Assert.Contains("Assert-WindowsPackageLayout $packageDir", buildScript, StringComparison.Ordinal);
        Assert.Contains("\"app/Hermaeus.Desktop.exe\"", buildScript, StringComparison.Ordinal);
        Assert.Contains("\"app/LocalApi/Hermaeus.LocalApi.exe\"", buildScript, StringComparison.Ordinal);
        Assert.Contains("\"icons/hermaeus.ico\"", buildScript, StringComparison.Ordinal);
        Assert.Contains("Get-ChildItem $PackagePath -Filter \"*.pdb\" -File -Recurse", buildScript, StringComparison.Ordinal);
        Assert.Contains("Microsoft.VisualStudio.DevShell.dll", buildScript, StringComparison.Ordinal);
        Assert.Contains("Enter-VsDevShell", buildScript, StringComparison.Ordinal);
        Assert.Contains("if ($TargetRuntime -eq \"win-arm64\") { \"arm64\" } else { \"amd64\" }", buildScript, StringComparison.Ordinal);
        Assert.Contains("-Arch $targetArchitecture", buildScript, StringComparison.Ordinal);
        Assert.Contains("-HostArch \"amd64\"", buildScript, StringComparison.Ordinal);
        Assert.DoesNotContain("$env:ComSpec", buildScript, StringComparison.Ordinal);
        Assert.DoesNotContain("-no_logo", buildScript, StringComparison.Ordinal);
        Assert.DoesNotContain("Set-Content -NoNewline -Encoding ASCII (Join-Path $packageDir \"Launch-Hermaeus.cmd\")", buildScript, StringComparison.Ordinal);

        Assert.Contains("minimal open-source launcher", packaging, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("app\\Hermaeus.Desktop.exe", packaging, StringComparison.Ordinal);
        Assert.Contains("src/Hermaeus.Launcher/launcher.c", packaging, StringComparison.Ordinal);
        Assert.Contains("Hermaeus.exe", userGuide, StringComparison.Ordinal);
        Assert.Contains("app\\Hermaeus.Desktop.exe", userGuide, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string value, string fragment) =>
        value.Split(fragment, StringSplitOptions.None).Length - 1;

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
