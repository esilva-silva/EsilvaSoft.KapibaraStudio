namespace EsilvaSoft.KapibaraStudio.Application;

public sealed record ApplicationOperation(Guid Id, string Description, double? Progress, bool CanCancel,
    ApplicationOperationStatus Status, ApplicationOperationPriority Priority, DateTimeOffset StartedAt)
{
    public bool IsIndeterminate => Progress is null;
}
