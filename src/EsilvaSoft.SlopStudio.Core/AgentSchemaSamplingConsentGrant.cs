namespace EsilvaSoft.SlopStudio.Core;

/// <summary>
/// Explicit local consent to sample document structure for schema derivation, scoped to one namespace and bound to
/// the exact profile generation, output destination, maximum sample size and authorization policy revision it was
/// granted for. A later regeneration of the same connection (reconnect, credential rotation), a different recipient
/// of the derived schema, a larger sample or any change of the authorization policy does not carry this consent
/// forward; it must be granted again.
/// </summary>
/// <remarks>
/// Session and turn are deliberately not part of a durable consent: the per-invocation scope is enforced by the
/// authorization grant (<see cref="AgentPermissionGrant"/>) evaluated before the consent, and binding the consent to
/// the policy revision makes any change of that grant invalidate it. Grants decoded from the legacy schema version 1
/// carry none of these bindings (<see cref="IsLegacy"/>) and never authorize sampling.
/// </remarks>
public sealed class AgentSchemaSamplingConsentGrant
{
    /// <summary>Hard ceiling of documents a schema derivation may sample (09-segurança: teto 100).</summary>
    public const int MaximumAllowedSampleSize = 100;

    internal AgentSchemaSamplingConsentGrant(
        Guid principalId,
        Guid sourceGenerationId,
        AgentNamespaceScope scope,
        AgentOutputDestination destination,
        int maximumSampleSize,
        long policyRevision,
        DateTimeOffset grantedAtUtc,
        DateTimeOffset? expiresAtUtc)
        : this(principalId, sourceGenerationId, scope, grantedAtUtc, expiresAtUtc)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (maximumSampleSize is < 1 or > MaximumAllowedSampleSize)
            throw new ArgumentOutOfRangeException(nameof(maximumSampleSize));
        if (policyRevision is < 1 or long.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(policyRevision));
        Destination = destination;
        MaximumSampleSize = maximumSampleSize;
        PolicyRevision = policyRevision;
    }

    private AgentSchemaSamplingConsentGrant(
        Guid principalId,
        Guid sourceGenerationId,
        AgentNamespaceScope scope,
        DateTimeOffset grantedAtUtc,
        DateTimeOffset? expiresAtUtc)
    {
        if (principalId == Guid.Empty)
            throw new ArgumentException("O consentimento precisa de um principal.", nameof(principalId));
        if (sourceGenerationId == Guid.Empty)
            throw new ArgumentException("O consentimento precisa da geração de origem do perfil.", nameof(sourceGenerationId));
        ArgumentNullException.ThrowIfNull(scope);
        if (expiresAtUtc is { } expires && expires <= grantedAtUtc)
            throw new ArgumentException("A expiração precisa ser posterior à concessão.", nameof(expiresAtUtc));
        PrincipalId = principalId;
        SourceGenerationId = sourceGenerationId;
        Scope = scope;
        GrantedAtUtc = grantedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid PrincipalId { get; }
    public Guid SourceGenerationId { get; }
    public AgentNamespaceScope Scope { get; }

    /// <summary>Recipient of the derived schema; null only for a legacy (schema v1) grant.</summary>
    public AgentOutputDestination? Destination { get; }

    /// <summary>Largest sample the consent covers; zero only for a legacy (schema v1) grant.</summary>
    public int MaximumSampleSize { get; }

    /// <summary>Authorization policy revision the consent was granted under; zero only for a legacy grant.</summary>
    public long PolicyRevision { get; }

    public DateTimeOffset GrantedAtUtc { get; }
    public DateTimeOffset? ExpiresAtUtc { get; }

    /// <summary>True for a grant read from schema version 1, which lacks destination/sample/policy bindings.</summary>
    public bool IsLegacy => Destination is null;

    public bool IsExpired(DateTimeOffset nowUtc) => ExpiresAtUtc is { } expires && nowUtc >= expires;

    /// <summary>
    /// Whether this consent authorizes sampling for the exact request. Every binding must match; a legacy grant never
    /// matches. The sample size is a ceiling: a smaller sample is covered, a larger one is not.
    /// </summary>
    public bool Authorizes(
        Guid principalId,
        Guid sourceGenerationId,
        AgentNamespaceScope requestedScope,
        AgentOutputDestination? destination,
        int sampleSize,
        long policyRevision,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(requestedScope);
        return !IsLegacy &&
            PrincipalId == principalId &&
            SourceGenerationId == sourceGenerationId &&
            Scope.Covers(requestedScope) &&
            Destination == destination &&
            sampleSize >= 1 && sampleSize <= MaximumSampleSize &&
            PolicyRevision == policyRevision &&
            !IsExpired(nowUtc);
    }

    /// <summary>Decodes a schema version 1 grant. It is kept for listing/revocation but never authorizes.</summary>
    internal static AgentSchemaSamplingConsentGrant Legacy(
        Guid principalId,
        Guid sourceGenerationId,
        AgentNamespaceScope scope,
        DateTimeOffset grantedAtUtc,
        DateTimeOffset? expiresAtUtc) =>
        new(principalId, sourceGenerationId, scope, grantedAtUtc, expiresAtUtc);
}
