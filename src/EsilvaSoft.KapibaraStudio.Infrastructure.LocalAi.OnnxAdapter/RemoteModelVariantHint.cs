namespace EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;

/// <summary>When to choose a published folder; the list order is the display order.</summary>
public sealed record RemoteModelVariantHint(string Folder, string Hint, string? LocalizationKey = null);
