using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>
/// Streaming do runtime (R42): o contrato de <see cref="ILocalModelRuntime.StreamAsync"/> para runtimes sem streaming
/// e o comportamento real do <see cref="OnnxLocalModelRuntime"/> com modelo carregado.
/// </summary>
[TestFixture]
public sealed class OnnxLocalModelRuntimeStreamingTests
{
    private static readonly ModelGenerationRequest Request = new("db.clientes.find({", "})", 2048, 24);

    /// <summary>A implementação padrão existe para que nada quebre: um pedaço final com o mesmo texto e as medições.</summary>
    [Test]
    public async Task TheDefaultStreamDeliversTheWholeResultAsOneFinalChunk()
    {
        var runtime = new CompletionRuntimeFake
        {
            Handler = (_, _) => Task.FromResult(new ModelGenerationResult("find({ nome: 1 })", 7, TimeSpan.FromMilliseconds(12), "cpu", false, true)
                { TimeToFirstToken = TimeSpan.FromMilliseconds(4) })
        };

        var chunks = new List<GeneratedChunk>();
        // Membro de interface com corpo padrão: o acesso é pela interface, como fazem os consumidores do runtime.
        await foreach (var chunk in ((ILocalModelRuntime)runtime).StreamAsync(Request)) chunks.Add(chunk);

        Assert.That(chunks, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(chunks[0].Text, Is.EqualTo("find({ nome: 1 })"));
            Assert.That(chunks[0].IsFinal, Is.True);
            Assert.That(chunks[0].GeneratedTokens, Is.EqualTo(7));
            Assert.That(chunks[0].Provider, Is.EqualTo("cpu"));
            Assert.That(chunks[0].IsComplete, Is.False);
            Assert.That(chunks[0].UsedCpuFallback, Is.True);
            Assert.That(chunks[0].TimeToFirstToken, Is.EqualTo(TimeSpan.FromMilliseconds(4)));
            Assert.That(runtime.Generations, Is.EqualTo(1), "O modo padrão faz exatamente uma geração.");
        });
    }

    /// <summary>Cancelar durante a enumeração é cancelamento da geração, não uma falha do runtime.</summary>
    [Test]
    public void TheDefaultStreamPropagatesCancellationWithoutUnobservedFailures()
    {
        using var cancellation = new CancellationTokenSource();
        ILocalModelRuntime runtime = new CompletionRuntimeFake
        {
            Handler = async (_, token) => { await cancellation.CancelAsync(); token.ThrowIfCancellationRequested(); return new("", 0, TimeSpan.Zero, "cpu"); }
        };

        Assert.That(async () => { await foreach (var _ in runtime.StreamAsync(Request, cancellation.Token)) { } },
            Throws.InstanceOf<OperationCanceledException>());
    }

    /// <summary>Abandonar a enumeração do padrão não inicia geração nenhuma além da que já estava em curso.</summary>
    [Test]
    public async Task AbandoningTheDefaultStreamDoesNotStartAnotherGeneration()
    {
        var runtime = new CompletionRuntimeFake();
        await foreach (var _ in ((ILocalModelRuntime)runtime).StreamAsync(Request)) break;
        Assert.That(runtime.Generations, Is.EqualTo(1));
    }

}
