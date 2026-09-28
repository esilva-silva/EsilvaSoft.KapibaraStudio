using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class SessionConnectionSecretStoreTests
{
    [Test]
    public void SetPasswordMakesSecretAvailableOnlyForItsProfile()
    {
        var store = new SessionConnectionSecretStore();
        var profileId = Guid.NewGuid();

        store.SetPassword(profileId, "senha-secreta");

        Assert.Multiple(() =>
        {
            Assert.That(store.GetPassword(profileId), Is.EqualTo("senha-secreta"));
            Assert.That(store.GetPassword(Guid.NewGuid()), Is.Null);
        });
    }

    [Test]
    public void RemoveErasesPasswordFromSessionStore()
    {
        var store = new SessionConnectionSecretStore();
        var profileId = Guid.NewGuid();
        store.SetPassword(profileId, "senha-secreta");

        store.Remove(profileId);

        Assert.That(store.GetPassword(profileId), Is.Null);
    }
}
