# KS-US07-T04 — Atualizar guia e design system

**Estado de execução:** em andamento; guia e design system atualizados em 13/11/2026 para o que foi entregue. **Versão-alvo:** v0.11.0.

**User story:** [KS-US07 — Completar a experiência do agente local no painel](README.md). **Requisitos relacionados:** MVP-011, MVP-013. **Entregas oficiais:** F02-E03, F02-E05.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Descrever no guia e no design system o agente local, o raciocínio e seus estados.

**Depende de:** [KS-US07-T03 — Gerar e inspecionar os PNGs das mudanças](task-03-renderizacao-e-pngs.md)

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Design system](../../../docs/MVP/04-design-system.md)
- [Guia de uso](../../../docs/MVP/05-guia-de-uso.md)

## Execução

- [x] Documentar preferências, estados, limites e o que não é persistido.
- [x] Documentar o uso sem modelo e a escolha de modelo por papel.
- [ ] Conferir atalhos e termos pt-BR com a interface.
- [ ] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Guia e design system correspondem à interface entregue.

- **Automatizável:** trabalho documental: `node scripts/check-docs-reader.cjs` e `git diff --check`; não alegar execução das suítes do produto.
- **Real/manual:** cenário com modelo real é manual, `[Explicit]` com `Category("LocalModelIntegration")`, em ambiente identificado; sem modelo configurado, não declarar o cenário executado.
- **Conclusão:** todos os critérios obrigatórios aprovados e vinculados à entrega oficial. Critério inaplicável exige justificativa; falho, ignorado ou não exercitado permanece aberto.

## Registro de execução

Execução parcial registrada abaixo; itens não exercitados continuam abertos.

| Campo | Registro |
| --- | --- |
| Snapshot e alterações locais | `c41de38` no branch `ia-local-us06`, com as alterações deste lote ainda não commitadas |
| Data, responsável, ambiente e versões | 13/11/2026 · Claude Code a pedido do usuário · Windows 11 · .NET SDK 10.0.402 · Headless, sem modelo real |
| Cenários, comandos, esperado e observado | Guia: seleção de modelo por papel, alternador de raciocínio e orçamento conforme o pacote, raciocínio fora do histórico salvo com retenção, e mensagens próprias de falha do agente local. Design system: opção que o pacote não permite alterar fica visível, no estado efetivo e bloqueada, com o motivo em dica; código de erro local sempre com mensagem legível. `node scripts/check-docs-reader.cjs` aprovado. Build da solução com `-p:UsedAvaloniaProducts=`: 0 avisos, 0 erros. `dotnet test` da solução: Infrastructure.Agents.Tests 403/403; UnitTests 3.805 aprovados e 20 ignorados; IntegrationTests 1.143 aprovados, 20 ignorados e 2 falhas: `AttachmentsCountTowardsTheInputLimitAndFailTypedWithoutAnyProcess` (bloqueado por variáveis de ambiente do Claude Code herdadas da sessão) e `CancelledNativeDeletionPreservesLocalStateAndAllowsIdempotentRetry(True)`, teste de exclusão de sessão Copilot que falhou uma vez na suíte integral e passou três vezes isolado; causa não diagnosticada. |
| Resultado por critério | Parcial: documentos correspondem ao que este lote e o de KS-US06 entregaram. |
| Evidência oficial e artefatos sanitizados | `tests/EsilvaSoft.KapibaraStudio.IntegrationTests/AgentChatReasoningToggleUiTests.cs`, `LocalAgentProviderToolLoopTests.cs`; [guia](../../../docs/MVP/05-guia-de-uso.md) e [design system](../../../docs/MVP/04-design-system.md) atualizados |
| Limitações e bloqueios | A conferência de atalhos e termos pt-BR com a interface não foi refeita; estados de espera ainda não existem para documentar. |

Não armazenar credenciais, texto de raciocínio ou payloads sensíveis. Ao concluir, atualizar o estado de execução acima e os checklists com links para a evidência oficial; o estado dos requisitos continua no catálogo MVP.
