using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application;

/// <summary>Resolves an opaque credential reference for immediate, in-process use by an agent adapter.</summary>
/// <remarks>Implementations must not place resolved values in prompts, events, audit records, or diagnostics.</remarks>
public interface IAgentCredentialProvider
{
    Task<SecretStoreResult<string>> ResolveAsync(SecretReference reference, CancellationToken cancellationToken = default);
}
