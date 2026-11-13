# KS-US07-T01 — Completar as preferências de raciocínio

**Estado de execução:** em andamento; bloqueio do alternador em pacote que sempre raciocina concluído em 13/11/2026. **Versão-alvo:** v0.11.0.

**User story:** [KS-US07 — Completar a experiência do agente local no painel](README.md). **Requisitos relacionados:** MVP-011, MVP-013. **Entregas oficiais:** F02-E03, F02-E05.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Levar orçamento e exibição do raciocínio às Preferências, respeitando os limites do pacote.

**Depende de:** nenhuma tarefa anterior desta árvore.

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Design system](../../../docs/MVP/04-design-system.md)

## Execução

- [ ] Expor orçamento por passo e exibição do raciocínio, limitados ao mínimo e ao máximo do pacote carregado.
- [ ] Manter a preferência por provider em `AgentPanelPreferences` e migrar valores fora do limite sem perder a configuração.
- [x] Desabilitar o alternador quando o pacote só opera pensando ou não suporta raciocínio, com explicação.
- [ ] Testar persistência, limites e falha de gravação visível.
- [ ] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Preferências refletem o pacote e persistem; falha de persistência é visível.

- **Automatizável:** testes de comportamento e de falha da alteração, mais restore, build e testes das regras comuns; usam runtime e `HttpMessageHandler` falsos e não carregam modelo.
- **Real/manual:** cenário com modelo real é manual, `[Explicit]` com `Category("LocalModelIntegration")`, em ambiente identificado; sem modelo configurado, não declarar o cenário executado.
- **Conclusão:** todos os critérios obrigatórios aprovados e vinculados à entrega oficial. Critério inaplicável exige justificativa; falho, ignorado ou não exercitado permanece aberto.

## Registro de execução

Execução parcial registrada abaixo; itens não exercitados continuam abertos.

| Campo | Registro |
| --- | --- |
| Snapshot e alterações locais | `c41de38` no branch `ia-local-us06`, com as alterações deste lote ainda não commitadas |
| Data, responsável, ambiente e versões | 13/11/2026 · Claude Code a pedido do usuário · Windows 11 · .NET SDK 10.0.402 · Headless, sem modelo real |
| Cenários, comandos, esperado e observado | O orçamento já era limitado ao mínimo e máximo do pacote e a preferência já era guardada por provider (lido no código, não alterado). Lacuna corrigida: um pacote com `toggle: none` recusa o pedido de desligar o raciocínio com `LocalReasoningUnavailable`, mas o painel deixava desmarcar. Agora o status do provider informa `ReasoningCanBeDisabled`; nesse caso o painel mantém Raciocínio marcado e bloqueado, com dica exibida mesmo desabilitado, ignora uma preferência salva de desligar e envia o turno com raciocínio ligado. Testes `AnAlwaysReasoningPackageLocksTheToggleCheckedAndExplainsWhy`, `APackageThatCanStopReasoningKeepsTheToggleFreeAndHonoursTheSavedChoice` e `TheStatusTellsWhetherReasoningCanBeTurnedOff`. Build da solução com `-p:UsedAvaloniaProducts=`: 0 avisos, 0 erros. `dotnet test` da solução: Infrastructure.Agents.Tests 403/403; UnitTests 3.805 aprovados e 20 ignorados; IntegrationTests 1.143 aprovados, 20 ignorados e 2 falhas: `AttachmentsCountTowardsTheInputLimitAndFailTypedWithoutAnyProcess` (bloqueado por variáveis de ambiente do Claude Code herdadas da sessão) e `CancelledNativeDeletionPreservesLocalStateAndAllowsIdempotentRetry(True)`, teste de exclusão de sessão Copilot que falhou uma vez na suíte integral e passou três vezes isolado; causa não diagnosticada. |
| Resultado por critério | Parcial: alternador bloqueado e explicado aprovado; pacote que permite desligar continua livre e respeita a escolha salva. |
| Evidência oficial e artefatos sanitizados | `tests/EsilvaSoft.KapibaraStudio.IntegrationTests/AgentChatReasoningToggleUiTests.cs`, `LocalAgentProviderToolLoopTests.cs`; [guia](../../../docs/MVP/05-guia-de-uso.md) e [design system](../../../docs/MVP/04-design-system.md) atualizados |
| Limitações e bloqueios | Não feitos: opção de exibição do raciocínio nas Preferências, migração de orçamento salvo fora do limite do pacote (hoje é escolhido o valor permitido mais próximo, sem teste novo) e teste de falha de gravação. A dica não é atualizada ao trocar de idioma com o painel aberto. |

Não armazenar credenciais, texto de raciocínio ou payloads sensíveis. Ao concluir, atualizar o estado de execução acima e os checklists com links para a evidência oficial; o estado dos requisitos continua no catálogo MVP.
