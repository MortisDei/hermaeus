namespace Hermaeus.Rag.Pipeline;

// Preserve seeking for PdfPig rather than copying a potentially large source
// into memory. This bounds source extent, not repeated I/O or parser memory.
internal sealed class LimitedSourceStream : Stream
{
    private readonly Stream _source;
    private readonly long _maximumBytes;
    private readonly string _label;
    private readonly CancellationToken _cancellation;
    private readonly bool _leaveOpen;

    internal LimitedSourceStream(Stream source, long maximumBytes, string label,
        CancellationToken cancellation = default, bool leaveOpen = false)
    {
        if (!source.CanRead || !source.CanSeek)
            throw new ArgumentException("An ingest source must support reading and seeking.", nameof(source));
        if (maximumBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        _source = source;
        _maximumBytes = maximumBytes;
        _label = label;
        _cancellation = cancellation;
        _leaveOpen = leaveOpen;
        ValidateExtent();
    }

    private void ValidateExtent()
    {
        _cancellation.ThrowIfCancellationRequested();
        if (_source.Length > _maximumBytes || _source.Position > _maximumBytes)
            throw new InvalidOperationException($"Source exceeds the {_maximumBytes} byte ingest limit: {_label}");
    }

    private int ReadSize(int requested)
    {
        ValidateExtent();
        var remaining = _maximumBytes - _source.Position;
        return remaining >= requested ? requested : (int)remaining + 1;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var read = _source.Read(buffer[..ReadSize(buffer.Length)]);
        ValidateExtent();
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var read = await _source.ReadAsync(buffer[..ReadSize(buffer.Length)], cancellationToken);
        ValidateExtent();
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override long Seek(long offset, SeekOrigin origin)
    {
        ValidateExtent();
        var position = _source.Seek(offset, origin);
        ValidateExtent();
        return position;
    }

    public override long Length { get { ValidateExtent(); return _source.Length; } }
    public override long Position
    {
        get { ValidateExtent(); return _source.Position; }
        set => Seek(value, SeekOrigin.Begin);
    }
    public override bool CanRead => _source.CanRead;
    public override bool CanSeek => _source.CanSeek;
    public override bool CanWrite => false;
    public override void Flush() => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_leaveOpen)
            _source.Dispose();
        base.Dispose(disposing);
    }
}
