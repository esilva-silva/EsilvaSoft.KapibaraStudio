using EsilvaSoft.KapibaraStudio.Application.Agents.Broker;
using System.Text;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Mcp;

[TestFixture, Category("Unit")]
public sealed class AgentMcpConsoleTests
{
    [Test]
    public async Task ProtocolOutputSurvivesSuppressionWhileLibraryTextCannotContaminateIt()
    {
        using var console = new MemoryConsole();
        var session = AgentMcpConsoleSession.Open(console);
        var payload = Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"id\":1}\n");

        await console.ManagedOutput.WriteLineAsync("unexpected library text");
        await session.Output.WriteAsync(payload);

        Assert.Multiple(() =>
        {
            Assert.That(console.Output.ToArray(), Is.EqualTo(payload));
            Assert.That(session.Output, Is.SameAs(console.Output));
            Assert.That(session.Input, Is.SameAs(console.Input));
            Assert.That(console.InputOpenedAfterSuppression, Is.True);
        });
    }

    [Test]
    public async Task DiagnosticsRemainSeparateFromProtocolOutput()
    {
        using var console = new MemoryConsole();
        var session = AgentMcpConsoleSession.Open(console);

        await console.StandardError.WriteLineAsync("slop-mcp: plataforma sem endpoint local privado.");

        Assert.Multiple(() =>
        {
            Assert.That(console.Diagnostics.ToString(), Is.EqualTo("slop-mcp: plataforma sem endpoint local privado." + Environment.NewLine));
            Assert.That(session.Output.Length, Is.Zero);
            Assert.That(session.Input.Length, Is.Zero);
        });
    }

    [Test]
    public void SuppressionFailureDoesNotBeginReadingProtocolInput()
    {
        using var console = new MemoryConsole { RefuseSuppression = true };

        Assert.Throws<InvalidOperationException>(() => AgentMcpConsoleSession.Open(console));
        Assert.That(console.InputOpenCalls, Is.Zero);
    }

    private sealed class MemoryConsole : IAgentMcpConsole, IDisposable
    {
        public MemoryStream Input { get; } = new();
        public MemoryStream Output { get; } = new();
        public StringWriter Diagnostics { get; } = new();
        public TextWriter StandardError => Diagnostics;
        public TextWriter ManagedOutput { get; private set; }
        public bool RefuseSuppression { get; init; }
        public bool InputOpenedAfterSuppression { get; private set; }
        public int InputOpenCalls { get; private set; }

        public MemoryConsole() => ManagedOutput = new StreamWriter(Output, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

        public Stream OpenStandardOutput() => Output;

        public Stream OpenStandardInput()
        {
            InputOpenCalls++;
            InputOpenedAfterSuppression = ReferenceEquals(ManagedOutput, TextWriter.Null);
            return Input;
        }

        public void SuppressStandardTextOutput()
        {
            if (RefuseSuppression) throw new InvalidOperationException("Synthetic suppression failure.");
            ManagedOutput.Dispose();
            ManagedOutput = TextWriter.Null;
        }

        public void Dispose()
        {
            ManagedOutput.Dispose();
            Input.Dispose();
            Output.Dispose();
            Diagnostics.Dispose();
        }
    }
}
