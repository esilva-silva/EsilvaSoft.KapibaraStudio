namespace EsilvaSoft.SlopStudio.Core;

public enum AgentPermissionDenialReason
{
    None = 0,
    MissingPrincipal = 1,
    InvalidPrincipal = 2,
    MissingPermission = 3,
    UnknownPermission = 4,
    MissingRisk = 5,
    UnknownRisk = 6,
    InvalidScope = 7,
    InvalidPolicyRevision = 8,
    PolicyUnavailable = 9,
    InvalidPolicy = 10,
    PolicyRevisionMismatch = 11,
    ReadOnlyConnection = 12,
    RiskPermissionMismatch = 13,
    MissingGrant = 14,
    MissingInvocationContext = 15,
    InvalidInvocationContext = 16,
    MissingSourceGeneration = 17,
    MissingDestination = 18,
    UnknownDestination = 19,
    DestinationMismatch = 20,
    MissingOutputDataScope = 21,
    UnknownOutputDataScope = 22,
    SourceGenerationMismatch = 23
}
