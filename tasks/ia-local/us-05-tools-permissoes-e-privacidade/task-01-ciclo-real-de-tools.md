# KS-US05-T01 — Exercitar o ciclo real das tools permitidas

**Estado de execução:** aberta; criação do arquivo não executa nem homologa a entrega. **Versão-alvo:** v0.11.0.

**User story:** [KS-US05 — Comprovar tools, recusas e privacidade com modelo real](README.md). **Requisitos relacionados:** MVP-013. **Entregas oficiais:** F02-E03.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Demonstrar com modelo real o ciclo envio humano → streaming → chamada → retorno → conclusão para as tools vigentes.

**Depende de:** [KS-US02-T01 — Cobrir o ciclo de tools com runtime falso](../us-02-robustez-do-laco-do-agente/task-01-testes-do-ciclo-com-runtime-falso.md); [KS-US01-T01 — Registrar pacote de referência e limites](../us-01-referencia-e-contratos-do-pacote/task-01-registrar-referencia-e-limites.md)

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Tools de agentes](../../../docs/MVP/contratos/tools-agentes.md)
- [Contrato de IA local](../../../docs/MVP/contratos/ia-local.md)

## Execução

- [ ] Exercitar `list_connections`, `list_databases`, `list_collections`, `get_indexes`, `get_cached_schema` e `get_workspace_context` com MongoDB descartável.
- [ ] Exercitar `get_query_results` e `get_query_diagnostics` somente após execução humana concluída, com opt-in; sem execução, `available=false`.
- [ ] Exercitar `get_search_indexes` quando houver Atlas; sem Atlas, registrar o cenário como pendente.
- [ ] Separar a evidência com modelo real da evidência sintética do KapiLab.
- [ ] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Cada tool aplicável completa um ciclo real registrado; nenhuma tool consulta documentos por conta própria.

- **Automatizável:** testes de comportamento e de falha da alteração, mais restore, build e testes das regras comuns; usam runtime e `HttpMessageHandler` falsos e não carregam modelo.
- **Real/manual:** modelo real e MongoDB descartável, com execução humana das consultas.
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
