using Hermaeus.Core.Models;
using Xunit;

namespace Hermaeus.Tests;

public sealed class LocalAiSetupApprovalTests
{
    [Fact]
    public void Approval_stays_disabled_until_the_required_plan_is_reviewed()
    {
        var action = new LocalAiSetupAction(
            "download-runtime",
            LocalAiSetupActionKind.DownloadLlamaServer,
            "Download runtime",
            "/tmp/runtime",
            ["https://example.test/runtime"],
            LocalAiSetupRiskLevel.Medium,
            "Installs the runtime.",
            RequiresNetwork: true,
            RequiresApproval: true,
            CanRun: true);

        Assert.False(action.CanApprove);
        Assert.True((action with { PlanReviewed = true }).CanApprove);
    }

    [Fact]
    public void An_unrunnable_action_cannot_be_approved_even_after_review()
    {
        var action = new LocalAiSetupAction(
            "blocked",
            LocalAiSetupActionKind.InstallXttsDependencies,
            "Blocked",
            "/tmp/runtime",
            [],
            LocalAiSetupRiskLevel.High,
            "Needs another prerequisite.",
            RequiresNetwork: true,
            RequiresApproval: true,
            CanRun: false)
        {
            PlanReviewed = true
        };

        Assert.False(action.CanApprove);
    }
}
