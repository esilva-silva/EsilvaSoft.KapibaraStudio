namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

/// <summary>A published file: <paramref name="Sha256"/> is the LFS content hash when present; <paramref name="GitBlobSha1"/> is the git object id.</summary>
public sealed record RemoteModelFile(string Path, long Size, string? Sha256, string GitBlobSha1);
