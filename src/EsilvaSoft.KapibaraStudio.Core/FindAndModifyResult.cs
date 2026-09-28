namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Resultado de uma operação atômica find-and-modify, retornado após a atualização.</summary>
public sealed record FindAndModifyResult(string? DocumentJson);
