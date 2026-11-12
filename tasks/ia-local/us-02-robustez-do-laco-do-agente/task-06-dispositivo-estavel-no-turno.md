# KS-US02-T06 — Manter o dispositivo durante o turno

**Estado de execução:** em andamento; implementação concluída em 12/11/2026, falha real de acelerador não exercitada. **Versão-alvo:** v0.11.0.

**User story:** [KS-US02 — Tornar o laço do agente local previsível em sucesso e falha](README.md). **Requisitos relacionados:** MVP-013. **Entregas oficiais:** F02-E03.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Impedir que um passo posterior do mesmo turno recarregue o modelo em outro dispositivo.

**Depende de:** [KS-US02-T01 — Cobrir o ciclo de tools com runtime falso](task-01-testes-do-ciclo-com-runtime-falso.md)

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Contrato de IA local](../../../docs/MVP/contratos/ia-local.md)

## Execução

- [x] Fixar o dispositivo efetivo no primeiro passo e reutilizá-lo até o fim do turno.
- [x] Em falha do dispositivo após o primeiro passo, encerrar o turno com erro visível em vez de trocar em silêncio.
- [ ] Testar com runtime falso a falha no primeiro passo e em passo posterior.
- [x] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Fallback só ocorre antes do primeiro chunk do turno; falha posterior vira erro explícito.

- **Automatizável:** testes de comportamento e de falha da alteração, mais restore, build e testes das regras comuns; usam runtime e `HttpMessageHandler` falsos e não carregam modelo.
- **Real/manual:** cenário com modelo real é manual, `[Explicit]` com `Category("LocalModelIntegration")`, em ambiente identificado; sem modelo configurado, não declarar o cenário executado.
- **Conclusão:** todos os critérios obrigatórios aprovados e vinculados à entrega oficial. Critério inaplicável exige justificativa; falho, ignorado ou não exercitado permanece aberto.

## Registro de execução

Execução parcial registrada abaixo; itens não exercitados continuam abertos.

| Campo | Registro |
| --- | --- |
| Snapshot e alterações locais | `fcc4b72` em `master`, com as alterações deste lote ainda não commitadas |
| Data, responsável, ambiente e versões | 12/11/2026 · Claude Code a pedido do usuário · Windows 11 · .NET SDK 10.0.402 · sem modelo real, sem MongoDB |
| Cenários, comandos, esperado e observado | Novo `ModelGenerationRequest.AllowAccelerationFallback` (padrão verdadeiro); o provider só o mantém no primeiro passo do turno e `OnnxLocalModelRuntime.StreamAsync` só recomeça em CPU quando ele está ligado. Teste `OnlyTheFirstStepOfATurnMayFallBackToAnotherDevice` confere a sequência verdadeiro, falso, verdadeiro em dois turnos. Restore `--locked-mode`, build da solução com `-p:UsedAvaloniaProducts=` (0 avisos, 0 erros) e `dotnet test` da solução: Infrastructure.Agents.Tests 403/403; UnitTests 3.794 aprovados e 20 ignorados; IntegrationTests 1.126 aprovados, 20 ignorados e 1 falha (`AttachmentsCountTowardsTheInputLimitAndFailTypedWithoutAnyProcess`, bloqueado por variáveis de ambiente do Claude Code herdadas da sessão; não toca o código alterado). Foco: `--filter FullyQualifiedName~LocalAgentProvider` 38/38. |
| Resultado por critério | Parcial: o pedido enviado ao runtime está comprovado; a condição no runtime ONNX foi apenas compilada. |
| Evidência oficial e artefatos sanitizados | `tests/EsilvaSoft.KapibaraStudio.UnitTests/LocalAgentProviderToolLoopTests.cs`; contrato atualizado em [IA local](../../../docs/MVP/contratos/ia-local.md) |
| Limitações e bloqueios | Não há teste que force falha do acelerador em passo posterior; exige GPU/DML real ou um duplo do runtime nativo. |

Não armazenar credenciais, texto de raciocínio ou payloads sensíveis. Ao concluir, atualizar o estado de execução acima e os checklists com links para a evidência oficial; o estado dos requisitos continua no catálogo MVP.
