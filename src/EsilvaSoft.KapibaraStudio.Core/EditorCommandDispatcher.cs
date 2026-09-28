namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>
/// Matches persisted editor gestures without taking a dependency on a UI toolkit. Matching is decided by the kind of the
/// gesture: a named-key gesture matches by key identity and a punctuation gesture matches by the produced symbol first and
/// the physical symbol second, so a physical US key never invokes a command when the active layout produced another symbol.
/// A key event is resolved against exactly one <see cref="EditorCommandScope"/>; there is no implicit fallback to
/// <see cref="EditorCommandScope.Global"/>, and precedence between scopes belongs to the caller.
/// </summary>
public sealed class EditorCommandDispatcher(IReadOnlyDictionary<string, IReadOnlyList<EditorKeyGesture>> bindings)
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<EditorKeyGesture>> _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));

    /// <summary>The command bound to the event inside <paramref name="scope"/>, or null when nothing matches.</summary>
    public string? Match(EditorKeyEvent keyEvent, EditorCommandScope scope)
    {
        if (!keyEvent.HasTrigger) return null;
        foreach (var command in EditorCommandIds.InScope(scope))
        {
            if (!_bindings.TryGetValue(command, out var gestures)) continue;
            foreach (var gesture in gestures)
                if (Matches(gesture, keyEvent)) return command;
        }
        return null;
    }

    private static bool Matches(EditorKeyGesture gesture, EditorKeyEvent keyEvent)
    {
        if (gesture.Modifiers != keyEvent.Modifiers) return false;
        // Named keys such as Tab, Escape and Enter also produce a symbol; deciding by the gesture kind keeps them matching by
        // key identity while punctuation still honours the character produced by the active layout.
        if (gesture.Key is { } key) return keyEvent.PhysicalKey == key;
        if (keyEvent.Symbol is { } symbol) return symbol == gesture.Symbol;
        return keyEvent.PhysicalSymbol == gesture.Symbol && keyEvent.PhysicalSymbol is not null;
    }
}
