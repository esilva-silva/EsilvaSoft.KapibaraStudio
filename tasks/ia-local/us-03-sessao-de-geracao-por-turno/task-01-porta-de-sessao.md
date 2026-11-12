# KS-US03-T01 — Definir a porta de sessão de geração

**Estado de execução:** concluída na parte automatizável em 12/11/2026, reaproveitando o contrato existente em vez de criar uma porta nova. **Versão-alvo:** v0.11.0.

**User story:** [KS-US03 — Reutilizar o estado de geração entre os passos do turno](README.md). **Requisitos relacionados:** MVP-013. **Entregas oficiais:** F02-E03, F02-E05.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Definir em `LocalAi.Core` a abstração de sessão de geração que o provider usará, sem expor tipos do runtime.

**Depende de:** [KS-US02-T01 — Cobrir o ciclo de tools com runtime falso](../us-02-robustez-do-laco-do-agente/task-01-testes-do-ciclo-com-runtime-falso.md)

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Contrato de IA local](../../../docs/MVP/contratos/ia-local.md)
- [Arquitetura e invariantes](../../../docs/MVP/03-arquitetura-e-invariantes.md)

## Execução

- [ ] Definir operações de iniciar com prompt, anexar tokens ou texto renderizado, gerar em streaming, voltar a um ponto anterior e liberar.
- [x] Fixar as regras: uma sessão por turno, nunca compartilhada entre abas, sem serialização de KV, tokens ou texto.
- [x] Declarar como o runtime informa que não suporta sessão, para o provider cair no caminho atual.
- [x] Cobrir o contrato com um falso em memória.
- [x] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Contrato compilado e testado com falso; nenhuma dependência de ONNX em `LocalAi.Core`.

- **Automatizável:** testes de comportamento e de falha da alteração, mais restore, build e testes das regras comuns; usam runtime e `HttpMessageHandler` falsos e não carregam modelo.
- **Real/manual:** cenário com modelo real é manual, `[Explicit]` com `Category("LocalModelIntegration")`, em ambiente identificado; sem modelo configurado, não declarar o cenário executado.
- **Conclusão:** todos os critérios obrigatórios aprovados e vinculados à entrega oficial. Critério inaplicável exige justificativa; falho, ignorado ou não exercitado permanece aberto.

## Registro de execução

Execução parcial registrada abaixo; itens não exercitados continuam abertos.

| Campo | Registro |
| --- | --- |
| Snapshot e alterações locais | `fcc4b72` em `master`, com as alterações dos lotes KS-US01/02/03 ainda não commitadas |
| Data, responsável, ambiente e versões | 12/11/2026 · Claude Code a pedido do usuário · Windows 11 · .NET SDK 10.0.402 · sem modelo real, sem MongoDB |
| Cenários, comandos, esperado e observado | Decisão de desenho: a porta neutra já existia em `LocalAi.Core` como `ModelGenerationRequest.PrefixCache` (`PrefixCacheState`: identidade opaca, modo e tempo de vida, sem KV, tokens ou texto serializados). Ela passou a ser usada também pelos passos de um turno do agente; não foi criada uma interface de anexar/voltar. O runtime que não honra a sessão simplesmente gera com o prompt completo, então o provider não precisa de um segundo caminho. Build da solução com `-p:UsedAvaloniaProducts=`: 0 avisos, 0 erros. `dotnet test` da solução: Infrastructure.Agents.Tests 403/403; UnitTests 3.800 aprovados e 20 ignorados; IntegrationTests 1.126 aprovados, 20 ignorados e 1 falha (`AttachmentsCountTowardsTheInputLimitAndFailTypedWithoutAnyProcess`, bloqueado por variáveis de ambiente do Claude Code herdadas da sessão). KapiLab.Tests, fora da solução, rodado depois de acrescentar `--session-mode`: 717 aprovados, 7 ignorados e 2 falhas de sinalização de processo (`RealWindowsCtrlCPreservesThePriorSampleAndPublishesAnIncompleteCancelledReport`, que depende de CTRL_C_EVENT de console real, e `CancellationStopsTheCurrentChildAndKeepsEarlierResults`, que passou ao ser reexecutado isoladamente); nenhuma das duas foi comparada com o checkout sem as alterações. |
| Resultado por critério | Aprovado no desenho adotado. O primeiro item do checklist (operações explícitas de anexar e voltar) não foi implementado: foi substituído pelo retorno ao prefixo comum por IDs, feito pelo runtime. |
| Evidência oficial e artefatos sanitizados | `src/EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi.OnnxAdapter/PrefixReusePolicy.cs`, `tests/EsilvaSoft.KapibaraStudio.UnitTests/PrefixReusePolicyTests.cs` e `LocalAgentProviderToolLoopTests.cs`; contrato atualizado em [IA local](../../../docs/MVP/contratos/ia-local.md) e opção documentada no [KapiLab](../../../tools/KapiLab/README.md) |
| Limitações e bloqueios | Sem operação de anexar tokens exposta ao provider, o ganho por passo é o prefixo comum entre prompts renderizados, não a continuação exata do que o modelo acabou de gerar. `LocalAi.Core` continua sem dependência de ONNX. |

Não armazenar credenciais, texto de raciocínio ou payloads sensíveis. Ao concluir, atualizar o estado de execução acima e os checklists com links para a evidência oficial; o estado dos requisitos continua no catálogo MVP.
