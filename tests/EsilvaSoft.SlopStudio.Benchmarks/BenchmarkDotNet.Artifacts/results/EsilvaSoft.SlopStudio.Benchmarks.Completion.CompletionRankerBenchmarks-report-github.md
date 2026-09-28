```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9457/25H2/2025Update/HudsonValley2)
AMD Ryzen 9 7900 3.70GHz, 1 CPU, 24 logical and 12 physical cores
.NET SDK 10.0.401
  [Host] : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

IterationCount=3  LaunchCount=1  WarmupCount=1  

```
| Method                            | CandidateCount | Mean | Error |
|---------------------------------- |--------------- |-----:|------:|
| &#39;CompletionRanker.Rank (top 100)&#39; | 20             |   NA |    NA |

Benchmarks with issues:
  CompletionRankerBenchmarks.'CompletionRanker.Rank (top 100)': Job-FGEKWY(IterationCount=3, LaunchCount=1, WarmupCount=1) [CandidateCount=20]
