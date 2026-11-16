# KS-US08-T03 — Homologar ou manter desativado o cache de prefixo

**Estado de execução:** aberta; em 13/11/2026 só o gate foi reverificado, sem medição. **Versão-alvo:** v0.11.0.

**User story:** [KS-US08 — Concluir o autocomplete explícito com o pacote v4](README.md). **Requisitos relacionados:** MVP-008, MVP-012. **Entregas oficiais:** F02-E02.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Decidir, por pacote de CPU, se o cache de prefixo pode ser habilitado.

**Depende de:** [KS-US08-T01 — Validar o fluxo de prévia e aceite](task-01-fluxo-ctrl-ponto-e-virgula.md); KT-US08-T03 — Medir TTFT de CPU com cache de prefixo (repositório de modelos `KapibaraStudio.MongoIA`, `tasks/us-08-pendencias-do-autocomplete/task-03-ttft-cpu-e-cache-de-prefixo.md`)

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Contrato de IA local](../../../docs/MVP/contratos/ia-local.md)
- [KapiLab](../../../tools/KapiLab/README.md)

## Execução

- [ ] Medir com `bench autocomplete --session-mode --device cpu` os gates: equivalência greedy ≥ 99%, acerto real ≥ 95%, T0 ≤ 150/250 ms, T1 ≤ 500/650 ms, mais TTFT e RAM com quatro sessões.
- [ ] Verificar invalidação por aba, documento, conexão, banco, coleção e linguagem, e no cancelamento e na falha.
- [ ] Habilitar apenas com `PrefixCacheMode=Reuse` e metadata do pacote; registrar o aceite por pacote.
- [x] Sem aceite, manter `Disabled` e registrar o impacto no TTFT.
- [ ] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Cada pacote tem decisão registrada; nenhuma habilitação sem os dois requisitos e sem os gates.

- **Automatizável:** testes de comportamento e de falha da alteração, mais restore, build e testes das regras comuns; usam runtime e `HttpMessageHandler` falsos e não carregam modelo.
- **Real/manual:** benchmark manual com modelo real, nunca em CI.
- **Conclusão:** todos os critérios obrigatórios aprovados e vinculados à entrega oficial. Critério inaplicável exige justificativa; falho, ignorado ou não exercitado permanece aberto.

## Registro de execução

Execução parcial registrada abaixo; itens não exercitados continuam abertos.

| Campo | Registro |
| --- | --- |
| Snapshot e alterações locais | `d8be8dc` em `master`, com as alterações deste lote ainda não commitadas |
| Data, responsável, ambiente e versões | 13/11/2026 · Claude Code a pedido do usuário · Windows 11 · .NET SDK 10.0.402 · Headless, sem modelo real e sem MongoDB |
| Cenários, comandos, esperado e observado | Nenhum pacote foi medido. O gate do produto continua exigindo CPU, `PrefixCacheMode=Reuse` e homologação declarada no pacote (`PrefixReusePolicyTests`, 3 testes, e `PrefixSessionCacheTests`), e o padrão permanece `Disabled`. Rodada focal em UnitTests (`InlineCompletionUiTests`, `EditorContextV2ContractTests`, `InnerCloserPostProcessingTests`, `AiCompletionProviderTests`, `PrefixSessionCacheTests`, `PrefixReusePolicyTests`): 67/67. Build da solução com `-p:UsedAvaloniaProducts=`: 0 avisos, 0 erros. `dotnet test` da solução: Infrastructure.Agents.Tests 403/403; UnitTests 3.805 aprovados e 20 ignorados; IntegrationTests 1.143 aprovados, 20 ignorados e 2 falhas: `AttachmentsCountTowardsTheInputLimitAndFailTypedWithoutAnyProcess` (bloqueado por variáveis de ambiente do Claude Code herdadas da sessão) e `PreviousSessionKeepsItsUuidRepresentationAndGetsStandardIdentifierMode("JavaLegacy")`, que falhou uma vez na suíte integral e passou três vezes isolado. É a terceira falha intermitente diferente em rodadas integrais desta sessão; nenhuma se repetiu e nenhuma foi diagnosticada. |
| Resultado por critério | Pendente: gates de equivalência, acerto, TTFT e RAM por pacote. |
| Evidência oficial e artefatos sanitizados | Testes citados acima, em `tests/` e `tools/KapiLab.Tests`; nenhum artefato novo em `docs` |
| Limitações e bloqueios | Bloqueio: exige benchmark manual com os pacotes v4 reais em CPU. Impacto conhecido pelo repositório de modelos: sem cache, Nano int4 e Small int8 reprovam o TTFT de CPU. Nenhuma invalidação por aba, conexão ou coleção foi reexercitada com modelo. |

Não armazenar credenciais, texto de raciocínio ou payloads sensíveis. Ao concluir, atualizar o estado de execução acima e os checklists com links para a evidência oficial; o estado dos requisitos continua no catálogo MVP.
