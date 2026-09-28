namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;

/// <summary>Owns cancellation for one editor only; no token source is shared between tabs.</summary>
public sealed class EditorRequestScope : IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _currentCancellation;
    private CompletionRequest? _current;
    private bool _disposed;

    public RequestLease Begin(CompletionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            ThrowIfDisposed();
            _currentCancellation?.Cancel();
            _currentCancellation?.Dispose();
            _current = request;
            _currentCancellation = new CancellationTokenSource();
            return new(this, request, _currentCancellation.Token);
        }
    }

    public bool IsCurrent(CompletionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate) return !_disposed && _current is not null
            && _current.RequestId == request.RequestId
            && _current.PresentationGeneration == request.PresentationGeneration
            && _current.Context.Version == request.Context.Version;
    }

    public void Cancel()
    {
        lock (_gate) _currentCancellation?.Cancel();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _currentCancellation?.Cancel();
            _currentCancellation?.Dispose();
            _currentCancellation = null;
            _current = null;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(EditorRequestScope));
    }

    public sealed class RequestLease(EditorRequestScope owner, CompletionRequest request, CancellationToken cancellationToken) : IDisposable
    {
        private readonly EditorRequestScope _owner = owner;
        public CompletionRequest Request { get; } = request;
        public CancellationToken CancellationToken { get; } = cancellationToken;
        public bool IsCurrent => _owner.IsCurrent(Request);
        public void Dispose() { }
    }
}
