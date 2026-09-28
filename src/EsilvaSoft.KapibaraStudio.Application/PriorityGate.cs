namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>Single-holder async gate: the highest priority waiter enters first, FIFO within the same priority.</summary>
internal sealed class PriorityGate
{
    private readonly object _lock = new();
    private readonly List<Waiter> _waiters = [];
    private bool _held;
    private long _sequence;

    public Task WaitAsync(int priority, CancellationToken cancellationToken)
    {
        Waiter waiter;
        lock (_lock)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_held) { _held = true; return Task.CompletedTask; }
            waiter = new(priority, _sequence++);
            _waiters.Add(waiter);
        }
        return cancellationToken.CanBeCanceled ? WaitCoreAsync(waiter, cancellationToken) : waiter.Completion.Task;
    }

    public bool HasWaiters(int minimumPriority)
    {
        lock (_lock) return _waiters.Exists(waiter => waiter.Priority >= minimumPriority);
    }

    public void Release()
    {
        Waiter next;
        lock (_lock)
        {
            if (_waiters.Count == 0) { _held = false; return; }
            next = _waiters.OrderByDescending(waiter => waiter.Priority).ThenBy(waiter => waiter.Sequence).First();
            _waiters.Remove(next);
        }
        // Ownership passes directly to the waiter; a cancellation racing with this hand-off is observed by the holder.
        next.Completion.TrySetResult();
    }

    private async Task WaitCoreAsync(Waiter waiter, CancellationToken cancellationToken)
    {
        using (cancellationToken.Register(() =>
        {
            lock (_lock) if (!_waiters.Remove(waiter)) return;
            waiter.Completion.TrySetCanceled(cancellationToken);
        }))
            await waiter.Completion.Task.ConfigureAwait(false);
    }

    private sealed class Waiter(int priority, long sequence)
    {
        public int Priority { get; } = priority;
        public long Sequence { get; } = sequence;
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
