using EsilvaSoft.KapibaraStudio.Autocomplete.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>
/// Um campo da coleção alvo no vocabulário de tipos do contrato <c>editor-context-v2</c> (o mesmo dos dados de
/// treino: <c>string</c>, <c>objectid</c>, <c>int32</c>, <c>int64</c>, <c>decimal128</c>, <c>array</c>...).
/// </summary>
/// <param name="Path">Caminho pontilhado do campo.</param>
/// <param name="Type">Tipo predominante.</param>
/// <param name="Item">Tipo dos elementos quando <paramref name="Type"/> é <c>array</c> e ele é conhecido.</param>
public sealed record AutocompleteTargetField(string Path, string Type, string? Item = null)
{
    /// <summary>
    /// Projeta um schema inferido (amostra, validador ou aprendido) para a linha <c>TARGET FIELDS</c>, na ordem de
    /// descoberta. Campos sem tipo conhecido ficam de fora: o modelo nunca viu <c>unknown</c> no treino. Arrays
    /// recebem <c>array&lt;object&gt;</c> quando os elementos são documentos; o schema não guarda o tipo de elementos
    /// escalares, e então o campo sai como <c>array</c>.
    /// </summary>
    public static IReadOnlyList<AutocompleteTargetField> FromSchema(CollectionSchema? schema)
    {
        if (schema is null) return [];
        var fields = new List<AutocompleteTargetField>();
        foreach (var node in schema.Descendants())
        {
            if (node.Types.Count == 0) continue;
            var type = ContractType(node.PrimaryType);
            if (type == "array")
                fields.Add(new(node.Path, "array", node.Flags.HasFlag(FieldTraits.ArrayOfDocuments) ? "object" : null));
            else
                fields.Add(new(node.Path, type));
        }
        return fields;
    }

    /// <summary>Nome BSON do app → vocabulário do contrato (o mesmo de <c>render_contract.py</c>).</summary>
    public static string ContractType(string bsonType)
    {
        ArgumentNullException.ThrowIfNull(bsonType);
        return bsonType switch
        {
            "int" or "integer" => "int32",
            "long" => "int64",
            "decimal" => "decimal128",
            "boolean" => "bool",
            "document" => "object",
            _ => bsonType.ToLowerInvariant()
        };
    }
}
