using System.Text.RegularExpressions;

namespace Aether.Core.Services;

/// <summary>
/// Strips markdown that should never reach a TTS provider: fenced code
/// blocks and inline code become a short spoken placeholder instead of
/// being read character by character.
/// </summary>
public static class ChatSpeechSanitizer
{
    private static readonly Regex FencedCodeBlock = new(@"```.*?```", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex InlineCode = new(@"`[^`\n]*`", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"[ \t]{2,}", RegexOptions.Compiled);
    private static readonly Regex BlankLines = new(@"\n{3,}", RegexOptions.Compiled);

    public static string Sanitize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var result = FencedCodeBlock.Replace(text, " Code block omitted. ");
        result = InlineCode.Replace(result, " code omitted ");
        result = Whitespace.Replace(result, " ");
        result = BlankLines.Replace(result, "\n\n");
        return result.Trim();
    }
}
