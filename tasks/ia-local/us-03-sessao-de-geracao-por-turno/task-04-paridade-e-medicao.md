# KS-US03-T04 — Provar paridade e medir o ganho

**Estado de execução:** aberta; criação do arquivo não executa nem homologa a entrega. **Versão-alvo:** v0.11.0.

**User story:** [KS-US03 — Reutilizar o estado de geração entre os passos do turno](README.md). **Requisitos relacionados:** MVP-013. **Entregas oficiais:** F02-E03, F02-E05.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Comparar os dois caminhos e medir o tempo por passo em CPU.

**Depende de:** [KS-US03-T03 — Usar a sessão no laço do agente](task-03-provider-usa-sessao.md); [KS-US01-T01 — Registrar pacote de referência e limites](../us-01-referencia-e-contratos-do-pacote/task-01-registrar-referencia-e-limites.md)

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Contrato de IA local](../../../docs/MVP/contratos/ia-local.md)
- [KapiLab](../../../tools/KapiLab/README.md)

## Execução

- [ ] Comparar, em modo greedy, os tokens gerados pelo caminho com sessão e pelo caminho com nova renderização nas tarefas de referência.
- [ ] Medir tempo até o primeiro token de cada passo e tempo total do turno, separando aquecimento.
- [ ] Registrar pico de RAM com a sessão ativa.
- [ ] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Paridade dentro do limite fixado em KS-US01 e ganho registrado; diferença de saída sem causa conhecida mantém a tarefa aberta.

- **Automatizável:** paridade com runtime falso; medições reais não entram no CI.
- **Real/manual:** medição manual com modelo real e hardware identificados.
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
