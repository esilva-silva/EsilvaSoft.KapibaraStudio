# KS-US04-T01 — Executar o smoke de guidance e decidir

**Estado de execução:** aberta; criação do arquivo não executa nem homologa a entrega. **Versão-alvo:** v0.11.0.

**User story:** [KS-US04 — Restringir a saída de tool call quando o runtime permitir](README.md). **Requisitos relacionados:** MVP-013. **Entregas oficiais:** F02-E03.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Verificar se o runtime fixado aceita gramática com o pacote de referência e decidir o destino da história.

**Depende de:** [KS-US01-T01 — Registrar pacote de referência e limites](../us-01-referencia-e-contratos-do-pacote/task-01-registrar-referencia-e-limites.md)

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [KapiLab](../../../tools/KapiLab/README.md)
- [Contrato de IA local](../../../docs/MVP/contratos/ia-local.md)

## Execução

- [ ] Rodar `kapilab guidance smoke` em CPU com o pacote de referência.
- [ ] Medir o custo de tempo por token com a gramática ativa.
- [ ] Registrar a decisão: implementar, adiar ou encerrar com limitação.
- [ ] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Decisão registrada com saída do smoke; sem modelo, a tarefa fica bloqueada.

- **Automatizável:** os testes existentes do smoke no KapiLab.
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
