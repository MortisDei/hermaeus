using Hermaeus.Core.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class ChatSpeechSanitizerTests
{
    [Fact]
    public void Sanitize_replaces_fenced_code_blocks_with_placeholder()
    {
        var result = ChatSpeechSanitizer.Sanitize("Here is code:\n```csharp\nvar x = 1;\n```\nDone.");

        Assert.DoesNotContain("var x", result);
        Assert.DoesNotContain("```", result);
        Assert.Contains("Code block omitted", result);
        Assert.Contains("Done.", result);
    }

    [Fact]
    public void Sanitize_replaces_inline_code_with_placeholder()
    {
        var result = ChatSpeechSanitizer.Sanitize("Run `dotnet build` to compile.");

        Assert.DoesNotContain("`", result);
        Assert.DoesNotContain("dotnet build", result);
        Assert.Contains("code omitted", result);
    }

    [Fact]
    public void Sanitize_returns_empty_string_for_blank_input()
    {
        Assert.Equal(string.Empty, ChatSpeechSanitizer.Sanitize("   "));
        Assert.Equal(string.Empty, ChatSpeechSanitizer.Sanitize(string.Empty));
    }

    [Fact]
    public void Sanitize_collapses_repeated_horizontal_whitespace()
    {
        Assert.Equal("Hello world", ChatSpeechSanitizer.Sanitize("Hello    world"));
    }

    [Fact]
    public void Sanitize_leaves_plain_prose_unchanged()
    {
        Assert.Equal("This is a normal sentence.", ChatSpeechSanitizer.Sanitize("This is a normal sentence."));
    }
}
