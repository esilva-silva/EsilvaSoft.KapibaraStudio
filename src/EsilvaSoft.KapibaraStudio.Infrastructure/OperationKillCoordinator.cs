using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

/// <summary>Rechecks the captured target immediately before requesting interruption.</summary>
internal static class OperationKillCoordinator
{
    internal static async Task ExecuteAsync(
        OperationKillRequest request,
        Func<CancellationToken, Task<string>> readCurrentOperations,
        Func<long, CancellationToken, Task> requestKill,
        CancellationToken cancellationToken)
    {
        var operationId = request.ValidateForCurrentOperation();
        var currentOperations = await readCurrentOperations(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!request.MatchesCurrentOperation(currentOperations))
            throw new InvalidOperationException("A operação não corresponde mais ao alvo carregado. Atualize a lista antes de tentar novamente.");

        await requestKill(operationId, cancellationToken).ConfigureAwait(false);
    }
}
