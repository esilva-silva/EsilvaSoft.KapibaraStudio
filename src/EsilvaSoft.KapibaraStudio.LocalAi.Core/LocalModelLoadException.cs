namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

public sealed class LocalModelLoadException : Exception
{
    public LocalModelLoadException() { }
    public LocalModelLoadException(string message) : base(message) { }
    public LocalModelLoadException(string message, Exception? innerException) : base(message, innerException) { }
    public LocalModelLoadException(LocalModelLoadStage stage, string message, Exception? innerException = null) : base(message, innerException) => Stage = stage;
    public LocalModelLoadStage Stage { get; } = LocalModelLoadStage.Session;
}
