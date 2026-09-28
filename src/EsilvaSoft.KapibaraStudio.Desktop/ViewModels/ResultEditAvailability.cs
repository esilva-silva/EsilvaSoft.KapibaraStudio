namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

/// <summary>Whether a result document can be opened as an editable copy, and why not.</summary>
public sealed record ResultEditAvailability(bool CanOpen, string Reason);
