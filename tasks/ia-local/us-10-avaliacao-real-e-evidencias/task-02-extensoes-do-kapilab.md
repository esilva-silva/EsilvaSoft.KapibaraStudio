# KS-US10-T02 — Estender o `kapilab agent run`

**Estado de execução:** em andamento; sementes, modo de raciocínio e métricas concluídos em 13/11/2026, sem comando de benchmark. **Versão-alvo:** v0.11.0.

**User story:** [KS-US10 — Avaliar com modelo real e consolidar as evidências](README.md). **Requisitos relacionados:** MVP-008, MVP-011..013. **Entregas oficiais:** F02-E05.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Dar ao KapiLab o que falta para medir o agente de forma repetível.

**Depende de:** [KS-US02-T03 — Preencher semente e amostragem do pacote](../us-02-robustez-do-laco-do-agente/task-03-semente-e-amostragem.md)

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [KapiLab](../../../tools/KapiLab/README.md)

## Execução

- [x] Adicionar execução com várias sementes e alternância e orçamento de raciocínio.
- [ ] Adicionar o benchmark do agente com tempo até a primeira resposta, tokens de raciocínio, estouro de orçamento e tool inventada.
- [x] Manter o relatório versionado e apenas com hashes de conteúdo.
- [x] Cobrir com testes do KapiLab; benchmark exige opt-in local e não roda em CI.
- [ ] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Comandos documentados no README do KapiLab e testados; o repositório de modelos consegue usá-los em KT-US06-T03.

- **Automatizável:** testes de comportamento e de falha da alteração, mais restore, build e testes das regras comuns; usam runtime e `HttpMessageHandler` falsos e não carregam modelo.
- **Real/manual:** cenário com modelo real é manual, `[Explicit]` com `Category("LocalModelIntegration")`, em ambiente identificado; sem modelo configurado, não declarar o cenário executado.
- **Conclusão:** todos os critérios obrigatórios aprovados e vinculados à entrega oficial. Critério inaplicável exige justificativa; falho, ignorado ou não exercitado permanece aberto.

## Registro de execução

Execução parcial registrada abaixo; itens não exercitados continuam abertos.

| Campo | Registro |
| --- | --- |
| Snapshot e alterações locais | `d8be8dc` em `master`, com as alterações deste lote ainda não commitadas |
| Data, responsável, ambiente e versões | 13/11/2026 · Claude Code a pedido do usuário · Windows 11 · .NET SDK 10.0.402 · Headless, sem modelo real e sem MongoDB |
| Cenários, comandos, esperado e observado | `kapilab agent run` ganhou `--seeds` (até 10; cada tarefa roda uma vez por semente, enviada ao provider como `SamplingSeed`), `--thinking default|on|off` e `--think-budget`. O relatório, ainda `kapilab-agent-run-report-v1` e só com campos acrescentados, traz por execução a semente, o tempo até a primeira resposta e os blocos de raciocínio cortados, e no resumo média e percentil 95 desse tempo, turnos com chamada inválida, turnos com orçamento estourado e taxa de aprovação por semente. Validação, escolha de raciocínio e resumo são funções puras cobertas por `AgentModelRunOptionsTests`. KapiLab.Tests, fora da solução: 732 aprovados, 7 ignorados e 1 falha (`RealWindowsCtrlCPreservesThePriorSampleAndPublishesAnIncompleteCancelledReport`, que depende de CTRL_C_EVENT de console real e já falhava antes deste lote); os 14 testes de `AgentModelRunOptionsTests` passam. Build da solução com `-p:UsedAvaloniaProducts=`: 0 avisos, 0 erros. `dotnet test` da solução: Infrastructure.Agents.Tests 403/403; UnitTests 3.805 aprovados e 20 ignorados; IntegrationTests 1.143 aprovados, 20 ignorados e 2 falhas: `AttachmentsCountTowardsTheInputLimitAndFailTypedWithoutAnyProcess` (bloqueado por variáveis de ambiente do Claude Code herdadas da sessão) e `PreviousSessionKeepsItsUuidRepresentationAndGetsStandardIdentifierMode("JavaLegacy")`, que falhou uma vez na suíte integral e passou três vezes isolado. É a terceira falha intermitente diferente em rodadas integrais desta sessão; nenhuma se repetiu e nenhuma foi diagnosticada. |
| Resultado por critério | Parcial: opções e resumo aprovados por teste; README do KapiLab atualizado. |
| Evidência oficial e artefatos sanitizados | Testes citados acima, em `tests/` e `tools/KapiLab.Tests`; nenhum artefato novo em `docs` |
| Limitações e bloqueios | Não feito: comando `agent bench` separado, tokens por segundo e pico de RAM; as métricas novas saem do próprio `agent run`, sem descarte de aquecimento. Nenhuma rodada com pacote real usou as opções novas. `invalidToolCallTurns` agrupa envelope inválido, tool fora do plano e limite de chamadas, que o provider reporta com o mesmo código. |

Não armazenar credenciais, texto de raciocínio ou payloads sensíveis. Ao concluir, atualizar o estado de execução acima e os checklists com links para a evidência oficial; o estado dos requisitos continua no catálogo MVP.
