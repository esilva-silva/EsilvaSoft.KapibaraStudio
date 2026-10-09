using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Defines a confirmed update to a MongoDB view pipeline.</summary>
public sealed record ViewUpdateRequest(
    string Database,
    string View,
    string PipelineJson,
    string ConfirmationName,
    string? SourceCollection = null)
{
    public ViewUpdateRequest Validate()
    {
        if (string.IsNullOrWhiteSpace(Database))
        {
            throw new ArgumentException("O banco de dados é obrigatório.", nameof(Database));
        }

        if (string.IsNullOrWhiteSpace(View) || View.StartsWith("system.", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A view informada não pode ser alterada pela interface.", nameof(View));
        }

        if (!string.Equals(View, ConfirmationName?.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Digite o nome exato da view para confirmar a alteração.", nameof(ConfirmationName));
        }

        if (string.IsNullOrWhiteSpace(PipelineJson))
        {
            throw new ArgumentException("O pipeline da view é obrigatório.", nameof(PipelineJson));
        }

        if (SourceCollection is not null
            && (string.IsNullOrWhiteSpace(SourceCollection)
                || SourceCollection.StartsWith("system.", StringComparison.OrdinalIgnoreCase)
                || string.Equals(View, SourceCollection, StringComparison.Ordinal)))
        {
            throw new ArgumentException("A origem da view precisa ser outra coleção ou view de usuário.", nameof(SourceCollection));
        }

        try
        {
            using var pipeline = JsonDocument.Parse(PipelineJson);
            if (pipeline.RootElement.ValueKind != JsonValueKind.Array
                || pipeline.RootElement.EnumerateArray().Any(stage => stage.ValueKind != JsonValueKind.Object))
            {
                throw new ArgumentException("O pipeline da view precisa ser um array de documentos JSON.", nameof(PipelineJson));
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("O pipeline da view não contém JSON válido.", nameof(PipelineJson), exception);
        }

        return this;
    }
}
