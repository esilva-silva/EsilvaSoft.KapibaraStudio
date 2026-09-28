# Avaliação de contexto para IA — editor-context-v1

Saída efêmera do `AiContextEvaluationHarness`; não commitar.

## Execução

- Versão do formato: 1
- Gerado em (UTC): 2026-09-19T11:19:41.4774979+00:00
- Contrato: `editor-context-v1`
- Contador de tokens: `DeterministicTokenCounter` (contagem em tokens reais é A34c)
- Semente raiz: 20260319
- Repetições cronometradas por caso: 5 (aquecimento: 2)

## Máquina

- Sistema: Microsoft Windows 10.0.26200
- Arquitetura: X64
- Processadores lógicos: 24
- Runtime: .NET 10.0.12
- GC de servidor: False
- Depurador anexado: False
- Configuração: Release

## Distribuição do dataset

| Categoria | Valor | Casos |
| --- | --- | --- |
| total | — | 10000 |
| forma | Filter | 3334 |
| forma | Aggregation | 3333 |
| forma | Update | 3333 |
| schema | Small | 3334 |
| schema | Medium | 3333 |
| schema | Large | 3333 |
| lookup | com | 1667 |
| lookup | sem | 8333 |
| schema aprendido | com | 5004 |
| schema aprendido | sem | 4996 |
| coleções alvo distintas | — | 40 |

## Métricas agregadas

| Métrica | Média | Desvio padrão | Mín | p50 | p95 | Máx |
| --- | --- | --- | --- | --- | --- | --- |
| tokens do prompt | 580.80 | 264.44 | 289.00 | 479.00 | 1199.00 | 1439.00 |
| tokens dos fatos | 2379.35 | 2085.01 | 141.00 | 1029.00 | 5624.00 | 5722.00 |
| fatos selecionados | 214.86 | 189.05 | 13.00 | 91.00 | 512.00 | 512.00 |
| seleção (µs) | 51.18 | 66.80 | 2.30 | 15.80 | 156.70 | 960.00 |
| montagem (µs) | 5.48 | 3.40 | 3.40 | 5.30 | 6.50 | 40.90 |
| total (µs) | 56.66 | 68.29 | 5.80 | 21.90 | 162.00 | 979.40 |

## Orçamento e determinismo

- Casos que precisariam de corte: 0 de 10000 (0.00 %)
- Maior excesso: 0 tokens
- Casos não determinísticos: 0

## Recortes

| Dimensão | Valor | Casos | Tokens (média) | Tokens (desvio) | Total µs (média) | Estouros |
| --- | --- | --- | --- | --- | --- | --- |
| shape | Aggregation | 3333 | 624.7 | 265.4 | 80.0 | 0 |
| shape | Filter | 3334 | 553.8 | 261.2 | 44.9 | 0 |
| shape | Update | 3333 | 563.9 | 261.2 | 45.1 | 0 |
| schemaSize | Large | 3333 | 863.1 | 279.6 | 58.2 | 0 |
| schemaSize | Medium | 3333 | 479.8 | 66.6 | 56.2 | 0 |
| schemaSize | Small | 3334 | 399.5 | 66.5 | 55.6 | 0 |
| lookup | com | 1667 | 657.4 | 264.6 | 111.9 | 0 |
| lookup | sem | 8333 | 565.5 | 261.7 | 45.6 | 0 |
| learnedSchema | com | 5004 | 585.0 | 268.6 | 62.1 | 0 |
| learnedSchema | sem | 4996 | 576.6 | 260.1 | 51.2 | 0 |
