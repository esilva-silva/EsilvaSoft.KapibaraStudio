# KS-US06-T02 — Separar a escolha de modelo por papel

**Estado de execução:** em andamento; filtro por papel concluído em 12/11/2026. **Versão-alvo:** v0.11.0.

**User story:** [KS-US06 — Encontrar, baixar e escolher o modelo de cada papel](README.md). **Requisitos relacionados:** MVP-012, MVP-013. **Entregas oficiais:** F02-E05.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Concluir a seleção separada de modelo para autocomplete e para agente.

**Depende de:** nenhuma tarefa anterior desta árvore.

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Design system](../../../docs/MVP/04-design-system.md)
- [Contrato de IA local](../../../docs/MVP/contratos/ia-local.md)

## Execução

- [x] Filtrar cada lista pelas capacidades do pacote e pelo contrato conhecido.
- [ ] Manter a opção de seguir o modelo do autocomplete apenas quando o pacote serve aos dois papéis.
- [ ] Garantir um modelo carregado por vez: trocar de papel libera o anterior sem dispose concorrente.
- [x] Testar a persistência da escolha e a recuperação quando o pacote escolhido foi removido.
- [ ] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Cada papel usa o modelo escolhido; pacote removido leva a estado explicado, não a erro silencioso.

- **Automatizável:** testes de comportamento e de falha da alteração, mais restore, build e testes das regras comuns; usam runtime e `HttpMessageHandler` falsos e não carregam modelo.
- **Real/manual:** cenário com modelo real é manual, `[Explicit]` com `Category("LocalModelIntegration")`, em ambiente identificado; sem modelo configurado, não declarar o cenário executado.
- **Conclusão:** todos os critérios obrigatórios aprovados e vinculados à entrega oficial. Critério inaplicável exige justificativa; falho, ignorado ou não exercitado permanece aberto.

## Registro de execução

Execução parcial registrada abaixo; itens não exercitados continuam abertos.

| Campo | Registro |
| --- | --- |
| Snapshot e alterações locais | `403061a` em `master`, com as alterações deste lote ainda não commitadas |
| Data, responsável, ambiente e versões | 12/11/2026 · Claude Code a pedido do usuário · Windows 11 · .NET SDK 10.0.402 · sem modelo real, sem MongoDB |
| Cenários, comandos, esperado e observado | As duas listas de modelos mostravam todos os pacotes instalados. Agora o autocomplete lista apenas pacotes com capacidade `Autocomplete`, e o agente apenas os que `LocalAgentProvider.CanServeAgent` aceita: pacote de agente Qwen3 com bloco `agent` e contrato conhecido, ou pacote FIM com chat. A regra é a mesma da disponibilidade do provider, extraída para `CapabilitiesOf`. Uma escolha já salva fora do filtro continua visível e não é trocada. Testes `EachRoleListsOnlyThePackagesThatCanServeIt`, `ASavedChoiceOutsideTheRoleFilterStaysVisibleInsteadOfBeingDroppedSilently` e o existente `RefreshKeepsAnUnavailableSavedAgentModelVisible`. Build da solução com `-p:UsedAvaloniaProducts=`: 0 avisos, 0 erros. `dotnet test` da solução: Infrastructure.Agents.Tests 403/403; UnitTests 3.802 aprovados e 20 ignorados; IntegrationTests 1.142 aprovados, 20 ignorados e 1 falha (`AttachmentsCountTowardsTheInputLimitAndFailTypedWithoutAnyProcess`, bloqueado por variáveis de ambiente do Claude Code herdadas da sessão). Na rodada anterior da mesma data, `StartupCompositionResumesCrashJournalsInBackgroundOnTheSingleOwner` falhou uma vez na suíte integral, passou três vezes isolado e não se repetiu; a causa não foi diagnosticada. |
| Resultado por critério | Parcial: filtro, persistência da escolha e pacote removido aprovados. |
| Evidência oficial e artefatos sanitizados | `AutocompleteSettingsViewModel.RefreshModelsAsync`, `LocalAgentProvider.CapabilitiesOf` e `tests/EsilvaSoft.KapibaraStudio.UnitTests/AutocompleteSettingsViewModelTests.cs` |
| Limitações e bloqueios | A opção de seguir o modelo do autocomplete não foi restringida: todo pacote FIM com chat serve ao agente no caminho de propostas. Uma escolha salva que não serve ao papel aparece na lista sem rótulo explicando a incompatibilidade. "Um modelo carregado por vez" é do serviço de modelos e não foi reexercitado aqui. Nenhum PNG foi gerado: a mudança altera o conteúdo das listas, não o layout. |

Não armazenar credenciais, texto de raciocínio ou payloads sensíveis. Ao concluir, atualizar o estado de execução acima e os checklists com links para a evidência oficial; o estado dos requisitos continua no catálogo MVP.
