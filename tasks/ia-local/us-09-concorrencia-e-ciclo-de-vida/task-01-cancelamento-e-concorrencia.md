# KS-US09-T01 — Validar cancelamento isolado e concorrência

**Estado de execução:** em andamento; isolamento coberto por testes com runtime falso em 13/11/2026. **Versão-alvo:** v0.11.0.

**User story:** [KS-US09 — Garantir concorrência, liberação e recuperação](README.md). **Requisitos relacionados:** MVP-011, MVP-012, MVP-013. **Entregas oficiais:** F02-E04.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Validar que operações de IA local em abas e papéis diferentes não interferem entre si.

**Depende de:** [KS-US02-T04 — Encerrar raciocínio e tool pendente no cancelamento](../us-02-robustez-do-laco-do-agente/task-04-cancelamento-do-raciocinio.md); [KS-US08-T01 — Validar o fluxo de prévia e aceite](../us-08-autocomplete-explicito/task-01-fluxo-ctrl-ponto-e-virgula.md)

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Arquitetura e invariantes](../../../docs/MVP/03-arquitetura-e-invariantes.md)
- [Contrato de IA local](../../../docs/MVP/contratos/ia-local.md)

## Execução

- [x] Testar autocomplete em uma aba e agente em outra, com cancelamento de apenas uma.
- [x] Testar preempção: novo pedido com um modelo carregado por vez, sem perder o contexto capturado.
- [x] Testar que resultado tardio não atualiza aba diferente da que o originou.
- [ ] Repetir com modelo real o cancelamento após o primeiro token.
- [ ] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Testes de falha e concorrência passam; o cenário real confirma a interrupção sem alegar rollback.

- **Automatizável:** testes de comportamento e de falha da alteração, mais restore, build e testes das regras comuns; usam runtime e `HttpMessageHandler` falsos e não carregam modelo.
- **Real/manual:** cenário com modelo real é manual, `[Explicit]` com `Category("LocalModelIntegration")`, em ambiente identificado; sem modelo configurado, não declarar o cenário executado.
- **Conclusão:** todos os critérios obrigatórios aprovados e vinculados à entrega oficial. Critério inaplicável exige justificativa; falho, ignorado ou não exercitado permanece aberto.

## Registro de execução

Execução parcial registrada abaixo; itens não exercitados continuam abertos.

| Campo | Registro |
| --- | --- |
| Snapshot e alterações locais | `d8be8dc` em `master`, com as alterações deste lote ainda não commitadas |
| Data, responsável, ambiente e versões | 13/11/2026 · Claude Code a pedido do usuário · Windows 11 · .NET SDK 10.0.402 · Headless, sem modelo real e sem MongoDB |
| Cenários, comandos, esperado e observado | Mapeamento: cancelar um turno não afeta outra sessão nem descarrega o modelo (`CancellingOneSessionTurnDoesNotCancelAnotherSessionOrUnloadTheModel`), turno do agente tem preferência sobre autocomplete em segundo plano e ambos concluem (`AgentTurnPreemptsBackgroundAutocompleteAndAutocompleteKeepsLoadedOnlyAfterwards`, `ConcurrentAutocompleteAndAgentTurnsBothCompleteOnOneModel`), fila serializa sem fome (`ConcurrentBackgroundGenerationsAreSerializedByTheQueueAndNoneIsStarved`), resultado tardio não alcança outra aba (`ALateSuggestionFromOneTabNeverReachesAnother`), e os testes novos de cancelamento durante raciocínio e durante espera de tool de KS-US02. Rodada focal em UnitTests (`LocalAiModelServiceTests`, `LocalAiModelServiceStreamingTests`, `LocalAiOfflinePreemptionTests`, testes do `LocalAgentProvider`, `AgentReasoningRetentionTests`, `AgentChatHistoryOptOutRaceTests`): 87/87. Build da solução com `-p:UsedAvaloniaProducts=`: 0 avisos, 0 erros. `dotnet test` da solução: Infrastructure.Agents.Tests 403/403; UnitTests 3.805 aprovados e 20 ignorados; IntegrationTests 1.143 aprovados, 20 ignorados e 2 falhas: `AttachmentsCountTowardsTheInputLimitAndFailTypedWithoutAnyProcess` (bloqueado por variáveis de ambiente do Claude Code herdadas da sessão) e `PreviousSessionKeepsItsUuidRepresentationAndGetsStandardIdentifierMode("JavaLegacy")`, que falhou uma vez na suíte integral e passou três vezes isolado. É a terceira falha intermitente diferente em rodadas integrais desta sessão; nenhuma se repetiu e nenhuma foi diagnosticada. |
| Resultado por critério | Parcial: isolamento e preempção aprovados sem modelo. |
| Evidência oficial e artefatos sanitizados | Testes citados acima, em `tests/` e `tools/KapiLab.Tests`; nenhum artefato novo em `docs` |
| Limitações e bloqueios | Não executado: cancelamento após o primeiro token com modelo real. Existe o teste Explicit novo `CancellingARealAgentTurnEndsItWithoutAnErrorAndTheSessionAnswersAgain`, que só roda com `KAPI_AGENT_MODEL`. |

Não armazenar credenciais, texto de raciocínio ou payloads sensíveis. Ao concluir, atualizar o estado de execução acima e os checklists com links para a evidência oficial; o estado dos requisitos continua no catálogo MVP.
