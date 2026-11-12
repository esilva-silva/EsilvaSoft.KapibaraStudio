# KS-US10-T04 — Validar a experiência em desktop nativo

**Estado de execução:** aberta; criação do arquivo não executa nem homologa a entrega. **Versão-alvo:** v0.11.0.

**User story:** [KS-US10 — Avaliar com modelo real e consolidar as evidências](README.md). **Requisitos relacionados:** MVP-008, MVP-011..013. **Entregas oficiais:** F02-E05.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Validar em desktop nativo os atalhos, o foco, a prévia e o painel do agente.

**Depende de:** [KS-US07-T03 — Gerar e inspecionar os PNGs das mudanças](../us-07-experiencia-no-painel-agente/task-03-renderizacao-e-pngs.md); [KS-US08-T01 — Validar o fluxo de prévia e aceite](../us-08-autocomplete-explicito/task-01-fluxo-ctrl-ponto-e-virgula.md)

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Design system](../../../docs/MVP/04-design-system.md)
- [Guia de uso](../../../docs/MVP/05-guia-de-uso.md)

## Execução

- [ ] Percorrer em Windows nativo `Ctrl+;`, Tab, Escape, envio ao agente, aprovação de tool, cancelamento e uso sem modelo.
- [ ] Repetir em Linux nativo quando houver ambiente; sem ele, registrar como pendente.
- [ ] Registrar o que Headless não cobre: leitor de tela, IME e diálogos nativos.
- [ ] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Jornada registrada por plataforma; nada é inferido de Windows para Linux nem de Headless para nativo.

- **Automatizável:** não aplicável.
- **Real/manual:** desktop nativo com ambiente identificado.
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
