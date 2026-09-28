using AvaloniaEdit.Document;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.Language.Text;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>
/// O caminho automático (uma computação por tecla) precisa entregar ao motor o snapshot do próprio editor, com
/// linhagem de versões, e não um texto solto reembrulhado a cada tecla: só assim o cache de tokens acerta, que é
/// exatamente o que o caminho por tecla existe para aproveitar.
/// </summary>
/// <remarks>
/// Os testes são síncronos de propósito: <see cref="TextDocument"/> pertence à thread que o criou, e a continuação de
/// um <c>await</c> voltaria do pool. Bloquear mantém a thread do teste como a única a tocar o documento.
/// </remarks>
[TestFixture]
public sealed class InlineCompletionSnapshotTests
{
    private static WorkspaceTabViewModel Tab(WorkspaceTestContext context) => new(context.Workspace)
    {
        Profile = ConnectionProfile.Create("Local", "mongodb://localhost"),
        Database = "shop",
        InlinePreemptiveCompletion = new TraditionalPreemptiveCompletionProvider(
            new CompletionService(new KnowledgeCatalog([new LanguageCatalogSource()])))
    };

    private static int Reads(WorkspaceTabViewModel tab, ITextSnapshot snapshot, int caret)
    {
        var probe = new ProbeSnapshot(snapshot);
        tab.GetInlineCompletionAsync(probe, caret, CancellationToken.None).GetAwaiter().GetResult();
        return probe.ReadCount;
    }

    [Test]
    public void TheAutomaticPathReusesTheTokensOfTheEditorSnapshotInsteadOfRelexingOnEveryKeystroke()
    {
        var context = new WorkspaceTestContext();
        var tab = Tab(context);
        var document = new TextDocument("Conn");

        // Duas leituras: a lexificação do documento e o trecho anterior ao cursor que projeta a inserção.
        var first = Reads(tab, new AvaloniaTextSnapshot(document), 4);

        // Mesma versão do documento, outro cursor: o cache de contexto não pode servir esta análise (a chave inclui o
        // cursor), então ela chega mesmo ao léxico — e lá os tokens já estão. Neste cursor o portão de confiança se
        // abstém, então nem a leitura do prefixo acontece: o custo da análise inteira é zero leitura do documento.
        var sameVersion = Reads(tab, new AvaloniaTextSnapshot(document), 3);

        // Uma tecla de verdade: versão nova da mesma linhagem, uma lexificação; a análise seguinte volta a reaproveitar.
        document.Insert(4, "e");
        var afterKeystroke = Reads(tab, new AvaloniaTextSnapshot(document), 5);
        var repeated = Reads(tab, new AvaloniaTextSnapshot(document), 4);

        Assert.That(first, Is.EqualTo(2), "Pré-condição: a primeira análise lexifica o documento.");
        Assert.That(sameVersion, Is.Zero, "Uma segunda análise da mesma versão não relê nem relexifica o documento.");
        Assert.That((afterKeystroke, repeated), Is.EqualTo((2, 1)), "Uma tecla custa uma lexificação, não uma por análise.");
    }

    [Test]
    public void TextWithoutLineageStillWorksButCannotReuseTokens()
    {
        // Contraste explícito com o teste acima, e a razão de a sobrecarga por snapshot existir: sem linhagem de
        // versões cada chamada é uma versão isolada, e o léxico precisa reler tudo, inclusive para o mesmo texto.
        var context = new WorkspaceTestContext();
        var tab = Tab(context);
        var reads = 0;
        for (var index = 0; index < 3; index++)
            reads += Reads(tab, new StringTextSnapshot("Conn", new(TextSnapshotVersion.NewDocumentId(), index + 1)), 4);
        Assert.That(reads, Is.EqualTo(6), "Sem linhagem comum, toda análise relexifica.");

        // A sobrecarga por texto continua funcionando: é o caminho de teste e o da aba sem editor real.
        var suggestion = tab.GetInlineCompletionAsync("Conn", 4, CancellationToken.None).GetAwaiter().GetResult();
        Assert.That(suggestion!.Text, Is.EqualTo("ectionPool"));
    }

    /// <summary>Conta as leituras do documento sem alterar o contrato; igual ao usado em <c>TokenCacheTests</c>.</summary>
    private sealed class ProbeSnapshot(ITextSnapshot inner) : ITextSnapshot
    {
        private readonly ITextSnapshot _inner = inner;
        private int _readCount;
        public int ReadCount => Volatile.Read(ref _readCount);
        public TextSnapshotVersion Version => _inner.Version;
        public int Length => _inner.Length;
        public char this[int index] => _inner[index];
        public string GetText(int start, int length)
        {
            Interlocked.Increment(ref _readCount);
            return _inner.GetText(start, length);
        }
        public IReadOnlyList<TextChange>? GetChangesSince(ITextSnapshot previous) =>
            _inner.GetChangesSince(previous is ProbeSnapshot probe ? probe._inner : previous);
    }
}
