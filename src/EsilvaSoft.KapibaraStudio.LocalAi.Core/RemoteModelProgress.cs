namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

public sealed record RemoteModelProgress(long CompletedBytes, long TotalBytes, string CurrentFile);
