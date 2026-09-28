using System.Globalization;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed class ResultDocumentViewModel
{
    private IReadOnlyList<ResultFieldViewModel>? _fields;

    public ResultDocumentViewModel(string json, int position, UuidRepresentation representation = UuidRepresentation.Standard)
        : this(StructuredResultDocument.Detached(json, position), representation) { }

    public ResultDocumentViewModel(string json, int position, IdentifierDisplayOptions options)
        : this(StructuredResultDocument.Detached(json, position), options) { }

    public ResultDocumentViewModel(StructuredResultDocument document, UuidRepresentation representation = UuidRepresentation.Standard)
        : this(document, new IdentifierDisplayOptions(IdentifierRepresentationMode.Standard, representation)) { }

    public ResultDocumentViewModel(StructuredResultDocument document, IdentifierDisplayOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        Document = document;
        Options = options;
        var display = IdentifierRepresentationService.FormatForDisplay(document.Json, options);
        DisplayJson = display.Text;
        UnknownLegacyUuidCount = display.UnknownLegacyCount;
        FormattedJson = ExtendedJsonFormatter.TryFormat(document.Json, out var formatted, out _) ? IdentifierRepresentationService.FormatForDisplay(formatted, options).Text : document.Json;
        Label = LocalizationViewModel.Current.Format("documentLabel", (document.Position + 1).ToString(CultureInfo.InvariantCulture));
        if (document.Root is { ValueKind: JsonValueKind.Object } root && root.TryGetProperty("_id", out var id))
        {
            IdentityText = "_id: " + DescribeIdentity(id, options);
            Identity = IdentifierRepresentationService.Describe(id, options);
            IdentityValueText = IdentifierRepresentationService.FormatForDisplay(id.GetRawText(), options).Text;
        }
        else IdentityText = document.IsValid ? LocalizationViewModel.Current.Resolve("missingId") : LocalizationViewModel.Current.Resolve("invalidJson");
    }

    public StructuredResultDocument Document { get; }
    /// <summary>Canonical Extended JSON used for identity, write preconditions and export.</summary>
    public string Json => Document.Json;
    /// <summary>Human-readable text with ObjectId and UUID constructors, used by the Documentos clipboard and editors.</summary>
    public string DisplayJson { get; }
    /// <summary>Indented human-readable text: JSON view, read-only document window, “Copiar JSON” and the editable copy.</summary>
    public string FormattedJson { get; }
    public int UnknownLegacyUuidCount { get; }
    public IdentifierDisplayOptions Options { get; }
    public UuidRepresentation Representation => Options.Uuid;
    public string Label { get; }
    public string IdentityText { get; }
    /// <summary>The <c>_id</c> when it is an ObjectId or UUID, with its stored BSON type; null for other or missing values.</summary>
    public IdentifierValue? Identity { get; }
    /// <summary>Human text of <c>_id</c> that restores the same BSON value (“Copiar _id”); null without <c>_id</c>.</summary>
    public string? IdentityValueText { get; }
    /// <summary>UUID v4 mode only: the alternative UUID of an ObjectId <c>_id</c>. The query and the document keep the ObjectId.</summary>
    public string? IdentityUuidEquivalent => Options.Mode == IdentifierRepresentationMode.UuidV4 && Identity is { Kind: IdentifierKind.ObjectId } identity ? identity.UuidEquivalent : null;
    /// <summary>Console query that finds this document by its stored <c>_id</c> type; null without a known collection or <c>_id</c>.</summary>
    public string? IdentityScript => IdentityValueText is { } id && IdentityFilter is not null && Document.Set.Origin.Collection is { Length: > 0 } collection
        ? "db.getCollection(" + JsonSerializer.Serialize(collection) + ").find({ _id: " + id + " })"
        : null;
    public string? IdentityFilter => Document.IsValid ? Document.IdentityFilter : null;
    public string OriginText => Document.Set.Origin.Source;
    public string DestinationText => Document.Set.Origin.Destination;
    public string Summary => Label + " · " + Document.Set.Label;
    public string FlagsText => string.Join(" · ", new[]
    {
        Document.IsTruncated ? LocalizationViewModel.Current.Resolve("limitedNotice").ToLowerInvariant() : null,
        Document.Set.Completeness switch { ResultCompleteness.PartialProjection => LocalizationViewModel.Current.Resolve("partialProjection").ToLowerInvariant(), ResultCompleteness.Derived => LocalizationViewModel.Current.Resolve("derivedAggregation"), _ => null },
        Document.IsValid ? null : LocalizationViewModel.Current.Resolve("invalidJson")
    }.OfType<string>());
    public IReadOnlyList<ResultFieldViewModel> Fields => _fields ??= Document.Root is { } root ? new ResultFieldViewModel("documento", root, Options).Children : [];

    private static string DescribeIdentity(JsonElement id, IdentifierDisplayOptions options)
    {
        var description = ExtendedJsonValue.Describe(id, options);
        var text = description.Shape == ExtendedJsonShape.Scalar ? description.Display : id.GetRawText();
        return text.Length > 120 ? text[..120] + "…" : text;
    }
}
