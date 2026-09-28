using CommunityToolkit.Mvvm.ComponentModel;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

/// <param name="workspace">Application service that performs the confirmed operation.</param>
/// <param name="profile">Destination captured when the editor opened.</param>
/// <param name="database">Destination database.</param>
/// <param name="collection">Destination collection.</param>
/// <param name="original">Snapshot being edited or deleted; null for insertion.</param>
/// <param name="operation">Inserir, Editar or Excluir.</param>
/// <param name="rereadBeforeWrite">
/// True when the snapshot came from a result in memory: after confirmation the document is read again by <c>_id</c> and
/// compared before the write, which still uses the <c>$$ROOT</c> precondition.
/// </param>
/// <param name="writeBlockReason">Evaluated when saving; a non-null text blocks the write, e.g. a closed connection.</param>
public sealed partial class DocumentMutationViewModel(WorkspaceService workspace, ConnectionProfile profile, string database, string collection, ResultDocumentViewModel? original, string operation,
    bool rereadBeforeWrite = false, Func<string?>? writeBlockReason = null) : ObservableObject, IDisposable
{
    private static string T(string key) => LocalizationViewModel.Current.Resolve(key);
    private static string F(string key, params object?[] args) => LocalizationViewModel.Current.Format(key, args);
    private CancellationTokenSource? _cancellation;
    public string Context => $"{profile.Name} › {database} › {collection} · {profile.RoutingLabel}";
    public string Operation => operation;
    public string ApplyLabel => operation switch { "Editar" => T("mutationEditLabel"), "Excluir" => T("mutationDeleteLabel"), _ => operation + "…" };
    public string Identity => original?.IdentityFilter ?? T("newDocument");
    public string Policy => profile.IsReadOnly
        ? T("mutationReadOnlyPolicy")
        : rereadBeforeWrite
            ? T("mutationRereadPolicy")
            : T("mutationWritePolicy");
    public bool IsDelete => Operation == "Excluir";
    public string? WriteBlockReason => profile.IsReadOnly ? T("mutationReadOnlyBlocked") : writeBlockReason?.Invoke();
    public bool CanApply => !IsRunning && WriteBlockReason is null;
    // The editable text uses the UUID constructors; the precondition below keeps the canonical snapshot.
    [ObservableProperty] private string _text = original?.FormattedJson ?? "{}";
    [ObservableProperty] private string _status = (profile.IsReadOnly ? T("mutationReadOnlyBlocked") : writeBlockReason?.Invoke()) ?? T("mutationReviewConfirm");
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanApply))] private bool _isRunning;
    public bool Succeeded { get; private set; }
    public DocumentWriteConflict Conflict { get; private set; }

    public async Task ExecuteConfirmedAsync()
    {
        if (IsRunning) return;
        profile.EnsureWriteAllowed();
        if (writeBlockReason?.Invoke() is { } blocked) { Status = blocked; return; }
        if (operation != "Inserir" && original?.IdentityFilter is null) throw new InvalidOperationException(T("mutationIdRequired"));
        var text = Text;
        var operationLabel = operation switch { "Inserir" => T("insert"), "Editar" => T("edit"), "Excluir" => T("remove"), _ => operation };
        using var globalOperation = workspace.Operations.Begin(F("documentOperationContext", operationLabel, Context), ApplicationOperationPriority.High);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(globalOperation.Token); _cancellation = cancellation;
        Succeeded = false; Conflict = DocumentWriteConflict.None; IsRunning = true;
        try
        {
            var snapshot = original?.Json;
            if (rereadBeforeWrite && original is not null && operation != "Inserir")
            {
                Status = T("mutationRereading");
                var page = await workspace.QueryAsync(profile, new MongoQuery(database, collection, original.IdentityFilter!, Limit: 1), cancellation.Token);
                if (page.Documents.Count == 0)
                {
                    Conflict = DocumentWriteConflict.Removed;
                    Status = T("mutationRemovedAfterRead");
                    return;
                }
                if (!await Task.Run(() => ExtendedJsonComparer.AreEquivalent(page.Documents[0], original.Json), cancellation.Token))
                {
                    Conflict = DocumentWriteConflict.Changed;
                    Status = T("mutationChangedAfterRead");
                    return;
                }
                snapshot = page.Documents[0];
            }
            var filter = original is null ? "{}" : "{\"$and\":[" + original.IdentityFilter + ",{\"$expr\":{\"$eq\":[\"$$ROOT\",{\"$literal\":" + snapshot + "}]}}]}";
            var result = operation switch
            {
                "Inserir" => await workspace.InsertAsync(profile, database, collection, text, cancellation.Token),
                "Editar" => await workspace.ReplaceAsync(profile, database, collection, filter, text, cancellation.Token),
                "Excluir" => await workspace.DeleteAsync(profile, database, collection, filter, cancellation.Token),
                _ => throw new InvalidOperationException(T("unknownDocumentOperation"))
            };
            Succeeded = result.MatchedCount > 0; Status = Succeeded ? T("mutationCompletedRefresh") : T("mutationConflict");
        }
        catch (OperationCanceledException) { Status = T("mutationCancelled"); }
        catch (Exception ex) { Status = F("failurePrefix", DesktopOperationErrorMessages.Describe(ex)); }
        finally { globalOperation.Complete(cancellation.IsCancellationRequested ? ApplicationOperationStatus.Cancelled : Succeeded ? ApplicationOperationStatus.Success : Conflict != DocumentWriteConflict.None ? ApplicationOperationStatus.Warning : ApplicationOperationStatus.Error, Status); IsRunning = false; _cancellation = null; }
    }
    public void Cancel() => _cancellation?.Cancel();
    public void Dispose() => Cancel();
}
