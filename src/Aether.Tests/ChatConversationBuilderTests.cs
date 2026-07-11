using Aether.Core.Services;
using Xunit;

namespace Aether.Tests;

public sealed class ChatConversationBuilderTests
{
    [Fact]
    public void AutoTitleFrom_collapses_newlines_and_trims()
    {
        Assert.Equal("hello world", ChatConversationBuilder.AutoTitleFrom("hello\nworld  "));
    }

    [Fact]
    public void AutoTitleFrom_truncates_long_content_with_an_ellipsis()
    {
        var content = new string('a', 100);
        var title = ChatConversationBuilder.AutoTitleFrom(content);

        Assert.Equal(60, title.Length);
        Assert.EndsWith("...", title);
    }
}
