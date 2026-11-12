# KS-US01-T04 — Neutralizar marcadores reservados em texto não confiável

**Estado de execução:** concluída na parte automatizável em 12/11/2026; nenhuma correção de produto foi necessária. **Versão-alvo:** v0.11.0.

**User story:** [KS-US01 — Fixar a referência e fazer valer os contratos do pacote](README.md). **Requisitos relacionados:** MVP-012, MVP-013. **Entregas oficiais:** F02-E01.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Garantir que mensagem do usuário, anexos e resultados de tool contendo marcadores do template não sejam interpretados como controle.

**Depende de:** nenhuma tarefa anterior desta árvore.

## Ponto de partida

- O formatter aplica `CompletionOutputProcessor.ContainsReservedOrSensitiveText` a usuário e tools; a cobertura de `<tool_call>` e `<think>` precisa ser verificada no código.

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Contrato de IA local](../../../docs/MVP/contratos/ia-local.md)
- [Tools de agentes](../../../docs/MVP/contratos/tools-agentes.md)

## Execução

- [x] Verificar o comportamento atual com `<tool_call>`, `</tool_call>`, `<tool_response>`, `<think>`, `</think>` e marcadores de turno do ChatML em cada origem de texto.
- [x] Corrigir onde o marcador atravessa, preservando o conteúdo legítimo para o usuário e o encoder JSON do contrato.
- [ ] Testar marcador partido entre chunks, em Unicode escapado e dentro de resultado truncado.
- [x] Confirmar que só a saída do próprio modelo é analisada pelo `LocalAgentToolCallParser`.
- [x] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Nenhuma origem externa produz `ToolRequested` ou evento de raciocínio; os bytes do contrato para texto comum não mudam.

- **Automatizável:** testes de comportamento e de falha da alteração, mais restore, build e testes das regras comuns; usam runtime e `HttpMessageHandler` falsos e não carregam modelo.
- **Real/manual:** cenário com modelo real é manual, `[Explicit]` com `Category("LocalModelIntegration")`, em ambiente identificado; sem modelo configurado, não declarar o cenário executado.
- **Conclusão:** todos os critérios obrigatórios aprovados e vinculados à entrega oficial. Critério inaplicável exige justificativa; falho, ignorado ou não exercitado permanece aberto.

## Registro de execução

Execução parcial registrada abaixo; itens não exercitados continuam abertos.

| Campo | Registro |
| --- | --- |
| Snapshot e alterações locais | `fcc4b72` em `master`, com as alterações deste lote ainda não commitadas |
| Data, responsável, ambiente e versões | 12/11/2026 · Claude Code a pedido do usuário · Windows 11 · .NET SDK 10.0.402 · sem modelo real, sem MongoDB |
| Cenários, comandos, esperado e observado | Verificado no código e por teste: mensagem, contexto autorizado, anexos e resultado de tool chegam ao modelo como JSON do encoder padrão, com `<` e `>` escapados; o prompt de sistema por turno com `<tool_call>`, `<think>` ou `<|im_start|>` é recusado com `ContextRejected`. Testes `ReservedMarkersInUserTextAttachmentsAndToolResultsReachTheModelOnlyAsEscapedData` e `AForgedMarkerInThePerTurnSystemPromptRejectsTheContextBeforeTheModel`. Restore `--locked-mode`, build da solução com `-p:UsedAvaloniaProducts=` (0 avisos, 0 erros) e `dotnet test` da solução: Infrastructure.Agents.Tests 403/403; UnitTests 3.794 aprovados e 20 ignorados; IntegrationTests 1.126 aprovados, 20 ignorados e 1 falha (`AttachmentsCountTowardsTheInputLimitAndFailTypedWithoutAnyProcess`, bloqueado por variáveis de ambiente do Claude Code herdadas da sessão; não toca o código alterado). Foco: `--filter FullyQualifiedName~LocalAgentProvider` 38/38. |
| Resultado por critério | Aprovado para as origens externas do provider local. |
| Evidência oficial e artefatos sanitizados | `tests/EsilvaSoft.KapibaraStudio.UnitTests/LocalAgentProviderToolLoopTests.cs`; contrato atualizado em [IA local](../../../docs/MVP/contratos/ia-local.md) |
| Limitações e bloqueios | Marcador partido entre chunks é coberto pelos testes existentes do parser, não reexecutados isoladamente; o comportamento do template nativo com as sequências escapadas não foi observado com modelo real. |

Não armazenar credenciais, texto de raciocínio ou payloads sensíveis. Ao concluir, atualizar o estado de execução acima e os checklists com links para a evidência oficial; o estado dos requisitos continua no catálogo MVP.
