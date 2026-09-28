using System.Net;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Infrastructure.Agents.Anthropic;

/// <summary>
/// Provider Claude embutido pela Claude API oficial (SDK C# <c>Anthropic</c>), autenticado somente por API Key do
/// usuário resolvida do cofre do SO por <see cref="IAgentCredentialProvider"/>. Não importa sessão/token de
/// Claude Desktop/Code, não lê variáveis <c>ANTHROPIC_*</c> nem perfis <c>ant</c>, e não oferece login de assinatura.
/// O loop de tools pertence ao runtime: o adapter só traduz o stream e devolve resultados já decididos pelo registry.
/// Não há transcript persistido nem log de chave, prompt ou resposta.
/// </summary>
public sealed class ClaudeAgentProvider : IAgentProvider, IDisposable
{
    public const string Id = "claude";

    private readonly IAgentCredentialProvider _credentials;
    private readonly Func<ClaudeAgentProviderOptions> _options;
    private readonly IAgentToolRegistry? _toolRegistry;
    private readonly HttpMessageHandler _handler;
    private readonly bool _ownsHandler;
    private readonly Lock _gate = new();
    private SecretReference? _rejectedReference;
    private int _disposed;

    public ClaudeAgentProvider(
        IAgentCredentialProvider credentials,
        ClaudeAgentProviderOptions options,
        IAgentToolRegistry? toolRegistry = null)
        : this(credentials, () => options, toolRegistry, null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
    }

