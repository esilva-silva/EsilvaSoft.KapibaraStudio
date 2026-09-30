using System.Runtime.CompilerServices;

// These assemblies are trusted runtime/persistence boundaries. External callers cannot mint principals or policy grants.
[assembly: InternalsVisibleTo("EsilvaSoft.KapibaraStudio.Application")]
[assembly: InternalsVisibleTo("EsilvaSoft.KapibaraStudio.Infrastructure")]
[assembly: InternalsVisibleTo("EsilvaSoft.KapibaraStudio.Infrastructure.System")]
[assembly: InternalsVisibleTo("EsilvaSoft.KapibaraStudio.UnitTests")]
[assembly: InternalsVisibleTo("EsilvaSoft.KapibaraStudio.TestSupport")]
[assembly: InternalsVisibleTo("EsilvaSoft.KapibaraStudio.IntegrationTests")]
