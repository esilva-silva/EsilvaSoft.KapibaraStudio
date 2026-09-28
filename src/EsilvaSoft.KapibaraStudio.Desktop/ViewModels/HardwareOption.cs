using CommunityToolkit.Mvvm.ComponentModel;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class HardwareOption(AiAccelerationMode mode, string label) : ObservableObject
{
    public AiAccelerationMode Mode { get; } = mode;
    [ObservableProperty] private string _label = label;
    [ObservableProperty] private bool _isAvailable = true;
    public override string ToString() => Label;
}