    /// <param name="credentials">Resolve a API Key apenas para uso imediato do adapter.</param>
    /// <param name="options">Fonte da configuração; lida uma vez por sessão, antes de qualquer await.</param>
    /// <param name="toolRegistry">Catálogo liberado; null desabilita tool calling.</param>
    /// <param name="handler">Transporte HTTP. Null cria um handler próprio; um handler recebido não é descartado.</param>
    public ClaudeAgentProvider(
        IAgentCredentialProvider credentials,
        Func<ClaudeAgentProviderOptions> options,
        IAgentToolRegistry? toolRegistry,
        HttpMessageHandler? handler)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(options);
        _credentials = credentials;
        _options = options;
        _toolRegistry = toolRegistry;
        _ownsHandler = handler is null;
        _handler = handler ?? new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = DecompressionMethods.None,
            AllowAutoRedirect = false,
            // Sem cookies: a Claude API não precisa de estado HTTP e nada deve sobreviver entre sessões.
            UseCookies = false,
        };
    }

    public string ProviderId => Id;

    public const string DisplayName = "Claude (Anthropic API)";

    /// <summary>
    /// Capacidades que o adapter implementa. A evidência é de contrato automatizado (fixtures offline): a homologação
    /// com a Claude API real, conta e modelo segue pendente e só pode promovê-la com registro no relatório 17.
    /// Arquivos, comandos, subagentes, MCP nativo e resumo de raciocínio ficam desabilitados na baseline.
    /// </summary>
    internal static AgentProviderCapabilities ImplementedCapabilities { get; } = new()
    {
        Chat = true,
        Streaming = true,
        ToolCalling = true,
        Sessions = true,
        ModelSelection = true,
        UsesNetwork = true,
        Evidence = AgentCapabilityEvidence.AutomatedContract,
    };

    /// <summary>Descrição estática, sem cofre, rede ou autenticação.</summary>
    public AgentProviderDescriptor Describe() =>
        new(Id, DisplayName, [AgentAuthenticationMethod.ApiKey], ImplementedCapabilities);

    /// <summary>Estado sem rede nem envio de dados; lê configuração e o item do cofre para saber se existe.</summary>
    public async Task<AgentProviderStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        ClaudeAgentProviderOptions options;
        try
        {
            options = SnapshotOptions();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return new AgentProviderStatus(false, AgentProviderAuthState.Unknown, AgentProviderCapabilities.None,
                unavailableCode: "InvalidConfiguration");
        }

        var availability = await GetAvailabilityAsync(options, null, cancellationToken).ConfigureAwait(false);
        var authState = availability.Reason switch
        {
            ClaudeAgentUnavailableReason.None => AgentProviderAuthState.Configured,
            ClaudeAgentUnavailableReason.NotConfigured or ClaudeAgentUnavailableReason.CredentialMissing =>
                AgentProviderAuthState.NotConfigured,
            ClaudeAgentUnavailableReason.VaultUnavailable => AgentProviderAuthState.VaultUnavailable,
            ClaudeAgentUnavailableReason.CredentialRejected => AgentProviderAuthState.Invalid,
            _ => AgentProviderAuthState.Unknown,
        };
        return new AgentProviderStatus(availability.IsAvailable, authState, availability.Capabilities,
            options.AllowedModelIds, options.DefaultModel,
            availability.IsAvailable ? null : availability.Reason.ToString());
    }

    /// <summary>
    /// Estado sem rede: valida configuração e presença da chave no cofre. Não chama a Claude API, portanto uma chave
    /// presente ainda pode ser recusada na primeira chamada (estado <see cref="ClaudeAgentUnavailableReason.CredentialRejected"/>).
    /// </summary>
    public Task<ClaudeAgentAvailability> GetAvailabilityAsync(string? modelId = null, CancellationToken cancellationToken = default) =>
        GetAvailabilityAsync(SnapshotOptions(), modelId, cancellationToken);

    private async Task<ClaudeAgentAvailability> GetAvailabilityAsync(
        ClaudeAgentProviderOptions options, string? modelId, CancellationToken cancellationToken)
    {
        if (!TrySelectModel(options, modelId, out var model, out var reason))
        {
            return Unavailable(reason);
        }

        var credential = await ResolveApiKeyAsync(options, cancellationToken).ConfigureAwait(false);
        if (credential.Reason != ClaudeAgentUnavailableReason.None)
        {
            return Unavailable(credential.Reason);
        }

        // Efetivas: tool calling só enquanto o registry libera ao menos uma tool; seleção só com mais de um modelo.
        return new ClaudeAgentAvailability(true, ClaudeAgentUnavailableReason.None, ImplementedCapabilities with
        {
            ToolCalling = ClaudeToolCatalog.Snapshot(_toolRegistry).Count > 0,
            ModelSelection = options.AllowedModelIds.Count > 1,
        }, model);
    }

    public async Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(options);
        if (!string.Equals(options.ProviderId, Id, StringComparison.Ordinal))
        {
            throw new ArgumentException("A sessão não pertence ao provider Claude.", nameof(options));
        }

        // Snapshot antes de qualquer await: configuração, modelo e catálogo ficam fixos para esta sessão.
        var configuration = SnapshotOptions();
        if (!TrySelectModel(configuration, options.ModelId, out var model, out var reason))
        {
            throw new ClaudeProviderUnavailableException(reason);
        }

        var tools = ClaudeToolCatalog.Snapshot(_toolRegistry);
        var credential = await ResolveApiKeyAsync(configuration, cancellationToken).ConfigureAwait(false);
        if (credential.Reason != ClaudeAgentUnavailableReason.None)
        {
            throw new ClaudeProviderUnavailableException(credential.Reason);
        }

        // A chave lida aqui só comprova presença; cada turno a resolve de novo e a descarta ao terminar.
        return new ClaudeAgentSession(this, configuration, model, tools);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0 && _ownsHandler)
        {
            _handler.Dispose();
        }
    }

    internal HttpMessageHandler Handler => _handler;

    /// <summary>Resultado da resolução: chave ou motivo seguro. A chave nunca é registrada nem copiada para eventos.</summary>
    internal readonly record struct CredentialLease(string? ApiKey, ClaudeAgentUnavailableReason Reason);

    internal async Task<CredentialLease> ResolveApiKeyAsync(ClaudeAgentProviderOptions options, CancellationToken cancellationToken)
    {
        if (options.ApiKeyReference is not { } reference)
        {
            return new(null, ClaudeAgentUnavailableReason.NotConfigured);
        }

        lock (_gate)
        {
            if (reference.Equals(_rejectedReference))
            {
                // Chave recusada não entra em loop: nenhuma nova chamada até uma nova versão da referência.
                return new(null, ClaudeAgentUnavailableReason.CredentialRejected);
            }
        }

        SecretStoreResult<string> result;
        try
        {
            result = await _credentials.ResolveAsync(reference, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new(null, ClaudeAgentUnavailableReason.VaultUnavailable);
        }

        if (!result.IsSuccess)
        {
            return new(null, result.Failure!.Code == SecretStoreFailureCode.NotFound
                ? ClaudeAgentUnavailableReason.CredentialMissing
                : ClaudeAgentUnavailableReason.VaultUnavailable);
        }

        var key = result.Value;
        return string.IsNullOrWhiteSpace(key) || key.Any(char.IsControl) || key.Length > 1024
            ? new(null, ClaudeAgentUnavailableReason.CredentialMissing)
            : new(key, ClaudeAgentUnavailableReason.None);
    }

    /// <summary>401 da Claude API: a referência fica marcada nesta execução até ser substituída por outra versão.</summary>
    internal void ReportCredentialRejected(SecretReference reference)
    {
        lock (_gate)
        {
            _rejectedReference = reference;
        }
    }

    private ClaudeAgentProviderOptions SnapshotOptions()
    {
        var options = _options() ?? throw new InvalidOperationException("Configuração do provider Claude ausente.");
        options.Validate();
        return options;
    }

    private static bool TrySelectModel(
        ClaudeAgentProviderOptions options, string? requested, out string model, out ClaudeAgentUnavailableReason reason)
    {
        model = string.Empty;
        var candidate = string.IsNullOrEmpty(requested) ? options.DefaultModel : requested;
        if (candidate is null)
        {
            reason = ClaudeAgentUnavailableReason.NoModelSelected;
            return false;
        }

        // Somente IDs da configuração validada; texto do usuário ou do modelo nunca escolhe outro endpoint/modelo.
        if (!options.AllowedModelIds.Contains(candidate, StringComparer.Ordinal))
        {
            reason = ClaudeAgentUnavailableReason.ModelNotAllowed;
            return false;
        }

        model = candidate;
        reason = ClaudeAgentUnavailableReason.None;
        return true;
    }

    // Indisponível não declara capacidades, só o fato restritivo de que dados sairiam da máquina.
    private static ClaudeAgentAvailability Unavailable(ClaudeAgentUnavailableReason reason) =>
        new(false, reason, AgentProviderCapabilities.None with { UsesNetwork = true });
}
