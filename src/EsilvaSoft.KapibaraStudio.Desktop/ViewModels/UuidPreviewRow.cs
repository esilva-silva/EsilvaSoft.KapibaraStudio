using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed record UuidPreviewRow(UuidRepresentation Representation, string Code, string Details, bool IsSelected)
{
    public string Marker => IsSelected ? "Selecionada" : "";
}
