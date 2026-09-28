using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application;

/// <summary>Reads a sanitized snapshot of legacy credential locations before any migration.</summary>
public interface ILegacyCredentialInventoryRepository
{
    Task<LegacyCredentialInventory> ReadAsync(CancellationToken cancellationToken = default);
}
