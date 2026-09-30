using System.Runtime.CompilerServices;
using System.Threading.Channels;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.ClaudeCode;

/// <summary>
/// Estado final de um turno do Claude Code visto pelo adapter. <see cref="AgentTurnOutcome.OutcomeUnknown"/> quando a
/// árvore de processos foi encerrada (cancelamento): o que a CLI já fez não é desfeito nem confirmado.
/// </summary>
internal sealed record ClaudeCodeTurnSummary(
    AgentTurnOutcome Outcome,
    string? ErrorCode,
    bool Resumed,
    double? TotalCostUsd = null,
    long? InputTokens = null,
    long? OutputTokens = null,
    int? NumTurns = null,
    int PermissionDenials = 0,
    string? TerminalReason = null,
    int ApiRetries = 0,
    string? LastApiRetryCategory = null,
    int NativeToolCalls = 0,
    int DiscardedLines = 0,
    string? ObservedModel = null)
{
    /// <summary>Chamadas de tools do produto (MCP) observadas; o broker as executou e auditou.</summary>
    public int ProductToolCalls { get; init; }

    /// <summary>Estado do canal MCP da sessão neste turno (nulo quando o plano não usou o canal).</summary>
    public AgentMcpChannelStatus? McpStatus { get; init; }

    /// <summary>A sessão persistida não existia mais e o turno continuou numa sessão nova (aviso visível publicado).</summary>
    public bool ResumeFallback { get; init; }
}

/// <summary>Dependências e pedidos fixados na criação da sessão (P7-CLP-4).</summary>
internal sealed record ClaudeCodeSessionContext(
    IAgentMcpChannelProvisioner? McpChannel,
    Guid? ConversationId,
    string? ResumeProviderSessionId,
    Action<AgentProviderSessionUpdate>? Observer)
{
    public static ClaudeCodeSessionContext None { get; } = new(null, null, null, null);
}

/// <summary>
/// Sessão do modo Claude (assinatura): um processo <c>claude -p</c> por turno, continuidade por <c>--resume</c>, um turno
/// ativo por vez e cancelamento exclusivamente por encerramento da árvore de processos (decisão do usuário, 25/09/2026;
/// o <c>control_request</c> interno não é usado). Cada turno obedece literalmente ao <see cref="AgentTurnPlan"/> da
/// <c>AgentModePolicy</c> (ADR-056): sem plano, nada é executado. As tools do produto chegam pelo canal MCP da sessão
/// (aberto sob demanda no primeiro turno que precisa dele, revinculado ao plano antes de cada processo e revogado no
/// descarte). O ID da sessão da CLI pode vir persistido da conversa (<c>ResumeProviderSessionId</c>); o ID efetivo é
/// informado ao chamador pelo observador de <see cref="AgentSessionOptions"/>. O produtor escreve numa fila limitada;
/// tipos e textos nativos não saem do adapter.
/// </summary>
internal sealed partial class ClaudeCodeAgentSession : IAgentSession
{
    private const int EventQueueCapacity = 64;

    private readonly ClaudeCodeAgentProvider _provider;
    private readonly ClaudeCodeAgentProviderOptions _options;
    private readonly ClaudeCodeLaunchProfile _profile;
    private readonly IAgentMcpChannelProvisioner? _mcp;
    private readonly Action<AgentProviderSessionUpdate>? _observer;
    private readonly Lock _gate = new();
    private string _cliSessionId = Guid.NewGuid().ToString("D");
    private bool _established;
    private bool _persistedResumePending;
    private string? _pendingNotice;
    private string? _reportedSessionId;
    private Guid? _conversationId;
    private AgentMcpChannelHandle? _channel;
    private McpServerLaunchSpec? _launch;
    private TurnContext? _active;
    private ClaudeCodeTurnSummary? _lastTurn;
    private (AgentTurnId TurnId, bool PromptSent)? _lastDelivery;
    private int _disposed;

