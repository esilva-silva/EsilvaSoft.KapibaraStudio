using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private Task<bool> RunUserAdministrationAsync(Func<CancellationToken, Task> operation) =>
        RunAsync(async cancellationToken =>
        {
            try { await operation(cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (OperationCanceledException)
            {
                throw new MongoUserAdministrationException(MongoUserAdministrationFailureKind.CommandFailed);
            }
            catch (MongoUserAdministrationException) { throw; }
            catch (Exception exception) when (exception.GetType().Name.StartsWith("Mongo", StringComparison.Ordinal)) { throw; }
            catch (Exception exception)
            {
                throw new MongoUserAdministrationException(exception is ArgumentException or FormatException or JsonException
                    ? MongoUserAdministrationFailureKind.InvalidInput
                    : MongoUserAdministrationFailureKind.CommandFailed);
            }
        });

    private Task<bool> RunRoleAdministrationAsync(Func<CancellationToken, Task> operation) =>
        RunAsync(async cancellationToken =>
        {
            try { await operation(cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (OperationCanceledException)
            {
                throw new MongoRoleAdministrationException(MongoRoleAdministrationFailureKind.CommandFailed);
            }
            catch (MongoRoleAdministrationException) { throw; }
            catch (Exception exception) when (exception.GetType().Name.StartsWith("Mongo", StringComparison.Ordinal)) { throw; }
            catch (Exception exception)
            {
                throw new MongoRoleAdministrationException(exception is ArgumentException or FormatException or JsonException
                    ? MongoRoleAdministrationFailureKind.InvalidInput
                    : MongoRoleAdministrationFailureKind.CommandFailed);
            }
        });

    private async Task RecordSensitiveAdministrationAuditAsync(string action, ConnectionProfile profile,
        string database, string summary)
    {
        try
        {
            await _workspace.SaveAuditAsync(AuditEntry.Create(action, profile.Id, database, null, summary));
        }
        catch
        {
            // A local store failure may include a path or serialized payload in its exception message.
            if (ReferenceEquals(SelectedProfile, profile)
                && string.Equals(SelectedDatabase, database, StringComparison.Ordinal))
                StatusMessage = T("sensitiveAuditNotRecorded");
        }
    }
}
