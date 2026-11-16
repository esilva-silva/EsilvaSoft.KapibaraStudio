# KS-US08-T02 — Validar contexto v2 e fechamento de delimitadores

**Estado de execução:** em andamento; contrato v2 coberto por testes existentes, reexecutados em 13/11/2026. **Versão-alvo:** v0.11.0.

**User story:** [KS-US08 — Concluir o autocomplete explícito com o pacote v4](README.md). **Requisitos relacionados:** MVP-008, MVP-012. **Entregas oficiais:** F02-E02.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Confirmar o comportamento do Studio com pacotes que declaram `editor-context-v2`.

**Depende de:** [KS-US01-T02 — Revalidar paridade de prompt no runtime atual](../us-01-referencia-e-contratos-do-pacote/task-02-revalidar-paridade-de-prompt.md)

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Contrato de IA local](../../../docs/MVP/contratos/ia-local.md)

## Execução

- [x] Validar a linha de campos do alvo: apenas schema em cache, sem I/O, sem `_id` e caminhos sensíveis, até 96 campos e 2.048 caracteres.
- [x] Validar o fechamento de 1 a 4 delimitadores internos somente no contrato v2.
- [x] Validar que pacote v1 mantém os mesmos bytes e que o orçamento vem do pacote.
- [ ] Respeitar opt-in de entrada JSON e fontes habilitadas.
- [ ] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Comportamentos acima testados; sem campos em cache, os bytes são iguais aos do v1.

- **Automatizável:** testes de comportamento e de falha da alteração, mais restore, build e testes das regras comuns; usam runtime e `HttpMessageHandler` falsos e não carregam modelo.
- **Real/manual:** cenário com modelo real é manual, `[Explicit]` com `Category("LocalModelIntegration")`, em ambiente identificado; sem modelo configurado, não declarar o cenário executado.
- **Conclusão:** todos os critérios obrigatórios aprovados e vinculados à entrega oficial. Critério inaplicável exige justificativa; falho, ignorado ou não exercitado permanece aberto.

## Registro de execução

Execução parcial registrada abaixo; itens não exercitados continuam abertos.

| Campo | Registro |
| --- | --- |
| Snapshot e alterações locais | `d8be8dc` em `master`, com as alterações deste lote ainda não commitadas |
| Data, responsável, ambiente e versões | 13/11/2026 · Claude Code a pedido do usuário · Windows 11 · .NET SDK 10.0.402 · Headless, sem modelo real e sem MongoDB |
| Cenários, comandos, esperado e observado | Mapeamento: linha de campos do alvo, exclusão de `_id` e de caminhos inseguros e limites (`UnsafePathsAndTheIdAreDroppedAndTheLineIsBounded`, `AnUnsafeTargetCollectionIsNotUsedAsLabel`), paridade byte a byte com o renderer de treino (`TheHeaderMatchesTheTrainingRendererByteForByte`, `TheModelPrefixMatchesTheTrainingRendererByteForByte`), v1 inalterado (`WithoutTargetFieldsV2IsV1ByteForByte`, `V1IgnoresTargetFields`) e fechamento de delimitadores só no v2 (`ThePipelineClosesInnerDelimitersOnlyForTheV2Contract`, `MissingInnerClosersAreAppended`, `EdgesOfTheRule`). Rodada focal em UnitTests (`InlineCompletionUiTests`, `EditorContextV2ContractTests`, `InnerCloserPostProcessingTests`, `AiCompletionProviderTests`, `PrefixSessionCacheTests`, `PrefixReusePolicyTests`): 67/67. Estes testes já existiam; neste lote foram apenas reexecutados e mapeados aos itens da tarefa, sem alteração de produto. Build da solução com `-p:UsedAvaloniaProducts=`: 0 avisos, 0 erros. `dotnet test` da solução: Infrastructure.Agents.Tests 403/403; UnitTests 3.805 aprovados e 20 ignorados; IntegrationTests 1.143 aprovados, 20 ignorados e 2 falhas: `AttachmentsCountTowardsTheInputLimitAndFailTypedWithoutAnyProcess` (bloqueado por variáveis de ambiente do Claude Code herdadas da sessão) e `PreviousSessionKeepsItsUuidRepresentationAndGetsStandardIdentifierMode("JavaLegacy")`, que falhou uma vez na suíte integral e passou três vezes isolado. É a terceira falha intermitente diferente em rodadas integrais desta sessão; nenhuma se repetiu e nenhuma foi diagnosticada. |
| Resultado por critério | Parcial: contrato e pós-processamento aprovados sem modelo. |
| Evidência oficial e artefatos sanitizados | Testes citados acima, em `tests/` e `tools/KapiLab.Tests`; nenhum artefato novo em `docs` |
| Limitações e bloqueios | Não reverificado neste lote: que o orçamento vem do pacote ao escolher o modelo e o respeito ao opt-in de entrada JSON (têm testes em outras suítes, não mapeados aqui). Qualidade com o pacote v4 real fica em KS-US10-T03. |

Não armazenar credenciais, texto de raciocínio ou payloads sensíveis. Ao concluir, atualizar o estado de execução acima e os checklists com links para a evidência oficial; o estado dos requisitos continua no catálogo MVP.
