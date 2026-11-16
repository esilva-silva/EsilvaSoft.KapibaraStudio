# KS-US09-T03 — Validar retenção e opt-out

**Estado de execução:** em andamento; retenção do raciocínio e persistência cobertas por testes existentes, reexecutados em 13/11/2026. **Versão-alvo:** v0.11.0.

**User story:** [KS-US09 — Garantir concorrência, liberação e recuperação](README.md). **Requisitos relacionados:** MVP-011, MVP-012, MVP-013. **Entregas oficiais:** F02-E04.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Validar que conversas, raciocínio e rascunhos seguem as preferências de retenção e opt-out.

**Depende de:** [KS-US05-T03 — Adicionar o canário de privacidade do raciocínio](../us-05-tools-permissoes-e-privacidade/task-03-canario-de-privacidade-do-raciocinio.md)

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Arquitetura e invariantes](../../../docs/MVP/03-arquitetura-e-invariantes.md)
- [Tools de agentes](../../../docs/MVP/contratos/tools-agentes.md)

## Execução

- [ ] Validar opt-out geral e por conexão para rascunhos e opt-in para entrada JSON.
- [x] Validar retenção e expurgo do raciocínio conforme dias configurados.
- [ ] Conferir que resultados, diagnósticos e credenciais não entram em sessão, contexto serializado ou log.
- [x] Usar o proprietário LiteDB registrado em DI; migração apenas aditiva e versionada.
- [ ] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Cada preferência tem teste de respeito e de expurgo; nenhuma segunda conexão ao arquivo local.

- **Automatizável:** testes de comportamento e de falha da alteração, mais restore, build e testes das regras comuns; usam runtime e `HttpMessageHandler` falsos e não carregam modelo.
- **Real/manual:** cenário com modelo real é manual, `[Explicit]` com `Category("LocalModelIntegration")`, em ambiente identificado; sem modelo configurado, não declarar o cenário executado.
- **Conclusão:** todos os critérios obrigatórios aprovados e vinculados à entrega oficial. Critério inaplicável exige justificativa; falho, ignorado ou não exercitado permanece aberto.

## Registro de execução

Execução parcial registrada abaixo; itens não exercitados continuam abertos.

| Campo | Registro |
| --- | --- |
| Snapshot e alterações locais | `d8be8dc` em `master`, com as alterações deste lote ainda não commitadas |
| Data, responsável, ambiente e versões | 13/11/2026 · Claude Code a pedido do usuário · Windows 11 · .NET SDK 10.0.402 · Headless, sem modelo real e sem MongoDB |
| Cenários, comandos, esperado e observado | Mapeamento: raciocínio ausente por padrão, só com opt-in e com expiração (`AgentReasoningRetentionTests`); no repositório LiteDB, expurgo apenas do expirado, política do proprietário local e desligar a retenção sem apagar o histórico (`ExpirationPrunesOnlyExpiredReasoningUnderTheRegisteredOwner`, `ReasoningPersistenceRequiresTheLocalOwnerOptInPolicy`, `DisablingRetentionPurgesReasoningOnlyAndLeavesConversationHistory`, `AQueuedAutosaveCannotRestoreReasoningAfterTheOwnerPolicyIsDisabled`); segredos redigidos e resultado de tool recusado na gravação (`SecretsAreRedactedBeforeWritingAndTheStoredCopyIsReturned`, `ToolCallsRejectResultTextAttachmentsAndProposalReferencesWithoutChangingStoredConversation`); migração aditiva (`VersionOneConversationReadsAndMigratesAdditivelyOnNextWrite`). O canário de KS-US05 confirma que o raciocínio não chega ao LiteDB sem opt-in. Rodada focal em UnitTests (`LocalAiModelServiceTests`, `LocalAiModelServiceStreamingTests`, `LocalAiOfflinePreemptionTests`, testes do `LocalAgentProvider`, `AgentReasoningRetentionTests`, `AgentChatHistoryOptOutRaceTests`): 87/87. Rodada focal em IntegrationTests (`LiteDbAgentConversationTests`, `LocalAgentComposedRuntimeTests`): 40/40. Estes testes já existiam; neste lote foram apenas reexecutados e mapeados aos itens da tarefa, sem alteração de produto. Build da solução com `-p:UsedAvaloniaProducts=`: 0 avisos, 0 erros. `dotnet test` da solução: Infrastructure.Agents.Tests 403/403; UnitTests 3.805 aprovados e 20 ignorados; IntegrationTests 1.143 aprovados, 20 ignorados e 2 falhas: `AttachmentsCountTowardsTheInputLimitAndFailTypedWithoutAnyProcess` (bloqueado por variáveis de ambiente do Claude Code herdadas da sessão) e `PreviousSessionKeepsItsUuidRepresentationAndGetsStandardIdentifierMode("JavaLegacy")`, que falhou uma vez na suíte integral e passou três vezes isolado. É a terceira falha intermitente diferente em rodadas integrais desta sessão; nenhuma se repetiu e nenhuma foi diagnosticada. |
| Resultado por critério | Parcial: retenção e persistência de conversas aprovadas. |
| Evidência oficial e artefatos sanitizados | Testes citados acima, em `tests/` e `tools/KapiLab.Tests`; nenhum artefato novo em `docs` |
| Limitações e bloqueios | Não reverificados aqui: opt-out geral e por conexão para rascunhos do editor e opt-in de entrada JSON, que pertencem a outras suítes. |

Não armazenar credenciais, texto de raciocínio ou payloads sensíveis. Ao concluir, atualizar o estado de execução acima e os checklists com links para a evidência oficial; o estado dos requisitos continua no catálogo MVP.
