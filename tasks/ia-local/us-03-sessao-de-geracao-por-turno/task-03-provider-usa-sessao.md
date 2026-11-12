# KS-US03-T03 — Usar a sessão no laço do agente

**Estado de execução:** concluída na parte automatizável em 12/11/2026, com desvio registrado no fechamento forçado. **Versão-alvo:** v0.11.0.

**User story:** [KS-US03 — Reutilizar o estado de geração entre os passos do turno](README.md). **Requisitos relacionados:** MVP-013. **Entregas oficiais:** F02-E03, F02-E05.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Fazer o `LocalAgentProvider` anexar resultado de tool e fechamento forçado à sessão em vez de renderizar o chat inteiro a cada passo.

**Depende de:** [KS-US03-T02 — Implementar a sessão no runtime ONNX](task-02-implementacao-onnx.md)

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Contrato de IA local](../../../docs/MVP/contratos/ia-local.md)

## Execução

- [ ] Anexar o resultado de tool renderizado no formato do template e continuar a geração na mesma sessão.
- [ ] Injetar `ForceCloseText` do pacote ao atingir o orçamento, sem nova renderização, mantendo o teto por turno.
- [x] Manter o histórico textual do turno idêntico ao produzido hoje, para sessões persistidas e paridade.
- [x] Cair no caminho atual quando o runtime não suporta sessão, sem mudar o resultado.
- [x] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Com sessão, nenhum passo após o primeiro refaz o prefill completo; os testes de KS-US02-T01 passam nos dois caminhos.

- **Automatizável:** testes de comportamento e de falha da alteração, mais restore, build e testes das regras comuns; usam runtime e `HttpMessageHandler` falsos e não carregam modelo.
- **Real/manual:** cenário com modelo real é manual, `[Explicit]` com `Category("LocalModelIntegration")`, em ambiente identificado; sem modelo configurado, não declarar o cenário executado.
- **Conclusão:** todos os critérios obrigatórios aprovados e vinculados à entrega oficial. Critério inaplicável exige justificativa; falho, ignorado ou não exercitado permanece aberto.

## Registro de execução

Execução parcial registrada abaixo; itens não exercitados continuam abertos.

| Campo | Registro |
| --- | --- |
| Snapshot e alterações locais | `fcc4b72` em `master`, com as alterações dos lotes KS-US01/02/03 ainda não commitadas |
| Data, responsável, ambiente e versões | 12/11/2026 · Claude Code a pedido do usuário · Windows 11 · .NET SDK 10.0.402 · sem modelo real, sem MongoDB |
| Cenários, comandos, esperado e observado | Com `PrefixCacheMode=Reuse` o provider pede a todos os passos do turno a sessão `agent-turn:<id do turno>` e a encerra no fim do turno por conclusão, falha ou cancelamento; sem o opt-in não pede sessão. O histórico textual e o prompt renderizado são idênticos nos dois casos. Testes `WithReuseEnabledEveryStepOfATurnSharesOneGenerationSessionThatEndsWithTheTurn`, `TheGenerationSessionAlsoEndsWhenTheTurnFailsOrIsCancelled` e `WithoutTheOptInNoGenerationSessionIsRequested`; os 20 testes de KS-US02-T01 continuam passando. Build da solução com `-p:UsedAvaloniaProducts=`: 0 avisos, 0 erros. `dotnet test` da solução: Infrastructure.Agents.Tests 403/403; UnitTests 3.800 aprovados e 20 ignorados; IntegrationTests 1.126 aprovados, 20 ignorados e 1 falha (`AttachmentsCountTowardsTheInputLimitAndFailTypedWithoutAnyProcess`, bloqueado por variáveis de ambiente do Claude Code herdadas da sessão). KapiLab.Tests, fora da solução, rodado depois de acrescentar `--session-mode`: 717 aprovados, 7 ignorados e 2 falhas de sinalização de processo (`RealWindowsCtrlCPreservesThePriorSampleAndPublishesAnIncompleteCancelledReport`, que depende de CTRL_C_EVENT de console real, e `CancellationStopsTheCurrentChildAndKeepsEarlierResults`, que passou ao ser reexecutado isoladamente); nenhuma das duas foi comparada com o checkout sem as alterações. |
| Resultado por critério | Aprovado para o que o provider pede ao runtime. Desvio: o resultado de tool e o `ForceCloseText` não são anexados à sessão; o passo seguinte renderiza o prompt e o runtime reaproveita o prefixo comum. |
| Evidência oficial e artefatos sanitizados | `src/EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi.OnnxAdapter/PrefixReusePolicy.cs`, `tests/EsilvaSoft.KapibaraStudio.UnitTests/PrefixReusePolicyTests.cs` e `LocalAgentProviderToolLoopTests.cs`; contrato atualizado em [IA local](../../../docs/MVP/contratos/ia-local.md) e opção documentada no [KapiLab](../../../tools/KapiLab/README.md) |
| Limitações e bloqueios | O critério "nenhum passo após o primeiro refaz o prefill completo" só é atendido quando o runtime honra a sessão (CPU, opt-in e pacote homologado); isso não foi observado com modelo real. O passo sem raciocínio após o fechamento forçado usa outro gerador e refaz o prefill uma vez. |

Não armazenar credenciais, texto de raciocínio ou payloads sensíveis. Ao concluir, atualizar o estado de execução acima e os checklists com links para a evidência oficial; o estado dos requisitos continua no catálogo MVP.
