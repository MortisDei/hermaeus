using Hermaeus.Core.Models;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class RefusalEvaluatorTests
{
    [Theory]
    [InlineData("I cannot determine the serial number from the provided context.", "direct-refusal")]
    [InlineData("The provided context does not contain the serial number.", "indirect-refusal")]
    [InlineData("I cannot verify that with certainty from the available evidence.", "hedged-refusal")]
    [InlineData("Which device do you mean? Please provide its model or source record.", "clarification-request")]
    public void Valid_refusal_forms_are_accepted(string answer, string classification)
    {
        var evaluation = RefusalEvaluator.Evaluate(answer, shouldRefuse: true);

        Assert.True(evaluation.IsCorrect);
        Assert.True(evaluation.DetectedRefusal);
        Assert.Equal(classification, evaluation.Classification);
        Assert.Equal(RefusalEvaluator.CurrentVersion, evaluation.EvaluatorVersion);
    }

    [Theory]
    [InlineData("I cannot verify this, but it is 73%.", "mixed-answer")]
    [InlineData("The answer is 73%.", "hallucinated-answer")]
    public void Hallucinated_or_mixed_answers_do_not_pass_as_refusals(string answer, string classification)
    {
        var evaluation = RefusalEvaluator.Evaluate(answer, shouldRefuse: true);

        Assert.Equal(classification, evaluation.Classification);
        Assert.False(evaluation.IsCorrect);
    }

    [Fact]
    public void Ordinary_continuation_is_not_mistaken_for_a_refusal()
    {
        var evaluation = RefusalEvaluator.Evaluate(
            "The answer is available in the supplied record and the status is ready.", shouldRefuse: true);

        Assert.Equal("hallucinated-answer", evaluation.Classification);
        Assert.False(evaluation.DetectedRefusal);
        Assert.False(evaluation.IsCorrect);
    }

    [Fact]
    public void Benchmark_result_persists_refusal_classification_and_detail()
    {
        var result = BenchmarkService.ScoreDeterministic(
            new BenchmarkCase { ShouldRefuse = true },
            "The provided context does not contain the requested value.");

        Assert.True(result.Passed);
        Assert.Equal("indirect-refusal", result.RefusalAssessment);
        Assert.NotEmpty(result.RefusalDetail);
    }
}
