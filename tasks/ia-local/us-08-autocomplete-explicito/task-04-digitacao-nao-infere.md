# KS-US08-T04 — Guardar que digitar não infere

**Estado de execução:** em andamento; guarda coberta por testes existentes, reexecutados em 13/11/2026. **Versão-alvo:** v0.11.0.

**User story:** [KS-US08 — Concluir o autocomplete explícito com o pacote v4](README.md). **Requisitos relacionados:** MVP-008, MVP-012. **Entregas oficiais:** F02-E02.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Manter por teste a garantia de que digitação não carrega modelo nem gera inferência.

**Depende de:** nenhuma tarefa anterior desta árvore.

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Contrato de IA local](../../../docs/MVP/contratos/ia-local.md)
- [Arquitetura e invariantes](../../../docs/MVP/03-arquitetura-e-invariantes.md)

## Execução

- [x] Verificar por teste que digitar, colar e navegar não chamam o runtime nem carregam modelo.
- [x] Verificar que `Ctrl+Espaço` continua determinístico e sem IA.
- [ ] Verificar o orçamento da busca de nomes por tecla como medição manual, sem promovê-la ao CI.
- [ ] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

A guarda falha se qualquer caminho de digitação alcançar o runtime.

- **Automatizável:** testes de comportamento e de falha da alteração, mais restore, build e testes das regras comuns; usam runtime e `HttpMessageHandler` falsos e não carregam modelo.
- **Real/manual:** cenário com modelo real é manual, `[Explicit]` com `Category("LocalModelIntegration")`, em ambiente identificado; sem modelo configurado, não declarar o cenário executado.
- **Conclusão:** todos os critérios obrigatórios aprovados e vinculados à entrega oficial. Critério inaplicável exige justificativa; falho, ignorado ou não exercitado permanece aberto.

## Registro de execução

Execução parcial registrada abaixo; itens não exercitados continuam abertos.

| Campo | Registro |
| --- | --- |
| Snapshot e alterações locais | `d8be8dc` em `master`, com as alterações deste lote ainda não commitadas |
| Data, responsável, ambiente e versões | 13/11/2026 · Claude Code a pedido do usuário · Windows 11 · .NET SDK 10.0.402 · Headless, sem modelo real e sem MongoDB |
| Cenários, comandos, esperado e observado | Mapeamento: digitação nunca alcança a IA mesmo com opt-in legado e modelo pronto (`TypingNeverReachesTheAiEvenWithTheLegacyOptInAndAModelReady`), pedido automático nunca carrega, descarrega nem troca modelo (`AnAutomaticRequestNeverLoadsUnloadsOrSwapsTheModelOfAnotherKey`, que tem três casos ignorados por dispositivo nesta máquina), ghost determinístico sem modelo nem conexão (`DeterministicGhostAppearsWithoutAnyAiModelOrConnectionAndAcceptingItIsASingleUndo`) e Ctrl+; desligado não chega ao modelo (`WithTheAiAutocompleteDisabledCtrlSemicolonNeverReachesTheModelAndOpensTheList`). Rodada focal em UnitTests (`InlineCompletionUiTests`, `EditorContextV2ContractTests`, `InnerCloserPostProcessingTests`, `AiCompletionProviderTests`, `PrefixSessionCacheTests`, `PrefixReusePolicyTests`): 67/67. Estes testes já existiam; neste lote foram apenas reexecutados e mapeados aos itens da tarefa, sem alteração de produto. Build da solução com `-p:UsedAvaloniaProducts=`: 0 avisos, 0 erros. `dotnet test` da solução: Infrastructure.Agents.Tests 403/403; UnitTests 3.805 aprovados e 20 ignorados; IntegrationTests 1.143 aprovados, 20 ignorados e 2 falhas: `AttachmentsCountTowardsTheInputLimitAndFailTypedWithoutAnyProcess` (bloqueado por variáveis de ambiente do Claude Code herdadas da sessão) e `PreviousSessionKeepsItsUuidRepresentationAndGetsStandardIdentifierMode("JavaLegacy")`, que falhou uma vez na suíte integral e passou três vezes isolado. É a terceira falha intermitente diferente em rodadas integrais desta sessão; nenhuma se repetiu e nenhuma foi diagnosticada. |
| Resultado por critério | Parcial: guarda aprovada para digitação e pedido automático. |
| Evidência oficial e artefatos sanitizados | Testes citados acima, em `tests/` e `tools/KapiLab.Tests`; nenhum artefato novo em `docs` |
| Limitações e bloqueios | Colar e navegar não têm caso próprio identificado. A medição manual do orçamento da busca de nomes por tecla não foi feita. |

Não armazenar credenciais, texto de raciocínio ou payloads sensíveis. Ao concluir, atualizar o estado de execução acima e os checklists com links para a evidência oficial; o estado dos requisitos continua no catálogo MVP.
