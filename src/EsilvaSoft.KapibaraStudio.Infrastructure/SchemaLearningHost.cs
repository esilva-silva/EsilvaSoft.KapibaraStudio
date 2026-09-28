using EsilvaSoft.KapibaraStudio.Application.SchemaLearning;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

/// <summary>
/// Application-lifetime owner of the schema learning producer side (L15 composition).
///
/// <para><b>Why the coordinator is not a DI service of its own.</b> <see cref="SchemaLearningCoordinator"/>
/// implements only <see cref="IAsyncDisposable"/>, and <c>Microsoft.Extensions.DependencyInjection</c> throws
/// <see cref="InvalidOperationException"/> ("type only implements IAsyncDisposable") when a container holding such
/// a singleton is disposed synchronously — which is exactly what the desktop shutdown path does
/// (<c>App.axaml.cs</c>: <c>desktop.Exit += (_, _) =&gt; _serviceProvider.Dispose()</c>). Registering the
/// coordinator directly would therefore turn every application exit into a visible crash. This host is created by
/// the container instead: it builds the coordinator inside its own constructor, so the container never captures
/// the coordinator for disposal, and it exposes an ordinary synchronous <see cref="Dispose"/> that drains the
/// queue through the coordinator's own bounded shutdown. Resolve <see cref="Coordinator"/> from here when the
/// instance itself is needed; <see cref="SchemaLearningService"/> — not disposable, so safe to register — is what
/// producers inject.</para>
///
/// <para>One host, one queue, one worker, for the whole application: the coordinator is explicitly not a
/// per-request/per-tab object (schema-learning.md § Fila e amostragem).</para>
/// </summary>
public sealed class SchemaLearningHost : IDisposable
{
    private bool _disposed;

    public SchemaLearningHost(BackgroundSchemaAnalyzer analyzer, ILearnedSchemaRepository repository, LearnedSchemaCatalogSource? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(analyzer);
        ArgumentNullException.ThrowIfNull(repository);
        Coordinator = new SchemaLearningCoordinator(analyzer, repository, committed: catalog is null ? null : key => catalog.InvalidateNamespace(key));
        Service = new SchemaLearningService(Coordinator);
    }

    /// <summary>The single queue/worker of schema learning; owned here, disposed with this host.</summary>
    public SchemaLearningCoordinator Coordinator { get; }

    /// <summary>Facade the result-delivery path calls; registered in DI on top of this host.</summary>
    public SchemaLearningService Service { get; }

    /// <summary>
    /// Drains and stops the coordinator. Blocking on purpose and safe to do so: the coordinator's shutdown is
    /// bounded by its own drain/force timeouts and never awaits an unfinished worker, so this cannot hang exit.
    /// Nothing is swallowed here — a failure stopping the queue stays visible instead of becoming a silent leak.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Task.Run keeps the continuation off any UI synchronization context that might be installed at exit.
        Task.Run(async () => await Coordinator.DisposeAsync().ConfigureAwait(false)).GetAwaiter().GetResult();
    }
}
