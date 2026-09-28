using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    [ObservableProperty]
    private string _identifierSnippet = LocalizationViewModel.Current.Resolve("identifierSnippetInitial");

    [ObservableProperty]
    private string _identifierExtendedJsonSnippet = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IdentifierLabel))]
    private UuidRepresentation _uuidRepresentation = UuidRepresentation.Standard;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IdentifierLabel))]
    private IdentifierRepresentationMode _identifierMode = IdentifierRepresentationMode.Standard;

    public string IdentifierLabel => LocalizationViewModel.Current.Format("identifierLabel", IdentifierRepresentationService.DisplayName(IdentifierMode), UuidCodec.DisplayName(UuidRepresentation), UuidCodec.SubType(UuidRepresentation));

    [ObservableProperty]
    private string _identifierInput = "";

    [ObservableProperty]
    private string _identifierInterpretation = LocalizationViewModel.Current.Resolve("identifierInterpretationInitial");

    /// <summary>ObjectId mode generates an ObjectId, UUID v4 mode a UUID v4 and Standard one of each; both lines write the same values.</summary>
    [RelayCommand]
    private void GenerateIdentifier()
    {
        var generated = IdentifierRepresentationService.Generate(new(IdentifierMode, UuidRepresentation));
        IdentifierSnippet = string.Join("\n", generated.Select(value => value.Script));
        IdentifierExtendedJsonSnippet = string.Join("\n", generated.Select(value => value.ExtendedJson));
        StatusMessage = LocalizationViewModel.Current.Format("identifierGenerated", IdentifierRepresentationService.DisplayName(IdentifierMode));
    }

    /// <summary>Interprets pasted text with the central parser; wrappers and constructors keep their explicit BSON type.</summary>
    [RelayCommand]
    private void InterpretIdentifier()
    {
        try
        {
            var value = IdentifierRepresentationService.ParseIdentifier(IdentifierInput, new(IdentifierMode, UuidRepresentation));
            var kind = value.Kind switch
            {
                IdentifierKind.ObjectId => LocalizationViewModel.Current.Resolve("identifierObjectIdType"),
                IdentifierKind.Uuid => LocalizationViewModel.Current.Format("identifierUuidType", value.ExtendedJson.Contains("\"subType\":\"03\"", StringComparison.Ordinal) ? "3" : "4"),
                _ => LocalizationViewModel.Current.Resolve("unknownLegacyUuidValue")
            };
            var lines = new List<string>
            {
                LocalizationViewModel.Current.Format("identifierTypeLine", kind, value.IsExplicitType ? LocalizationViewModel.Current.Resolve("identifierExplicit") : LocalizationViewModel.Current.Resolve("identifierInferred")),
                LocalizationViewModel.Current.Format("identifierValueLine", value.Text),
                LocalizationViewModel.Current.Format("identifierCanonicalLine", value.ExtendedJson)
            };
            if (value.UuidEquivalent is { } uuid) lines.Add(LocalizationViewModel.Current.Format("equivalentUuidNote", uuid));
            lines.Add(LocalizationViewModel.Current.Format("identifierFilterLine", value.Text));
            IdentifierInterpretation = string.Join("\n", lines);
        }
        catch (FormatException exception)
        {
            IdentifierInterpretation = exception.Message;
        }
    }
}
