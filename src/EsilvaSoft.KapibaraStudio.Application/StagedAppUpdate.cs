using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>A verified package waiting for the application to exit; <paramref name="LastApplyError"/> reports a failed previous attempt.</summary>
public sealed record StagedAppUpdate(AppVersion Version, string? LastApplyError);
