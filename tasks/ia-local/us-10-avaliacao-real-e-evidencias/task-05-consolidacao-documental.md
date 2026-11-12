# KS-US10-T05 — Consolidar evidências e estado

**Estado de execução:** aberta; criação do arquivo não executa nem homologa a entrega. **Versão-alvo:** v0.11.0.

**User story:** [KS-US10 — Avaliar com modelo real e consolidar as evidências](README.md). **Requisitos relacionados:** MVP-008, MVP-011..013. **Entregas oficiais:** F02-E05.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Levar as evidências à árvore oficial e atualizar o estado dos requisitos conforme o resultado.

**Depende de:** [KS-US10-T03 — Executar as medições reais](task-03-medicoes-reais.md); [KS-US10-T04 — Validar a experiência em desktop nativo](task-04-experiencia-nativa.md)

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Baseline histórica](../../../docs/MVP/09-baseline-de-validacao.md)
- [Contrato de IA local](../../../docs/MVP/contratos/ia-local.md)

## Execução

- [ ] Registrar as evidências na baseline e vinculá-las às entregas F02-E01..E05 na fase oficial.
- [ ] Atualizar MVP-012 e MVP-013 no catálogo apenas com requisito, snapshot, ambiente, procedimento, resultado e evidência.
- [ ] Atualizar o contrato de IA local com o que mudou e marcar as tarefas F02-T01..T05 conforme a evidência.
- [ ] Rodar `node scripts/check-docs-reader.cjs`.
- [ ] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Cada entrega aponta procedimento, esperado, observado e evidência; pendências ficam registradas com precisão.

- **Automatizável:** trabalho documental: `node scripts/check-docs-reader.cjs` e `git diff --check`; não alegar execução das suítes do produto.
- **Real/manual:** não aplicável; consolida execuções já realizadas.
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
