# KS-US09 — Garantir concorrência, liberação e recuperação

**Estado de execução:** aberta; criação desta pasta não executa nem homologa nenhuma entrega. **Versão-alvo:** v0.11.0.

**Requisitos relacionados:** MVP-011, MVP-012, MVP-013. **Entregas oficiais:** F02-E04.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## História

**Como** pessoa que trabalha com várias abas e sessões, **quero** que a IA local não interfira entre abas, libere memória e recupere minhas conversas, **para** usar o Studio por longos períodos sem travar nem perder trabalho.

## Contexto verificado

- Invariantes: não compartilhar `CancellationTokenSource` entre abas; capturar origem antes de operações assíncronas; nunca sobrescrever sessão ilegível com vazia; proprietário único do LiteDB.
- A baseline registra cancelamento real de CPU como opt-in por `KAPILAB_REAL_CPU_MODEL`, confirmando saída do processo líder.
- Liberação por inatividade e fechamento, retenção e recuperação com modelo real estão pendentes.

## Critérios de aceite

- [ ] Cancelar uma operação não afeta outra aba; autocomplete e agente independentes preservam contexto.
- [ ] O modelo é liberado por inatividade, fechamento e troca, sem dispose concorrente.
- [ ] Retenção e opt-out são respeitados; nada de resultado ou credencial em snapshot.
- [ ] Sessão ilegível é preservada e a falha é visível.

## Fora de escopo

- Diagnóstico causal dos locks LiteDB (fase 03).

## Dependências

- [KS-US02 — Tornar o laço do agente local previsível em sucesso e falha](../us-02-robustez-do-laco-do-agente/README.md)
- [KS-US08 — Concluir o autocomplete explícito com o pacote v4](../us-08-autocomplete-explicito/README.md)

## Tarefas

| Tarefa | Resultado | Depende de |
| --- | --- | --- |
| [KS-US09-T01 — Validar cancelamento isolado e concorrência](task-01-cancelamento-e-concorrencia.md) | Isolamento entre abas e papéis | KS-US02-T04, KS-US08-T01 |
| [KS-US09-T02 — Validar a liberação do modelo](task-02-liberacao-do-modelo.md) | Liberação por inatividade, fechamento e troca | KS-US09-T01 |
| [KS-US09-T03 — Validar retenção e opt-out](task-03-retencao-e-opt-out.md) | Retenção conforme preferências | KS-US05-T03 |
| [KS-US09-T04 — Validar recuperação e falha de persistência](task-04-recuperacao-de-sessao.md) | Recuperação comprovada | KS-US09-T03 |

A história só fecha quando todos os critérios de aceite têm evidência proporcional. Evidência sintética e real permanecem separadas; preparação não equivale a aceite.
