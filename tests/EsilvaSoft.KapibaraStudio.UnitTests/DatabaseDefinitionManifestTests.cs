using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class DatabaseDefinitionManifestTests
{
    private static readonly IReadOnlyList<CollectionDefinitionSnapshot> OneCollection =
    [
        new("movies", "{ \"capped\": false }", null,
        [new IndexDefinitionSnapshot("title_1", "{ \"title\": 1 }", "{ \"unique\": true }")])
    ];

    [Test]
    public void ValidateAcceptsCollectionOptionsValidatorIndexesAndDependentViews()
    {
        var manifest = new DatabaseDefinitionManifest(
            DatabaseDefinitionManifest.CurrentSchemaVersion,
            [new CollectionDefinitionSnapshot(
                "movies",
                "{ \"collation\": { \"locale\": \"en\" } }",
                new CollectionValidationInfo(
                    "{ \"$jsonSchema\": { \"bsonType\": \"object\" } }",
                    CollectionValidationLevel.Strict,
                    CollectionValidationAction.Error),
                [new IndexDefinitionSnapshot("title_1", "{ \"title\": 1 }", "{ \"unique\": true }")])],
            [new ViewDefinitionSnapshot("recent_movies", "movies", "[{ \"$match\": { \"year\": { \"$gte\": 2000 } } }]", null)]);

        Assert.That(manifest.Validate(), Is.EqualTo(manifest));
    }

    [Test]
    public void ValidateRejectsUnsupportedDefinitionSchemaVersion()
    {
        var manifest = new DatabaseDefinitionManifest(2, OneCollection, Array.Empty<ViewDefinitionSnapshot>());

        Assert.That(() => manifest.Validate(), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void ValidateRejectsDuplicateIndexNames()
    {
        var manifest = new DatabaseDefinitionManifest(
            DatabaseDefinitionManifest.CurrentSchemaVersion,
            [new CollectionDefinitionSnapshot(
                "movies",
                "{}",
                null,
                [
                    new IndexDefinitionSnapshot("same", "{ \"title\": 1 }", "{}"),
                    new IndexDefinitionSnapshot("same", "{ \"year\": 1 }", "{}")
                ])],
            Array.Empty<ViewDefinitionSnapshot>());

        Assert.That(() => manifest.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ValidateRejectsMissingViewDependencyAndCollectionViewCollision()
    {
        var missingDependency = new DatabaseDefinitionManifest(
            DatabaseDefinitionManifest.CurrentSchemaVersion,
            OneCollection,
            [new ViewDefinitionSnapshot("recent_movies", "missing", "[]", null)]);
        var collision = new DatabaseDefinitionManifest(
            DatabaseDefinitionManifest.CurrentSchemaVersion,
            OneCollection,
            [new ViewDefinitionSnapshot("movies", "movies", "[]", null)]);

        Assert.That(() => missingDependency.Validate(), Throws.TypeOf<ArgumentException>());
        Assert.That(() => collision.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ValidateRejectsCyclicViewDependencies()
    {
        var manifest = new DatabaseDefinitionManifest(
            DatabaseDefinitionManifest.CurrentSchemaVersion,
            Array.Empty<CollectionDefinitionSnapshot>(),
            [
                new ViewDefinitionSnapshot("first", "second", "[]", null),
                new ViewDefinitionSnapshot("second", "first", "[]", null)
            ]);

        Assert.That(() => manifest.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ValidateRejectsInvalidJsonAndUnsupportedCollectionNamespaceCharacters()
    {
        var invalidJson = new DatabaseDefinitionManifest(
            DatabaseDefinitionManifest.CurrentSchemaVersion,
            [new CollectionDefinitionSnapshot("movies", "[]", null, Array.Empty<IndexDefinitionSnapshot>())],
            Array.Empty<ViewDefinitionSnapshot>());
        var systemCollection = new DatabaseDefinitionManifest(
            DatabaseDefinitionManifest.CurrentSchemaVersion,
            [new CollectionDefinitionSnapshot("system.views", "{}", null, Array.Empty<IndexDefinitionSnapshot>())],
            Array.Empty<ViewDefinitionSnapshot>());
        var dottedCollection = new DatabaseDefinitionManifest(
            DatabaseDefinitionManifest.CurrentSchemaVersion,
            [new CollectionDefinitionSnapshot("movie.archive", "{}", null, Array.Empty<IndexDefinitionSnapshot>())],
            Array.Empty<ViewDefinitionSnapshot>());

        Assert.That(() => invalidJson.Validate(), Throws.TypeOf<ArgumentException>());
        Assert.That(() => systemCollection.Validate(), Throws.TypeOf<ArgumentException>());
        Assert.That(() => dottedCollection.Validate(), Throws.TypeOf<ArgumentException>());
    }
}
