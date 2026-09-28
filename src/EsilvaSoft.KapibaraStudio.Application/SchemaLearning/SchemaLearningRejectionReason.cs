namespace EsilvaSoft.KapibaraStudio.Application.SchemaLearning;

/// <summary>Typed reason a result set did not enter the learning queue. Never a magic string.</summary>
public enum SchemaLearningRejectionReason
{
    /// <summary>Admitted; no rejection.</summary>
    None = 0,
    /// <summary>Collection is turned off globally or for this profile.</summary>
    CollectionDisabled,
    /// <summary>Method is not a raw collection read (<c>aggregate</c>, <c>count</c>, script output, unknown).</summary>
    MethodNotEligible,
    /// <summary>A projection was applied, so absent fields are not evidence of absence.</summary>
    ProjectedResult,
    /// <summary>Documents were computed by a pipeline, not stored as returned.</summary>
    DerivedResult,
    /// <summary>Completeness could not be established.</summary>
    UnknownCompleteness,
    /// <summary>Origin carries no profile identifier.</summary>
    OriginWithoutProfile,
    /// <summary>Origin carries no database name.</summary>
    OriginWithoutDatabase,
    /// <summary>Origin carries no collection name.</summary>
    OriginWithoutCollection,
    /// <summary>The result is not a list of documents, or the list is empty.</summary>
    NoDocuments
}
