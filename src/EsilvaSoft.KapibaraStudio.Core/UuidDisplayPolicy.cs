using System.Collections.Frozen;

namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>
/// Immutable identifier mode plus global and per-connection UUID representation, captured before an operation starts.
/// The mode is global; only the UUID byte order can be overridden per connection.
/// </summary>
public sealed class UuidDisplayPolicy
{
    public static UuidDisplayPolicy Default { get; } = new(UuidRepresentation.Standard, null);

    public UuidDisplayPolicy(UuidRepresentation global, IReadOnlyDictionary<Guid, UuidRepresentation>? overrides,
        IdentifierRepresentationMode mode = IdentifierRepresentationMode.Standard)
    {
        if (!Enum.IsDefined(global) || overrides?.Values.Any(value => !Enum.IsDefined(value)) == true)
            throw new ArgumentOutOfRangeException(nameof(global), "Representação UUID desconhecida.");
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode), "Modo de identificador desconhecido.");
        Global = global;
        Mode = mode;
        Overrides = (overrides ?? new Dictionary<Guid, UuidRepresentation>()).ToFrozenDictionary();
    }

    public UuidRepresentation Global { get; }
    public IdentifierRepresentationMode Mode { get; }
    public IReadOnlyDictionary<Guid, UuidRepresentation> Overrides { get; }
    public UuidRepresentation Resolve(Guid? profileId) => profileId is { } id && Overrides.TryGetValue(id, out var value) ? value : Global;
    public IdentifierDisplayOptions ResolveOptions(Guid? profileId) => new(Mode, Resolve(profileId));
}
