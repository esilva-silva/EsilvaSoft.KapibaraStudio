using MongoDB.Bson;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

/// <summary>Validates pipeline structure without interpreting BSON literals or executing server expressions.</summary>
public static class AggregationPipelineValidator
{
    public static void ValidateReadPipeline(BsonArray pipeline) => Validate(pipeline, "pipeline", 0);

    private static void Validate(BsonArray pipeline, string path, int depth)
    {
        if (depth > 64) throw new ArgumentException($"{path}: pipelines aninhados excedem a profundidade de 64.");
        for (var index = 0; index < pipeline.Count; index++)
        {
            var location = $"{path}[{index}] (estágio {index + 1})";
            if (!pipeline[index].IsBsonDocument || pipeline[index].AsBsonDocument.ElementCount != 1)
                throw new ArgumentException($"{location}: informe um documento com exatamente um operador de estágio.");
            var stage = pipeline[index].AsBsonDocument.GetElement(0);
            if (!stage.Name.StartsWith('$')) throw new ArgumentException($"{location}: o nome do estágio deve começar com $.");
            if (stage.Name is "$out" or "$merge")
                throw new InvalidOperationException($"{location}: {stage.Name} escreve no servidor e não é permitido no cursor de leitura. O pipeline não foi enviado.");
            // Traverse only actual pipeline positions; $literal and fields named $out are data.
            if (stage.Name == "$facet" && stage.Value.IsBsonDocument)
                foreach (var facet in stage.Value.AsBsonDocument)
                    ValidateNested(facet.Value, $"{location}.$facet.{facet.Name}", depth);
            if (stage.Name is "$lookup" or "$unionWith" && stage.Value.IsBsonDocument &&
                stage.Value.AsBsonDocument.TryGetValue("pipeline", out var nested))
                ValidateNested(nested, $"{location}.{stage.Name}.pipeline", depth);
        }
    }

    private static void ValidateNested(BsonValue value, string path, int depth)
    {
        if (!value.IsBsonArray) throw new ArgumentException($"{path}: o pipeline deve ser um array de estágios.");
        Validate(value.AsBsonArray, path, depth + 1);
    }
}
