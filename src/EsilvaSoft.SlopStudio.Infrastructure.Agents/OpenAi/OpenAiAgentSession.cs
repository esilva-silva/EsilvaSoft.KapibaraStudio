using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Infrastructure.Agents.OpenAi;

/// <summary>
/// Logical, in-memory OpenAI session. One turn at a time; each turn has its own CTS, bounded event channel and
/// pending tool calls, so cancelling it never touches another session. History keeps only the user text and the final
/// assistant text of completed turns (never context, tool arguments or tool data) and is dropped on disposal.
/// </summary>
internal sealed partial class OpenAiAgentSession : IAgentSession
{
    private const int EventCapacity = 32;

    private readonly OpenAiAgentProvider _provider;
    private readonly OpenAiAgentProviderOptions _options;
    private readonly string _model;
    private readonly object _gate = new();
    private readonly List<(string User, string Assistant)> _history = [];
    private readonly HashSet<AgentTurnId> _turnIds = [];
    private ActiveTurn? _active;
    private bool _disposed;

    public OpenAiAgentSession(OpenAiAgentProvider provider, OpenAiAgentProviderOptions options, string model)
    {
        _provider = provider;
        _options = options;
        _model = model;
    }

    public async IAsyncEnumerable<AgentProviderEvent> RunTurnAsync(
        AgentTurnRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ActiveTurn turn;
        (string User, string Assistant)[] history;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_active is not null)
            {
                throw new InvalidOperationException("A sessão OpenAI já tem um turno ativo.");
            }

            if (!_turnIds.Add(request.TurnId))
            {
                throw new InvalidOperationException("Turno duplicado nesta sessão.");
            }

            turn = new ActiveTurn(request.TurnId, cancellationToken);
            _active = turn;
            history = [.. _history];
        }

        // Snapshot of the request, history and announced tools before any await.
        var tools = OpenAiToolCatalog.Create(_provider.Tools);
        var snapshot = new TurnInput(request.UserMessage, request.AuthorizedContext, history, tools);
        turn.Producer = Task.Run(() => ProduceAsync(turn, snapshot), CancellationToken.None);
        try
        {
            await foreach (var item in turn.Events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return item;
            }
        }
        finally
        {
            turn.Cancel();
            try
            {
                await turn.Producer.WaitAsync(_options.StopTimeout, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The producer observes the turn token; a late stop is abandoned without publishing anything.
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
        ActiveTurn? turn;
        lock (_gate)
        {
            turn = _active;
        }

        // Only a call this session announced in the active turn can be answered, and only once.
        if (turn is null || turn.TurnId != result.TurnId || !Enum.IsDefined(result.Status) ||
            !turn.Pending.TryRemove(result.ToolCallId, out var pending))
        {
            return Task.FromException(new InvalidOperationException("A chamada de tool não está pendente nesta sessão."));
        }

        pending.Completion.TrySetResult(result);
        return Task.CompletedTask;
    }

    // The OpenAI API adapter never asks for approvals: domain approvals belong to the runtime and the registry.
    public Task SubmitApprovalAsync(AgentApprovalDecision decision, CancellationToken cancellationToken) =>
        Task.FromException(new NotSupportedException("O provider OpenAI não solicita aprovações."));

    public async Task CancelTurnAsync(AgentTurnId turnId, CancellationToken cancellationToken)
    {
        ActiveTurn? turn;
        lock (_gate)
        {
            turn = _active is { } active && active.TurnId == turnId ? active : null;
        }

        if (turn is null)
        {
            return;
        }

        turn.Cancel();
        // Confirms that no further request or event is produced; a hung producer surfaces as TimeoutException.
        await turn.Producer.WaitAsync(_options.StopTimeout, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        ActiveTurn? turn;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            turn = _active;
            _history.Clear();
        }

        if (turn is null)
        {
            return;
        }

        turn.Cancel();
        try
        {
            await turn.Producer.WaitAsync(_options.StopTimeout, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Disposal is best effort; the turn token is already cancelled.
        }
    }

    /// <summary>
    /// Grava a troca concluída no histórico em memória da sessão. Nunca descarta trocas antigas para caber no limite:
    /// <c>RunConversationAsync</c> recusa um novo turno com <c>ContextBudgetExceeded</c> assim que o histórico atinge
    /// <see cref="OpenAiAgentProviderOptions.MaxHistoryTurns"/>, então este método só é chamado enquanto ainda há espaço.
    /// </summary>
    private void Commit(ActiveTurn turn, string user, string assistant)
    {
        lock (_gate)
        {
            if (_disposed || !ReferenceEquals(_active, turn) || turn.Token.IsCancellationRequested || _options.MaxHistoryTurns == 0)
            {
                return;
            }

            _history.Add((user, assistant));
        }
    }

    private sealed record TurnInput(
        string UserMessage, string? AuthorizedContext, IReadOnlyList<(string User, string Assistant)> History,
        OpenAiToolCatalog Tools);

    private sealed record PendingCall(string NativeId)
    {
        public TaskCompletionSource<AgentToolResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class ActiveTurn : IDisposable
    {
        private readonly CancellationTokenSource _cts;

        public ActiveTurn(AgentTurnId turnId, CancellationToken external)
        {
            TurnId = turnId;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(external);
            Token = _cts.Token;
        }

        public AgentTurnId TurnId { get; }

        public CancellationToken Token { get; }

        public Channel<AgentProviderEvent> Events { get; } = Channel.CreateBounded<AgentProviderEvent>(
            new BoundedChannelOptions(EventCapacity)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
            });

        public ConcurrentDictionary<AgentToolCallId, PendingCall> Pending { get; } = new();

        public Task Producer { get; set; } = Task.CompletedTask;

        public void Cancel()
        {
            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public void AbandonPending()
        {
            foreach (var key in Pending.Keys)
            {
                if (Pending.TryRemove(key, out var pending))
                {
                    pending.Completion.TrySetCanceled();
                }
            }
        }

        public void Dispose() => _cts.Dispose();
    }
}
