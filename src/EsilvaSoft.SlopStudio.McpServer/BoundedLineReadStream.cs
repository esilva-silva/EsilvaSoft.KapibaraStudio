namespace EsilvaSoft.SlopStudio.McpServer;

/// <summary>
/// Read-only wrapper of stdin that ends the input (EOF) when a single JSON-RPC line exceeds the limit, so an oversized
/// message can never be buffered by the transport. <see cref="LimitExceeded"/> lets the proxy report it on stderr.
/// </summary>
internal sealed class BoundedLineReadStream(Stream inner, int maximumLineBytes) : Stream
{
    private int _currentLine;

    public bool LimitExceeded { get; private set; }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => Account(inner.Read(buffer, offset, count), buffer.AsSpan(offset));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (LimitExceeded) return 0;
        var read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        return Account(read, buffer.Span);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    private int Account(int read, ReadOnlySpan<byte> data)
    {
        if (LimitExceeded) return 0;
        for (var index = 0; index < read; index++)
        {
            if (data[index] == (byte)'\n')
            {
                _currentLine = 0;
                continue;
            }
            if (++_currentLine > maximumLineBytes)
            {
                LimitExceeded = true;
                return 0;
            }
        }
        return read;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }
}
