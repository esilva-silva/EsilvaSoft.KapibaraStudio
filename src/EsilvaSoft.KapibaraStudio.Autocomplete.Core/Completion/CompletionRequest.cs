namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;

/// <summary>Identity captured with the request; a response is never valid for another stamp.</summary>
public sealed record CompletionRequest
{
    public CompletionRequest(CompletionContext context, long requestId, int presentationGeneration)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestId);
        ArgumentOutOfRangeException.ThrowIfNegative(presentationGeneration);
        Context = context;
        RequestId = requestId;
        PresentationGeneration = presentationGeneration;
    }

    public CompletionContext Context { get; }
    public long RequestId { get; }
    public int PresentationGeneration { get; }
}
