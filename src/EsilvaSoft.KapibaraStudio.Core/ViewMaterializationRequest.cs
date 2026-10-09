using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Explicit human request to replace one same-database collection with a view's current result.</summary>
public sealed record ViewMaterializationRequest(
    string Database,
    string View,
    string Destination,
    string PipelineJson,
    string ConfirmationName,
    string ExpectedViewDefinitionJson,
    string ExpectedDestinationDefinitionJson)
{
    public ViewMaterializationRequest Validate()
    {
        if (string.IsNullOrWhiteSpace(Database))
            throw new ArgumentException("O banco de dados é obrigatório.", nameof(Database));
        if (string.IsNullOrWhiteSpace(View) || View.StartsWith("system.", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A origem precisa ser uma view de usuário.", nameof(View));
        if (string.IsNullOrWhiteSpace(Destination)
            || Destination.StartsWith("system.", StringComparison.OrdinalIgnoreCase)
            || string.Equals(View, Destination, StringComparison.Ordinal))
            throw new ArgumentException("O destino precisa ser outra coleção de usuário no mesmo banco.", nameof(Destination));
        if (!string.Equals(Destination, ConfirmationName?.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Digite o nome exato do destino para confirmar a substituição.", nameof(ConfirmationName));
        if (string.IsNullOrWhiteSpace(ExpectedViewDefinitionJson)
            || ExpectedViewDefinitionJson == "{}"
            || string.IsNullOrWhiteSpace(ExpectedDestinationDefinitionJson))
            throw new ArgumentException("Revise a definição da view e do destino antes de executar.");
        if (string.IsNullOrWhiteSpace(PipelineJson))
            throw new ArgumentException("Informe um pipeline JSON, mesmo que seja [].", nameof(PipelineJson));

        try
        {
            using var pipeline = JsonDocument.Parse(PipelineJson);
            if (pipeline.RootElement.ValueKind != JsonValueKind.Array
                || pipeline.RootElement.GetArrayLength() > 20
                || pipeline.RootElement.EnumerateArray().Any(stage => stage.ValueKind != JsonValueKind.Object
                    || stage.EnumerateObject().Count() != 1))
                throw new ArgumentException("Informe até 20 estágios JSON com um operador por estágio.", nameof(PipelineJson));
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("O pipeline não contém JSON válido.", nameof(PipelineJson), exception);
        }

        return this;
    }
}

public sealed record ViewMaterializationResult(
    bool ReplacedExisting,
    string DestinationDefinitionJson);
