using Anthropic;
using Anthropic.Core;
using Anthropic.Models.Messages;

namespace AnthropicSdkSpike;

// Biblioteca sem entry point. Métodos compilados; nenhum deles é executado pelo spike.
public static class StreamingContract
{
    public static AnthropicClient CreateOfflineClient() => new(new ClientOptions
    {
        // Definidos ANTES do construtor: impede auto-resolução de credenciais/perfil.
        ApiKey = null,
        AuthToken = null,
        WebhookKey = null,
        Credentials = null,
        BaseUrl = "https://anthropic-spike.invalid",
        HttpClient = new HttpClient(new NoNetworkHandler()),
        MaxRetries = 0,
        Timeout = TimeSpan.FromSeconds(5),
        ResponseValidation = true,
    });

    // Valida apenas os tipos de configuração/streaming/cancelamento da versão fixada.
    // Não é adapter de produto, não chama tools e não aceita credenciais.
    public static async Task CompileStreamingFlowAsync(
        string modelId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        using var client = CreateOfflineClient();
        MessageCreateParams parameters = new()
        {
            Model = modelId,
            MaxTokens = 32,
            Messages = [new() { Role = Role.User, Content = "Mensagem sintética do spike." }],
        };

        await foreach (var streamEvent in client.Messages.CreateStreaming(
            parameters, cancellationToken: cancellationToken))
        {
            // Sem stdout ou persistência de conteúdo. Apenas referência compilada ao evento.
            _ = streamEvent;
        }
    }
}
