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

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
