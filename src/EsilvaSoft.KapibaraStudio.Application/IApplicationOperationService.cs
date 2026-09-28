namespace EsilvaSoft.KapibaraStudio.Application;

public interface IApplicationOperationService
{
    IReadOnlyList<ApplicationOperation> ActiveOperations { get; }
    ApplicationOperation? LastCompleted { get; }
    event EventHandler? Changed;
    ApplicationOperationScope Begin(string description, ApplicationOperationPriority priority = ApplicationOperationPriority.Normal,
        bool canCancel = true, CancellationToken cancellationToken = default);
    void Cancel(Guid id);
}
