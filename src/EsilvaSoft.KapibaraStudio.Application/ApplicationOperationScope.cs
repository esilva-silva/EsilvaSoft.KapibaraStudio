namespace EsilvaSoft.KapibaraStudio.Application;

public sealed class ApplicationOperationScope : IDisposable
{
    private readonly ApplicationOperationService _owner;
    private readonly CancellationTokenSource _cancellation;
    private readonly object _gate = new();
    private ApplicationOperation _snapshot;
    private long _lastProgress;
    private bool _completed;
    private bool _disposed;
    public ApplicationOperation Snapshot { get { lock (_gate) return _snapshot; } }
    public CancellationToken Token { get; }

    internal ApplicationOperationScope(ApplicationOperationService owner, string description, ApplicationOperationPriority priority, bool canCancel, CancellationToken token)
    {
        _owner = owner;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        Token = _cancellation.Token;
        _snapshot = new(Guid.NewGuid(), description, null, canCancel, ApplicationOperationStatus.Running, priority, DateTimeOffset.UtcNow);
    }

    public void Report(long completed, long total, string? description = null)
    {
        lock (_gate)
        {
            if (_completed || Environment.TickCount64 - _lastProgress < 100 && !(total > 0 && completed >= total && _snapshot.Progress != 100)) return;
            _lastProgress = Environment.TickCount64;
            _snapshot = _snapshot with { Description = description ?? _snapshot.Description,
                Progress = total > 0 ? Math.Clamp(100d * completed / total, 0, 100) : null };
        }
        _owner.Update(this, false);
    }

    public void Complete(ApplicationOperationStatus status = ApplicationOperationStatus.Success, string? description = null)
    {
        if (status is ApplicationOperationStatus.Idle or ApplicationOperationStatus.Running) throw new ArgumentOutOfRangeException(nameof(status));
        lock (_gate)
        {
            if (_completed) return;
            _completed = true;
            _snapshot = _snapshot with { Status = status, Description = description ?? _snapshot.Description, CanCancel = false };
        }
        _owner.Update(this, true);
    }

    internal void Cancel()
    {
        lock (_gate)
        {
            if (_disposed || _completed || !_snapshot.CanCancel) return;
            // Schedule callbacks: a driver callback must never block the status-bar click.
            _ = _cancellation.CancelAsync();
        }
    }

    public void Dispose()
    {
        Complete(Token.IsCancellationRequested ? ApplicationOperationStatus.Cancelled : ApplicationOperationStatus.Warning);
        lock (_gate) { if (_disposed) return; _disposed = true; _cancellation.Dispose(); }
    }
}
