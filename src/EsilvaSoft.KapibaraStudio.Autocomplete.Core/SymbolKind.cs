namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

/// <summary>Kind of knowledge the IDE can suggest. <see cref="SymbolKinds"/> uses the same order as flags.</summary>
public enum SymbolKind
{
    Connection, Database, Collection, View, TimeSeriesCollection, Field, Index,
    DslRoot, ConnectionMethod, DatabaseMethod, CollectionMethod, CursorMethod, GlobalFunction,
    BsonConstructor, BsonType, QueryOperator, ProjectionOperator, UpdateOperator,
    AggregationStage, ExpressionOperator, Accumulator, WindowOperator, SystemVariable,
    SearchOperator, SearchOption, Keyword, Snippet, LocalVariable, EnvironmentKey
}
