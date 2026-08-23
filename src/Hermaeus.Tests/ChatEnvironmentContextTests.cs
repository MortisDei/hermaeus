using Hermaeus.ViewModels;
using Xunit;

namespace Hermaeus.Tests;

public sealed class ChatEnvironmentContextTests
{
    [Fact]
    public void Disabled_capabilities_are_not_claimed()
    {
        var context = ChatEnvironmentContext.Build(new ChatEnvironmentCapabilities(
            "Local Model", IsRemote: false, AcceptsImages: false, ReadyAttachmentCount: 0,
            KnowledgeDatasetName: string.Empty, MemoryContextEnabled: false, RecallContextEnabled: false));

        Assert.Contains("local runtime", context);
        Assert.DoesNotContain("image attachments", context);
        Assert.DoesNotContain("saved-memory context", context);
        Assert.DoesNotContain("Knowledge dataset \"", context);
        Assert.Contains("Unavailable here: web access, shell commands, tool calls, and Agent workspace actions", context);
    }

    [Fact]
    public void Enabled_capabilities_are_described_from_supplied_state()
    {
        var context = ChatEnvironmentContext.Build(new ChatEnvironmentCapabilities(
            "Remote Model", IsRemote: true, AcceptsImages: true, ReadyAttachmentCount: 2,
            KnowledgeDatasetName: "Manuals", MemoryContextEnabled: true, RecallContextEnabled: true));

        Assert.Contains("remote provider", context);
        Assert.Contains("image attachments", context);
        Assert.Contains("Knowledge dataset \"Manuals\"", context);
        Assert.Contains("saved-memory context", context);
        Assert.Contains("Recall context", context);
        Assert.Contains("2 ready attachment(s)", context);
    }
}
