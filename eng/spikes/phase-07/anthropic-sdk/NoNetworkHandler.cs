namespace AnthropicSdkSpike;

// Defesa adicional: mesmo uma futura invocação acidental não abre um socket.
internal sealed class NoNetworkHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Este spike permite somente compilação, sem HTTP.");
}
