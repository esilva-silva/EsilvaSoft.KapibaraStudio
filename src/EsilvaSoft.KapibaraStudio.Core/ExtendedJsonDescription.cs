namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Human description of one Extended JSON value. <see cref="Display"/> keeps the literal digits of numeric wrappers.</summary>
public readonly record struct ExtendedJsonDescription(string TypeName, string Display, ExtendedJsonShape Shape, int ChildCount);
