# KS-US09-T01 — Validar cancelamento isolado e concorrência

**Estado de execução:** aberta; criação do arquivo não executa nem homologa a entrega. **Versão-alvo:** v0.11.0.

**User story:** [KS-US09 — Garantir concorrência, liberação e recuperação](README.md). **Requisitos relacionados:** MVP-011, MVP-012, MVP-013. **Entregas oficiais:** F02-E04.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Validar que operações de IA local em abas e papéis diferentes não interferem entre si.

**Depende de:** [KS-US02-T04 — Encerrar raciocínio e tool pendente no cancelamento](../us-02-robustez-do-laco-do-agente/task-04-cancelamento-do-raciocinio.md); [KS-US08-T01 — Validar o fluxo de prévia e aceite](../us-08-autocomplete-explicito/task-01-fluxo-ctrl-ponto-e-virgula.md)

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Arquitetura e invariantes](../../../docs/MVP/03-arquitetura-e-invariantes.md)
- [Contrato de IA local](../../../docs/MVP/contratos/ia-local.md)

## Execução

- [ ] Testar autocomplete em uma aba e agente em outra, com cancelamento de apenas uma.
- [ ] Testar preempção: novo pedido com um modelo carregado por vez, sem perder o contexto capturado.
- [ ] Testar que resultado tardio não atualiza aba diferente da que o originou.
- [ ] Repetir com modelo real o cancelamento após o primeiro token.
- [ ] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Testes de falha e concorrência passam; o cenário real confirma a interrupção sem alegar rollback.

- **Automatizável:** testes de comportamento e de falha da alteração, mais restore, build e testes das regras comuns; usam runtime e `HttpMessageHandler` falsos e não carregam modelo.
- **Real/manual:** cenário com modelo real é manual, `[Explicit]` com `Category("LocalModelIntegration")`, em ambiente identificado; sem modelo configurado, não declarar o cenário executado.
- **Conclusão:** todos os critérios obrigatórios aprovados e vinculados à entrega oficial. Critério inaplicável exige justificativa; falho, ignorado ou não exercitado permanece aberto.

## Registro de execução

Preencher ao executar; nenhum resultado novo é declarado neste arquivo.

| Campo | Registro |
| --- | --- |
| Snapshot e alterações locais | A registrar |
| Data, responsável, ambiente e versões | A registrar |
| Cenários, comandos, esperado e observado | A registrar |
| Resultado por critério | Pendente |
| Evidência oficial e artefatos sanitizados | A vincular em docs |
| Limitações e bloqueios | A registrar com impacto, recurso faltante e próximo passo |

Não armazenar credenciais, texto de raciocínio ou payloads sensíveis. Ao concluir, atualizar o estado de execução acima e os checklists com links para a evidência oficial; o estado dos requisitos continua no catálogo MVP.
