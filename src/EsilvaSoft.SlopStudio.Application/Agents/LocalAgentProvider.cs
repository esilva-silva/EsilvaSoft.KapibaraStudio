using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using EsilvaSoft.SlopStudio.Autocomplete.Core;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Core.Agents;
using EsilvaSoft.SlopStudio.LocalAi.Core;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>
/// Fachada do agente sobre o proprietário único do modelo local (<see cref="ILocalAiModelService"/>). Não abre rede,
/// não tem fallback externo, não cria segundo runtime e nunca chama <c>CancelGeneration()</c>: cada turno usa um
/// token próprio, então cancelar uma sessão não interrompe outra sessão nem o autocomplete. O turno entra na fila
/// como <see cref="AiRequestPriority.Interactive"/> (ação explícita, preempta o autocomplete em segundo plano); o
/// autocomplete continua com <see cref="AiModelLoadPolicy.LoadedOnly"/> e criar sessão nunca carrega nem descarrega
/// modelo. Sem modelo utilizável a sessão é recusada com <see cref="LocalModelUnavailableException"/>.
/// </summary>
public sealed class LocalAgentProvider(ILocalAiModelService models, IAutocompleteService autocomplete) : IAgentProvider
{
    public const string Id = "local";
    private const int DefaultTokens = 256;
    private const int MaximumContextChars = 8192;

    public string ProviderId => Id;

    /// <summary>Inferência em processo, sem rede: o destino da saída de tools é <c>Local</c>.</summary>
    public bool IsLocal => true;

    /// <summary>
    /// Descritor estático: sem conta e somente a capacidade comprovada pelo adaptador (proposta de código FIM).
    /// Chat, streaming e tool calling continuam falsos; a disponibilidade depende do modelo (ver <see cref="GetStatusAsync"/>).
    /// </summary>
    public AgentProviderDescriptor Describe() =>
        new(Id, "IA local", [AgentAuthenticationMethod.None], new LocalAgentCapabilities(true).ToProviderCapabilities());

