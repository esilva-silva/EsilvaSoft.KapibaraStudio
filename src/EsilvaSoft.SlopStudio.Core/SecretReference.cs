namespace EsilvaSoft.SlopStudio.Core;

/// <summary>Opaque, versioned pointer to an operating-system secret; it contains no credential material.</summary>
public sealed record SecretReference
{
    public Guid Id { get; }
    public int Version { get; }

    public SecretReference(Guid id, int version = 1)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A referência do segredo precisa ter um identificador.", nameof(id));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);

        Id = id;
        Version = version;
    }
}
