using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class ProbeCancellationContractTests
{
    [Fact]
    public async Task Pre_cancelled_help_probe_returns_unavailable_without_starting_or_validating_a_command()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Null(await LocalModelCapabilityService.RunHelpCommandAsync(
            "not-a-probe-executable", [], cancelled.Token, TimeSpan.FromMilliseconds(-2)));
    }

    [Fact]
    public async Task Pre_cancelled_system_probe_returns_empty_without_starting_or_validating_a_command()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Empty(await SystemInfoService.RunCommandAsync(
            "not-a-probe-executable", [], cancelled.Token, TimeSpan.FromMilliseconds(-2)));
    }
}
