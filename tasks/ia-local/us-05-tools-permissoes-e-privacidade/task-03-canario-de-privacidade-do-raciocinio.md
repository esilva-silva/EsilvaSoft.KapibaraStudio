# KS-US05-T03 — Adicionar o canário de privacidade do raciocínio

**Estado de execução:** em andamento; canário aprovado em 12/11/2026 para o caminho sem opt-in de retenção. **Versão-alvo:** v0.11.0.

**User story:** [KS-US05 — Comprovar tools, recusas e privacidade com modelo real](README.md). **Requisitos relacionados:** MVP-013. **Entregas oficiais:** F02-E03.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Provar por teste que o texto bruto de raciocínio não sai do painel sem opt-in.

**Depende de:** [KS-US02-T01 — Cobrir o ciclo de tools com runtime falso](../us-02-robustez-do-laco-do-agente/task-01-testes-do-ciclo-com-runtime-falso.md)

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Contrato de IA local](../../../docs/MVP/contratos/ia-local.md)
- [Arquitetura e invariantes](../../../docs/MVP/03-arquitetura-e-invariantes.md)

## Execução

- [x] Injetar um marcador único no raciocínio com runtime falso e procurá-lo em logs, auditoria, resumo de sessão, contexto serializado e mensagens enviadas a outro provider.
- [ ] Verificar que, sem opt-in de retenção, nada é persistido; com opt-in, apenas no local e pelo prazo configurado.
- [x] Verificar que o raciocínio de turnos anteriores não é reenviado ao modelo.
- [ ] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

O canário só aparece onde o usuário autorizou; o teste falha se qualquer caminho o vazar.

- **Automatizável:** testes de comportamento e de falha da alteração, mais restore, build e testes das regras comuns; usam runtime e `HttpMessageHandler` falsos e não carregam modelo.
- **Real/manual:** não aplicável; teste determinístico.
- **Conclusão:** todos os critérios obrigatórios aprovados e vinculados à entrega oficial. Critério inaplicável exige justificativa; falho, ignorado ou não exercitado permanece aberto.

## Registro de execução

Execução parcial registrada abaixo; itens não exercitados continuam abertos.

| Campo | Registro |
| --- | --- |
| Snapshot e alterações locais | Testes em `403061a` (`master`); este registro é alteração posterior |
| Data, responsável, ambiente e versões | 12/11/2026 · Claude Code a pedido do usuário · Windows 11 · .NET SDK 10.0.402 · sem modelo real, sem MongoDB |
| Cenários, comandos, esperado e observado | Teste `RawReasoningStaysInReasoningEventsAndOutOfAuditPersistenceAndTheNextPrompts`: um marcador único é emitido como raciocínio antes de uma chamada de tool, em dois turnos. Controle positivo: o marcador aparece nos eventos e somente em `ReasoningDelta`. Ausente da auditoria serializada, dos prompts do passo e do turno seguintes, de todas as coleções do LiteDB e, por varredura de bytes em UTF-8 e UTF-16LE, de todos os arquivos do workspace e sidecars. Build da solução com `-p:UsedAvaloniaProducts=`: 0 avisos, 0 erros. `dotnet test` da solução: Infrastructure.Agents.Tests 403/403; UnitTests 3.802 aprovados e 20 ignorados; IntegrationTests 1.142 aprovados, 20 ignorados e 1 falha (`AttachmentsCountTowardsTheInputLimitAndFailTypedWithoutAnyProcess`, bloqueado por variáveis de ambiente do Claude Code herdadas da sessão). Na rodada anterior da mesma data, `StartupCompositionResumesCrashJournalsInBackgroundOnTheSingleOwner` falhou uma vez na suíte integral, passou três vezes isolado e não se repetiu; a causa não foi diagnosticada. |
| Resultado por critério | Parcial: eventos, auditoria, prompts seguintes e persistência sem opt-in aprovados. |
| Evidência oficial e artefatos sanitizados | `tests/EsilvaSoft.KapibaraStudio.IntegrationTests/LocalAgentComposedRuntimeTests.cs` e `tests/EsilvaSoft.KapibaraStudio.TestSupport/LocalAgentScriptedDoubles.cs` |
| Limitações e bloqueios | Não cobertos por este teste: logs de diagnóstico do serviço de modelos, mensagens enviadas a outro provider e o caminho com opt-in de retenção (este último tem os testes existentes `AgentReasoningRetentionTests`, na camada de conversa do painel, não reexecutados isoladamente). No fechamento forçado o raciocínio volta ao mesmo modelo no passo seguinte do mesmo turno, por desenho. |

Não armazenar credenciais, texto de raciocínio ou payloads sensíveis. Ao concluir, atualizar o estado de execução acima e os checklists com links para a evidência oficial; o estado dos requisitos continua no catálogo MVP.
