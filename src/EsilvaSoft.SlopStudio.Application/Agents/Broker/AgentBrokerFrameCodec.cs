using System.Buffers.Binary;
using System.Text.Json;

namespace EsilvaSoft.SlopStudio.Application.Agents.Broker;

/// <summary>
/// Length-prefixed JSON framing. The size is validated before any payload byte is buffered, so a hostile peer cannot
/// make the reader allocate beyond the per-direction ceiling. EOF exactly at a frame boundary is a clean close; any
/// other truncation is a protocol failure.
/// </summary>
public static class AgentBrokerFrameCodec
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = 64
    };

    /// <returns>The message, or <see langword="null"/> when the peer closed the stream at a frame boundary.</returns>
    /// <exception cref="AgentBrokerProtocolException">Oversized, truncated or malformed frame.</exception>
    public static async Task<AgentBrokerMessage?> ReadAsync(Stream stream, int maximumFrameBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = new byte[4];
        var headerRead = await ReadExactlyOrEofAsync(stream, header, cancellationToken).ConfigureAwait(false);
        if (headerRead == 0) return null;
        if (headerRead != header.Length)
            throw new AgentBrokerProtocolException(AgentBrokerProtocol.ErrorCodes.ProtocolViolation);
        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length <= 0 || length > maximumFrameBytes)
            throw new AgentBrokerProtocolException(AgentBrokerProtocol.ErrorCodes.ProtocolViolation);
        var payload = new byte[length];
        if (await ReadExactlyOrEofAsync(stream, payload, cancellationToken).ConfigureAwait(false) != length)
            throw new AgentBrokerProtocolException(AgentBrokerProtocol.ErrorCodes.ProtocolViolation);
        try
        {
            return JsonSerializer.Deserialize<AgentBrokerMessage>(payload, SerializerOptions) is { Type: { Length: > 0 } } message
                ? message
                : throw new AgentBrokerProtocolException(AgentBrokerProtocol.ErrorCodes.ProtocolViolation);
        }
        catch (JsonException)
        {
            throw new AgentBrokerProtocolException(AgentBrokerProtocol.ErrorCodes.ProtocolViolation);
        }
    }

    /// <exception cref="AgentBrokerProtocolException">The serialized message exceeds the ceiling of its direction.</exception>
    public static async Task WriteAsync(Stream stream, AgentBrokerMessage message, int maximumFrameBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(message);
        var payload = Serialize(message);
        if (payload.Length > maximumFrameBytes)
            throw new AgentBrokerProtocolException(AgentBrokerProtocol.ErrorCodes.ProtocolViolation);
        var frame = new byte[payload.Length + 4];
        BinaryPrimitives.WriteInt32BigEndian(frame, payload.Length);
        payload.CopyTo(frame, 4);
        await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static byte[] Serialize(AgentBrokerMessage message) =>
        JsonSerializer.SerializeToUtf8Bytes(message ?? throw new ArgumentNullException(nameof(message)), SerializerOptions);

    private static async Task<int> ReadExactlyOrEofAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false);
            if (read == 0) return total;
            total += read;
        }
        return total;
    }
}
