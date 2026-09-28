using System.Buffers;
using System.Text;

namespace EsilvaSoft.SlopStudio.Infrastructure.Agents.ClaudeCode;

/// <summary>
/// Leitor de linhas NDJSON com limite por linha e por total. Uma linha maior que o limite é descartada por inteiro
/// sem ser acumulada (a leitura segue até o próximo <c>\n</c>), então uma saída gigante nunca esgota a memória.
/// </summary>
internal sealed class ClaudeCodeLineReader(Stream stream, int maxLineBytes, long maxTotalBytes)
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly byte[] _chunk = new byte[16 * 1024];
    private readonly ArrayBufferWriter<byte> _line = new();
    private int _chunkStart;
    private int _chunkEnd;
    private bool _discarding;
    private bool _eof;

    public long TotalBytes { get; private set; }

    public async ValueTask<LineRead> ReadAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            if (_chunkStart == _chunkEnd)
            {
                if (_eof)
                {
                    return EndOfStream();
                }

                var read = await stream.ReadAsync(_chunk, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    _eof = true;
                    return EndOfStream();
                }

                TotalBytes += read;
                if (TotalBytes > maxTotalBytes)
                {
                    return new LineRead(LineReadKind.TotalLimitExceeded, null);
                }

                _chunkStart = 0;
                _chunkEnd = read;
            }

            var span = _chunk.AsSpan(_chunkStart, _chunkEnd - _chunkStart);
            var newline = span.IndexOf((byte)'\n');
            var segment = newline < 0 ? span : span[..newline];
            _chunkStart += newline < 0 ? span.Length : newline + 1;
            if (!_discarding)
            {
                if (_line.WrittenCount + segment.Length > maxLineBytes)
                {
                    _discarding = true;
                    _line.Clear();
                }
                else
                {
                    _line.Write(segment);
                }
            }

            if (newline < 0)
            {
                continue;
            }

            if (_discarding)
            {
                _discarding = false;
                return new LineRead(LineReadKind.Oversized, null);
            }

            var line = Decode();
            if (line is null)
            {
                return new LineRead(LineReadKind.InvalidEncoding, null);
            }

            if (line.Length == 0)
            {
                continue;
            }

            return new LineRead(LineReadKind.Line, line);
        }
    }

    private LineRead EndOfStream()
    {
        // Última linha sem \n ainda é entregue uma vez.
        if (_discarding)
        {
            _discarding = false;
            return new LineRead(LineReadKind.Oversized, null);
        }

        if (_line.WrittenCount > 0)
        {
            var line = Decode();
            return line is null ? new LineRead(LineReadKind.InvalidEncoding, null)
                : line.Length == 0 ? new LineRead(LineReadKind.EndOfStream, null)
                : new LineRead(LineReadKind.Line, line);
        }

        return new LineRead(LineReadKind.EndOfStream, null);
    }

    private string? Decode()
    {
        var bytes = _line.WrittenSpan;
        if (bytes.Length > 0 && bytes[^1] == (byte)'\r')
        {
            bytes = bytes[..^1];
        }

        try
        {
            return StrictUtf8.GetString(bytes).Trim();
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
        finally
        {
            _line.Clear();
        }
    }
}

internal enum LineReadKind
{
    Line,
    Oversized,
    InvalidEncoding,
    TotalLimitExceeded,
    EndOfStream,
}

internal readonly record struct LineRead(LineReadKind Kind, string? Text);
