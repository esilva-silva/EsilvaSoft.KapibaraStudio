using System.Text;

namespace EsilvaSoft.SlopStudio.Spikes.McpTests;

// Captura somente o tráfego sintético destas fixtures sobre pipes reais do subprocesso.
internal sealed class RecordingStream(Stream inner) : Stream
{
    private readonly MemoryStream capture = new();
    public string CapturedText { get { lock (capture) return Encoding.UTF8.GetString(capture.ToArray()); } }
    public override bool CanRead => inner.CanRead;
    public override bool CanWrite => inner.CanWrite;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken token) => inner.FlushAsync(token);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
    {
        int count = await inner.ReadAsync(buffer, token);
        lock (capture) capture.Write(buffer.Span[..count]);
        return count;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) =>
        await ReadAsync(buffer.AsMemory(offset, count), token);

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
    {
        await inner.WriteAsync(buffer, token);
        lock (capture) capture.Write(buffer.Span);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token) =>
        WriteAsync(buffer.AsMemory(offset, count), token).AsTask();

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }
}
