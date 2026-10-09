using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class CollectionValidationReadbackTests
{
    [Test]
    public void MatchesEquivalentBsonDespiteFieldOrderAndCanonicalExtendedJson()
    {
        var request = new CollectionValidationRequest(
            "catalogo", "clientes", "{\"$jsonSchema\":{\"bsonType\":\"object\",\"minimum\":1}}",
            CollectionValidationLevel.Strict, CollectionValidationAction.Error, "clientes");
        var observed = new CollectionValidationInfo(
            "{\"$jsonSchema\":{\"minimum\":{\"$numberInt\":\"1\"},\"bsonType\":\"object\"}}",
            CollectionValidationLevel.Strict, CollectionValidationAction.Error);

        Assert.That(CollectionValidationReadback.Matches(request, observed), Is.True);
    }

    [Test]
    public void RejectsDifferentValidatorOrServerOptions()
    {
        var request = new CollectionValidationRequest(
            "catalogo", "clientes", "{\"$jsonSchema\":{\"bsonType\":\"object\"}}",
            CollectionValidationLevel.Strict, CollectionValidationAction.Error, "clientes");
        var differentValidator = new CollectionValidationInfo("{}", CollectionValidationLevel.Strict, CollectionValidationAction.Error);
        var differentLevel = new CollectionValidationInfo(request.ValidatorJson, CollectionValidationLevel.Moderate, CollectionValidationAction.Error);
        var malformed = new CollectionValidationInfo("{", CollectionValidationLevel.Strict, CollectionValidationAction.Error);

        Assert.Multiple(() =>
        {
            Assert.That(CollectionValidationReadback.Matches(request, differentValidator), Is.False);
            Assert.That(CollectionValidationReadback.Matches(request, differentLevel), Is.False);
            Assert.That(CollectionValidationReadback.Matches(request, malformed), Is.False);
        });
    }
}
