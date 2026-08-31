using Xunit;

namespace Hermaeus.Tests;

/// <summary>
/// R32 CI topology is part of the required-check contract. These guards keep
/// the authoritative job names out of branch-only pushes, where a skipped
/// required job would otherwise look successful to branch protection.
/// </summary>
public sealed class CiWorkflowGuardTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    private static string RequiredWorkflow() =>
        File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "ci.yml"));

    private static string BranchWorkflow() =>
        File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "branch-ci.yml"));

    [Fact]
    public void Required_checks_only_run_for_pull_requests_and_main_pushes()
    {
        var workflow = RequiredWorkflow();

        Assert.Contains("branches: [main]", workflow, StringComparison.Ordinal);
        Assert.Contains("pull_request:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("r*/round", workflow, StringComparison.Ordinal);
        Assert.Contains("build-and-test:", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Branch_workflow_uses_distinct_non_required_check_names()
    {
        var workflow = BranchWorkflow();

        Assert.Contains("branches: ['r*/round']", workflow, StringComparison.Ordinal);
        Assert.Contains("name: branch-build-and-test (${{ matrix.os }})", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("name: build-and-test", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("build-and-test:", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Branch_workflow_checks_the_exact_same_repository_pull_request()
    {
        var workflow = BranchWorkflow();

        Assert.Contains("pull-requests: read", workflow, StringComparison.Ordinal);
        Assert.Contains("-f base=main", workflow, StringComparison.Ordinal);
        Assert.Contains("head=${REPOSITORY_OWNER}:${BRANCH_NAME}", workflow, StringComparison.Ordinal);
        Assert.Contains("if: needs.find_open_pr.outputs.has_open_pr == 'false'", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Concurrency_groups_cannot_cross_branch_pr_or_main_authority()
    {
        var required = RequiredWorkflow();
        var branch = BranchWorkflow();

        Assert.Contains("ci-pr-{0}", required, StringComparison.Ordinal);
        Assert.Contains("'ci-main'", required, StringComparison.Ordinal);
        Assert.Contains("cancel-in-progress: ${{ github.event_name == 'pull_request' }}", required, StringComparison.Ordinal);
        Assert.Contains("group: branch-ci-${{ github.ref }}", branch, StringComparison.Ordinal);
        Assert.Contains("cancel-in-progress: true", branch, StringComparison.Ordinal);
    }

    [Fact]
    public void Defender_exclusion_stays_on_trusted_push_workflows_only()
    {
        var required = RequiredWorkflow();
        var branch = BranchWorkflow();

        Assert.Contains("github.event_name == 'push'", required, StringComparison.Ordinal);
        Assert.Contains("if: runner.os == 'Windows'", branch, StringComparison.Ordinal);
        Assert.DoesNotContain("pull_request", branch, StringComparison.Ordinal);
    }
}
