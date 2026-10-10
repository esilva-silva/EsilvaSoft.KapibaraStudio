using System.CommandLine;
using EsilvaSoft.KapibaraStudio.KapiLab.Cli;

return await KapiLabCommandLine.Build().Parse(args).InvokeAsync();
