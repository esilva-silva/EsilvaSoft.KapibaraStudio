# KS-US03 — Reutilizar o estado de geração entre os passos do turno

**Estado de execução:** aberta; criação desta pasta não executa nem homologa nenhuma entrega. **Versão-alvo:** v0.11.0.

**Requisitos relacionados:** MVP-013. **Entregas oficiais:** F02-E03, F02-E05.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## História

**Como** pessoa que usa o agente local em CPU, **quero** que cada passo do turno continue de onde o anterior parou, **para** não esperar o reprocessamento de todo o prompt a cada chamada de tool.

## Contexto verificado

- Hoje cada passo chama `StreamAsync` com o chat inteiro renderizado de novo, ou seja, prefill completo por passo.
- O fechamento forçado do raciocínio para a geração, grava `<think>` + texto + `ForceCloseText` no histórico e chama o modelo novamente, desligando o raciocínio pelo resto do turno.
- `PrefixSessionCache` reaproveita KV apenas no autocomplete em CPU, greedy, com opt-in; o agente não usa esse caminho.
- O pacote candidato levou cerca de 125 s por tarefa no `kapilab agent run`.

## Critérios de aceite

- [ ] Existe uma porta neutra de sessão de geração, isolada por turno e por aba.
- [ ] O runtime ONNX mantém um gerador vivo durante o turno e o libera com segurança.
- [ ] O provider usa a sessão quando disponível e mantém o caminho atual como alternativa.
- [ ] A saída é equivalente à do caminho atual e o ganho de tempo em CPU está medido.

## Fora de escopo

- Cache entre turnos ou entre abas.
- Habilitar `PrefixCacheMode=Reuse`.

## Dependências

- [KS-US02 — Tornar o laço do agente local previsível em sucesso e falha](../us-02-robustez-do-laco-do-agente/README.md)

## Tarefas

| Tarefa | Resultado | Depende de |
| --- | --- | --- |
| [KS-US03-T01 — Definir a porta de sessão de geração](task-01-porta-de-sessao.md) | Contrato neutro em `LocalAi.Core` | KS-US02-T01 |
| [KS-US03-T02 — Implementar a sessão no runtime ONNX](task-02-implementacao-onnx.md) | Gerador vivo por turno no adapter ONNX | KS-US03-T01 |
| [KS-US03-T03 — Usar a sessão no laço do agente](task-03-provider-usa-sessao.md) | Passos sem prefill completo | KS-US03-T02 |
| [KS-US03-T04 — Provar paridade e medir o ganho](task-04-paridade-e-medicao.md) | Paridade greedy e tempos por passo | KS-US03-T03, KS-US01-T01 |

A história só fecha quando todos os critérios de aceite têm evidência proporcional. Evidência sintética e real permanecem separadas; preparação não equivale a aceite.
