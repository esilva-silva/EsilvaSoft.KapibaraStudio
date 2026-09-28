namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>
/// Normalized key data supplied by the desktop shell to <see cref="EditorCommandDispatcher"/>. A modifier pressed alone
/// carries no trigger at all: every trigger field is null and <see cref="HasTrigger"/> is false.
/// </summary>
public readonly record struct EditorKeyEvent(EditorKeyModifiers Modifiers, char? Symbol, EditorKey? PhysicalKey, char? PhysicalSymbol = null)
{
    /// <summary>False when only modifiers were pressed; such an event can never match a gesture.</summary>
    public bool HasTrigger => Symbol is not null || PhysicalKey is not null || PhysicalSymbol is not null;
}
