# KS-US03-T02 — Implementar a sessão no runtime ONNX

**Estado de execução:** em andamento; implementação concluída em 12/11/2026, sem execução com modelo real. **Versão-alvo:** v0.11.0.

**User story:** [KS-US03 — Reutilizar o estado de geração entre os passos do turno](README.md). **Requisitos relacionados:** MVP-013. **Entregas oficiais:** F02-E03, F02-E05.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Implementar a porta de sessão em `OnnxLocalModelRuntime`, mantendo um gerador vivo durante o turno.

**Depende de:** [KS-US03-T01 — Definir a porta de sessão de geração](task-01-porta-de-sessao.md)

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Contrato de IA local](../../../docs/MVP/contratos/ia-local.md)
- [KapiLab](../../../tools/KapiLab/README.md)

## Execução

- [ ] Confirmar na versão fixada do GenAI as operações de anexar tokens e de voltar a posição, com um smoke no KapiLab antes de depender delas.
- [x] Implementar a sessão respeitando a regra de EOS: consumir token apenas se a sequência cresceu e preservar o último token válido.
- [x] Manter a serialização do serviço e liberar o gerador ao fim do turno, no cancelamento e na troca de modelo, sem dispose concorrente.
- [x] Não registrar texto nem identidade em métricas.
- [x] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Sessão funciona em CPU com o pacote de referência; fechar aba, cancelar e trocar modelo liberam o gerador.

- **Automatizável:** testes de comportamento e de falha da alteração, mais restore, build e testes das regras comuns; usam runtime e `HttpMessageHandler` falsos e não carregam modelo.
- **Real/manual:** provas com modelo real são Explicit; o CI usa apenas o falso.
- **Conclusão:** todos os critérios obrigatórios aprovados e vinculados à entrega oficial. Critério inaplicável exige justificativa; falho, ignorado ou não exercitado permanece aberto.

## Registro de execução

Execução parcial registrada abaixo; itens não exercitados continuam abertos.

| Campo | Registro |
| --- | --- |
| Snapshot e alterações locais | `fcc4b72` em `master`, com as alterações dos lotes KS-US01/02/03 ainda não commitadas |
| Data, responsável, ambiente e versões | 12/11/2026 · Claude Code a pedido do usuário · Windows 11 · .NET SDK 10.0.402 · sem modelo real, sem MongoDB |
| Cenários, comandos, esperado e observado | `OnnxLocalModelRuntime.Generate` passou a decidir o reuso por `PrefixReusePolicy.IsEligible`: mesmo gate do produto (CPU, `PrefixCacheMode=Reuse`, pacote homologado ou override do laboratório), agora aceitando prompt de chat com amostragem; o autocomplete continua restrito à geração gulosa. A chave efetiva (`PrefixReusePolicy.SessionKey`) inclui temperatura, top-p, top-k e semente nos pedidos de chat, porque o gerador nativo conserva os parâmetros com que nasceu. Regra de EOS, serialização do serviço e liberação por cancelamento, troca de modelo e fim da sessão são as já existentes do cache de sessão. Testes `PrefixReusePolicyTests` (3) e `PrefixSessionCacheTests` existentes. Build da solução com `-p:UsedAvaloniaProducts=`: 0 avisos, 0 erros. `dotnet test` da solução: Infrastructure.Agents.Tests 403/403; UnitTests 3.800 aprovados e 20 ignorados; IntegrationTests 1.126 aprovados, 20 ignorados e 1 falha (`AttachmentsCountTowardsTheInputLimitAndFailTypedWithoutAnyProcess`, bloqueado por variáveis de ambiente do Claude Code herdadas da sessão). KapiLab.Tests, fora da solução, rodado depois de acrescentar `--session-mode`: 717 aprovados, 7 ignorados e 2 falhas de sinalização de processo (`RealWindowsCtrlCPreservesThePriorSampleAndPublishesAnIncompleteCancelledReport`, que depende de CTRL_C_EVENT de console real, e `CancellationStopsTheCurrentChildAndKeepsEarlierResults`, que passou ao ser reexecutado isoladamente); nenhuma das duas foi comparada com o checkout sem as alterações. |
| Resultado por critério | Parcial: a decisão de reuso e a chave estão cobertas por teste; o caminho nativo (`RewindTo` e `AppendTokens` com prompt de chat) foi apenas compilado. |
| Evidência oficial e artefatos sanitizados | `src/EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi.OnnxAdapter/PrefixReusePolicy.cs`, `tests/EsilvaSoft.KapibaraStudio.UnitTests/PrefixReusePolicyTests.cs` e `LocalAgentProviderToolLoopTests.cs`; contrato atualizado em [IA local](../../../docs/MVP/contratos/ia-local.md) e opção documentada no [KapiLab](../../../tools/KapiLab/README.md) |
| Limitações e bloqueios | Não houve smoke no KapiLab com pacote de agente em CPU: o único pacote de agente disponível é DML fp16, em que a sessão não tem efeito. As operações de voltar e anexar são as mesmas já usadas pelo autocomplete na versão fixada do GenAI. |

Não armazenar credenciais, texto de raciocínio ou payloads sensíveis. Ao concluir, atualizar o estado de execução acima e os checklists com links para a evidência oficial; o estado dos requisitos continua no catálogo MVP.
