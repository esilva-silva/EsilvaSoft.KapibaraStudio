using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class ViewDependencyChainTests
{
    [Test]
    public async Task ResolveFindsTransitiveSourceWithoutReadingDocuments()
    {
        var definitions = new Dictionary<string, ViewNamespaceDefinition>
        {
            ["filtered"] = new(true, "source"),
            ["source"] = new(false)
        };
        var loaded = new List<string>();

        var result = await ViewDependencyChain.ResolveAsync("dashboard", "filtered", (name, _) =>
        {
            loaded.Add(name);
            return Task.FromResult<ViewNamespaceDefinition?>(definitions.GetValueOrDefault(name));
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Chain, Is.EqualTo(new List<string> { "filtered", "source" }));
            Assert.That(loaded, Is.EqualTo(new List<string> { "filtered", "source" }));
        });
    }

    [Test]
    public async Task ResolveRejectsCycleBeforeDispatch()
    {
        var definitions = new Dictionary<string, ViewNamespaceDefinition>
        {
            ["filtered"] = new(true, "dashboard")
        };

        var result = await ViewDependencyChain.ResolveAsync("dashboard", "filtered", (name, _) =>
            Task.FromResult<ViewNamespaceDefinition?>(definitions.GetValueOrDefault(name)));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.CycleAt, Is.EqualTo("dashboard"));
        });
    }

    [Test]
    public async Task ResolveReportsMissingSourceAndStopsAtDepthLimit()
    {
        var missing = await ViewDependencyChain.ResolveAsync("dashboard", "absent", (_, _) =>
            Task.FromResult<ViewNamespaceDefinition?>(null));
        var tooDeep = await ViewDependencyChain.ResolveAsync("dashboard", "view0", (name, _) =>
        {
            var index = int.Parse(name.AsSpan(4), System.Globalization.CultureInfo.InvariantCulture);
            return Task.FromResult<ViewNamespaceDefinition?>(new(true, $"view{index + 1}"));
        });

        Assert.Multiple(() =>
        {
            Assert.That(missing.MissingAt, Is.EqualTo("absent"));
            Assert.That(tooDeep.IsTruncated, Is.True);
            Assert.That(tooDeep.Chain, Has.Count.EqualTo(ViewDependencyChain.MaximumDepth));
        });
    }
}
