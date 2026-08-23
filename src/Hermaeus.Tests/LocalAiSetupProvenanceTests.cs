using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class LocalAiSetupProvenanceTests
{
    [Fact]
    public void Hermaeus_managed_hugging_face_downloads_keep_their_repo_identity()
    {
        var repo = LocalAiSetupService.TryGetHuggingFaceRepoId(LocalAiSetupService.Phi4ModelUrl);

        Assert.Equal("bartowski/microsoft_Phi-4-mini-reasoning-GGUF", repo);
    }

    [Fact]
    public void Non_hugging_face_urls_do_not_claim_hugging_face_provenance()
    {
        Assert.Null(LocalAiSetupService.TryGetHuggingFaceRepoId("https://example.test/model.gguf"));
    }
}
