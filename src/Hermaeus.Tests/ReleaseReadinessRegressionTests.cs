using Hermaeus.Services;
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
    public void Linux_desktop_install_uses_the_canonical_icon_and_uninstall_removes_it()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var buildScript = File.ReadAllText(Path.Combine(repoRoot, "build.sh"));

        Assert.Contains("cp \"$ROOT_DIR/src/Hermaeus.Desktop/Assets/hermaeus-app.png\" \"$ICON_DIR/hermaeus-app.png\"", buildScript, StringComparison.Ordinal);
        Assert.Contains("PNG_ICON_DIR=\"$DATA_HOME/icons/hicolor/512x512/apps\"", buildScript, StringComparison.Ordinal);
        Assert.Contains("PNG_ICON_FILE=\"$PNG_ICON_DIR/hermaeus.png\"", buildScript, StringComparison.Ordinal);
        Assert.Contains("cp \"$INSTALL_DIR/icons/hermaeus-app.png\" \"$PNG_ICON_FILE\"", buildScript, StringComparison.Ordinal);
        Assert.Contains("PNG_ICON_FILE=\"$DATA_HOME/icons/hicolor/512x512/apps/hermaeus.png\"", buildScript, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(buildScript, "Icon=hermaeus"));
        Assert.Equal(2, CountOccurrences(buildScript, "StartupWMClass=hermaeus"));
        Assert.Equal(2, CountOccurrences(buildScript, "StartupNotify=true"));
        Assert.Contains("Exec=sh -c \"exec \\\"\\\\${1%%/*}/Hermaeus\\\"\" sh %k", buildScript, StringComparison.Ordinal);
        Assert.Contains("rm -f \"$DESKTOP_FILE\" \"$PNG_ICON_FILE\"", buildScript, StringComparison.Ordinal);

        var program = File.ReadAllText(Path.Combine(repoRoot, "src/Hermaeus.Desktop/Program.cs"));
        var mainWindow = File.ReadAllText(Path.Combine(repoRoot, "src/Hermaeus.Desktop/Views/MainWindow.axaml"));
        Assert.Contains("WmClass = \"hermaeus\"", program, StringComparison.Ordinal);
        Assert.Contains("Icon=\"/Assets/hermaeus-app.png\"", mainWindow, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string value, string fragment) =>
        value.Split(fragment, StringSplitOptions.None).Length - 1;

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
