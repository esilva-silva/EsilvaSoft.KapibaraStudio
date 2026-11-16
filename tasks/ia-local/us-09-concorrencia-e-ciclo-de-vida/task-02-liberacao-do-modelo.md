# KS-US09-T02 — Validar a liberação do modelo

**Estado de execução:** em andamento; liberação coberta por testes com runtime falso em 13/11/2026, sem medição de memória. **Versão-alvo:** v0.11.0.

**User story:** [KS-US09 — Garantir concorrência, liberação e recuperação](README.md). **Requisitos relacionados:** MVP-011, MVP-012, MVP-013. **Entregas oficiais:** F02-E04.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Comprovar que o runtime libera o modelo nas condições previstas.

**Depende de:** [KS-US09-T01 — Validar cancelamento isolado e concorrência](task-01-cancelamento-e-concorrencia.md)

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Contrato de IA local](../../../docs/MVP/contratos/ia-local.md)

## Execução

- [x] Validar liberação por inatividade, ao fechar a aba ou o aplicativo e ao trocar modelo, provider ou configuração.
- [x] Validar que trabalhador em uso libera ao retornar, sem dispose concorrente.
- [ ] Medir a memória antes e depois com modelo real e comparar ao limite de KS-US01.
- [ ] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Liberação observada em cada condição; memória retorna ao patamar registrado.

- **Automatizável:** testes de comportamento e de falha da alteração, mais restore, build e testes das regras comuns; usam runtime e `HttpMessageHandler` falsos e não carregam modelo.
- **Real/manual:** medição manual com modelo real.
- **Conclusão:** todos os critérios obrigatórios aprovados e vinculados à entrega oficial. Critério inaplicável exige justificativa; falho, ignorado ou não exercitado permanece aberto.

## Registro de execução

Execução parcial registrada abaixo; itens não exercitados continuam abertos.

| Campo | Registro |
| --- | --- |
| Snapshot e alterações locais | `d8be8dc` em `master`, com as alterações deste lote ainda não commitadas |
| Data, responsável, ambiente e versões | 13/11/2026 · Claude Code a pedido do usuário · Windows 11 · .NET SDK 10.0.402 · Headless, sem modelo real e sem MongoDB |
| Cenários, comandos, esperado e observado | Mapeamento em `LocalAiModelServiceTests`: descarga por inatividade e recarga no uso seguinte (`IdleTimeoutUnloadsAndNextUseReloadsWithFreshDeadline`), inatividade espera a geração ativa (`IdleTimeoutWaitsForActiveGenerationAndConfigurationAppliesWithoutReload`), troca de modelo e de configuração liberam o anterior antes do próximo (`SwitchingModelsReleasesThePreviousModelBeforeLoadingTheNext`, `ChangingCacheConfigurationReleasesTheOldRuntimeBeforeTheNextRequest`). A sessão de geração do turno do agente é encerrada ao fim do turno (KS-US03). Rodada focal em UnitTests (`LocalAiModelServiceTests`, `LocalAiModelServiceStreamingTests`, `LocalAiOfflinePreemptionTests`, testes do `LocalAgentProvider`, `AgentReasoningRetentionTests`, `AgentChatHistoryOptOutRaceTests`): 87/87. Estes testes já existiam; neste lote foram apenas reexecutados e mapeados aos itens da tarefa, sem alteração de produto. Build da solução com `-p:UsedAvaloniaProducts=`: 0 avisos, 0 erros. `dotnet test` da solução: Infrastructure.Agents.Tests 403/403; UnitTests 3.805 aprovados e 20 ignorados; IntegrationTests 1.143 aprovados, 20 ignorados e 2 falhas: `AttachmentsCountTowardsTheInputLimitAndFailTypedWithoutAnyProcess` (bloqueado por variáveis de ambiente do Claude Code herdadas da sessão) e `PreviousSessionKeepsItsUuidRepresentationAndGetsStandardIdentifierMode("JavaLegacy")`, que falhou uma vez na suíte integral e passou três vezes isolado. É a terceira falha intermitente diferente em rodadas integrais desta sessão; nenhuma se repetiu e nenhuma foi diagnosticada. |
| Resultado por critério | Parcial: liberação aprovada com runtime falso. |
| Evidência oficial e artefatos sanitizados | Testes citados acima, em `tests/` e `tools/KapiLab.Tests`; nenhum artefato novo em `docs` |
| Limitações e bloqueios | Não feito: medir memória antes e depois com modelo real e comparar a um limite; os limites de KS-US01-T01 ainda não foram fixados. Fechamento do aplicativo não foi exercitado. |

Não armazenar credenciais, texto de raciocínio ou payloads sensíveis. Ao concluir, atualizar o estado de execução acima e os checklists com links para a evidência oficial; o estado dos requisitos continua no catálogo MVP.
