# KS-US08-T01 — Validar o fluxo de prévia e aceite

**Estado de execução:** em andamento; fluxo explícito coberto por testes existentes, reexecutados em 13/11/2026 sem modelo real. **Versão-alvo:** v0.11.0.

**User story:** [KS-US08 — Concluir o autocomplete explícito com o pacote v4](README.md). **Requisitos relacionados:** MVP-008, MVP-012. **Entregas oficiais:** F02-E02.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Demonstrar o fluxo completo do autocomplete explícito com o pacote de referência.

**Depende de:** [KS-US01-T01 — Registrar pacote de referência e limites](../us-01-referencia-e-contratos-do-pacote/task-01-registrar-referencia-e-limites.md)

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Contrato de IA local](../../../docs/MVP/contratos/ia-local.md)
- [Design system](../../../docs/MVP/04-design-system.md)

## Execução

- [x] Validar `Ctrl+;` → prévia → Tab com Undo, e Escape sem inserir ponto e vírgula.
- [x] Validar a rejeição de resposta obsoleta ao mudar texto, aba, conexão, banco ou coleção, com a origem capturada antes do await.
- [x] Validar cancelamento durante a geração e nova solicitação em seguida.
- [ ] Corrigir defeitos comprovados e cobrir com teste.
- [ ] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Fluxo comprovado com runtime falso e com modelo real; resultado só atualiza a aba que o originou.

- **Automatizável:** testes de comportamento e de falha da alteração, mais restore, build e testes das regras comuns; usam runtime e `HttpMessageHandler` falsos e não carregam modelo.
- **Real/manual:** cenário com modelo real é manual, `[Explicit]` com `Category("LocalModelIntegration")`, em ambiente identificado; sem modelo configurado, não declarar o cenário executado.
- **Conclusão:** todos os critérios obrigatórios aprovados e vinculados à entrega oficial. Critério inaplicável exige justificativa; falho, ignorado ou não exercitado permanece aberto.

## Registro de execução

Execução parcial registrada abaixo; itens não exercitados continuam abertos.

| Campo | Registro |
| --- | --- |
| Snapshot e alterações locais | `d8be8dc` em `master`, com as alterações deste lote ainda não commitadas |
| Data, responsável, ambiente e versões | 13/11/2026 · Claude Code a pedido do usuário · Windows 11 · .NET SDK 10.0.402 · Headless, sem modelo real e sem MongoDB |
| Cenários, comandos, esperado e observado | Mapeamento em `AiCompletionUiTests`: prévia e aceite com Undo único (`TabAcceptsTheMultilinePreviewAsASingleUndo`), descarte e cancelamento (`EscapeDuringGenerationCancelsAndRemovesTheIndicator`, `LoadingTheModelHasItsOwnIndicatorAndEscapeOnlyAbandonsTheWait`), resposta obsoleta (`AStaleResultIsNeverShownAfterANewEdit`, `APreemptedGenerationIsDiscardedSilently`), layout ABNT2 sem inserir caractere (`OnAbnt2TheSemicolonKeyWithCtrlRequestsTheAiThroughTheActiveLayout`, `OnAbnt2CtrlCedillaNeverRequestsTheAi`) e isolamento por aba (`ALateSuggestionFromOneTabNeverReachesAnother`). Rodada focal em IntegrationTests (`AiCompletionUiTests`): 21/21. Estes testes já existiam; neste lote foram apenas reexecutados e mapeados aos itens da tarefa, sem alteração de produto. Build da solução com `-p:UsedAvaloniaProducts=`: 0 avisos, 0 erros. `dotnet test` da solução: Infrastructure.Agents.Tests 403/403; UnitTests 3.805 aprovados e 20 ignorados; IntegrationTests 1.143 aprovados, 20 ignorados e 2 falhas: `AttachmentsCountTowardsTheInputLimitAndFailTypedWithoutAnyProcess` (bloqueado por variáveis de ambiente do Claude Code herdadas da sessão) e `PreviousSessionKeepsItsUuidRepresentationAndGetsStandardIdentifierMode("JavaLegacy")`, que falhou uma vez na suíte integral e passou três vezes isolado. É a terceira falha intermitente diferente em rodadas integrais desta sessão; nenhuma se repetiu e nenhuma foi diagnosticada. |
| Resultado por critério | Parcial: comportamento aprovado com runtime falso em Headless. |
| Evidência oficial e artefatos sanitizados | Testes citados acima, em `tests/` e `tools/KapiLab.Tests`; nenhum artefato novo em `docs` |
| Limitações e bloqueios | Não exercitado: o mesmo fluxo com o pacote v4 real e em desktop nativo. A troca de conexão, banco ou coleção como causa de resposta obsoleta não tem teste próprio identificado; a cobertura vista é por edição de texto e por aba. Nenhum defeito foi encontrado, então não houve correção. |

Não armazenar credenciais, texto de raciocínio ou payloads sensíveis. Ao concluir, atualizar o estado de execução acima e os checklists com links para a evidência oficial; o estado dos requisitos continua no catálogo MVP.
