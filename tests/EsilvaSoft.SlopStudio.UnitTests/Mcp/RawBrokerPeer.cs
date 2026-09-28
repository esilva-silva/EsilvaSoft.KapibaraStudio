using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using EsilvaSoft.SlopStudio.Application.Agents.Broker;

namespace EsilvaSoft.SlopStudio.UnitTests.Mcp;

/// <summary>Test-authored IPC peer speaking frames directly, independent of the proxy's AgentBrokerClient.</summary>
internal sealed class RawBrokerPeer : IAsyncDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private RawBrokerPeer(NamedPipeClientStream pipe) => Pipe = pipe;

    public NamedPipeClientStream Pipe { get; }
    public string Nonce { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(AgentBrokerProtocol.NonceBytes));

    public static async Task<RawBrokerPeer> ConnectAsync(Guid workspaceId)
    {
        var pipe = new NamedPipeClientStream(".", AgentBrokerEndpoint.ForWorkspace(workspaceId).PipeName,
            PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync((int)Timeout.TotalMilliseconds);
        return new RawBrokerPeer(pipe);
    }

    public Task SendAsync(AgentBrokerMessage message) =>
        AgentBrokerFrameCodec.WriteAsync(Pipe, message, AgentBrokerProtocol.MaximumResponseFrameBytes,
            new CancellationTokenSource(Timeout).Token);

    public async Task<AgentBrokerMessage?> ReceiveAsync()
    {
        using var timeout = new CancellationTokenSource(Timeout);
        try
        {
            return await AgentBrokerFrameCodec.ReadAsync(Pipe, AgentBrokerProtocol.MaximumResponseFrameBytes, timeout.Token);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public Task SendHelloAsync(int major = AgentBrokerProtocol.MajorVersion) =>
        SendAsync(new AgentBrokerMessage
        {
            Type = AgentBrokerProtocol.MessageTypes.Hello, Protocol = AgentBrokerProtocol.Name, Major = major,
            Minor = 0, Nonce = Nonce
        });

    /// <returns>The broker's answer to the authenticate frame.</returns>
    public async Task<AgentBrokerMessage?> AuthenticateAsync(Guid channelId, string proof)
    {
        await SendHelloAsync();
        var ack = await ReceiveAsync();
        Assert.That(ack?.Type, Is.EqualTo(AgentBrokerProtocol.MessageTypes.HelloAck));
        await SendAsync(new AgentBrokerMessage
        {
            Type = AgentBrokerProtocol.MessageTypes.Authenticate, ChannelId = channelId, Proof = proof,
            Nonce = Nonce, PeerNonce = ack!.Nonce
        });
        return await ReceiveAsync();
    }

    public Task CallAsync(long id, string name, string argumentsJson)
    {
        using var document = JsonDocument.Parse(argumentsJson);
        return SendAsync(new AgentBrokerMessage
        {
            Type = AgentBrokerProtocol.MessageTypes.CallTool, Id = id, Name = name,
            Arguments = document.RootElement.Clone(), TimeoutMilliseconds = 10_000
        });
    }

    public async ValueTask DisposeAsync() => await Pipe.DisposeAsync();
}
