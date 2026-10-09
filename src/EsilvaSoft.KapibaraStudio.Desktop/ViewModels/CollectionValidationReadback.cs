using EsilvaSoft.KapibaraStudio.Core;
using MongoDB.Bson;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

internal static class CollectionValidationReadback
{
    public static bool Matches(CollectionValidationRequest request, CollectionValidationInfo observed)
    {
        try
        {
            return request.ValidationLevel == observed.ValidationLevel
                && request.ValidationAction == observed.ValidationAction
                && Equivalent(BsonDocument.Parse(request.ValidatorJson), BsonDocument.Parse(observed.ValidatorJson));
        }
        catch (Exception)
        {
            // A falha em interpretar a releitura nunca deve converter o estado observado em aceite.
            return false;
        }
    }

    private static bool Equivalent(BsonValue expected, BsonValue actual)
    {
        if (expected is BsonDocument left && actual is BsonDocument right)
        {
            return left.ElementCount == right.ElementCount
                && left.Elements.All(element => right.TryGetValue(element.Name, out var value)
                    && Equivalent(element.Value, value));
        }

        if (expected is BsonArray leftArray && actual is BsonArray rightArray)
        {
            return leftArray.Count == rightArray.Count
                && leftArray.Zip(rightArray).All(pair => Equivalent(pair.First, pair.Second));
        }

        return expected.Equals(actual);
    }
}
