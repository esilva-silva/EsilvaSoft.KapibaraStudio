# Perfil de latência da IA local

Saída efêmera do `AiRuntimeHarness`; não commitar.

## Execução

- Versão do formato: 1
- Gerado em (UTC): 2026-09-19T21:51:38.5320428+00:00
- Repetições cronometradas por cenário: 9 (aquecimento: 1)
- Cenários: curto-g32, curto-g96, medio-g32, medio-g96, longo-g32, longo-g96

## Máquina

- Sistema: Microsoft Windows 10.0.26200
- Arquitetura: X64
- Processadores lógicos: 24
- Runtime: .NET 10.0.12
- GC de servidor: False
- Depurador anexado: False
- Configuração: Release

## Hardware detectado

| Tipo | Provider | Dispositivo | Disponível | Motivo |
| --- | --- | --- | --- | --- |
| Cpu | CPU | AMD Ryzen 9 7900 12-Core Processor | True | — |
| Gpu | DirectML | AMD Radeon RX 7800 XT | True | — |
| Npu |  | NPU | False | Nenhum provider de NPU (QNN, OpenVINO ou VitisAI) disponível nesta distribuição ou máquina. |

## Alvos

| Alvo | Evidência | Provider efetivo | Carga (ms) | Working set pós-carga (MiB) | Pico (MiB) | Casos | Gerados | Recusas |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SlopCoder-Mongo-0.5B-ONNX-int4 · Cpu | RealModel | CPU | 920 | 1459 | 1778 | 54 | 54 | — |
| SlopCoder-Mongo-0.5B-ONNX-int4 · Gpu | RealModel |  | 536 | 827 | 827 | 1 | 0 | Refused=1 |
| SlopCoder-Mongo-1.5B-full ONNX DML-FP16 · Cpu | RealModel |  | 24 | 6551 | 6551 | 1 | 0 | Refused=1 |
| SlopCoder-Mongo-1.5B-full ONNX DML-FP16 · Gpu | RealModel | DirectML | 2333 | 6660 | 7141 | 54 | 54 | — |

## Métricas por alvo

### SlopCoder-Mongo-0.5B-ONNX-int4 · Cpu (RealModel)

| Métrica | Média | Desvio padrão | Mín | p50 | p95 | Máx |
| --- | --- | --- | --- | --- | --- | --- |
| TTFT do runtime (ms) | 866.44 | 987.78 | 74.91 | 246.10 | 2254.05 | 2282.68 |
| 1º texto observado (ms) | 868.47 | 990.04 | 75.31 | 246.70 | 2259.00 | 2287.55 |
| total (ms) | 1165.02 | 1046.84 | 223.46 | 588.15 | 2631.06 | 2652.41 |
| tokens/s (decode) | 79.79 | 10.70 | 61.79 | 85.06 | 87.98 | 108.04 |
| contexto (µs) | 360.08 | 211.25 | 135.00 | 270.80 | 690.08 | 695.60 |
| tokenização (µs) | 215.47 | 158.21 | 70.40 | 126.75 | 484.57 | 493.30 |
| tokens do prompt | 626.00 | 692.50 | 85.00 | 199.00 | 1594.00 | 1594.00 |
| tokens gerados | 24.00 | 5.40 | 17.00 | 25.00 | 30.00 | 30.00 |
| working set (MiB) | 1586.17 | 133.58 | 1479.82 | 1503.80 | 1776.09 | 1777.86 |

| Cenário | Tokens do prompt | Tokens gerados (p50) | TTFT p50 (ms) | TTFT p95 (ms) | Total p50 (ms) | Total p95 (ms) | tokens/s p50 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| curto-g32 | 85 | 17 | 110.0 | 112.1 | 293.5 | 295.8 | 87.4 |
| curto-g96 | 85 | 17 | 109.8 | 111.3 | 293.3 | 294.1 | 87.6 |
| medio-g32 | 199 | 30 | 246.8 | 248.3 | 588.5 | 591.5 | 84.7 |
| medio-g96 | 199 | 30 | 245.8 | 249.3 | 588.0 | 592.5 | 85.2 |
| longo-g32 | 1594 | 25 | 2245.7 | 2254.2 | 2614.3 | 2625.1 | 65.9 |
| longo-g96 | 1594 | 25 | 2248.8 | 2271.4 | 2624.6 | 2646.1 | 65.3 |

### SlopCoder-Mongo-0.5B-ONNX-int4 · Gpu (RealModel)

