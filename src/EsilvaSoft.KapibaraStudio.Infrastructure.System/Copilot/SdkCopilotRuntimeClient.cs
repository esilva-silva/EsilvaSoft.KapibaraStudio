using GitHub.Copilot;
using GitHub.Copilot.Rpc;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;

internal sealed class SdkCopilotRuntimeClient : ICopilotRuntimeClient
{
    private readonly CopilotClient client;
    public SdkCopilotRuntimeClient(CopilotClient client) => this.client = client ?? throw new ArgumentNullException(nameof(client));
    public async Task StartAsync(CancellationToken token) => await client.StartAsync(token).ConfigureAwait(false);
    public async Task<CopilotRuntimeAuthentication> GetAuthStatusAsync(CancellationToken token)
    {
        var status = await client.GetAuthStatusAsync(token).ConfigureAwait(false);
        return new(status.IsAuthenticated, status.AuthType);
    }
    public async Task<IReadOnlyList<string>> ListModelIdsAsync(CancellationToken token) =>
        (await client.ListModelsAsync(token).ConfigureAwait(false)).Select(static model => model.Id).ToArray();
    public async Task<bool> HasSessionAsync(string id, CancellationToken token) =>
        await client.GetSessionMetadataAsync(id, token).ConfigureAwait(false) is not null;
    public async Task<ICopilotRuntimeSession> CreateSessionAsync(SessionConfig config, CancellationToken token) =>
        new SdkSession(await client.CreateSessionAsync(config, token).ConfigureAwait(false));
    public async Task<ICopilotRuntimeSession> ResumeSessionAsync(string id, ResumeSessionConfig config, CancellationToken token) =>
        new SdkSession(await client.ResumeSessionAsync(id, config, token).ConfigureAwait(false));
    public async Task DeleteSessionAsync(string id, CancellationToken token) => await client.DeleteSessionAsync(id, token).ConfigureAwait(false);
    public ValueTask DisposeAsync() => client.DisposeAsync();

    private sealed class SdkSession(CopilotSession session) : ICopilotRuntimeSession
    {
        public string SessionId => session.SessionId;
        public async Task<bool> RestrictBuiltInAgentsAsync(CancellationToken token) =>
            (await session.Rpc.Options.UpdateAsync(includedBuiltinAgents: [], cancellationToken: token).ConfigureAwait(false)).Success;
        public IDisposable Subscribe(Action<SessionEvent> handler) => session.On<SessionEvent>(handler);
        public async Task SendAsync(MessageOptions message, CancellationToken token) => await session.SendAsync(message, token).ConfigureAwait(false);
        public async Task AbortAsync(CancellationToken token) => await session.AbortAsync(token).ConfigureAwait(false);
        public async Task SubmitToolResultAsync(string requestId, ToolResultObject result, CancellationToken token) =>
            await session.Rpc.Tools.HandlePendingToolCallAsync(requestId, result, null, token).ConfigureAwait(false);
        public ValueTask DisposeAsync() => session.DisposeAsync();
    }
}