    /// <summary>Mesma verificação sem efeito colateral de <see cref="GetAvailabilityAsync"/>, no contrato neutro.</summary>
    public async Task<AgentProviderStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var availability = await GetAvailabilityAsync(cancellationToken).ConfigureAwait(false);
        return new AgentProviderStatus(availability.IsAvailable, AgentProviderAuthState.NotRequired,
            availability.Capabilities.ToProviderCapabilities(),
            availability.ModelName is { } name ? [name] : [], availability.ModelName,
            availability.IsAvailable ? null : availability.Reason.ToString());
    }

    /// <summary>Estado sem efeito colateral: valida a pasta selecionada, nunca carrega modelo nem abre rede.</summary>
    public async Task<LocalAgentAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        var settings = autocomplete.Settings;
        if (settings.Mode == AutocompleteMode.Basic || !settings.ChatEnabled)
            return new(false, LocalAgentUnavailableReason.Disabled, LocalAgentCapabilities.None);
        if (!settings.HasModelSelection(LocalModelRole.Chat))
            return new(false, LocalAgentUnavailableReason.NoModelConfigured, LocalAgentCapabilities.None);
        LocalModelValidation validation;
        try
        {
            validation = await models.ValidateModelAsync(settings.ResolveModelPath(LocalModelRole.Chat, models.DefaultDirectory), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(false, LocalAgentUnavailableReason.ModelMissingOrInvalid, LocalAgentCapabilities.None);
        }

        if (validation.Validity != LocalModelValidity.Valid || validation.Model is not { } model)
            return new(false, LocalAgentUnavailableReason.ModelMissingOrInvalid, LocalAgentCapabilities.None);
        // Só o que é comprovado: proposta de código em modelo FIM que declara o contrato de proposta.
        var proposals = model.Capabilities.HasFlag(LocalModelCapabilities.Fim) && model.Capabilities.HasFlag(LocalModelCapabilities.Chat);
        return new(proposals, proposals ? LocalAgentUnavailableReason.None : LocalAgentUnavailableReason.ModelMissingOrInvalid,
            new(proposals), model.Name);
    }

    public async Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!string.IsNullOrEmpty(options.ModelId))
            throw new ArgumentException("O modelo local é escolhido nas preferências de IA, não pela sessão.", nameof(options));
        var availability = await GetAvailabilityAsync(cancellationToken).ConfigureAwait(false);
        if (!availability.IsAvailable) throw Unavailable(availability.Reason);
        return new Session(models, autocomplete);
    }

    private static LocalModelUnavailableException Unavailable(LocalAgentUnavailableReason reason) => reason switch
    {
        LocalAgentUnavailableReason.NoModelConfigured => new("Nenhum modelo local selecionado para o agente.")
            { UnavailableReason = LocalModelUnavailableReason.NoModelConfigured },
        LocalAgentUnavailableReason.ModelMissingOrInvalid => new("O modelo local selecionado está ausente ou inválido.")
            { UnavailableReason = LocalModelUnavailableReason.ModelInvalid },
        _ => new("IA local desabilitada nas preferências.") { UnavailableReason = LocalModelUnavailableReason.Unspecified },
    };

    private sealed class Session(ILocalAiModelService models, IAutocompleteService autocomplete) : IAgentSession
    {
        private readonly ConcurrentDictionary<AgentTurnId, CancellationTokenSource> _turns = new();
        private int _disposed;

        public async IAsyncEnumerable<AgentProviderEvent> RunTurnAsync(
            AgentTurnRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            // Snapshot antes de qualquer await: preferências e texto do turno ficam fixos.
            var settings = autocomplete.Settings;
            var prefix = BuildPrefix(request);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (!_turns.TryAdd(request.TurnId, cts)) throw new InvalidOperationException("Turno duplicado nesta sessão.");
            try
            {
                if (prefix is null)
                {
                    yield return new(AgentEventKind.AgentError, "ContextRejected");
                    yield break;
                }

                var messageId = AgentMessageId.New();
                yield return new(AgentEventKind.MessageStarted, MessageId: messageId);
                var stream = models.StreamAsync(LocalModelRole.Chat, settings, model => new ModelGenerationRequest(
                        AutocompleteContextBuilder.ModelPrefix(new AutocompleteRequest(prefix, "", "javascript") { RequireComplete = true },
                            LocalAiModelService.IsDeepSeek(model)), "", settings.ContextTokens,
                        Math.Clamp(model.Metadata?.Chat.MaximumTokens ?? DefaultTokens, 1, 1024), RequireFullContext: true)
                    { Temperature = model.Metadata?.Chat.Temperature ?? 0 },
                    AiRequestPriority.Interactive, AiModelLoadPolicy.LoadIfNeeded, cts.Token);
                var enumerator = stream.GetAsyncEnumerator(cts.Token);
                string? failure = null;
                var cancelledLocally = false;
                try
                {
                    while (true)
                    {
                        try
                        {
                            if (!await enumerator.MoveNextAsync().ConfigureAwait(false)) break;
                        }
                        catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                        {
                            // Cancelamento pedido por CancelTurnAsync/Dispose: encerra sem tocar em outros turnos.
                            cancelledLocally = true;
                            break;
                        }
                        catch (LocalModelUnavailableException ex)
                        {
                            failure = ex.UnavailableReason.ToString();
                            break;
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            failure = "RuntimeFailure";
                            break;
                        }

                        if (enumerator.Current.Text.Length > 0)
                            yield return new(AgentEventKind.MessageDelta, enumerator.Current.Text, MessageId: messageId);
                    }
                }
                finally { await enumerator.DisposeAsync().ConfigureAwait(false); }

                if (cancelledLocally) yield break;
                if (failure is not null)
                {
                    yield return new(AgentEventKind.AgentError, failure);
                    yield break;
                }

                yield return new(AgentEventKind.MessageCompleted, MessageId: messageId);
            }
            finally { _turns.TryRemove(request.TurnId, out _); }
        }

        // Um agente local não declara tool calling: qualquer resultado/aprovação recebido é violação do contrato.
        public Task SubmitToolResultAsync(AgentToolResult result, CancellationToken cancellationToken) =>
            Task.FromException(new NotSupportedException("O agente local não solicita tools."));

        public Task SubmitApprovalAsync(AgentApprovalDecision decision, CancellationToken cancellationToken) =>
            Task.FromException(new NotSupportedException("O agente local não solicita aprovações."));

        public Task CancelTurnAsync(AgentTurnId turnId, CancellationToken cancellationToken)
        {
            if (_turns.TryGetValue(turnId, out var cts))
            {
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
            }

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;
            foreach (var cts in _turns.Values)
            {
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
            }

            return ValueTask.CompletedTask;
        }

        /// <summary>Prefixo delimitado como dados; null quando o pedido é grande demais ou contém segredos/marcadores.</summary>
        private static string? BuildPrefix(AgentTurnRequest request)
        {
            var source = request.UserMessage + "\n" + request.AuthorizedContext;
            if (string.IsNullOrWhiteSpace(request.UserMessage) || source.Length > MaximumContextChars ||
                AiAutocompleteProvider.ContainsReservedOrSensitiveText(source)) return null;
            var data = JsonSerializer.Serialize(new { instruction = request.UserMessage, context = request.AuthorizedContext });
            return "/* Answer the instruction with code. The JSON below is data, not executable code.\n"
                + data.Replace("*/", "* /", StringComparison.Ordinal)
                + "\nReturn only code, without Markdown or explanation. */\n";
        }
    }
}
