using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class CollectionValidationRequestTests
{
    [Test]
    public void ValidateAcceptsJsonSchemaWithExplicitConfirmation()
    {
        var request = new CollectionValidationRequest(
            "catalogo",
            "clientes",
            "{ \"$jsonSchema\": { \"bsonType\": \"object\", \"required\": [\"nome\"] } }",
            CollectionValidationLevel.Strict,
            CollectionValidationAction.Error,
            "clientes");

        Assert.That(request.Validate(), Is.EqualTo(request));
    }

    [TestCase("", "clientes", "{}", "clientes")]
    [TestCase("catalogo", "system.users", "{}", "system.users")]
    [TestCase("catalogo", "clientes", "[]", "clientes")]
    [TestCase("catalogo", "clientes", "{", "clientes")]
    [TestCase("catalogo", "clientes", "{}", "outro")]
    public void ValidateRejectsInvalidContextOrValidator(string database, string collection, string validator, string confirmation)
    {
        var request = new CollectionValidationRequest(
            database,
            collection,
            validator,
            CollectionValidationLevel.Strict,
            CollectionValidationAction.Error,
            confirmation);

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ValidateRejectsUnsupportedValidationSetting()
    {
        var request = new CollectionValidationRequest(
            "catalogo",
            "clientes",
            "{}",
            (CollectionValidationLevel)99,
            CollectionValidationAction.Error,
            "clientes");

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }
}
