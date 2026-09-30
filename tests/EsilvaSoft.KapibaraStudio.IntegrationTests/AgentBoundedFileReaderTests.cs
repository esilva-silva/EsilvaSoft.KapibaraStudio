using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.SystemAdapters;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture, Category("Integration")]
public sealed class AgentBoundedFileReaderTests
{
    [TestCase(0, 0, AgentFileReadState.Read)]
    [TestCase(4, 4, AgentFileReadState.Read)]
    [TestCase(5, 4, AgentFileReadState.TooLarge)]
    public async Task ReadPreservesExactBytesAndReportsOverflow(int size, int limit, AgentFileReadState expected)
    {
        using var directory = new SyntheticDirectory();
        var path = Path.Combine(directory.Path, "bytes.bin");
        var bytes = Enumerable.Range(0, size).Select(static value => (byte)value).ToArray();
        await File.WriteAllBytesAsync(path, bytes);
        var result = await new LocalAgentBoundedFileReader().ReadAsync(path, limit, default);
        var synchronous = new LocalAgentBoundedFileReader().Read(path, limit);
        Assert.Multiple(() =>
        {
            Assert.That(result.State, Is.EqualTo(expected));
            Assert.That(result.Bytes, Is.EqualTo(expected == AgentFileReadState.Read ? bytes : []));
            Assert.That(synchronous.State, Is.EqualTo(result.State));
            Assert.That(synchronous.Bytes, Is.EqualTo(result.Bytes));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes), "Read must preserve the source.");
        });
    }

    [Test]
    public async Task MissingFileIsReportedWithoutCreatingIt()
    {
        using var directory = new SyntheticDirectory();
        var path = Path.Combine(directory.Path, "missing.bin");
        var result = await new LocalAgentBoundedFileReader().ReadAsync(path, 4, default);
        Assert.That(result.State, Is.EqualTo(AgentFileReadState.NotFound));
        Assert.That(File.Exists(path), Is.False);
    }

    [Test]
    public void CancelledReadPropagatesCancellationBeforeOpeningFile()
    {
        using var directory = new SyntheticDirectory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(async () => await new LocalAgentBoundedFileReader().ReadAsync(
            Path.Combine(directory.Path, "missing.bin"), 4, cancellation.Token));
    }
}
