namespace EsilvaSoft.KapibaraStudio.Core;

public sealed record IndexInfo(string Name, string Keys, string Options, string Definition, bool Unique, bool Sparse, string Ttl, string PartialFilter);
