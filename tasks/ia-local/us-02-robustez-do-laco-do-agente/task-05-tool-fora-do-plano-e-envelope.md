# KS-US02-T05 — Alinhar ao contrato tool fora do plano e envelope inválido

**Estado de execução:** em andamento; comportamento atual coberto por teste e documentado em 12/11/2026, decisão com o repositório de modelos pendente. **Versão-alvo:** v0.11.0.

**User story:** [KS-US02 — Tornar o laço do agente local previsível em sucesso e falha](README.md). **Requisitos relacionados:** MVP-013. **Entregas oficiais:** F02-E03.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Fazer o tratamento de envelope inválido e de tool fora do plano coincidir com o contrato e com o que o modelo é treinado a esperar.

**Depende de:** [KS-US02-T01 — Cobrir o ciclo de tools com runtime falso](task-01-testes-do-ciclo-com-runtime-falso.md)

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Contrato de IA local](../../../docs/MVP/contratos/ia-local.md)
- [Tools de agentes](../../../docs/MVP/contratos/tools-agentes.md)

## Execução

- [x] Manter o retry único de `InvalidToolCallFormat` para envelope inválido.
- [ ] Decidir, junto ao repositório de modelos, se tool fora do plano continua como erro imediato ou devolve resultado de erro ao modelo; registrar no contrato.
- [ ] Garantir `UnknownTool` para os 13 nomes recusados antes de consultar perfil, policy ou auditoria.
- [ ] Manter a validação de argumentos no registry e testar argumento inválido de ponta a ponta.
- [ ] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Comportamento único, documentado e testado; o catálogo não é ampliado para satisfazer fixture antiga.

- **Automatizável:** testes de comportamento e de falha da alteração, mais restore, build e testes das regras comuns; usam runtime e `HttpMessageHandler` falsos e não carregam modelo.
- **Real/manual:** cenário com modelo real é manual, `[Explicit]` com `Category("LocalModelIntegration")`, em ambiente identificado; sem modelo configurado, não declarar o cenário executado.
- **Conclusão:** todos os critérios obrigatórios aprovados e vinculados à entrega oficial. Critério inaplicável exige justificativa; falho, ignorado ou não exercitado permanece aberto.

## Registro de execução

Execução parcial registrada abaixo; itens não exercitados continuam abertos.

| Campo | Registro |
| --- | --- |
| Snapshot e alterações locais | `fcc4b72` em `master`, com as alterações deste lote ainda não commitadas |
| Data, responsável, ambiente e versões | 12/11/2026 · Claude Code a pedido do usuário · Windows 11 · .NET SDK 10.0.402 · sem modelo real, sem MongoDB |
| Cenários, comandos, esperado e observado | Retry único de envelope inválido e falha na segunda ocorrência; tool fora do plano e nomes recusados (`mongo_find`, `drop_index`) nunca viram `ToolRequested` e encerram o turno com `InvalidToolCallFormat`. Testes `AnInvalidEnvelopeIsRetriedOnceWithTheContractErrorAndThenFailsTheTurn` e `AToolOutsideTheTurnPlanIsNeverRequested`. Restore `--locked-mode`, build da solução com `-p:UsedAvaloniaProducts=` (0 avisos, 0 erros) e `dotnet test` da solução: Infrastructure.Agents.Tests 403/403; UnitTests 3.794 aprovados e 20 ignorados; IntegrationTests 1.126 aprovados, 20 ignorados e 1 falha (`AttachmentsCountTowardsTheInputLimitAndFailTypedWithoutAnyProcess`, bloqueado por variáveis de ambiente do Claude Code herdadas da sessão; não toca o código alterado). Foco: `--filter FullyQualifiedName~LocalAgentProvider` 38/38. |
| Resultado por critério | Parcial: comportamento vigente comprovado; nenhuma mudança de comportamento feita. |
| Evidência oficial e artefatos sanitizados | `tests/EsilvaSoft.KapibaraStudio.UnitTests/LocalAgentProviderToolLoopTests.cs`; contrato atualizado em [IA local](../../../docs/MVP/contratos/ia-local.md) |
| Limitações e bloqueios | Falta decidir com o repositório de modelos (KT-US04) se tool fora do plano devolve erro ao modelo em vez de encerrar; `UnknownTool` no registry e argumento inválido de ponta a ponta não foram reexercitados aqui. |

Não armazenar credenciais, texto de raciocínio ou payloads sensíveis. Ao concluir, atualizar o estado de execução acima e os checklists com links para a evidência oficial; o estado dos requisitos continua no catálogo MVP.
