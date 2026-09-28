using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

/// <summary>
/// One context chip above the composer (ADR-056): exactly what will be sent with the next message. Automatic chips
/// (active file, tab metadata) follow the active tab until the user removes them for this message; attached files
/// stay until sent or removed. A refusal of <see cref="AgentAttachmentResolver"/> is shown on the chip itself in
/// warning style plus text, never by color alone. Contents are never kept here: only the path used to resolve.
/// </summary>
public sealed partial class AgentContextChipViewModel : ObservableObject
{
    private static LocalizationViewModel Text => LocalizationViewModel.Current;

    public AgentContextChipViewModel(AgentAttachmentKind kind, string name, string? qualifier, string? path, bool isAutomatic)
    {
        Kind = kind;
        _name = name;
        _qualifier = qualifier;
        Path = path;
        IsAutomatic = isAutomatic;
    }

    public AgentAttachmentKind Kind { get; }

    /// <summary>Full path of a workspace/external file (resolution only; never displayed or persisted).</summary>
    public string? Path { get; }

    /// <summary>Added by <c>AutomaticContext</c> (removable only for the current message).</summary>
    public bool IsAutomatic { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label), nameof(RemoveLabel))]
    private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label))]
    private string? _qualifier;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError), nameof(ErrorText), nameof(Tooltip))]
    private AgentAttachmentError? _error;

    /// <summary>"clientes.json · ativo" / "developercluster › CakeShop".</summary>
    public string Label => string.IsNullOrEmpty(Qualifier) ? Name : Text.Format("agentChipQualified", Name, Qualifier);

    public string KindGlyph => Kind switch
    {
        AgentAttachmentKind.TabMetadata => "⛁",
        AgentAttachmentKind.ExternalFile => "⇱",
        _ => "▤",
    };

    public string KindText => Text.Resolve(Kind switch
    {
        AgentAttachmentKind.ActiveFile => "agentChipKindActive",
        AgentAttachmentKind.WorkspaceFile => "agentChipKindWorkspace",
        AgentAttachmentKind.ExternalFile => "agentChipKindExternal",
        _ => "agentChipKindTab",
    });

    public string RemoveLabel => Text.Format("agentChipRemove", Name);

    public bool HasError => Error is not null;

    public string ErrorText => Error is { } error ? DescribeError(error) : "";

    public string Tooltip => HasError ? Label + " — " + ErrorText : KindText + ": " + Label;

    public AgentAttachmentRequest ToRequest() => new(Kind, Path);

    public static string DescribeError(AgentAttachmentError error) =>
        Text.HasTranslation("agentChipError." + error) ? Text.Resolve("agentChipError." + error) : error.ToString();

    /// <summary>History line of one sent attachment: name and size (never content).</summary>
    public static string DescribeDescriptor(AgentAttachmentDescriptor descriptor) =>
        descriptor.Kind == AgentAttachmentKind.TabMetadata
            ? descriptor.DisplayName
            : Text.Format("agentChipSize", descriptor.DisplayName, FormatSize(descriptor.SizeBytes));

    internal static string FormatSize(long bytes) => bytes < 1024
        ? bytes.ToString(CultureInfo.CurrentCulture) + " B"
        : (bytes / 1024d).ToString("0.#", CultureInfo.CurrentCulture) + " KB";
}
