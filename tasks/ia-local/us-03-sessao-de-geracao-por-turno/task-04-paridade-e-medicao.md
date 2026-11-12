# KS-US03-T04 — Provar paridade e medir o ganho

**Estado de execução:** aberta; em 12/11/2026 só foi preparado o meio de medir. **Versão-alvo:** v0.11.0.

**User story:** [KS-US03 — Reutilizar o estado de geração entre os passos do turno](README.md). **Requisitos relacionados:** MVP-013. **Entregas oficiais:** F02-E03, F02-E05.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Comparar os dois caminhos e medir o tempo por passo em CPU.

**Depende de:** [KS-US03-T03 — Usar a sessão no laço do agente](task-03-provider-usa-sessao.md); [KS-US01-T01 — Registrar pacote de referência e limites](../us-01-referencia-e-contratos-do-pacote/task-01-registrar-referencia-e-limites.md)

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Contrato de IA local](../../../docs/MVP/contratos/ia-local.md)
- [KapiLab](../../../tools/KapiLab/README.md)

## Execução

- [ ] Comparar, em modo greedy, os tokens gerados pelo caminho com sessão e pelo caminho com nova renderização nas tarefas de referência.
- [ ] Medir tempo até o primeiro token de cada passo e tempo total do turno, separando aquecimento.
- [ ] Registrar pico de RAM com a sessão ativa.
- [ ] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Paridade dentro do limite fixado em KS-US01 e ganho registrado; diferença de saída sem causa conhecida mantém a tarefa aberta.

- **Automatizável:** paridade com runtime falso; medições reais não entram no CI.
- **Real/manual:** medição manual com modelo real e hardware identificados.
- **Conclusão:** todos os critérios obrigatórios aprovados e vinculados à entrega oficial. Critério inaplicável exige justificativa; falho, ignorado ou não exercitado permanece aberto.

## Registro de execução

Execução parcial registrada abaixo; itens não exercitados continuam abertos.

| Campo | Registro |
| --- | --- |
| Snapshot e alterações locais | `fcc4b72` em `master`, com as alterações dos lotes KS-US01/02/03 ainda não commitadas |
| Data, responsável, ambiente e versões | 12/11/2026 · Claude Code a pedido do usuário · Windows 11 · .NET SDK 10.0.402 · sem modelo real, sem MongoDB |
| Cenários, comandos, esperado e observado | Acrescentada a opção `--session-mode` ao `kapilab agent run`, que liga a sessão por turno e a aceita em pacote ainda não homologado; o relatório registra `sessionMode`. Nenhuma comparação de tokens nem medição de tempo ou RAM foi executada. Build da solução com `-p:UsedAvaloniaProducts=`: 0 avisos, 0 erros. `dotnet test` da solução: Infrastructure.Agents.Tests 403/403; UnitTests 3.800 aprovados e 20 ignorados; IntegrationTests 1.126 aprovados, 20 ignorados e 1 falha (`AttachmentsCountTowardsTheInputLimitAndFailTypedWithoutAnyProcess`, bloqueado por variáveis de ambiente do Claude Code herdadas da sessão). KapiLab.Tests, fora da solução, rodado depois de acrescentar `--session-mode`: 717 aprovados, 7 ignorados e 2 falhas de sinalização de processo (`RealWindowsCtrlCPreservesThePriorSampleAndPublishesAnIncompleteCancelledReport`, que depende de CTRL_C_EVENT de console real, e `CancellationStopsTheCurrentChildAndKeepsEarlierResults`, que passou ao ser reexecutado isoladamente); nenhuma das duas foi comparada com o checkout sem as alterações. |
| Resultado por critério | Pendente: paridade greedy entre os dois caminhos, tempo por passo e pico de RAM. |
| Evidência oficial e artefatos sanitizados | `src/EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi.OnnxAdapter/PrefixReusePolicy.cs`, `tests/EsilvaSoft.KapibaraStudio.UnitTests/PrefixReusePolicyTests.cs` e `LocalAgentProviderToolLoopTests.cs`; contrato atualizado em [IA local](../../../docs/MVP/contratos/ia-local.md) e opção documentada no [KapiLab](../../../tools/KapiLab/README.md) |
| Limitações e bloqueios | Bloqueio: falta pacote de agente exportado para CPU (KT-US02-T03 ou KT-US07-T01 do repositório de modelos) e os limites de KS-US01-T01. Próximo passo: rodar as mesmas tarefas com e sem `--session-mode` em `--device cpu`, com a máquina ociosa. |

Não armazenar credenciais, texto de raciocínio ou payloads sensíveis. Ao concluir, atualizar o estado de execução acima e os checklists com links para a evidência oficial; o estado dos requisitos continua no catálogo MVP.
