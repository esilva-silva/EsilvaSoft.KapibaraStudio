namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;

/// <summary>Cursor role inferred from lexical and structural evidence. Unknown is intentionally safe to complete broadly.</summary>
public enum CompletionCursorRole : byte
{
    StatementStart, MemberAccess, IndexerString, CallArgument, PropertyKey, PropertyKeyString,
    PropertyValue, ArrayElement, StringArgument, FieldReferenceString, VariableReferenceString,
    NonCompletable, Unknown
}
