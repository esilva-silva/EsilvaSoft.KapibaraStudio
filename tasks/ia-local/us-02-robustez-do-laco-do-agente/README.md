# KS-US02 — Tornar o laço do agente local previsível em sucesso e falha

**Estado de execução:** aberta; criação desta pasta não executa nem homologa nenhuma entrega. **Versão-alvo:** v0.11.0.

**Requisitos relacionados:** MVP-013. **Entregas oficiais:** F02-E03.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## História

**Como** pessoa que conversa com o agente local, **quero** que cada turno termine em resposta, erro claro ou cancelamento limpo, **para** não ficar com conversa travada, histórico corrompido ou resposta sem explicação.

## Contexto verificado

- `LocalAgentProviderTests` tem 18 testes, quase todos de disponibilidade, FIM, cancelamento e preempção; o ciclo tool → resultado → resposta e os orçamentos não são exercitados com runtime falso.
- `LocalContextExhausted` só surge quando o runtime lança `LocalModelContextException`; não há checagem prévia de prompt, orçamento e resposta contra a janela.
- `Seed` nunca é preenchido pelo provider.
- No cancelamento o turno encerra sem fechar o raciocínio como cancelado; `ReasoningOutcome` é sempre concluído.
- Tool fora do plano gera erro imediato, sem retry; o fallback para CPU pode ocorrer em passo posterior de um mesmo turno.

## Critérios de aceite

- [ ] O ciclo completo de vários passos e os limites de chamadas e de orçamento têm testes com runtime falso.
- [ ] Estouro de janela é detectado antes de gerar e reportado como `LocalContextExhausted`.
- [ ] Amostragem segue o pacote e é reproduzível com semente.
- [ ] Cancelamento encerra raciocínio e tool pendente com estado explícito, sem herdar token cancelado.
- [ ] Envelope inválido e tool fora do plano seguem o contrato, e o dispositivo não muda no meio do turno.

## Fora de escopo

- Sessão de geração com KV vivo (KS-US03).
- Decodificação restrita (KS-US04).

## Dependências

- [KS-US01 — Fixar a referência e fazer valer os contratos do pacote](../us-01-referencia-e-contratos-do-pacote/README.md)

## Tarefas

| Tarefa | Resultado | Depende de |
| --- | --- | --- |
| [KS-US02-T01 — Cobrir o ciclo de tools com runtime falso](task-01-testes-do-ciclo-com-runtime-falso.md) | Testes do ciclo tool → resultado → resposta | — |
| [KS-US02-T02 — Checar a janela de contexto antes de gerar](task-02-checagem-previa-de-janela.md) | `LocalContextExhausted` antecipado | KS-US02-T01 |
| [KS-US02-T03 — Preencher semente e amostragem do pacote](task-03-semente-e-amostragem.md) | Geração reproduzível com semente | KS-US02-T01 |
| [KS-US02-T04 — Encerrar raciocínio e tool pendente no cancelamento](task-04-cancelamento-do-raciocinio.md) | Cancelamento com estado explícito | KS-US02-T01 |
| [KS-US02-T05 — Alinhar ao contrato tool fora do plano e envelope inválido](task-05-tool-fora-do-plano-e-envelope.md) | Erros de tool conforme o contrato | KS-US02-T01 |
| [KS-US02-T06 — Manter o dispositivo durante o turno](task-06-dispositivo-estavel-no-turno.md) | Sem troca de dispositivo entre passos | KS-US02-T01 |

A história só fecha quando todos os critérios de aceite têm evidência proporcional. Evidência sintética e real permanecem separadas; preparação não equivale a aceite.
