using System.Text;

namespace Aether.Core.Services;

/// <summary>
/// Accumulates streamed LLM tokens and emits complete sentences (or runs of
/// short sentences merged up to a minimum length) so streaming speech can
/// start speaking before the full reply has finished generating. Pure,
/// stateful, and has no knowledge of playback; callers sanitize each chunk
/// (see <see cref="ChatSpeechSanitizer"/>) before speaking it.
/// </summary>
public sealed class SentenceChunker
{
    private const int MinChunkLength = 60;

    private readonly StringBuilder _buffer = new();
    private int _scanIndex;

    public IReadOnlyList<string> Append(string token)
    {
        if (string.IsNullOrEmpty(token))
            return [];

        _buffer.Append(token);
        return ScanForChunks();
    }

    /// <summary>Emits whatever text remains as a final chunk, or null if nothing is buffered.</summary>
    public string? Flush()
    {
        var remainder = _buffer.ToString().Trim();
        _buffer.Clear();
        _scanIndex = 0;
        return remainder.Length == 0 ? null : remainder;
    }

    private List<string> ScanForChunks()
    {
        var chunks = new List<string>();
        while (true)
        {
            var text = _buffer.ToString();
            var cut = FindCut(text, _scanIndex);
            if (cut is null)
            {
                _scanIndex = Math.Max(0, text.Length - 1);
                break;
            }

            var (terminatorEnd, sufficient) = cut.Value;
            if (!sufficient)
            {
                _scanIndex = terminatorEnd;
                continue;
            }

            var chunk = text[..terminatorEnd].Trim();
            if (chunk.Length > 0)
                chunks.Add(chunk);

            var remainder = text[terminatorEnd..].TrimStart();
            _buffer.Clear();
            _buffer.Append(remainder);
            _scanIndex = 0;
        }

        return chunks;
    }

    private static (int TerminatorEnd, bool Sufficient)? FindCut(string text, int fromIndex)
    {
        for (var i = Math.Max(0, fromIndex); i < text.Length - 1; i++)
        {
            var c = text[i];
            if (c != '.' && c != '!' && c != '?')
                continue;
            if (!char.IsWhiteSpace(text[i + 1]))
                continue;

            var terminatorEnd = i + 1;
            var prefixLength = text[..terminatorEnd].Trim().Length;
            return (terminatorEnd, prefixLength >= MinChunkLength);
        }

        return null;
    }
}
