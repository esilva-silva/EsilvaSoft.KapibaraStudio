using BenchmarkDotNet.Running;
using EsilvaSoft.KapibaraStudio.Benchmarks;

// dotnet run -c Release --project tests/EsilvaSoft.KapibaraStudio.Benchmarks -- --filter "*"   (BenchmarkDotNet)
// dotnet run -c Release --project tests/EsilvaSoft.KapibaraStudio.Benchmarks -- memory         (catalog memory scenario)
if (args is ["memory"])
{
    await MemoryScenario.RunAsync(Console.Out);
    return;
}
BenchmarkSwitcher.FromAssembly(typeof(SyntheticWorkload).Assembly).Run(args);
