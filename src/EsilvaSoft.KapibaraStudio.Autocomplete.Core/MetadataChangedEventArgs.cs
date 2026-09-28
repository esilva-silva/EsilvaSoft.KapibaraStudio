namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

/// <summary>
/// Raised after write-through, invalidation, disconnection, a stored sample and the terminal state of every background load
/// (success, failure, cancellation or superseded result) whose key is still cached. Handlers run on the thread that changed the cache.
/// </summary>
public sealed class MetadataChangedEventArgs(Guid profileId, MetadataKey? key = null) : EventArgs
{
    public Guid ProfileId { get; } = profileId;
    /// <summary>The single key whose state changed; null when several keys of the profile may have changed and every one must be re-read.</summary>
    public MetadataKey? Key { get; } = key;
}
