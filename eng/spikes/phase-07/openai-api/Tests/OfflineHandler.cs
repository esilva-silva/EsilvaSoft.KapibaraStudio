using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace EsilvaSoft.SlopStudio.Spikes.OpenAiApi.Tests;

// Não delega a HttpClientHandler/SocketsHttpHandler: não existe caminho de rede.
internal sealed class OfflineHandler(params string[] streams) : HttpMessageHandler
{
    public List<string> Requests { get; } = [];
    public List<string> AuthorizationHeaders { get; } = [];
    public bool BlockUntilCancelled { get; init; }
    public TaskCompletionSource RequestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (request.RequestUri != new Uri("https://fixture.invalid/v1/chat/completions"))
            throw new InvalidOperationException("Destino fora da fixture offline.");
        if (request.Headers.Authorization is not null)
        {
            AuthorizationHeaders.Add(request.Headers.Authorization.ToString());
        }
        Requests.Add(await request.Content!.ReadAsStringAsync(token));
        RequestStarted.TrySetResult();
        if (BlockUntilCancelled) await Task.Delay(Timeout.InfiniteTimeSpan, token);
        if (Requests.Count > streams.Length) throw new InvalidOperationException("Request inesperado; replay bloqueado pela fixture.");
        var content = new StreamContent(new FragmentedStream(Encoding.UTF8.GetBytes(streams[Requests.Count - 1])));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    // Divide inclusive codepoints UTF-8 e delimitadores SSE para exercitar o parser do SDK.
    private sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, 3)], token);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) =>
            base.ReadAsync(buffer, offset, Math.Min(count, 3), token);
    }
}
