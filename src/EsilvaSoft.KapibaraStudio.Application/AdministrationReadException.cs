namespace EsilvaSoft.KapibaraStudio.Application;

public enum AdministrationReadFailure
{
    PermissionDenied,
    Unavailable
}

public sealed class AdministrationReadException(
    AdministrationReadFailure failure,
    string source,
    Exception innerException) : Exception($"{source}: {failure}", innerException)
{
    public AdministrationReadFailure Failure { get; } = failure;
    public string CommandSource { get; } = source;
}
