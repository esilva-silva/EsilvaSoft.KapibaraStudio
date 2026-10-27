using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using static EsilvaSoft.KapibaraStudio.UnitTests.EditorKeyBindingsTestFixture;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class EditorKeyBindingsTests
{
    // Escopos escritos à mão a partir da tabela oficial, não derivados de EditorCommandIds.
    private static readonly string[] GlobalCommands = ["editor.completion.show", "editor.completion.ai"];
    private static readonly string[] ListCommands =
        ["editor.completion.next", "editor.completion.previous", "editor.completion.accept", "editor.completion.accept.enter", "editor.completion.close"];
    private static readonly string[] SnippetCommands = ["editor.snippet.next", "editor.snippet.previous", "editor.snippet.cancel"];
    private static readonly string[] InlineCommands = ["editor.inline.accept", "editor.inline.dismiss"];
    private static readonly EditorCommandScope[] NonGlobalScopes = [EditorCommandScope.List, EditorCommandScope.Snippet, EditorCommandScope.Inline];
    private static readonly string[] CtrlDotOnly = ["Ctrl+."];
    private static readonly string[] TabCommands = ["editor.completion.accept", "editor.snippet.next", "editor.inline.accept"];

    [Test]
    public void DefaultsBindEveryEditorCommandAndListFlagsKeepConservativeValues()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Texts(EditorKeyBindings.Resolve(null)), Is.EqualTo(ExpectedDefaults));
            Assert.That(Texts(EditorKeyBindings.Resolve(new EditorKeyBindings())), Is.EqualTo(ExpectedDefaults), "Sem entradas, cada comando mantém o padrão.");
            Assert.That(new WorkspacePreferences().EditorKeyBindings, Is.Null);
            Assert.That((new AutocompleteSettings().CompletionAutoOpenOnTrigger, new AutocompleteSettings().CompletionEnterAccepts), Is.EqualTo((false, true)));
            Assert.That(JsonSerializer.Serialize(new WorkspacePreferences()), Does.Not.Contain("EditorKeyBindings"), "Ausência não é materializada ao gravar.");
        });
    }

    [Test]
    public void EveryCommandHasExactlyOneScopeAndTheScopesPartitionTheCommandList()
    {
        var scopes = Enum.GetValues<EditorCommandScope>();
        Assert.Multiple(() =>
        {
            Assert.That(EditorCommandIds.All, Is.EquivalentTo(ExpectedDefaults.Keys), "A tabela de padrões cobre exatamente os comandos conhecidos.");
            Assert.That(EditorCommandIds.All, Is.Unique);
            Assert.That(scopes.SelectMany(EditorCommandIds.InScope), Is.EqualTo(EditorCommandIds.All).AsCollection, "Os escopos particionam All preservando a ordem relativa.");
            Assert.That(EditorCommandIds.InScope(EditorCommandScope.Global), Is.EqualTo(GlobalCommands).AsCollection);
            Assert.That(EditorCommandIds.InScope(EditorCommandScope.List), Is.EqualTo(ListCommands).AsCollection);
            Assert.That(EditorCommandIds.InScope(EditorCommandScope.Snippet), Is.EqualTo(SnippetCommands).AsCollection);
            Assert.That(EditorCommandIds.InScope(EditorCommandScope.Inline), Is.EqualTo(InlineCommands).AsCollection);
            Assert.That(EditorCommandIds.TryGetScope("editor.snippet.previous", out var snippet) ? snippet : (EditorCommandScope?)null, Is.EqualTo(EditorCommandScope.Snippet));
            Assert.That(EditorCommandIds.TryGetScope("editor.completion.unknown", out _), Is.False);
            Assert.That(EditorCommandIds.TryGetScope("Editor.Completion.Show", out _), Is.False, "Os ids são chaves exatas em minúsculas.");
            Assert.That(EditorCommandIds.InScope((EditorCommandScope)99), Is.Empty);
            Assert.That(EditorCommandIds.All, Has.All.Matches<string>(command =>
                    !command.Any(char.IsUpper) && command.StartsWith("editor.", StringComparison.Ordinal)),
                "Ids são chaves persistidas com segmentos minúsculos.");
        });
    }

    [TestCase("Ctrl+.", EditorKeyModifiers.Control, null, ".", "Ctrl+.")]
    [TestCase("Ctrl+;", EditorKeyModifiers.Control, null, ";", "Ctrl+;")]
    [TestCase("Ctrl+Space", EditorKeyModifiers.Control, "Space", null, "Ctrl+Space")]
    [TestCase("Shift+Tab", EditorKeyModifiers.Shift, "Tab", null, "Shift+Tab")]
    [TestCase("Escape", EditorKeyModifiers.None, "Escape", null, "Escape")]
    [TestCase("shift+ctrl+a", EditorKeyModifiers.Control | EditorKeyModifiers.Shift, "A", null, "Ctrl+Shift+A")]
    [TestCase("Ctrl++", EditorKeyModifiers.Control, null, "+", "Ctrl++")]
    [TestCase("Alt+]", EditorKeyModifiers.Alt, null, "]", "Alt+]")]
    [TestCase("Meta+0", EditorKeyModifiers.Meta, "D0", null, "Meta+0")]
    [TestCase("F5", EditorKeyModifiers.None, "F5", null, "F5")]
    public void GestureTextParsesToModifiersPlusNamedKeyOrSymbol(string text, EditorKeyModifiers modifiers, string? key, string? symbol, string canonical)
    {
        Assert.That(EditorKeyGesture.TryParse(text, out var gesture, out var error), Is.True, error);
        Assert.Multiple(() =>
        {
            Assert.That(gesture.Modifiers, Is.EqualTo(modifiers));
            Assert.That(gesture.Key?.ToString(), Is.EqualTo(key));
            Assert.That(gesture.Symbol?.ToString(), Is.EqualTo(symbol));
            Assert.That(gesture.ToString(), Is.EqualTo(canonical));
            Assert.That(EditorKeyGesture.Parse(canonical), Is.EqualTo(gesture), "O texto canônico volta ao mesmo gesto.");
        });
    }

    [Test]
    public void EquivalentGesturesAreEqualAndFactoriesApplyTheSameRules()
    {
        Assert.Multiple(() =>
        {
            Assert.That(EditorKeyGesture.Parse("Shift+Ctrl+."), Is.EqualTo(EditorKeyGesture.Parse("Ctrl+Shift+.")));
            Assert.That(EditorKeyGesture.ForSymbol(';', EditorKeyModifiers.Control), Is.EqualTo(EditorKeyGesture.Parse("Ctrl+;")));
            Assert.That(EditorKeyGesture.ForKey(EditorKey.Space, EditorKeyModifiers.Control), Is.EqualTo(EditorKeyGesture.Parse("ctrl+space")));
            Assert.That(EditorKeyGesture.Parse("Ctrl+."), Is.Not.EqualTo(EditorKeyGesture.Parse("Ctrl+;")));
            Assert.Throws<ArgumentException>(() => EditorKeyGesture.ForSymbol('a', EditorKeyModifiers.Control));
            Assert.Throws<ArgumentException>(() => EditorKeyGesture.ForSymbol('.'));
            Assert.Throws<ArgumentException>(() => EditorKeyGesture.ForKey(EditorKey.A, EditorKeyModifiers.Shift));
            Assert.Throws<ArgumentException>(() => EditorKeyGesture.ForKey((EditorKey)999, EditorKeyModifiers.Control));
            Assert.That(default(EditorKeyGesture).ToString(), Is.Empty);
        });
    }

    [Test]
    public void DispatcherUsesProducedSymbolBeforePhysicalFallbackAndHonorsCustomBindings()
    {
        var defaults = new EditorCommandDispatcher(EditorKeyBindings.Resolve(null));
        var custom = new EditorCommandDispatcher(EditorKeyBindings.Resolve(new EditorKeyBindings
        {
            Bindings = new() { [EditorCommandIds.CompletionShow] = ["Ctrl+Shift+Space"] }
        }));

        Assert.Multiple(() =>
        {
            Assert.That(defaults.Match(new(EditorKeyModifiers.Control, ';', null, '/'), EditorCommandScope.Global), Is.EqualTo(EditorCommandIds.CompletionAi),
                "O símbolo produzido funciona em layouts não-US.");
            Assert.That(defaults.Match(new(EditorKeyModifiers.Control, null, null, ';'), EditorCommandScope.Global), Is.EqualTo(EditorCommandIds.CompletionAi),
                "Sem símbolo produzido, a pontuação da tecla física é o fallback.");
            Assert.That(defaults.Match(new(EditorKeyModifiers.Control, ':', null, ';'), EditorCommandScope.Global), Is.Null,
                "Símbolo presente e diferente bloqueia o fallback físico.");
            Assert.That(defaults.Match(new(EditorKeyModifiers.Control, ' ', EditorKey.Space), EditorCommandScope.Global), Is.EqualTo(EditorCommandIds.CompletionShow),
                "Gesto de tecla nomeada casa por identidade de tecla, mesmo com símbolo produzido.");
            Assert.That(defaults.Match(new(EditorKeyModifiers.Control, null, EditorKey.Space), EditorCommandScope.Global), Is.EqualTo(EditorCommandIds.CompletionShow));
            Assert.That(defaults.Match(new(EditorKeyModifiers.None, null, EditorKey.Tab), EditorCommandScope.Inline), Is.EqualTo(EditorCommandIds.InlineAccept));
            Assert.That(custom.Match(new(EditorKeyModifiers.Control | EditorKeyModifiers.Shift, null, EditorKey.Space), EditorCommandScope.Global), Is.EqualTo(EditorCommandIds.CompletionShow));
            Assert.That(custom.Match(new(EditorKeyModifiers.Control, '.', EditorKey.Space, '.'), EditorCommandScope.Global), Is.Null,
                "A preferência substitui, em vez de complementar, o padrão do comando.");
        });
    }

    [Test]
    public void NamedKeysMatchByKeyIdentityEvenWhenTheToolkitAlsoDeliversTheirControlCharacter()
    {
        var dispatcher = new EditorCommandDispatcher(EditorKeyBindings.Resolve(null));
        // Avalonia entrega KeySymbol para teclas nomeadas: Tab -> "\t", Escape -> "", Enter -> "\r".
        var tab = new EditorKeyEvent(EditorKeyModifiers.None, '\t', EditorKey.Tab);
        var escape = new EditorKeyEvent(EditorKeyModifiers.None, '', EditorKey.Escape);
        var enter = new EditorKeyEvent(EditorKeyModifiers.None, '\r', EditorKey.Enter);
        Assert.Multiple(() =>
        {
            Assert.That(dispatcher.Match(tab, EditorCommandScope.Inline), Is.EqualTo(EditorCommandIds.InlineAccept));
            Assert.That(dispatcher.Match(tab, EditorCommandScope.Snippet), Is.EqualTo(EditorCommandIds.SnippetNext));
            Assert.That(dispatcher.Match(tab, EditorCommandScope.List), Is.EqualTo(EditorCommandIds.CompletionAccept));
            Assert.That(dispatcher.Match(tab, EditorCommandScope.Global), Is.Null);
            Assert.That(dispatcher.Match(new(EditorKeyModifiers.Shift, '\t', EditorKey.Tab), EditorCommandScope.Snippet), Is.EqualTo(EditorCommandIds.SnippetPrevious));
            Assert.That(dispatcher.Match(escape, EditorCommandScope.List), Is.EqualTo(EditorCommandIds.CompletionClose));
            Assert.That(dispatcher.Match(escape, EditorCommandScope.Snippet), Is.EqualTo(EditorCommandIds.SnippetCancel));
            Assert.That(dispatcher.Match(escape, EditorCommandScope.Inline), Is.EqualTo(EditorCommandIds.InlineDismiss));
            Assert.That(dispatcher.Match(enter, EditorCommandScope.List), Is.EqualTo(EditorCommandIds.CompletionAcceptEnter));
            Assert.That(dispatcher.Match(new(EditorKeyModifiers.None, null, EditorKey.Down), EditorCommandScope.List), Is.EqualTo(EditorCommandIds.CompletionNext));
            Assert.That(dispatcher.Match(new(EditorKeyModifiers.None, null, EditorKey.Up), EditorCommandScope.List), Is.EqualTo(EditorCommandIds.CompletionPrevious));
        });
    }

    private static IEnumerable<TestCaseData> EventsWithoutTrigger()
    {
        yield return new TestCaseData(EditorKeyModifiers.Control).SetName("NoTrigger_ControlAlone");
        yield return new TestCaseData(EditorKeyModifiers.Shift).SetName("NoTrigger_ShiftAlone");
        yield return new TestCaseData(EditorKeyModifiers.Alt).SetName("NoTrigger_AltAlone");
        yield return new TestCaseData(EditorKeyModifiers.Meta).SetName("NoTrigger_MetaAlone");
        yield return new TestCaseData(EditorKeyModifiers.Control | EditorKeyModifiers.Shift).SetName("NoTrigger_ControlShift");
        yield return new TestCaseData(EditorKeyModifiers.None).SetName("NoTrigger_UnknownKeyWithoutModifier");
    }

    [TestCaseSource(nameof(EventsWithoutTrigger))]
    public void AModifierAloneOrAnUnmappedKeyNeverMatchesAnyCommandInAnyScope(EditorKeyModifiers modifiers)
    {
        var dispatcher = new EditorCommandDispatcher(EditorKeyBindings.Resolve(null));
        // O desktop produz tudo null: EditorKey não tem LeftCtrl/RightCtrl e modificadores não trazem KeySymbol.
        var keyEvent = new EditorKeyEvent(modifiers, null, null, null);
        Assert.That(keyEvent.HasTrigger, Is.False);
        Assert.Multiple(() =>
        {
            foreach (var scope in Enum.GetValues<EditorCommandScope>())
                Assert.That(dispatcher.Match(keyEvent, scope), Is.Null, $"Nenhum comando no escopo {scope}.");
            Assert.That(dispatcher.Match(keyEvent, EditorCommandScope.Inline), Is.Not.EqualTo(EditorCommandIds.InlineAccept), "Nunca aceita o ghost text.");
            Assert.That(dispatcher.Match(keyEvent, EditorCommandScope.Global), Is.Not.EqualTo(EditorCommandIds.CompletionShow), "Nunca abre a lista.");
            Assert.That(dispatcher.Match(keyEvent, EditorCommandScope.Global), Is.Not.EqualTo(EditorCommandIds.CompletionAi));
        });
    }

    [Test]
    public void ScopesDoNotFallBackToGlobalAndCtrlDotIsNoLongerADefault()
    {
        var dispatcher = new EditorCommandDispatcher(EditorKeyBindings.Resolve(null));
        var show = new EditorKeyEvent(EditorKeyModifiers.Control, null, EditorKey.Space);
        var ctrlDot = new EditorKeyEvent(EditorKeyModifiers.Control, '.', null, '.');
        Assert.Multiple(() =>
        {
            Assert.That(dispatcher.Match(show, EditorCommandScope.Global), Is.EqualTo(EditorCommandIds.CompletionShow));
            foreach (var scope in NonGlobalScopes)
                Assert.That(dispatcher.Match(show, scope), Is.Null, $"Sem fallback implícito de {scope} para Global.");
            foreach (var scope in Enum.GetValues<EditorCommandScope>())
                Assert.That(dispatcher.Match(ctrlDot, scope), Is.Null, $"Ctrl+. saiu dos padrões (escopo {scope}).");
            Assert.That(EditorKeyBindings.Defaults.SelectMany(entry => entry.Value), Has.None.EqualTo("Ctrl+."));
        });
    }

    [Test]
    public void CtrlDotKeepsWorkingAsAnExplicitOverrideWrittenByTheUser()
    {
        var session = new EditorKeyBindings { Bindings = new() { ["editor.completion.show"] = ["Ctrl+."] } };
        var resolved = Texts(EditorKeyBindings.Resolve(session.Validate()));
        var dispatcher = new EditorCommandDispatcher(EditorKeyBindings.Resolve(session));
        Assert.Multiple(() =>
        {
            Assert.That(resolved["editor.completion.show"], Is.EqualTo(CtrlDotOnly).AsCollection, "O override explícito substitui o padrão.");
            Assert.That(resolved, Has.Count.EqualTo(ExpectedDefaults.Count), "Os demais comandos continuam nos padrões.");
            Assert.That(dispatcher.Match(new(EditorKeyModifiers.Control, '.', null, '.'), EditorCommandScope.Global), Is.EqualTo(EditorCommandIds.CompletionShow));
            Assert.That(dispatcher.Match(new(EditorKeyModifiers.Control, null, EditorKey.Space), EditorCommandScope.Global), Is.Null, "O override substitui, não complementa.");
            Assert.That(session.Bindings, Has.Count.EqualTo(1), "Validar não reescreve nem remove entradas da sessão.");
        });
    }

    [Test]
    public void AGestureRepeatedInsideAScopeIsInvalidWhileTheSameGestureInAnotherScopeIsLegal()
    {
        Assert.Multiple(() =>
        {
            var sameScope = Assert.Throws<InvalidDataException>(() => EditorKeyBindings.Resolve(new EditorKeyBindings
            {
                Bindings = new() { ["editor.completion.previous"] = ["Tab"] }
            }));
            Assert.That(sameScope!.Message, Does.Contain("gesto 'Tab'").And.Contain("editor.completion.previous").And.Contain("editor.completion.accept").And.Contain("List"));

            // Tab serve simultaneamente a List, Snippet e Inline nos padrões: escopos diferentes não conflitam.
            Assert.That(Texts(EditorKeyBindings.Resolve(null)).Where(entry => entry.Value.Contains("Tab")).Select(entry => entry.Key),
                Is.EquivalentTo(TabCommands));
            Assert.That(() => EditorKeyBindings.Resolve(new EditorKeyBindings
            {
                Bindings = new() { ["editor.completion.accept"] = ["Tab"], ["editor.snippet.next"] = ["Tab"], ["editor.inline.accept"] = ["Tab"] }
            }), Throws.Nothing);
        });
    }

    [TestCase("")]
    [TestCase(" ")]
    [TestCase("Ctrl+")]
    [TestCase("+")]
    [TestCase("+Ctrl")]
    [TestCase("Ctrl + .")]
    [TestCase("Ctrl+Shift")]
    [TestCase("Ctrl+Ctrl+.")]
    [TestCase("Ctrl++Shift+.")]
    [TestCase("Hyper+.")]
    [TestCase("Ctrl+Esc")]
    [TestCase("Ctrl+..")]
    [TestCase("Ctrl+D0")]
    [TestCase("Ctrl+ç")]
    [TestCase(".")]
    [TestCase("A")]
    [TestCase("Space")]
    [TestCase("Shift+;")]
    public void MalformedGestureTextIsRejectedWithAReason(string text)
    {
        Assert.Multiple(() =>
        {
            Assert.That(EditorKeyGesture.TryParse(text, out var gesture, out var error), Is.False);
            Assert.That(error, Is.Not.Empty);
            Assert.That(gesture, Is.EqualTo(default(EditorKeyGesture)));
            Assert.Throws<FormatException>(() => EditorKeyGesture.Parse(text));
        });
    }

    [Test]
    public void NullGestureTextIsRejected() =>
        Assert.That(EditorKeyGesture.TryParse(null, out _, out var error) ? null : error, Is.EqualTo("Gesto vazio."));
}
