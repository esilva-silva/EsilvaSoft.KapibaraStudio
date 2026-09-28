using System.Runtime.CompilerServices;

// These assemblies are trusted runtime/persistence boundaries. External callers cannot mint principals or policy grants.
[assembly: InternalsVisibleTo("EsilvaSoft.KapibaraStudio.Application")]
[assembly: InternalsVisibleTo("EsilvaSoft.KapibaraStudio.Infrastructure")]
[assembly: InternalsVisibleTo("EsilvaSoft.KapibaraStudio.UnitTests")]