    public ClaudeCodeAgentSession(ClaudeCodeAgentProvider provider, ClaudeCodeAgentProviderOptions options, ClaudeCodeLaunchProfile profile)
        : this(provider, options, profile, ClaudeCodeSessionContext.None)
    {
    }

    public ClaudeCodeAgentSession(
        ClaudeCodeAgentProvider provider, ClaudeCodeAgentProviderOptions options, ClaudeCodeLaunchProfile profile,
        ClaudeCodeSessionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _provider = provider;
        _options = options;
        _profile = profile;
        _mcp = context.McpChannel;
        _observer = context.Observer;
        _conversationId = context.ConversationId is { } conversation && conversation != Guid.Empty ? conversation : null;
        if (context.ResumeProviderSessionId is { } persisted)
        {
            // Retomada após reinício: o primeiro turno usa --resume com o ID persistido. ID malformado nunca vai ao argv:
            // a conversa continua numa sessão nova, com aviso visível.
            if (Guid.TryParseExact(persisted, "D", out var parsed) && parsed != Guid.Empty)
            {
                _cliSessionId = parsed.ToString("D");
                _established = true;
                _persistedResumePending = true;
            }
            else
            {
                _pendingNotice = ClaudeCodeErrorCodes.ResumeSessionInvalidNotice;
            }
        }
    }

    internal ClaudeCodeLaunchProfile Profile => _profile;

    /// <summary>Diagnóstico do último turno (sem texto); nunca persistido.</summary>
    internal ClaudeCodeTurnSummary? LastTurn
    {
        get
        {
            lock (_gate)
            {
                return _lastTurn;
            }
        }
    }

    internal (string SessionId, bool Established) CliSession
    {
        get
        {
            lock (_gate)
            {
                return (_cliSessionId, _established);
            }
        }
    }

    /// <summary>Canal MCP aberto desta sessão (diagnóstico de teste).</summary>
    internal AgentMcpChannelHandle? McpChannel
    {
        get
        {
            lock (_gate)
            {
                return _channel;
            }
        }
    }

    public async IAsyncEnumerable<AgentProviderEvent> RunTurnAsync(
        AgentTurnRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!request.TurnId.IsValid)
        {
            throw new ArgumentException("Turno inválido.", nameof(request));
        }

