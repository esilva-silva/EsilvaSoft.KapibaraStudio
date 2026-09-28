namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

/// <summary>The request does not fit the model window. A request error, not a model failure: no unload or cooldown.</summary>
public sealed class LocalModelContextException : ArgumentException
{
    public LocalModelContextException() { }
    public LocalModelContextException(string message) : base(message) { }
    public LocalModelContextException(string message, Exception? innerException) : base(message, innerException) { }
}
