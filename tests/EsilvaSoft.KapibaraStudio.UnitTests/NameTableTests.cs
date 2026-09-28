using EsilvaSoft.KapibaraStudio.Autocomplete.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class NameTableTests
{
    [Test]
    public void PrefixIsCaseInsensitiveAndPrecedesHumpsAndSubstrings()
    {
        var table = new NameTable<string>(["clienteNome", "Cliente.Id", "createdAt", "nomeCliente", "CLIENTE_ATIVO", "id"], name => name);
        var matches = Collect(table, "cli");
        Assert.Multiple(() =>
        {
            Assert.That(matches.TakeWhile(match => match.Match == CatalogMatch.Prefix).Select(match => match.Name), Is.EqualTo(Expect.Words("Cliente.Id clienteNome CLIENTE_ATIVO")));
            Assert.That(matches.Single(match => match.Name == "nomeCliente").Match, Is.EqualTo(CatalogMatch.Substring));
            Assert.That(Collect(table, "cN").Single().Name, Is.EqualTo("clienteNome"));
            Assert.That(Collect(table, "cN").Single().Match, Is.EqualTo(CatalogMatch.Humps));
            Assert.That(Collect(table, ""), Has.Count.EqualTo(6));
            Assert.That(Collect(table, "", maximum: 2), Has.Count.EqualTo(2));
            Assert.That(table.TryGetExact("id", out var exact) && exact == "id", Is.True);
            Assert.That(table.TryGetExact("ID", out _), Is.False);
        });
    }

    [Test]
    public void FilteredItemsDoNotConsumeTheMaximum()
    {
        var table = new NameTable<string>(["a1", "a2", "a3", "a4"], name => name);
        var result = new List<string>();
        table.Collect("a", 2, name => name != "a1", (name, _) => result.Add(name));
        Assert.That(result, Is.EqualTo(Expect.Words("a2 a3")));
    }

    [Test]
    public void SearchKeyLetsOperatorsMatchWithoutDollar() =>
        Assert.That(Collect(new NameTable<string>(["$eq", "$exists", "$in"], name => name, name => name.TrimStart('$')), "e").Select(match => match.Name),
            Is.EqualTo(Expect.Words("$eq $exists")));

    [Test]
    public void LargeScopeReturnsOnlyMatchingPrefixRange()
    {
        var table = new NameTable<string>(Enumerable.Range(0, 10_000).Select(index => $"field{index:D5}"), name => name);
        Assert.That(Collect(table, "field0001").Select(match => match.Name), Is.EqualTo(Enumerable.Range(10, 10).Select(index => $"field{index:D5}")));
    }

    private static List<(string Name, CatalogMatch Match)> Collect(NameTable<string> table, string query, int maximum = 100)
    {
        var result = new List<(string, CatalogMatch)>();
        table.Collect(query, maximum, null, (name, match) => result.Add((name, match)));
        return result;
    }
}
