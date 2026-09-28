namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

/// <summary>An explicit user action (chat, model test) enters the model queue before background autocomplete.</summary>
public enum AiRequestPriority { Background, Interactive }
