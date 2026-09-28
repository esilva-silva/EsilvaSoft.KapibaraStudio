using System.Diagnostics;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents;

internal enum AgentEventEnqueueResult
{
    Accepted,
    Completed,
    ConsumerUnavailable,
}

/// <summary>
/// Single-reader queue of one turn. Sequence numbers are assigned under the queue lock, so queue order equals sequence
/// order. Flow events from the provider pump wait for space (bounded by count and bytes); control events (tool,
/// approval and terminal) are never dropped and never wait, so broker and approver callers cannot deadlock on a slow
/// consumer. The lock is private, never held across awaits and never taken by other runtime locks.
/// </summary>
internal sealed class AgentRuntimeEventQueue
{
    private const long BaseEventBytes = 256;
    private readonly object _gate = new();
    private readonly Queue<Entry> _items = new();
    private readonly Func<long> _nextSequence;
    private readonly int _maxEvents;
    private readonly long _maxBytes;
    private readonly int _maxCoalescedChars;
    private TaskCompletionSource _readable = NewSignal();
    private TaskCompletionSource _writable = NewSignal();
    private Entry? _tail;
    private long _bytes;
    private bool _completed;

    public AgentRuntimeEventQueue(Func<long> nextSequence, int maxEvents, long maxBytes, int maxCoalescedChars)
    {
        _nextSequence = nextSequence;
        _maxEvents = maxEvents;
        _maxBytes = maxBytes;
        _maxCoalescedChars = maxCoalescedChars;
    }

    public bool IsCompleted
    {
        get
        {
            lock (_gate)
            {
                return _completed;
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _items.Count;
            }
        }
    }

    public static long EstimateBytes(string? text) => BaseEventBytes + ((long)(text?.Length ?? 0) * sizeof(char));

    /// <summary>Control event: never waits. Returns false after completion (late event discarded).</summary>
    public bool TryEnqueueControl(Func<long, AgentEvent> create)
    {
        lock (_gate)
        {
            if (_completed)
            {
                return false;
            }

            AddLocked(create(_nextSequence()));
            return true;
        }
    }

    /// <summary>
    /// Flow event: waits for space until <paramref name="consumerTimeout"/>. A delta for the message at the tail is
    /// merged into it, which aggregates text while the consumer is behind instead of growing the event count.
    /// </summary>
    public async Task<AgentEventEnqueueResult> EnqueueFlowAsync(
        Func<long, AgentEvent> create, string? text, AgentMessageId? coalesceMessage, TimeSpan consumerTimeout,
        CancellationToken cancellationToken)
    {
        var size = EstimateBytes(text);
        var clock = Stopwatch.StartNew();
        while (true)
        {
            Task waiter;
            lock (_gate)
            {
                if (_completed)
                {
                    return AgentEventEnqueueResult.Completed;
                }

                if (coalesceMessage is { } messageId && text is not null && _tail is { } tail &&
                    tail.Event.Kind == AgentEventKind.MessageDelta && tail.Event.MessageId == messageId &&
                    (tail.Event.Text?.Length ?? 0) + text.Length <= _maxCoalescedChars &&
                    _bytes + (text.Length * sizeof(char)) <= _maxBytes)
                {
                    tail.Event = tail.Event with { Text = tail.Event.Text + text };
                    tail.Bytes += text.Length * sizeof(char);
                    _bytes += text.Length * sizeof(char);
                    return AgentEventEnqueueResult.Accepted;
                }

                if (_items.Count < _maxEvents && (_items.Count == 0 || _bytes + size <= _maxBytes))
                {
                    AddLocked(create(_nextSequence()));
                    return AgentEventEnqueueResult.Accepted;
                }

                waiter = _writable.Task;
            }

            var remaining = consumerTimeout - clock.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                return AgentEventEnqueueResult.ConsumerUnavailable;
            }

            try
            {
                await waiter.WaitAsync(remaining, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return AgentEventEnqueueResult.ConsumerUnavailable;
            }
        }
    }

    /// <summary>Appends the terminal events atomically and rejects everything after them.</summary>
    public bool Complete(IReadOnlyList<Func<long, AgentEvent>> terminals)
    {
        lock (_gate)
        {
            if (_completed)
            {
                return false;
            }

            foreach (var create in terminals)
            {
                AddLocked(create(_nextSequence()));
            }

            _completed = true;
            _readable.TrySetResult();
            _writable.TrySetResult();
            return true;
        }
    }

    /// <summary>Returns null only after completion and after every queued event was read.</summary>
    public async ValueTask<AgentEvent?> DequeueAsync()
    {
        while (true)
        {
            Task waiter;
            lock (_gate)
            {
                if (_items.TryDequeue(out var entry))
                {
                    _bytes -= entry.Bytes;
                    if (ReferenceEquals(_tail, entry))
                    {
                        _tail = null;
                    }

                    var freed = _writable;
                    _writable = NewSignal();
                    freed.TrySetResult();
                    return entry.Event;
                }

                if (_completed)
                {
                    return null;
                }

                if (_readable.Task.IsCompleted)
                {
                    _readable = NewSignal();
                }

                waiter = _readable.Task;
            }

            await waiter.ConfigureAwait(false);
        }
    }

    private void AddLocked(AgentEvent item)
    {
        var entry = new Entry(item, EstimateBytes(item.Text));
        _items.Enqueue(entry);
        _tail = entry;
        _bytes += entry.Bytes;
        _readable.TrySetResult();
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class Entry(AgentEvent item, long bytes)
    {
        public AgentEvent Event { get; set; } = item;

        public long Bytes { get; set; } = bytes;
    }
}
