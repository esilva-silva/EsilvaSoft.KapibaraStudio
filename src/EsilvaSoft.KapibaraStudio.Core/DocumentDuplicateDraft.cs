using System.Text.Json;
using System.Text.Json.Nodes;

namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>
/// Cria um rascunho de inserção a partir de um documento Extended JSON sem reutilizar seu identificador.
/// </summary>
public static class DocumentDuplicateDraft
{
    public static string CreateWithoutId(string documentJson)
    {
        if (string.IsNullOrWhiteSpace(documentJson))
        {
            throw new ArgumentException("O documento da prévia é obrigatório.", nameof(documentJson));
        }

        try
        {
            var node = JsonNode.Parse(documentJson);
            if (node is not JsonObject document)
            {
                throw new ArgumentException("A prévia precisa ser um documento JSON para poder ser duplicada.", nameof(documentJson));
            }

            document.Remove("_id");
            return document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("A prévia não contém um documento JSON válido para duplicação.", nameof(documentJson), exception);
        }
    }
}
