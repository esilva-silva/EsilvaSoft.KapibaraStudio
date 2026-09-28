namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>Normalization of dates read back from the workspace LiteDB file.</summary>
internal static class LiteDbDates
{
    /// <summary>
    /// Returns the UTC instant a stored BSON date represents. LiteDB (global mapper and <c>BsonValue.AsDateTime</c>)
    /// returns dates converted to local time (<see cref="DateTimeKind.Local"/>); relabelling that value as UTC with
    /// <see cref="DateTime.SpecifyKind"/> shifts the instant by the machine offset. An
    /// <see cref="DateTimeKind.Unspecified"/> value can only be the UTC this application wrote.
    /// </summary>
    internal static DateTimeOffset ToUtcInstant(DateTime date) =>
        new(date.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(date, DateTimeKind.Utc)
            : date.ToUniversalTime());
}
