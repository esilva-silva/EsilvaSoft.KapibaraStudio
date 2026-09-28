namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;

/// <summary>Whether the fields advertised by a pipeline position are safe to use for completion.</summary>
public enum PipelineFieldState : byte
{
    Known,
    Unknown
}
