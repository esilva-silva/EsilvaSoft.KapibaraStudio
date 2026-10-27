namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

public interface ISnapdragonModelProvisioner
{
    void SetLocalization(Func<string, string> localize) { }
    Task<SnapdragonProvisioningReport> ProvisionAsync(string modelsDirectory, bool retry = false, CancellationToken cancellationToken = default);
}

public sealed record SnapdragonProvisioningReport(bool Eligible, IReadOnlyList<SnapdragonProvisionedPackage> Packages, string? Error = null);
public sealed record SnapdragonProvisionedPackage(string Repository, string? Path, bool Completed, bool Cancelled, string? Error);

/// <summary>The only publications eligible for automatic provisioning; CPU defaults remain separate.</summary>
public static class SnapdragonModelPublications
{
    public const string Folder = "htp-fim-exact-v1";
    public const string NanoRevision = "a85d11c9f8db2f4388836c991b292b1cdb160d39";
    public const string SmallRevision = "c03ba2339ba263a3881134d0acd3ee24e1a80717";
    public static IReadOnlyList<(string Repository, string Revision)> Pinned { get; } =
    [
        ("esilva/KapiCoder-Mongo-Nano-ONNX", NanoRevision),
        ("esilva/KapiCoder-Mongo-Small-ONNX", SmallRevision)
    ];

    public static bool Matches(RemoteModelVariant variant) => variant.Variant == Folder
        && Pinned.Any(publication => publication.Repository == variant.Repository && publication.Revision == variant.Revision);
}
