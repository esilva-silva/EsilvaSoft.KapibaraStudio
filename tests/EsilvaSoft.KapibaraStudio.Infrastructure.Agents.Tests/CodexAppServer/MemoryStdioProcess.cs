using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using EsilvaSoft.KapibaraStudio.Application.Agents;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Tests.CodexAppServer;

/// <summary>Controlled duplex endpoint; no process, environment or filesystem is acquired.</summary>
internal sealed class MemoryStdioProcess : IAgentStdioProcess
{
    private readonly InputStream _output = new();
    private readonly InputWriter _input = new();
    public int Id => 123;
    public Stream StandardOutput => _output;
    public TextWriter StandardInput => _input;
    public int KillCount { get; private set; }
    public int DisposeCount { get; private set; }
    public Exception? WriteFailure { get => _input.Failure; set => _input.Failure = value; }
    public TaskCompletionSource? WriteEntered { get => _input.Entered; set => _input.Entered = value; }
    public TaskCompletionSource? ReleaseWrite { get => _input.Release; set => _input.Release = value; }

    public void Send(string frame) => SendBytes(Encoding.UTF8.GetBytes(frame + "\n"));
    public void SendBytes(byte[] bytes) => _output.Chunks.Writer.TryWrite(bytes);
    public void CloseOutput() => _output.Chunks.Writer.TryComplete();
    public async Task<JsonElement> NextWrittenAsync()
    {
        var text = await _input.Frames.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
    public void KillTree() { KillCount++; CloseOutput(); }
    public ValueTask DisposeAsync() { DisposeCount++; CloseOutput(); return ValueTask.CompletedTask; }

    private sealed class InputWriter : TextWriter
    {
        private readonly StringBuilder _buffer = new();
        public readonly Channel<string> Frames = Channel.CreateUnbounded<string>();
        public Exception? Failure;
        public TaskCompletionSource? Entered;
        public TaskCompletionSource? Release;
        public override Encoding Encoding => Encoding.UTF8;
        public override async Task WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Entered?.TrySetResult();
            if (Release is { } release) await release.Task.WaitAsync(cancellationToken);
            if (Failure is { } failure) throw failure;
            _buffer.Append(buffer.Span);
        }
        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Frames.Writer.TryWrite(_buffer.ToString());
            _buffer.Clear();
            return Task.CompletedTask;
        }
    }

    private sealed class InputStream : Stream
    {
        public readonly Channel<byte[]> Chunks = Channel.CreateUnbounded<byte[]>();
        private ReadOnlyMemory<byte> _remaining;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.Length == 0) return 0;
            while (_remaining.IsEmpty)
            {
                try { _remaining = await Chunks.Reader.ReadAsync(cancellationToken); }
                catch (ChannelClosedException) { return 0; }
            }
            var count = Math.Min(buffer.Length, _remaining.Length);
            _remaining[..count].CopyTo(buffer);
            _remaining = _remaining[count..];
            return count;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
