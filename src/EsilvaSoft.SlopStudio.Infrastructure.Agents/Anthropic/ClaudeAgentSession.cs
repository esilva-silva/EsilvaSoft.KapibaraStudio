using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Anthropic.Models.Messages;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Infrastructure.Agents.Anthropic;

/// <summary>
/// Sessão lógica Claude: histórico autorizado somente em memória, um turno ativo por vez, cancelamento por turno.
/// O turno roda em um produtor próprio que escreve numa fila limitada; <see cref="SubmitToolResultAsync"/> só toca
/// o dicionário de chamadas pendentes, nunca o lock do consumidor do stream. Tipos do SDK não saem desta classe.
/// </summary>
internal sealed partial class ClaudeAgentSession : IAgentSession
{
    private const int EventQueueCapacity = 64;

    private readonly ClaudeAgentProvider _provider;
    private readonly ClaudeAgentProviderOptions _options;
    private readonly ClaudeAgentBudget _budget;
    private readonly string _model;
    private readonly IReadOnlyList<ClaudeToolDefinition> _tools;
    private readonly Lock _gate = new();
    private readonly List<MessageParam> _history = [];
    private int _historyChars;
    private long _sessionTokens;
    private bool _modelUnavailable;
    private TurnContext? _active;
    private int _disposed;

    public ClaudeAgentSession(
        ClaudeAgentProvider provider, ClaudeAgentProviderOptions options, string model, IReadOnlyList<ClaudeToolDefinition> tools)
    {
        _provider = provider;
        _options = options;
        _budget = options.Budget;
        _model = model;
        _tools = tools;
    }

    internal string ModelId => _model;

    public async IAsyncEnumerable<AgentProviderEvent> RunTurnAsync(
        AgentTurnRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!request.TurnId.IsValid)
        {
            throw new ArgumentException("Turno inválido.", nameof(request));
        }

        // Snapshot do pedido antes de qualquer await: texto e contexto autorizado ficam fixos para o turno.
        var userMessage = request.UserMessage ?? string.Empty;
        var authorizedContext = request.AuthorizedContext;
        var turn = new TurnContext(request.TurnId, _budget.MaxTurnDuration);
        lock (_gate)
        {
            // Revalida sob o mesmo lock que DisposeAsync usa para achar o turno ativo: um descarte concluído entre a
            // checagem inicial e aqui não pode deixar um turno vivo (e nunca cancelado) numa sessão descartada.
            if (Volatile.Read(ref _disposed) != 0)
            {
                turn.Dispose();
                throw new ObjectDisposedException(GetType().FullName);
            }

            if (_active is not null)
            {
                turn.Dispose();
                throw new InvalidOperationException("A sessão Claude já tem um turno ativo.");
            }

            _active = turn;
        }

        var channel = Channel.CreateBounded<AgentProviderEvent>(new BoundedChannelOptions(EventQueueCapacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
        var registration = cancellationToken.Register(static state => ((TurnContext)state!).Cancel(), turn);
        var producer = Task.Run(() => ProduceTurnAsync(turn, userMessage, authorizedContext, channel.Writer), CancellationToken.None);
        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
            {
                yield return item;
            }
        }
        finally
        {
            // Consumidor saiu (fim natural, cancelamento ou abandono): encerra requisição HTTP e esperas de tool.
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

    public Task SubmitToolResultAsync(AgentToolResult result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        TurnContext? turn;
        lock (_gate)
        {
            turn = _active;
        }

        // Só aceita resultado para uma chamada pendente do turno ativo, uma única vez.
        if (turn is null || turn.TurnId != result.TurnId || !Enum.IsDefined(result.Status) ||
            !turn.Pending.TryGetValue(result.ToolCallId, out var pending) || !pending.Result.TrySetResult(result))
        {
            return Task.FromException(new InvalidOperationException("Chamada de tool desconhecida ou já respondida."));
        }

        return Task.CompletedTask;
    }

    // O adapter Claude não solicita aprovações: aprovação de domínio pertence ao registry/runtime.
    public Task SubmitApprovalAsync(AgentApprovalDecision decision, CancellationToken cancellationToken) =>
        Task.FromException(new NotSupportedException("O provider Claude não solicita aprovações."));

    public Task CancelTurnAsync(AgentTurnId turnId, CancellationToken cancellationToken)
    {
        TurnContext? turn;
        lock (_gate)
        {
            turn = _active;
        }

        // Idempotente e isolado: só o turno indicado desta sessão; nada já enviado é desfeito.
        if (turn is not null && turn.TurnId == turnId)
        {
            turn.Cancel();
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        TurnContext? turn;
        lock (_gate)
        {
            turn = _active;
            // Descarta o histórico em memória; nada foi persistido.
            _history.Clear();
            _historyChars = 0;
        }

        turn?.Cancel();
        return ValueTask.CompletedTask;
    }

    /// <summary>Estado privado de um turno. Tokens separados distinguem cancelamento do usuário de prazo esgotado.</summary>
    private sealed class TurnContext : IDisposable
    {
        private readonly CancellationTokenSource _user = new();
        private readonly CancellationTokenSource _work;

        public TurnContext(AgentTurnId turnId, TimeSpan maxDuration)
        {
            TurnId = turnId;
            _work = CancellationTokenSource.CreateLinkedTokenSource(_user.Token);
            _work.CancelAfter(maxDuration);
        }

        public AgentTurnId TurnId { get; }

        /// <summary>Cancelado pelo usuário, pelo consumidor ou pelo descarte da sessão: encerra sem novo evento.</summary>
        public CancellationToken UserToken => _user.Token;

        /// <summary>Inclui o prazo máximo do turno; esgotado sem cancelamento do usuário vira erro seguro.</summary>
        public CancellationToken WorkToken => _work.Token;

        public ConcurrentDictionary<AgentToolCallId, PendingToolCall> Pending { get; } = new();

        public void Cancel()
        {
            try
            {
                _user.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            foreach (var pending in Pending.Values)
            {
                pending.Result.TrySetCanceled();
            }
        }

        public void Dispose()
        {
            _work.Dispose();
            _user.Dispose();
        }
    }

    private sealed record PendingToolCall(string NativeId)
    {
        public TaskCompletionSource<AgentToolResult> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
