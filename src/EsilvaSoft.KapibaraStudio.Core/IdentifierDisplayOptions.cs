namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Mode and UUID representation resolved for one connection before an operation starts.</summary>
public readonly record struct IdentifierDisplayOptions(IdentifierRepresentationMode Mode, UuidRepresentation Uuid)
{
    public static IdentifierDisplayOptions Default => new(IdentifierRepresentationMode.Standard, UuidRepresentation.Standard);
}
