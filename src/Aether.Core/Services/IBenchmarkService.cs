using Aether.Core.Models;

namespace Aether.Core.Services;

public interface IBenchmarkService
{
    Task InitializeAsync(CancellationToken ct = default);
    Task<IReadOnlyList<BenchmarkSuite>> GetSuitesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<BenchmarkRun>> GetRunsAsync(CancellationToken ct = default);
    Task SaveSuiteAsync(BenchmarkSuite suite, CancellationToken ct = default);
    Task DeleteRunAsync(string runId, CancellationToken ct = default);
    Task<BenchmarkRun?> GetRunAsync(string runId, CancellationToken ct = default);
    Task<BenchmarkRun> RunAsync(
        BenchmarkSuite suite,
        LlmModel model,
        IProgress<string>? progress = null,
        CancellationToken ct = default);
    Task<BenchmarkRun> RerunAsync(
        string runId,
        IProgress<string>? progress = null,
        CancellationToken ct = default);
    Task<string> ExportAsync(string runId, string targetDirectory, CancellationToken ct = default);
    IReadOnlyList<BenchmarkRun> Rank(IEnumerable<BenchmarkRun> runs);
}
