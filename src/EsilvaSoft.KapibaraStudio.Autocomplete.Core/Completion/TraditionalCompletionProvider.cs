namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;

public sealed class TraditionalCompletionProvider(CompletionService service) : ICompletionProvider
{
    private readonly CompletionService _service = service ?? throw new ArgumentNullException(nameof(service));
    public CompletionProviderKind Kind => CompletionProviderKind.Traditional;

    public async ValueTask<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var list = await _service.CompleteAsync(request.Context, cancellationToken).ConfigureAwait(false);
        return new(request, list);
    }
}
