namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>Independent cancellation and immutable snapshots; subscribers marshal notifications to their UI dispatcher.</summary>
public sealed class ApplicationOperationService : IApplicationOperationService
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, ApplicationOperationScope> _active = [];
    private ApplicationOperation? _lastCompleted;
    public event EventHandler? Changed;
    public IReadOnlyList<ApplicationOperation> ActiveOperations
    {
        get { lock (_gate) return _active.Values.Select(s => s.Snapshot).OrderByDescending(s => s.Priority).ThenBy(s => s.StartedAt).ToArray(); }
    }
    public ApplicationOperation? LastCompleted { get { lock (_gate) return _lastCompleted; } }

    public ApplicationOperationScope Begin(string description, ApplicationOperationPriority priority = ApplicationOperationPriority.Normal,
        bool canCancel = true, CancellationToken cancellationToken = default)
    {
        var scope = new ApplicationOperationScope(this, description, priority, canCancel, cancellationToken);
        lock (_gate) _active.Add(scope.Snapshot.Id, scope);
        Changed?.Invoke(this, EventArgs.Empty);
        return scope;
    }

    public void Cancel(Guid id)
    {
        ApplicationOperationScope? scope;
        lock (_gate) _active.TryGetValue(id, out scope);
        scope?.Cancel();
    }

    internal void Update(ApplicationOperationScope scope, bool completed)
    {
        lock (_gate)
        {
            if (!_active.ContainsKey(scope.Snapshot.Id)) return;
            if (completed) { _active.Remove(scope.Snapshot.Id); _lastCompleted = scope.Snapshot; }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
