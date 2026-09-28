namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;

/// <summary>Data-driven shape information at the cursor. A missing shape is an explicit conservative fallback.</summary>
public sealed record ShapeWalkResult(string? ShapeId, SymbolKinds ExpectedKinds, string ParentPath = "")
{
    /// <summary>
    /// Tipo de valor declarado pela forma na posição atual (por exemplo <c>FieldValue</c> ou <c>Boolean</c>), quando a
    /// forma declara valores; nulo em posições que só aceitam chaves.
    /// </summary>
    public string? ValueShape { get; init; }

    public static ShapeWalkResult Unknown { get; } = new(null, SymbolKinds.None);
}
