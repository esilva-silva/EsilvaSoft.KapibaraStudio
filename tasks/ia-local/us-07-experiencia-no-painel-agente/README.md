# KS-US07 — Completar a experiência do agente local no painel

**Estado de execução:** aberta; criação desta pasta não executa nem homologa nenhuma entrega. **Versão-alvo:** v0.11.0.

**Requisitos relacionados:** MVP-011, MVP-013. **Entregas oficiais:** F02-E03, F02-E05.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## História

**Como** pessoa que usa o painel Agente IA, **quero** controlar o raciocínio e entender o que o agente local está fazendo, **para** esperar com informação e ajustar custo e detalhe ao meu equipamento.

## Contexto verificado

- `AgentReasoningItem` tem estados de streaming e concluído, aviso de orçamento e restauração.
- `AgentChatPanel` tem alternador de raciocínio, orçamento e retenção; `AgentPanelPreferences.ReasoningByProvider` guarda a preferência, com orçamento inicial de 512.
- Faltam orçamento e exibição nas Preferências, estimativa de espera e estados de carregamento explícitos.

## Critérios de aceite

- [ ] Orçamento e exibição do raciocínio são configuráveis dentro dos limites do pacote.
- [ ] O painel mostra estados de sem modelo, carregando, pensando, chamando tool, cancelado e erro.
- [ ] PNGs reais das mudanças foram inspecionados em claro e escuro.
- [ ] Design system e guia descrevem o comportamento.

## Fora de escopo

- Botão e fluxos de NPU.
- Mudanças no painel de providers externos.

## Dependências

- [KS-US02 — Tornar o laço do agente local previsível em sucesso e falha](../us-02-robustez-do-laco-do-agente/README.md)

## Tarefas

| Tarefa | Resultado | Depende de |
| --- | --- | --- |
| [KS-US07-T01 — Completar as preferências de raciocínio](task-01-preferencias-de-raciocinio.md) | Orçamento e exibição configuráveis | — |
| [KS-US07-T02 — Mostrar estados e espera do agente local](task-02-estados-e-espera.md) | Estados visíveis do turno | KS-US02-T04 |
| [KS-US07-T03 — Gerar e inspecionar os PNGs das mudanças](task-03-renderizacao-e-pngs.md) | PNGs reais inspecionados | KS-US07-T01, KS-US07-T02 |
| [KS-US07-T04 — Atualizar guia e design system](task-04-guia-e-design-system.md) | Documentos de uso atualizados | KS-US07-T03 |

A história só fecha quando todos os critérios de aceite têm evidência proporcional. Evidência sintética e real permanecem separadas; preparação não equivale a aceite.
