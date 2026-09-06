using Hermaeus.Rag.Models;
using Hermaeus.Rag.Retrieval;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class RerankerBatchExperimentTests
{
    [Fact]
    public void Asset_identity_changes_when_the_selected_asset_set_changes()
    {
        using var temp = new TempDir();
        var firstModel = temp.PathFor("first/model.onnx");
        var firstVocab = temp.PathFor("first/vocab.txt");
        var secondModel = temp.PathFor("second/model.onnx");
        var secondVocab = temp.PathFor("second/vocab.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(firstModel)!);
        Directory.CreateDirectory(Path.GetDirectoryName(secondModel)!);
        File.WriteAllText(firstModel, "first");
        File.WriteAllText(firstVocab, "first");
        File.WriteAllText(secondModel, "second");
        File.WriteAllText(secondVocab, "second");

        var first = OnnxCrossEncoderReranker.CreateAssetIdentityKey(firstModel, firstVocab);
        var second = OnnxCrossEncoderReranker.CreateAssetIdentityKey(secondModel, secondVocab);

        Assert.NotEqual(first, second);
        Assert.False(OnnxCrossEncoderReranker.ShouldAttemptAssetLoad(first, first, null));
        Assert.False(OnnxCrossEncoderReranker.ShouldAttemptAssetLoad(first, null, first));
        Assert.True(OnnxCrossEncoderReranker.ShouldAttemptAssetLoad(second, first, first));
    }

    [Fact]
    public async Task Batch_experiment_reports_unknown_without_verified_pinned_assets()
    {
        using var temp = new TempDir();
        var settings = new SettingsService(temp.PathFor("settings.json"));
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        settings.Settings.Rag.RerankerModelPath = temp.PathFor("missing-reranker");
        using var reranker = new OnnxCrossEncoderReranker(settings);

        var candidates = new[]
        {
            Candidate("one", "one passage"),
            Candidate("two", "two passage")
        };

        var result = await reranker.RunBatchExperimentAsync("query", candidates);

        Assert.Equal(Hermaeus.Core.Models.CapabilityState.Unknown, result.State);
        Assert.Equal("reranker-batch-assets-unknown", result.EvidenceCode);
        Assert.Null(result.ScoreOrderEquivalent);
    }

    [Fact]
    public async Task Batch_experiment_rejects_unbounded_batch_sizes_before_loading_assets()
    {
        using var temp = new TempDir();
        var settings = new SettingsService(temp.PathFor("settings.json"));
        using var reranker = new OnnxCrossEncoderReranker(settings);

        var result = await reranker.RunBatchExperimentAsync(
            "query",
            [Candidate("one", "one"), Candidate("two", "two")],
            batchSize: OnnxCrossEncoderReranker.MaximumExperimentBatchSize + 1);

        Assert.Equal(Hermaeus.Core.Models.CapabilityState.Unavailable, result.State);
        Assert.Equal("reranker-batch-bounds", result.EvidenceCode);
    }

    private static ScoredChunk Candidate(string id, string content) =>
        new(new RagChunk { Id = id, Content = content }, 0.5f, ScoreSource.Semantic);
}
