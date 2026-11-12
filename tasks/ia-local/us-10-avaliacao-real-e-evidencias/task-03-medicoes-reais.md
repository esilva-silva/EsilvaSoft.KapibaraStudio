# KS-US10-T03 — Executar as medições reais

**Estado de execução:** aberta; criação do arquivo não executa nem homologa a entrega. **Versão-alvo:** v0.11.0.

**User story:** [KS-US10 — Avaliar com modelo real e consolidar as evidências](README.md). **Requisitos relacionados:** MVP-008, MVP-011..013. **Entregas oficiais:** F02-E05.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Executar a avaliação manual no pacote, hardware e corpus fixados.

**Depende de:** [KS-US10-T01 — Criar os testes Explicit do agente](task-01-testes-explicit-do-agente.md); [KS-US10-T02 — Estender o `kapilab agent run`](task-02-extensoes-do-kapilab.md); [KS-US01-T01 — Registrar pacote de referência e limites](../us-01-referencia-e-contratos-do-pacote/task-01-registrar-referencia-e-limites.md); [KS-US03-T04 — Provar paridade e medir o ganho](../us-03-sessao-de-geracao-por-turno/task-04-paridade-e-medicao.md); KT-US09-T03 — Entregar a nota de referência ao Studio (repositório de modelos `KapibaraStudio.MongoIA`, `tasks/us-09-publicacao-e-entrega/task-03-nota-de-entrega-ao-studio.md`)

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Contrato de IA local](../../../docs/MVP/contratos/ia-local.md)
- [Baseline histórica](../../../docs/MVP/09-baseline-de-validacao.md)

## Execução

- [ ] Medir qualidade do autocomplete e sucesso de tarefa do agente no corpus versionado.
- [ ] Medir TTFT, tempo até a primeira resposta, tokens por segundo, pico de RAM, cancelamento e liberação, separando aquecimento e condição de cache.
- [ ] Comparar com os limites de KS-US01 e corrigir e reavaliar falhas sem relaxar critério.
- [ ] Registrar resultados sintéticos, reais e nativos em seções separadas.
- [ ] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Cada limite tem valor medido e veredito; limite não atingido fica falho ou pendente.

- **Automatizável:** não aplicável; medições não entram em CI.
- **Real/manual:** pacote treinado, hardware e corpus identificados; sem eles a tarefa fica bloqueada.
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
