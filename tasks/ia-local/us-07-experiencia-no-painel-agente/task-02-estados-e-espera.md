# KS-US07-T02 — Mostrar estados e espera do agente local

**Estado de execução:** em andamento; mensagens de erro e de indisponibilidade concluídas em 13/11/2026, estados de espera não alterados. **Versão-alvo:** v0.11.0.

**User story:** [KS-US07 — Completar a experiência do agente local no painel](README.md). **Requisitos relacionados:** MVP-011, MVP-013. **Entregas oficiais:** F02-E03, F02-E05.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Tornar visível cada estado do turno local e dar noção de espera.

**Depende de:** [KS-US02-T04 — Encerrar raciocínio e tool pendente no cancelamento](../us-02-robustez-do-laco-do-agente/task-04-cancelamento-do-raciocinio.md)

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Design system](../../../docs/MVP/04-design-system.md)

## Execução

- [ ] Mostrar sem modelo, carregando modelo, pensando com contador de tokens e tempo, chamando tool, orçamento atingido, cancelado e erro.
- [ ] Indicar a espera com base no andamento real, sem prometer tempo que não foi medido.
- [ ] Garantir foco, atalhos e leitura por tecnologia assistiva nos novos elementos, dentro do que o Headless verifica.
- [ ] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Cada estado tem representação e teste; nenhum estado fica sem saída para o usuário.

- **Automatizável:** testes de comportamento e de falha da alteração, mais restore, build e testes das regras comuns; usam runtime e `HttpMessageHandler` falsos e não carregam modelo.
- **Real/manual:** cenário com modelo real é manual, `[Explicit]` com `Category("LocalModelIntegration")`, em ambiente identificado; sem modelo configurado, não declarar o cenário executado.
- **Conclusão:** todos os critérios obrigatórios aprovados e vinculados à entrega oficial. Critério inaplicável exige justificativa; falho, ignorado ou não exercitado permanece aberto.

## Registro de execução

Execução parcial registrada abaixo; itens não exercitados continuam abertos.

| Campo | Registro |
| --- | --- |
| Snapshot e alterações locais | `c41de38` no branch `ia-local-us06`, com as alterações deste lote ainda não commitadas |
| Data, responsável, ambiente e versões | 13/11/2026 · Claude Code a pedido do usuário · Windows 11 · .NET SDK 10.0.402 · Headless, sem modelo real |
| Cenários, comandos, esperado e observado | Os seis códigos de erro do agente local (`LocalContextExhausted`, `LocalToolsUnavailable`, `InvalidToolCallFormat`, `LocalReasoningUnavailable`, `LocalGenerationFailure`, `ContextRejected`) e os quatro motivos de indisponibilidade (`Disabled`, `NoModelConfigured`, `ModelMissingOrInvalid`, `AgentContractUnsupported`) não tinham tradução: o painel mostrava o código cru. Foram acrescentadas mensagens em pt-BR, inglês, espanhol e chinês. Teste `EveryLocalErrorAndUnavailabilityCodeHasAReadableMessageInEveryLanguage`, que percorre os códigos declarados pela sessão e o enum de motivos. Build da solução com `-p:UsedAvaloniaProducts=`: 0 avisos, 0 erros. `dotnet test` da solução: Infrastructure.Agents.Tests 403/403; UnitTests 3.805 aprovados e 20 ignorados; IntegrationTests 1.143 aprovados, 20 ignorados e 2 falhas: `AttachmentsCountTowardsTheInputLimitAndFailTypedWithoutAnyProcess` (bloqueado por variáveis de ambiente do Claude Code herdadas da sessão) e `CancelledNativeDeletionPreservesLocalStateAndAllowsIdempotentRetry(True)`, teste de exclusão de sessão Copilot que falhou uma vez na suíte integral e passou três vezes isolado; causa não diagnosticada. |
| Resultado por critério | Parcial: todo código de erro e de indisponibilidade do agente local tem mensagem nos quatro idiomas. |
| Evidência oficial e artefatos sanitizados | `tests/EsilvaSoft.KapibaraStudio.IntegrationTests/AgentChatReasoningToggleUiTests.cs`, `LocalAgentProviderToolLoopTests.cs`; [guia](../../../docs/MVP/05-guia-de-uso.md) e [design system](../../../docs/MVP/04-design-system.md) atualizados |
| Limitações e bloqueios | Não feitos: estado de carregamento do modelo, indicação de espera e verificação de foco e leitura assistiva. Os textos em espanhol e chinês não tiveram revisão de falante nativo. Os estados pensando, chamando tool, orçamento atingido e cancelado já existiam e não foram reexercitados. |

Não armazenar credenciais, texto de raciocínio ou payloads sensíveis. Ao concluir, atualizar o estado de execução acima e os checklists com links para a evidência oficial; o estado dos requisitos continua no catálogo MVP.
