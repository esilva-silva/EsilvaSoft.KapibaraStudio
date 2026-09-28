namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;

/// <summary>Reason why completion was requested. The engine is pure and does not turn an automatic request into I/O.</summary>
public enum CompletionTrigger : byte { Invoked, TriggerCharacter, Automatic }
