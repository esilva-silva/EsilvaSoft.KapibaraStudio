namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;

public sealed record CompletionResponse(CompletionRequest Request, CompletionList List)
{
    public bool IsFor(CompletionRequest request) => Request.RequestId == request.RequestId
        && Request.PresentationGeneration == request.PresentationGeneration
        && Request.Context.Version == request.Context.Version;
}
