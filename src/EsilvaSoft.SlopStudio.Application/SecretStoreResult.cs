namespace EsilvaSoft.SlopStudio.Application;

/// <summary>Result of an operation that does not return secret material.</summary>
public sealed class SecretStoreOperationResult
{
    private SecretStoreOperationResult(SecretStoreFailure? failure) => Failure = failure;

    public bool IsSuccess => Failure is null;
    public SecretStoreFailure? Failure { get; }

    public static SecretStoreOperationResult Success() => new(null);

    public static SecretStoreOperationResult Failed(SecretStoreFailureCode code) =>
        new(new SecretStoreFailure(code));
}

/// <summary>Typed result that never includes provider exception text on failure.</summary>
public sealed class SecretStoreResult<T>
{
    private readonly T? _value;

    internal SecretStoreResult(T? value, SecretStoreFailure? failure)
    {
        _value = value;
        Failure = failure;
    }

    public bool IsSuccess => Failure is null;
    public SecretStoreFailure? Failure { get; }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("A operação do cofre não foi concluída.");

}

public static class SecretStoreResults
{
    public static SecretStoreResult<T> Success<T>(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new SecretStoreResult<T>(value, null);
    }

    public static SecretStoreResult<T> Failed<T>(SecretStoreFailureCode code) =>
        new(default, new SecretStoreFailure(code));
}
