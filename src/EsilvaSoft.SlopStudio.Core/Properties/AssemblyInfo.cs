using System.Runtime.CompilerServices;

// These assemblies are trusted runtime/persistence boundaries. External callers cannot mint principals or policy grants.
[assembly: InternalsVisibleTo("EsilvaSoft.SlopStudio.Application")]
[assembly: InternalsVisibleTo("EsilvaSoft.SlopStudio.Infrastructure")]
[assembly: InternalsVisibleTo("EsilvaSoft.SlopStudio.UnitTests")]
