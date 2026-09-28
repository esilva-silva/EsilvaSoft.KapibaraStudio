using CommunityToolkit.Mvvm.ComponentModel;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

/// <summary>Read-only presentation of one result document. Built from memory only; it has no access to the workspace service.</summary>
public sealed partial class DocumentJsonViewModel(ResultDocumentViewModel document, double codeFontSize = 14) : ObservableObject
{
    private static string T(string key) => LocalizationViewModel.Current.Resolve(key);
    private static string F(string key, params object?[] args) => LocalizationViewModel.Current.Format(key, args);
    public ResultDocumentViewModel Document => document;
    public string WindowTitle => document.Label + " · JSON";
    public string Heading => document.Label + " · " + document.Document.Set.Label;
    public string Origin => document.OriginText;
    public string Destination => document.DestinationText;
    public string Identity => document.IdentityText + (document.FlagsText.Length == 0 ? "" : " · " + document.FlagsText);
    public string Presentation => F("identifierPresentation", IdentifierRepresentationService.DisplayName(document.Options.Mode), UuidCodec.DisplayName(document.Representation))
        + (document.UnknownLegacyUuidCount == 0 ? string.Empty : F("legacyUuidOrigin", document.UnknownLegacyUuidCount)) + T("canonicalExtendedJson");
    public string Json => document.FormattedJson;
    public double CodeFontSize => codeFontSize;
    public double CodeLineHeight => codeFontSize * 1.5;
    [ObservableProperty] private string _status = T("readOnlyDocumentStatus");
}
