namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

/// <summary>A background generation yielded the model to an explicit user action.</summary>
public sealed class LocalModelPreemptedException : LocalModelUnavailableException
{
    public LocalModelPreemptedException() : base("Geração em segundo plano substituída por uma ação explícita.") { }
    public LocalModelPreemptedException(string message) : base(message) { }
    public LocalModelPreemptedException(string message, Exception? innerException) : base(message, innerException) { }
}
