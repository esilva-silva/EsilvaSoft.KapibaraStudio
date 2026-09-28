namespace EsilvaSoft.SlopStudio.Application;

/// <summary>Failure metadata that deliberately carries no provider message or secret material.</summary>
public sealed record SecretStoreFailure
{
    public SecretStoreFailureCode Code { get; }

    public SecretStoreFailure(SecretStoreFailureCode code)
    {
        if (!Enum.IsDefined(code))
        {
            throw new ArgumentOutOfRangeException(nameof(code));
        }

        Code = code;
    }
}