        // Snapshot antes de qualquer await: texto, plano, prompt, anexos, permissões e sessão da CLI ficam fixos para o turno.
        var input = new TurnInput(
            request.UserMessage ?? string.Empty,
            request.AuthorizedContext,
            request.Plan,
            request.SystemPrompt,
            request.Attachments is null ? [] : [.. request.Attachments],
            request.Permissions,
            request.ConversationId,
            request.WorkspaceContext);
        var turn = new TurnContext(request.TurnId, _options.MaxTurnDuration);
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                turn.Dispose();
                throw new ObjectDisposedException(GetType().FullName);
            }

            if (_active is not null)
            {
                turn.Dispose();
                throw new InvalidOperationException("A sessão Claude (assinatura) já tem um turno ativo.");
            }

            _active = turn;
            turn.CliSessionId = _cliSessionId;
            turn.Resume = _established;
            turn.PersistedResume = _persistedResumePending && _established;
        }

        var channel = Channel.CreateBounded<AgentProviderEvent>(new BoundedChannelOptions(EventQueueCapacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
        var registration = cancellationToken.Register(static state => ((TurnContext)state!).Cancel(), turn);
        var producer = Task.Run(() => ProduceTurnAsync(turn, input, channel.Writer), CancellationToken.None);
        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
            {
                yield return item;
            }
        }
        finally
        {
            // Consumidor saiu (fim natural, cancelamento ou abandono): encerra a árvore de processos se ainda viva.
            turn.Cancel();
            await registration.DisposeAsync().ConfigureAwait(false);
            try
            {
                await producer.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // O produtor converte falhas em eventos; nada a propagar aqui.
            }

            lock (_gate)
            {
                if (ReferenceEquals(_active, turn))
                {
                    _active = null;
                }
            }

            turn.Dispose();
        }
    }

    // As tools do produto são executadas pelo broker (mesmo registry) através do proxy MCP que a própria CLI inicia; o
    // runtime não despacha pedidos de tool deste adapter.
    public Task SubmitToolResultAsync(AgentToolResult result, CancellationToken cancellationToken) =>
        Task.FromException(new InvalidOperationException("Chamada de tool desconhecida ou já respondida."));

    // Confirmações por chamada chegam pela ferramenta de aprovação do produto (--permission-prompt-tool, registry e porta
    // de confirmação da UI), nunca pelo runtime.
    public Task SubmitApprovalAsync(AgentApprovalDecision decision, CancellationToken cancellationToken) =>
        Task.FromException(new NotSupportedException("O provider Claude (assinatura) não solicita aprovações pelo runtime."));

    public Task CancelTurnAsync(AgentTurnId turnId, CancellationToken cancellationToken)
    {
        TurnContext? turn;
        lock (_gate)
        {
            turn = _active;
        }

        // Idempotente e isolado: encerra só a árvore do turno indicado desta sessão; nada é desfeito.
        if (turn is not null && turn.TurnId == turnId)
        {
            turn.Cancel();
        }

        return Task.CompletedTask;
    }

    /// <summary>Leituras nativas (Read/Glob/Grep) executadas pela própria CLI: o runtime só as exibe (nome e estado).</summary>
    public IReadOnlyCollection<string> ObservableNativeTools => ClaudeCodeAgentProviderOptions.NativeToolAllowlist;

    /// <summary>Códigos tipados e fixos do modo Claude (assinatura); o runtime os repassa à UI em vez de <c>ProviderError</c>.</summary>
    public IReadOnlyCollection<string> ProviderErrorCodes => ClaudeCodeErrorCodes.TurnErrorCodes;

    /// <summary>
    /// Depois que o prompt começou a ser escrito no stdin, encerrar a árvore não desfaz nem confirma o que a CLI fez:
    /// o runtime publica <see cref="AgentTurnOutcome.OutcomeUnknown"/> (retomável por <c>--resume</c>). Antes disso,
    /// nada saiu. Um turno ainda ativo é relatado de forma conservadora.
    /// </summary>
    public AgentTurnCancellationReport GetCancellationReport(AgentTurnId turnId)
    {
        lock (_gate)
        {
            if (_active is { } active && active.TurnId == turnId)
            {
                return AgentTurnCancellationReport.MayHaveTakenEffect;
            }

            return _lastDelivery is { } last && last.TurnId == turnId
                ? last.PromptSent ? AgentTurnCancellationReport.MayHaveTakenEffect : AgentTurnCancellationReport.NothingSent
                : AgentTurnCancellationReport.NotReported;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        TurnContext? turn;
        AgentMcpChannelHandle? channel;
        lock (_gate)
        {
            turn = _active;
            channel = _channel;
            _channel = null;
            _launch = null;
            // Só o identificador em memória é esquecido; o transcript gravado pela própria CLI em ~/.claude/projects
            // permanece (limitação aceita, plano 23 regra 8) e o app não o lê nem apaga.
            _established = false;
        }

        turn?.Cancel();
        // O canal da sessão é revogado de forma durável: o proxy de um processo que ainda não morreu perde o acesso.
        await CloseChannelAsync(channel).ConfigureAwait(false);
    }

    private async Task CloseChannelAsync(AgentMcpChannelHandle? channel)
    {
        if (channel is null || _mcp is null)
        {
            return;
        }

        try
        {
            await _mcp.CloseSessionAsync(channel, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A revogação é idempotente e o provisioner recupera canais pendentes; nada a propagar no descarte.
        }
    }

    private void CompleteTurn(TurnContext turn, ClaudeCodeTurnSummary summary, bool? established, bool resetSession)
    {
        AgentProviderSessionUpdate? update = null;
        lock (_gate)
        {
            _lastTurn = summary;
            _lastDelivery = (turn.TurnId, turn.PromptSent);
            if (Volatile.Read(ref _disposed) != 0 || !string.Equals(turn.CliSessionId, _cliSessionId, StringComparison.Ordinal))
            {
                return;
            }

            // M3: o prompt saiu com --session-id, mas o init nunca foi validado. A CLI pode já ter registrado esse ID;
            // reutilizá-lo com --session-id falharia ("já em uso") e --resume retomaria algo não verificado. Um turno de
            // --resume mantém o ID (a sessão já existia e foi validada antes).
            if (resetSession || (turn.PromptSent && established != true && !turn.Resume))
            {
                // Sessão da CLI inexistente/expirada: o próximo turno começa outra, sem transportar contexto.
                _cliSessionId = Guid.NewGuid().ToString("D");
                _established = false;
                _persistedResumePending = false;
            }
            else if (established == true)
            {
                _established = true;
                _persistedResumePending = false;
                if (!string.Equals(_reportedSessionId, _cliSessionId, StringComparison.Ordinal))
                {
                    // Só um ID cujo init foi validado é informado para persistência.
                    _reportedSessionId = _cliSessionId;
                    update = new AgentProviderSessionUpdate(_conversationId, AgentProviderSessionChange.Established, _cliSessionId);
                }
            }
        }

        if (update is not null)
        {
            Notify(update);
        }
    }

    private void Notify(AgentProviderSessionUpdate update)
    {
        if (_observer is null)
        {
            return;
        }

        try
        {
            _observer(update);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // O observador é do chamador: uma falha dele não interrompe o turno.
        }
    }

    /// <summary>Entrada do turno capturada antes do primeiro await.</summary>
    private sealed record TurnInput(
        string UserMessage,
        string? AuthorizedContext,
        AgentTurnPlan? Plan,
        string? SystemPrompt,
        IReadOnlyList<AgentContextAttachment> Attachments,
        AgentProviderPermissions? Permissions,
        Guid? ConversationId,
        AgentWorkspaceContext? WorkspaceContext);

    /// <summary>Estado privado do turno: tokens separados distinguem cancelamento de prazo; o processo é encerrado no Cancel.</summary>
    private sealed class TurnContext : IDisposable
    {
        private readonly CancellationTokenSource _user = new();
        private readonly CancellationTokenSource _work;
        private readonly Lock _processGate = new();
        private IClaudeCodeProcess? _process;

        public TurnContext(AgentTurnId turnId, TimeSpan maxDuration)
        {
            TurnId = turnId;
            _work = CancellationTokenSource.CreateLinkedTokenSource(_user.Token);
            _work.CancelAfter(maxDuration);
        }

        public AgentTurnId TurnId { get; }

        public string CliSessionId { get; set; } = string.Empty;

        public bool Resume { get; set; }

        /// <summary>Primeiro turno retomando um ID persistido: elegível ao fallback para uma sessão nova.</summary>
        public bool PersistedResume { get; set; }

        public bool ResumeFallback { get; set; }

        /// <summary>
        /// Verdadeiro a partir do instante em que o prompt começa a ser escrito no stdin: antes disso, cancelar não
        /// enviou nada (Cancelled); depois, o efeito na CLI é desconhecido (OutcomeUnknown).
        /// </summary>
        public bool PromptSent { get; set; }

        public CancellationToken UserToken => _user.Token;

        public CancellationToken WorkToken => _work.Token;

        /// <summary>Associa o processo; se o turno já foi cancelado, a árvore é encerrada imediatamente.</summary>
        public void Attach(IClaudeCodeProcess process)
        {
            lock (_processGate)
            {
                _process = process;
            }

            if (_work.IsCancellationRequested)
            {
                process.KillTree();
            }
        }

        public void KillProcessTree()
        {
            IClaudeCodeProcess? process;
            lock (_processGate)
            {
                process = _process;
            }

            process?.KillTree();
        }

        public void Cancel()
        {
            try
            {
                _user.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            KillProcessTree();
        }

        public void Dispose()
        {
            _work.Dispose();
            _user.Dispose();
        }
    }
}
