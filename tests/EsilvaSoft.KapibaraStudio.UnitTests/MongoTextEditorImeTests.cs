using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Interactivity;
using Avalonia.Media;
using EsilvaSoft.KapibaraStudio.Desktop.SyntaxHighlighting;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, NonParallelizable]
public sealed class MongoTextEditorImeTests
{
    private static readonly bool[] ExpectedCompositionStates = [true, true, false];

    [Test]
    public async Task PublishesPreeditLifecycleAndForwardsPlatformCalls()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch<bool>(() =>
        {
            var editor = new MongoTextEditor();
            var inner = new FakeTextInputMethodClient();
            var states = new List<bool>();
            editor.ImeCompositionChanged += (_, composing) => states.Add(composing);
            var args = new TextInputMethodClientRequestedEventArgs
            {
                RoutedEvent = InputElement.TextInputMethodClientRequestedEvent,
                Client = inner
            };

            editor.RaiseEvent(args);
            Assert.That(args.Client, Is.Not.SameAs(inner), "O cliente exposto precisa observar o preedit mantendo o cliente nativo delegado.");
            args.Client.SetPreeditText("kana", 2);
            args.Client.SetPreeditText("かな", 2);
            args.Client.SetPreeditText("");

            Assert.That(inner.PreeditCalls, Is.EqualTo(3), "Todas as atualizações continuam chegando ao cliente do AvaloniaEdit.");
            Assert.That(states, Is.EqualTo(ExpectedCompositionStates));
            return true;
        }, CancellationToken.None);
    }

    private sealed class FakeTextInputMethodClient : TextInputMethodClient
    {
        public int PreeditCalls { get; private set; }
        public override Visual TextViewVisual => new Visual();
        public override bool SupportsPreedit => true;
        public override bool SupportsSurroundingText => true;
        public override string SurroundingText => "";
        public override Rect CursorRectangle => default;
        public override TextSelection Selection { get; set; }
        public override void SetPreeditText(string? text) => PreeditCalls++;
        public override void SetPreeditText(string? text, int? cursor) => PreeditCalls++;
        public override void ExecuteContextMenuAction(ContextMenuAction action) { }
    }
}
