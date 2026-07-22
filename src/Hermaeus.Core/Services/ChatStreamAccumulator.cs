using System.Diagnostics;
using System.Text;

namespace Hermaeus.Core.Services;

/// <summary>
/// Batches streamed content deltas so the UI repaints on a throttle instead
/// of once per token. Extracted from ChatViewModel's local AppendStreamToken
/// closure so the batching threshold is independently testable.
/// </summary>
public sealed class ChatStreamAccumulator
{
    private readonly StringBuilder _buffer = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly int _flushIntervalMs;
    private readonly int _flushSizeThreshold;

    public ChatStreamAccumulator(int flushIntervalMs = 50, int flushSizeThreshold = 256)
    {
        _flushIntervalMs = flushIntervalMs;
        _flushSizeThreshold = flushSizeThreshold;
    }

    public int RenderBatches { get; private set; }

    public bool TryAppend(string token, bool force, out string flushed)
    {
        _buffer.Append(token);
        if (!force && _clock.ElapsedMilliseconds < _flushIntervalMs && _buffer.Length < _flushSizeThreshold)
        {
            flushed = string.Empty;
            return false;
        }

        if (_buffer.Length == 0)
        {
            flushed = string.Empty;
            return false;
        }

        flushed = _buffer.ToString();
        RenderBatches++;
        _buffer.Clear();
        _clock.Restart();
        return true;
    }
}
