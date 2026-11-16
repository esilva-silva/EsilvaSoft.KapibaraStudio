# KS-US10-T01 — Criar os testes Explicit do agente

**Estado de execução:** em andamento; em 13/11/2026 dois testes passaram com modelo real na GPU e o de cancelamento travou o processo de teste. **Versão-alvo:** v0.11.0.

**User story:** [KS-US10 — Avaliar com modelo real e consolidar as evidências](README.md). **Requisitos relacionados:** MVP-008, MVP-011..013. **Entregas oficiais:** F02-E05.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Criar os testes de integração com modelo real para o caminho de agente.

**Depende de:** [KS-US02-T01 — Cobrir o ciclo de tools com runtime falso](../us-02-robustez-do-laco-do-agente/task-01-testes-do-ciclo-com-runtime-falso.md)

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Contrato de IA local](../../../docs/MVP/contratos/ia-local.md)

## Execução

- [x] Criar testes `[Explicit]` com `Category("LocalModelIntegration")` para o caminho de agente, com variável de ambiente própria para o pacote e para o raciocínio.
- [ ] Cobrir resposta sem tool, uma chamada, várias chamadas, recusa e cancelamento.
- [x] Conferir que a guarda `RealModelHubAccessIsConfinedToExplicitLocalModelTests` continua passando e que o CI exclui a categoria.
- [ ] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Testes existem e passam localmente com modelo configurado; sem modelo, não são declarados executados.

- **Automatizável:** a guarda estática de `IntegrationTests`, executada localmente.
- **Real/manual:** cenário com modelo real é manual, `[Explicit]` com `Category("LocalModelIntegration")`, em ambiente identificado; sem modelo configurado, não declarar o cenário executado.
- **Conclusão:** todos os critérios obrigatórios aprovados e vinculados à entrega oficial. Critério inaplicável exige justificativa; falho, ignorado ou não exercitado permanece aberto.

## Registro de execução

Execução parcial registrada abaixo; itens não exercitados continuam abertos.

| Campo | Registro |
| --- | --- |
| Snapshot e alterações locais | `d8be8dc` em `master`, com as alterações deste lote ainda não commitadas |
| Data, responsável, ambiente e versões | 13/11/2026 · Claude Code a pedido do usuário · Windows 11 · .NET SDK 10.0.402 · Headless, sem modelo real e sem MongoDB |
| Cenários, comandos, esperado e observado | Três testes `[Explicit]` com `Category("LocalModelIntegration")` em `LocalAgentProviderModelIntegrationTests`, ativados por `KAPI_AGENT_MODEL` (pacote) e `KAPI_AGENT_DEVICE` (cpu, gpu ou auto): resposta sem tool, pedido de uma tool permitida com resposta a partir do resultado, e cancelamento com a sessão utilizável em seguida. Usam o provider e o runtime ONNX reais; o registry só anuncia `list_databases` e o resultado é sintético, sem MongoDB. Sem a variável, ficam fora da execução: os quatro testes da classe aparecem como ignorados (0 aprovados, 0 falhas). Guarda `RealModelHubAccessIsConfinedToExplicitLocalModelTests`: 1/1 aprovado. Build da solução com `-p:UsedAvaloniaProducts=`: 0 avisos, 0 erros. `dotnet test` da solução: Infrastructure.Agents.Tests 403/403; UnitTests 3.805 aprovados e 20 ignorados; IntegrationTests 1.143 aprovados, 20 ignorados e 2 falhas: `AttachmentsCountTowardsTheInputLimitAndFailTypedWithoutAnyProcess` (bloqueado por variáveis de ambiente do Claude Code herdadas da sessão) e `PreviousSessionKeepsItsUuidRepresentationAndGetsStandardIdentifierMode("JavaLegacy")`, que falhou uma vez na suíte integral e passou três vezes isolado. É a terceira falha intermitente diferente em rodadas integrais desta sessão; nenhuma se repetiu e nenhuma foi diagnosticada. |
| Resultado por critério | Parcial: testes compilam e ficam fora da suíte normal; nenhum foi executado com modelo real. |
| Evidência oficial e artefatos sanitizados | Testes citados acima, em `tests/` e `tools/KapiLab.Tests`; nenhum artefato novo em `docs` |
| Limitações e bloqueios | Faltam os casos de várias chamadas e de recusa. As asserções de pedido de tool e de uso do resultado dependem do comportamento do modelo e podem falhar com o pacote base sem ajuste. Não conferi a configuração do CI para a exclusão da categoria. |

### Rodada com modelo real — 13/11/2026

Pacote `KapiCoder-Mongo-Agent-Qwen3-4B-Thinking-ONNX-dml-fp16` (Qwen3-4B-Thinking-2507 base, sem ajuste, candidato não qualificado), `KAPI_AGENT_DEVICE=gpu`, RX 7800 XT, Windows 11, GenAI do checkout. Autorizada pelo usuário. Evidência funcional, não medição de aceite.

| Teste | Resultado |
| --- | --- |
| `RealAgentModelAnswersATurnWithoutTools` | Aprovado, 17 s incluindo a carga do modelo |
| `RealAgentModelRequestsAnAllowedToolAndAnswersFromItsResult` | Aprovado, 18 s: o modelo pediu `list_databases` e respondeu com o resultado devolvido |
| `CancellingARealAgentTurnEndsItWithoutAnErrorAndTheSessionAnswersAgain` | **Sem resultado**: iniciado às 12:59:15, nunca reportou. Às 16:35 o `testhost.exe` continuava listado, com uma única thread, cerca de 200 MB e sem memória de GPU; o `dotnet test` pai já não existia, `taskkill` e `Stop-Process` não localizavam o processo e os binários de teste ficaram bloqueados. A rodada foi interrompida pelo usuário |

Leitura provável, não comprovada: o processo travou ao encerrar, depois de um cancelamento com a geração DirectML em andamento. Não se sabe se o teste chegou às asserções nem em que ponto parou; não havia ferramenta de dump instalada. Próximo passo: repetir com `--blame-hang` e em CPU, para separar provider de driver, e comparar com a prova de cancelamento de processo do KapiLab.

Não armazenar credenciais, texto de raciocínio ou payloads sensíveis. Ao concluir, atualizar o estado de execução acima e os checklists com links para a evidência oficial; o estado dos requisitos continua no catálogo MVP.
