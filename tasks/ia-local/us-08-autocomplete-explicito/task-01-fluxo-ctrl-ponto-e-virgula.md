# KS-US08-T01 — Validar o fluxo de prévia e aceite

**Estado de execução:** aberta; criação do arquivo não executa nem homologa a entrega. **Versão-alvo:** v0.11.0.

**User story:** [KS-US08 — Concluir o autocomplete explícito com o pacote v4](README.md). **Requisitos relacionados:** MVP-008, MVP-012. **Entregas oficiais:** F02-E02.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Demonstrar o fluxo completo do autocomplete explícito com o pacote de referência.

**Depende de:** [KS-US01-T01 — Registrar pacote de referência e limites](../us-01-referencia-e-contratos-do-pacote/task-01-registrar-referencia-e-limites.md)

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Contrato de IA local](../../../docs/MVP/contratos/ia-local.md)
- [Design system](../../../docs/MVP/04-design-system.md)

## Execução

- [ ] Validar `Ctrl+;` → prévia → Tab com Undo, e Escape sem inserir ponto e vírgula.
- [ ] Validar a rejeição de resposta obsoleta ao mudar texto, aba, conexão, banco ou coleção, com a origem capturada antes do await.
- [ ] Validar cancelamento durante a geração e nova solicitação em seguida.
- [ ] Corrigir defeitos comprovados e cobrir com teste.
- [ ] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Fluxo comprovado com runtime falso e com modelo real; resultado só atualiza a aba que o originou.

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
