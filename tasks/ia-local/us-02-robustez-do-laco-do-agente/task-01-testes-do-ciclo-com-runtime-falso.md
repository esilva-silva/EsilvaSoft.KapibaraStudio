# KS-US02-T01 — Cobrir o ciclo de tools com runtime falso

**Estado de execução:** concluída na parte automatizável em 12/11/2026; modelo real fica em KS-US05 e KS-US10. **Versão-alvo:** v0.11.0.

**User story:** [KS-US02 — Tornar o laço do agente local previsível em sucesso e falha](README.md). **Requisitos relacionados:** MVP-013. **Entregas oficiais:** F02-E03.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Cobrir o laço de vários passos do `LocalAgentProvider` com um runtime falso determinístico, antes de alterar seu comportamento.

**Depende de:** nenhuma tarefa anterior desta árvore.

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Contrato de IA local](../../../docs/MVP/contratos/ia-local.md)
- [Tools de agentes](../../../docs/MVP/contratos/tools-agentes.md)

## Execução

- [x] Criar runtime falso que emite sequências de tokens roteirizadas, inclusive `<think>` e `<tool_call>`.
- [x] Testar `ToolRequested` → `SubmitToolResultAsync` → resposta final, com um e com vários passos.
- [x] Testar o limite de chamadas por turno, o orçamento de raciocínio por passo e por turno e o fechamento forçado.
- [x] Testar o rollback do histórico quando o turno falha e a remoção do raciocínio entre turnos.
- [x] Testar resultado de tool truncado e com erro.
- [x] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Os cenários acima passam sem modelo real e falham quando o comportamento correspondente é removido; não repetem a implementação.

- **Automatizável:** testes de comportamento e de falha da alteração, mais restore, build e testes das regras comuns; usam runtime e `HttpMessageHandler` falsos e não carregam modelo.
- **Real/manual:** não aplicável; modelo real fica em KS-US05 e KS-US10.
- **Conclusão:** todos os critérios obrigatórios aprovados e vinculados à entrega oficial. Critério inaplicável exige justificativa; falho, ignorado ou não exercitado permanece aberto.

## Registro de execução

Execução parcial registrada abaixo; itens não exercitados continuam abertos.

| Campo | Registro |
| --- | --- |
| Snapshot e alterações locais | `fcc4b72` em `master`, com as alterações deste lote ainda não commitadas |
| Data, responsável, ambiente e versões | 12/11/2026 · Claude Code a pedido do usuário · Windows 11 · .NET SDK 10.0.402 · sem modelo real, sem MongoDB |
| Cenários, comandos, esperado e observado | Runtime roteirizado por passo (tokens de `<think>` e `<tool_call>`) e registry falso que só anuncia descritores. 20 testes novos: ciclo de uma e de várias tools, limite de chamadas, orçamento por passo e por turno, fechamento forçado, retry de envelope, rollback do histórico, raciocínio fora do turno seguinte, resultado truncado e com erro. Restore `--locked-mode`, build da solução com `-p:UsedAvaloniaProducts=` (0 avisos, 0 erros) e `dotnet test` da solução: Infrastructure.Agents.Tests 403/403; UnitTests 3.794 aprovados e 20 ignorados; IntegrationTests 1.126 aprovados, 20 ignorados e 1 falha (`AttachmentsCountTowardsTheInputLimitAndFailTypedWithoutAnyProcess`, bloqueado por variáveis de ambiente do Claude Code herdadas da sessão; não toca o código alterado). Foco: `--filter FullyQualifiedName~LocalAgentProvider` 38/38. |
| Resultado por critério | Aprovado. Os testes falharam antes das correções correspondentes (estouro de janela e raciocínio cancelado), o que mostra que detectam regressão. |
| Evidência oficial e artefatos sanitizados | `tests/EsilvaSoft.KapibaraStudio.UnitTests/LocalAgentProviderToolLoopTests.cs`; contrato atualizado em [IA local](../../../docs/MVP/contratos/ia-local.md) |
| Limitações e bloqueios | O registry real e a auditoria não participam destes testes; a fronteira de permissões continua coberta pelos testes de registry existentes. |

Não armazenar credenciais, texto de raciocínio ou payloads sensíveis. Ao concluir, atualizar o estado de execução acima e os checklists com links para a evidência oficial; o estado dos requisitos continua no catálogo MVP.
