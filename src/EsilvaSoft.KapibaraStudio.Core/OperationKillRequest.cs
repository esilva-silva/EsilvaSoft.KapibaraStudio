using System.Globalization;

namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Requires the exact numeric operation identifier before requesting MongoDB to stop it.</summary>
public sealed record OperationKillRequest(string OperationId, string ConfirmationOperationId)
{
    public long GetOperationId()
    {
        if (!long.TryParse(OperationId?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var operationId)
            || operationId < 1)
        {
            throw new ArgumentException("O identificador da operação precisa ser um inteiro positivo.", nameof(OperationId));
        }

        if (!string.Equals(operationId.ToString(CultureInfo.InvariantCulture), ConfirmationOperationId?.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Digite o identificador exato da operação para confirmar a interrupção.", nameof(ConfirmationOperationId));
        }

        return operationId;
    }
}
