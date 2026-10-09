using MongoDB.Bson;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

/// <summary>Conservative read-stage subset accepted before the executor appends its own $out.</summary>
public static class ViewMaterializationPipelineValidator
{
    private static readonly HashSet<string> SupportedStages = new(StringComparer.Ordinal)
    {
        "$match", "$project", "$addFields", "$set", "$unset", "$unwind",
        "$group", "$sort", "$limit", "$skip", "$count", "$replaceRoot",
        "$replaceWith", "$sortByCount", "$bucket", "$bucketAuto", "$redact"
    };

    public static void Validate(BsonArray pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline.Count > 20)
            throw new ArgumentException("A materialização aceita até 20 estágios de leitura.", nameof(pipeline));

        AggregationPipelineValidator.ValidateReadPipeline(pipeline);
        for (var index = 0; index < pipeline.Count; index++)
        {
            var stage = pipeline[index].AsBsonDocument.GetElement(0).Name;
            if (!SupportedStages.Contains(stage))
                throw new NotSupportedException($"O estágio {stage} não está liberado para materialização desta view.");
        }
    }
}
