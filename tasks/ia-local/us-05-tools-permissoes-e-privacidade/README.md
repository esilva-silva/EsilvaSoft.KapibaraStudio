# KS-US05 — Comprovar tools, recusas e privacidade com modelo real

**Estado de execução:** aberta; criação desta pasta não executa nem homologa nenhuma entrega. **Versão-alvo:** v0.11.0.

**Requisitos relacionados:** MVP-013. **Entregas oficiais:** F02-E03.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## História

**Como** pessoa que autoriza o agente local a ver meu contexto, **quero** que ele use apenas o que liberei e nunca consulte ou altere meus dados por conta própria, **para** usar o agente sem risco para o banco e sem vazamento do raciocínio.

## Contexto verificado

- O destino local já recebe grants de turno (`NativeChatTurnPolicyProvider`) e tools in-process do `AgentToolRegistry`.
- A baseline de 11/10/2026 cobre registry, política e auditoria reais com grants sintéticos; não mede inferência nem sucesso de tarefa.
- Não há teste de canário de privacidade do raciocínio; existe retenção opcional em LiteDB com `AgentReasoningRetentionTests`.
- Atlas Search teve apenas falhas reais de serviço, sem prova de saída de sucesso.

## Critérios de aceite

- [ ] Ciclo permitido completa com modelo real usando metadados, índices e saídas humanas já capturadas.
- [ ] Operações proibidas são recusadas mesmo com aprovação; revogação impede publicação tardia.
- [ ] Texto de raciocínio não aparece em log, auditoria, outro provider nem em persistência sem opt-in.
- [ ] Propostas e criação de arquivo ocorrem apenas pelos fluxos autorizados, com conflito e Undo.

## Fora de escopo

- Ampliar o catálogo de tools.
- Homologar Atlas sem ambiente Atlas.

## Dependências

- [KS-US02 — Tornar o laço do agente local previsível em sucesso e falha](../us-02-robustez-do-laco-do-agente/README.md)

## Tarefas

| Tarefa | Resultado | Depende de |
| --- | --- | --- |
| [KS-US05-T01 — Exercitar o ciclo real das tools permitidas](task-01-ciclo-real-de-tools.md) | Ciclo real de tools com modelo | KS-US02-T01, KS-US01-T01 |
| [KS-US05-T02 — Validar recusas, limites e revogação](task-02-recusas-e-operacoes-proibidas.md) | Recusas comprovadas | KS-US05-T01 |
| [KS-US05-T03 — Adicionar o canário de privacidade do raciocínio](task-03-canario-de-privacidade-do-raciocinio.md) | Raciocínio fora de log, auditoria e outros providers | KS-US02-T01 |
| [KS-US05-T04 — Demonstrar propostas e criação de arquivo](task-04-propostas-e-arquivos.md) | Fluxos autorizados com conflito e Undo | KS-US05-T01 |

A história só fecha quando todos os critérios de aceite têm evidência proporcional. Evidência sintética e real permanecem separadas; preparação não equivale a aceite.
