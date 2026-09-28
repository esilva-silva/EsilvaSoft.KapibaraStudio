using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class WorkspaceTabViewModel
{
    [RelayCommand(CanExecute = nameof(CanExplainAggregation))]
    private async Task ExplainAggregationAsync()
    {
        if (!CanExplainAggregation) return;
        var profile = Profile!;
        var query = new AggregationQuery(Database, Collection, Text, Limit);
        using var operation = Operations.Begin(LocalizationViewModel.Current.Format("executingQuery", profile.Name, query.Database + " › " + query.Collection), ApplicationOperationPriority.High);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(operation.Token);
        _cancellation = cancellation;
        IsRunning = true; Status = LocalizationViewModel.Current.Resolve("analyzingPipeline"); Errors = "";
        try
        {
            var plan = await _workspace.ExplainAggregationAsync(profile, query, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            Messages = LocalizationViewModel.Current.Format("capturedPipelinePlan", profile.Name, query.Database, query.Collection, ExtendedJsonFormatter.Format(plan));
            Status = LocalizationViewModel.Current.Resolve("planAvailable"); ResultTabIndex = 1;
            operation.Complete();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Status = LocalizationViewModel.Current.Resolve("statusCancelled"); Messages = LocalizationViewModel.Current.Resolve("analysisCancelled"); ResultTabIndex = 1;
            operation.Complete(ApplicationOperationStatus.Cancelled);
        }
        catch (Exception ex)
        {
            Status = LocalizationViewModel.Current.Resolve("analysisFailed"); Errors = DesktopOperationErrorMessages.Describe(ex); ResultTabIndex = 2;
            operation.Complete(ApplicationOperationStatus.Error);
        }
        finally { _cancellation = null; IsRunning = false; ApplyPendingUuidPolicy(); }
    }
}
