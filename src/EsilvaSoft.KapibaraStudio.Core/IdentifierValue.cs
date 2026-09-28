namespace EsilvaSoft.KapibaraStudio.Core;

/// <param name="Kind">Identifier type; for BSON input it is the stored type, never a guess from the text.</param>
/// <param name="Text">Text that restores the same BSON value, e.g. <c>ObjectId("…")</c> or <c>CGUUID("…")</c>.</param>
/// <param name="ExtendedJson">Canonical Extended JSON of the same value.</param>
/// <param name="UuidEquivalent">ObjectId only: deterministic alternative UUID text. It is presentation and is never written.</param>
/// <param name="IsExplicitType">True for BSON wrappers and named constructors; false when inferred from bare hexadecimal text.</param>
public sealed record IdentifierValue(IdentifierKind Kind, string Text, string ExtendedJson, string? UuidEquivalent, bool IsExplicitType);
