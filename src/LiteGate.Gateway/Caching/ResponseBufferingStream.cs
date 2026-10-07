namespace LiteGate.Gateway.Caching;

/// <summary>
/// Pass-through stream that additionally buffers the response body in memory (up to a limit),
/// so a cacheable GET response can be stored once the pipeline has completed.
/// </summary>
public sealed class ResponseBufferingStream : Stream
{
    private readonly Stream _inner;
    private readonly long _maxBytes;
    private MemoryStream? _buffer = new();

    public ResponseBufferingStream(Stream inner, long maxBytes)
    {
        _inner = inner;
        _maxBytes = maxBytes;
    }

    /// <summary>False once the body exceeded the buffering limit and the response became uncacheable.</summary>
    public bool CacheCandidate => _buffer is not null;

    public byte[] GetBufferedBytes() => _buffer?.ToArray() ?? [];

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => _inner.CanWrite;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Flush() => _inner.Flush();

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        await _inner.FlushAsync(cancellationToken);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        _inner.Write(buffer, offset, count);
        Capture(buffer.AsSpan(offset, count));
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await _inner.WriteAsync(buffer, cancellationToken);
        Capture(buffer.Span);
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        await _inner.WriteAsync(buffer, offset, count, cancellationToken);
        Capture(buffer.AsSpan(offset, count));
    }

    private void Capture(ReadOnlySpan<byte> chunk)
    {
        if (_buffer is not { } target)
        {
            return;
        }

        if (target.Length + chunk.Length > _maxBytes)
        {
            _buffer = null;
            target.Dispose();
            return;
        }

        target.Write(chunk);
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _buffer?.Dispose();
            _buffer = null;
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        _buffer?.Dispose();
        _buffer = null;
        await base.DisposeAsync();
    }
}
