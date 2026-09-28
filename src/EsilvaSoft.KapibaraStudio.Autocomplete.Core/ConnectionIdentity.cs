using System.Security.Cryptography;
using System.Text;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

/// <summary>
/// Identity of a connection for metadata: profile, explicit target and a hash of the saved connection string.
/// Editing a profile or routing to another instance never reuses metadata from the previous configuration.
/// </summary>
/// <remarks>
/// Fingerprints the raw saved string, not the value resolved by <c>OperationEnvironment</c>: resolution is async and
/// I/O-bound, and an ENV/vault revision is volatile and must not enter this key. See PEND-K11-SECRET in decisions.md.
/// </remarks>
public sealed record ConnectionIdentity(Guid ProfileId, string? TargetHost, string Fingerprint)
{
    public static ConnectionIdentity From(ConnectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(profile.ConnectionString + "\n" + profile.TargetHost));
        return new(profile.Id, profile.TargetHost, Convert.ToHexString(hash, 0, 8));
    }
}
