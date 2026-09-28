namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

[Flags]
public enum FieldTraits { None = 0, Required = 1, Indexed = 2, Array = 4, ArrayOfDocuments = 8, Enum = 16, Truncated = 32 }