| Métrica | Média | Desvio padrão | Mín | p50 | p95 | Máx |
| --- | --- | --- | --- | --- | --- | --- |
| TTFT do runtime (ms) | — | — | — | — | — | — |
| 1º texto observado (ms) | — | — | — | — | — | — |
| total (ms) | — | — | — | — | — | — |
| tokens/s (decode) | — | — | — | — | — | — |
| contexto (µs) | — | — | — | — | — | — |
| tokenização (µs) | — | — | — | — | — | — |
| tokens do prompt | — | — | — | — | — | — |
| tokens gerados | — | — | — | — | — | — |
| working set (MiB) | — | — | — | — | — | — |

Primeira recusa: Não foi possível executar este modelo utilizando GPU.
Motivo: falha do provider DirectML: This session cannot use the graph capture feature as requested by the user  as all compute graph nodes have not been partitioned to the DmlExecutionProvider
Você pode selecionar: Automático ou CPU.

### SlopCoder-Mongo-1.5B-full ONNX DML-FP16 · Cpu (RealModel)

| Métrica | Média | Desvio padrão | Mín | p50 | p95 | Máx |
| --- | --- | --- | --- | --- | --- | --- |
| TTFT do runtime (ms) | — | — | — | — | — | — |
| 1º texto observado (ms) | — | — | — | — | — | — |
| total (ms) | — | — | — | — | — | — |
| tokens/s (decode) | — | — | — | — | — | — |
| contexto (µs) | — | — | — | — | — | — |
| tokenização (µs) | — | — | — | — | — | — |
| tokens do prompt | — | — | — | — | — | — |
| tokens gerados | — | — | — | — | — | — |
| working set (MiB) | — | — | — | — | — | — |

Primeira recusa: Não foi possível executar este modelo utilizando CPU.
Motivo: O modelo declara suporte apenas a GPU em slopstudio-model.json.
Você pode selecionar: Automático.

### SlopCoder-Mongo-1.5B-full ONNX DML-FP16 · Gpu (RealModel)

| Métrica | Média | Desvio padrão | Mín | p50 | p95 | Máx |
| --- | --- | --- | --- | --- | --- | --- |
| TTFT do runtime (ms) | 202.65 | 116.90 | 108.08 | 127.69 | 369.64 | 394.06 |
| 1º texto observado (ms) | 206.63 | 118.25 | 111.18 | 130.64 | 375.37 | 400.20 |
| total (ms) | 647.44 | 402.65 | 299.12 | 503.04 | 1465.09 | 1482.95 |
| tokens/s (decode) | 81.09 | 9.24 | 66.79 | 79.78 | 94.10 | 95.21 |
| contexto (µs) | 278.20 | 182.89 | 82.50 | 198.85 | 537.56 | 623.40 |
| tokenização (µs) | 176.92 | 149.16 | 52.00 | 85.40 | 398.62 | 425.60 |
| tokens do prompt | 626.00 | 692.50 | 85.00 | 199.00 | 1594.00 | 1594.00 |
| tokens gerados | 37.67 | 26.56 | 15.00 | 32.00 | 93.00 | 93.00 |
| working set (MiB) | 6926.53 | 130.79 | 6700.00 | 6926.73 | 7122.14 | 7141.39 |

| Cenário | Tokens do prompt | Tokens gerados (p50) | TTFT p50 (ms) | TTFT p95 (ms) | Total p50 (ms) | Total p95 (ms) | tokens/s p50 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| curto-g32 | 85 | 15 | 113.4 | 121.3 | 311.3 | 330.9 | 72.3 |
| curto-g96 | 85 | 15 | 115.5 | 126.1 | 314.6 | 323.9 | 71.6 |
| medio-g32 | 199 | 32 | 126.1 | 128.0 | 472.1 | 477.9 | 90.4 |
| medio-g96 | 199 | 39 | 128.3 | 129.4 | 538.2 | 540.6 | 93.5 |
| longo-g32 | 1594 | 32 | 359.9 | 363.1 | 787.2 | 791.2 | 73.5 |
| longo-g96 | 1594 | 93 | 368.8 | 385.9 | 1463.4 | 1479.3 | 84.6 |

## Limitação declarada

Este harness é um processo de console sem Avalonia: ele não mede orçamento por quadro da interface durante a geração. Ausência de travamento observável em CPU está coberta pelos testes Headless de A42; a medição de quadros exige o aplicativo nativo rodando e é evidência manual, não deste relatório.
