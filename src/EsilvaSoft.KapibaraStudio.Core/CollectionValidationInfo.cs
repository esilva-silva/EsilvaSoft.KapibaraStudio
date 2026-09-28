namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Describes the validator options currently reported by a MongoDB collection.</summary>
public sealed record CollectionValidationInfo(
    string ValidatorJson,
    CollectionValidationLevel ValidationLevel,
    CollectionValidationAction ValidationAction);
