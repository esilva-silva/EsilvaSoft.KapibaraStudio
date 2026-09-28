namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

[Flags]
public enum SymbolKinds
{
    None = 0,
    Connection = 1 << 0, Database = 1 << 1, Collection = 1 << 2, View = 1 << 3, TimeSeriesCollection = 1 << 4, Field = 1 << 5, Index = 1 << 6,
    DslRoot = 1 << 7, ConnectionMethod = 1 << 8, DatabaseMethod = 1 << 9, CollectionMethod = 1 << 10, CursorMethod = 1 << 11, GlobalFunction = 1 << 12,
    BsonConstructor = 1 << 13, BsonType = 1 << 14, QueryOperator = 1 << 15, ProjectionOperator = 1 << 16, UpdateOperator = 1 << 17,
    AggregationStage = 1 << 18, ExpressionOperator = 1 << 19, Accumulator = 1 << 20, WindowOperator = 1 << 21, SystemVariable = 1 << 22,
    SearchOperator = 1 << 23, SearchOption = 1 << 24, Keyword = 1 << 25, Snippet = 1 << 26, LocalVariable = 1 << 27, EnvironmentKey = 1 << 28,
    Namespaces = Connection | Database | Collection | View | TimeSeriesCollection,
    Methods = ConnectionMethod | DatabaseMethod | CollectionMethod | CursorMethod,
    Operators = QueryOperator | ProjectionOperator | UpdateOperator | ExpressionOperator | Accumulator | WindowOperator
}
