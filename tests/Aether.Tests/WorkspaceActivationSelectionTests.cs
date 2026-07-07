using Aether.Agent.Models;
using Xunit;

namespace Aether.Tests;

public sealed class WorkspaceActivationSelectionTests
{
    private sealed record Candidate(string Id);

    [Fact]
    public void ResolvePreferredModel_returns_the_matching_candidate()
    {
        var activation = new WorkspaceActivation("model-b", null, null, [], FromManifest: true);
        var candidates = new[] { new Candidate("model-a"), new Candidate("model-b") };

        var resolved = activation.ResolvePreferredModel(candidates, c => c.Id);

        Assert.Equal("model-b", resolved?.Id);
    }

    [Fact]
    public void ResolvePreferredModel_returns_null_when_no_preference_is_set()
    {
        var activation = new WorkspaceActivation(null, null, null, [], FromManifest: false);
        var candidates = new[] { new Candidate("model-a") };

        Assert.Null(activation.ResolvePreferredModel(candidates, c => c.Id));
    }

    [Fact]
    public void ResolveLinkedDataset_returns_the_matching_candidate()
    {
        var activation = new WorkspaceActivation(null, null, "dataset-1", [], FromManifest: true);
        var candidates = new[] { new Candidate("dataset-1"), new Candidate("dataset-2") };

        var resolved = activation.ResolveLinkedDataset(candidates, c => c.Id);

        Assert.Equal("dataset-1", resolved?.Id);
    }
}
