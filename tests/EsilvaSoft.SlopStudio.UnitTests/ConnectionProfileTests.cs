using EsilvaSoft.SlopStudio.Core;
using System.Text.Json;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class ConnectionProfileTests
{
    [Test]
    public void CreateWithBlankNameThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(() => ConnectionProfile.Create(" ", "mongodb://localhost:27017"));

        Assert.That(exception!.ParamName, Is.EqualTo("name"));
    }

    [Test]
    public void CreateTrimsValuesAndCreatesIdentifier()
    {
        var profile = ConnectionProfile.Create(" Desenvolvimento ", " mongodb://localhost:27017 ", " catalogo ");

        Assert.Multiple(() =>
        {
            Assert.That(profile.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(profile.Name, Is.EqualTo("Desenvolvimento"));
            Assert.That(profile.ConnectionString, Is.EqualTo("mongodb://localhost:27017"));
            Assert.That(profile.DefaultDatabase, Is.EqualTo("catalogo"));
        });
    }

    [Test]
    public void CreateCanMarkProfileAsFavorite()
    {
        var profile = ConnectionProfile.Create("Produção", "mongodb://localhost:27017", isFavorite: true);

        Assert.That(profile.IsFavorite, Is.True);
    }

    [Test]
    public void LocalAiContextPermissionDefaultsOnPerConnectionAndCanBeOptedOut()
    {
        var profile = ConnectionProfile.Create("Produção", "mongodb://localhost:27017");
        var optedOut = profile with { LocalAiContextEnabled = false };

        Assert.Multiple(() =>
        {
            Assert.That(profile.LocalAiContextEnabled, Is.True);
            Assert.That(optedOut.LocalAiContextEnabled, Is.False);
            Assert.That(profile.Duplicate("cópia").LocalAiContextEnabled, Is.True);
            Assert.That(optedOut.Duplicate("cópia privada").LocalAiContextEnabled, Is.False);
        });
    }

    [Test]
    public void LegacyConnectionProfileWithoutLocalAiContextFlagKeepsOptOutAvailable()
    {
        const string json = "{\"Id\":\"125c3c6e-946e-4a1f-b244-42305a3eea8e\",\"Name\":\"Antiga\",\"ConnectionString\":\"mongodb://localhost:27017\"}";

        var profile = JsonSerializer.Deserialize<ConnectionProfile>(json);

        Assert.That(profile?.LocalAiContextEnabled, Is.True);
    }

    [Test]
    public void CreatePersistsTrimmedEnvironmentAndHexColor()
    {
        var profile = ConnectionProfile.Create("Produção", "mongodb://localhost:27017", environment: " Produção ", color: " #1A2B3C ");

        Assert.Multiple(() =>
        {
            Assert.That(profile.Environment, Is.EqualTo("Produção"));
            Assert.That(profile.Color, Is.EqualTo("#1A2B3C"));
        });
    }

    [Test]
    public void CreateRejectsColorOutsideHexFormat()
    {
        Assert.That(
            () => ConnectionProfile.Create("Produção", "mongodb://localhost:27017", color: "vermelho"),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void CreateNormalizesCommaSeparatedTags()
    {
        var profile = ConnectionProfile.Create("Produção", "mongodb://localhost:27017", tags: " produção, cliente-a, PRODUÇÃO ");

        Assert.That(profile.Tags, Is.EqualTo("produção, cliente-a"));
    }

    [Test]
    public void CreateNormalizesFolderAndDuplicatePreservesIt()
    {
        var profile = ConnectionProfile.Create("Produção", "mongodb://localhost:27017", folder: " Cliente A / Produção / ");
        var copy = profile.Duplicate("Produção - cópia");

        Assert.Multiple(() =>
        {
            Assert.That(profile.Folder, Is.EqualTo("Cliente A / Produção"));
            Assert.That(copy.Folder, Is.EqualTo(profile.Folder));
        });
    }

    [Test]
    public void CreateRejectsFolderAboveLimit()
    {
        Assert.That(
            () => ConnectionProfile.Create("Produção", "mongodb://localhost:27017", folder: new string('x', 81)),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void CreateRejectsMoreThanTenTags()
    {
        var tags = string.Join(',', Enumerable.Range(1, 11).Select(value => $"tag{value}"));

        Assert.That(
            () => ConnectionProfile.Create("Produção", "mongodb://localhost:27017", tags: tags),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void DuplicateCreatesNewIdentifierAndPreservesConnectionMetadata()
    {
        var profile = ConnectionProfile.Create("Produção", "mongodb://localhost:27017", "catalogo", isReadOnly: true, isFavorite: true, environment: "produção", color: "#1A2B3C", tags: "cliente-a, crítico");
        var copy = profile.Duplicate("Produção - cópia");

        Assert.Multiple(() =>
        {
            Assert.That(copy.Id, Is.Not.EqualTo(profile.Id));
            Assert.That(copy.Name, Is.EqualTo("Produção - cópia"));
            Assert.That(copy.ConnectionString, Is.EqualTo(profile.ConnectionString));
            Assert.That(copy.DefaultDatabase, Is.EqualTo(profile.DefaultDatabase));
            Assert.That(copy.Tags, Is.EqualTo(profile.Tags));
            Assert.That(copy.IsReadOnly, Is.True);
            Assert.That(copy.IsFavorite, Is.True);
        });
    }

    [Test]
    public void CreateWithPasswordInUriPreservesExplicitCredentials()
    {
        var profile = ConnectionProfile.Create("Produção", "mongodb://admin:segredo@db.example:27017");

        Assert.That(profile.ResolveConnectionString(_ => throw new AssertionException("Não deveria resolver variável.")), Is.EqualTo("mongodb://admin:segredo@db.example:27017"));
    }

    [TestCase("mongodb://user:secret-question?fragment@host/db")]
    [TestCase("mongodb://user:secret-hash#fragment@host/db")]
    public void EndpointDoesNotExposeMalformedUserInfoBeforeQueryOrFragmentDelimiter(string connectionString)
    {
        var profile = ConnectionProfile.Create("Canário", connectionString);

        Assert.Multiple(() =>
        {
            Assert.That(profile.Endpoint, Is.EqualTo("Confira a URI configurada"));
            Assert.That(profile.RoutingLabel, Does.Not.Contain("secret").And.Not.Contain("user:"));
        });
    }

    [Test]
    public void CreateWithEnvironmentPasswordTemplateKeepsSecretOutOfProfileAndResolvesAtUseTime()
    {
        var profile = ConnectionProfile.Create("Produção", "mongodb://admin:${MONGODB_PASSWORD}@db.example:27017");

        var resolved = profile.ResolveConnectionString(variable => variable == "MONGODB_PASSWORD" ? "senha-codificada" : null);

        Assert.Multiple(() =>
        {
            Assert.That(profile.ConnectionString, Does.Not.Contain("senha-codificada"));
            Assert.That(resolved, Is.EqualTo("mongodb://admin:senha-codificada@db.example:27017"));
        });
    }

    [Test]
    public void ResolveConnectionStringWithMissingEnvironmentVariableThrows()
    {
        var profile = ConnectionProfile.Create("Produção", "mongodb://admin:${MONGODB_PASSWORD}@db.example:27017");

        Assert.That(() => profile.ResolveConnectionString(_ => null), Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void ReadOnlyProfileRejectsWrites()
    {
        var profile = ConnectionProfile.Create("Produção", "mongodb://localhost:27017", isReadOnly: true);

        Assert.That(() => profile.EnsureWriteAllowed(), Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void MarkConnectedPreservesProfileAndSetsTimestamp()
    {
        var profile = ConnectionProfile.Create("Produção", "mongodb://localhost:27017", "catalogo", isReadOnly: true);
        var occurredAt = new DateTimeOffset(2026, 9, 8, 12, 30, 0, TimeSpan.Zero);

        var connected = profile.MarkConnected(occurredAt);

        Assert.Multiple(() =>
        {
            Assert.That(connected.Id, Is.EqualTo(profile.Id));
            Assert.That(connected.ConnectionString, Is.EqualTo(profile.ConnectionString));
            Assert.That(connected.IsReadOnly, Is.True);
            Assert.That(connected.LastConnectedAt, Is.EqualTo(occurredAt));
        });
    }
}
