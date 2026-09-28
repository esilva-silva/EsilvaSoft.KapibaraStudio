using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application.SchemaLearning;

/// <summary>
/// The admission rule of the first persistent delivery, as testable code: only <c>find</c>/<c>findOne</c>,
/// only <see cref="ResultCompleteness.Complete"/>, and only with an unambiguous origin.
/// <c>PartialProjection</c>, <c>Derived</c> and <c>Unknown</c> stay temporary tab evidence.
/// Learning is never part of the success of the query: a rejection is not an error for the caller.
/// </summary>
public static class SchemaLearningAdmissionPolicy
{
    /// <summary>Methods whose documents are stored documents of the namespace, as returned.</summary>
    public static bool IsEligibleMethod(string? method) => method is "find" or "findOne";

    /// <summary>Evaluates a result set and builds the envelope when it is admissible.</summary>
    public static SchemaLearningAdmission Evaluate(StructuredResultSet resultSet, SchemaLearningBatchContext context, SchemaLearningPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(resultSet);
        if (!policy.CollectionEnabled) return SchemaLearningAdmission.Rejected(SchemaLearningRejectionReason.CollectionDisabled);
        if (!IsEligibleMethod(resultSet.Method)) return SchemaLearningAdmission.Rejected(SchemaLearningRejectionReason.MethodNotEligible);

        switch (resultSet.Completeness)
        {
            case ResultCompleteness.Complete: break;
            case ResultCompleteness.PartialProjection: return SchemaLearningAdmission.Rejected(SchemaLearningRejectionReason.ProjectedResult);
            case ResultCompleteness.Derived: return SchemaLearningAdmission.Rejected(SchemaLearningRejectionReason.DerivedResult);
            default: return SchemaLearningAdmission.Rejected(SchemaLearningRejectionReason.UnknownCompleteness);
        }

        var origin = resultSet.Origin;
        if (origin.ProfileId is not { } profileId || profileId == Guid.Empty)
            return SchemaLearningAdmission.Rejected(SchemaLearningRejectionReason.OriginWithoutProfile);
        if (string.IsNullOrWhiteSpace(origin.Database))
            return SchemaLearningAdmission.Rejected(SchemaLearningRejectionReason.OriginWithoutDatabase);
        if (string.IsNullOrWhiteSpace(origin.Collection))
            return SchemaLearningAdmission.Rejected(SchemaLearningRejectionReason.OriginWithoutCollection);
        if (resultSet.Documents is not { Count: > 0 } documents)
            return SchemaLearningAdmission.Rejected(SchemaLearningRejectionReason.NoDocuments);

        var json = new string[documents.Count];
        for (var index = 0; index < documents.Count; index++) json[index] = documents[index].Json;

        var key = LearnedSchemaKey.Create(profileId, origin.Database, origin.Collection);
        var envelope = SchemaLearningEnvelope.Create(key, context, policy, resultSet.Method!, json, resultSet.IsTruncated);
        return SchemaLearningAdmission.Admitted(envelope);
    }
}
