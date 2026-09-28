using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Defines a confirmed collection validator change sent through MongoDB collMod.</summary>
public sealed record CollectionValidationRequest(
    string Database,
    string Collection,
    string ValidatorJson,
    CollectionValidationLevel ValidationLevel,
    CollectionValidationAction ValidationAction,
    string ConfirmationName)
{
    public CollectionValidationRequest Validate()
    {
        if (string.IsNullOrWhiteSpace(Database))
        {
            throw new ArgumentException("O banco de dados é obrigatório.", nameof(Database));
        }

        if (string.IsNullOrWhiteSpace(Collection) || Collection.StartsWith("system.", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A coleção informada não pode ser alterada pela interface.", nameof(Collection));
        }

        if (!string.Equals(Collection, ConfirmationName?.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Digite o nome exato da coleção para confirmar a alteração da validação.", nameof(ConfirmationName));
        }

        if (!Enum.IsDefined(ValidationLevel) || !Enum.IsDefined(ValidationAction))
        {
            throw new ArgumentException("A configuração de validação informada não é suportada.");
        }

        try
        {
            using var validator = JsonDocument.Parse(ValidatorJson);
            if (validator.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("O validador precisa ser um documento JSON.", nameof(ValidatorJson));
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("O validador não contém JSON válido.", nameof(ValidatorJson), exception);
        }

        return this;
    }
}
