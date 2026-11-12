# KS-US10 — Avaliar com modelo real e consolidar as evidências

**Estado de execução:** aberta; criação desta pasta não executa nem homologa nenhuma entrega. **Versão-alvo:** v0.11.0.

**Requisitos relacionados:** MVP-008, MVP-011..013. **Entregas oficiais:** F02-E05.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## História

**Como** responsável pelo aceite da fase 02, **quero** resultados reais de qualidade, tempo e memória comparados aos limites fixados, **para** decidir o estado de MVP-012 e MVP-013 com evidência proporcional.

## Contexto verificado

- `LocalAgentProviderModelIntegrationTests` é Explicit e cobre apenas o caminho FIM com `SLOP_QWEN_MODEL`.
- `kapilab agent run` usa o provider real sobre KapiSim e emite `kapilab-agent-run-report-v1`; não aceita sementes múltiplas, alternância de raciocínio nem `agent bench`.
- A baseline separa testes falsos, reais e desktop nativo; TTFT, RAM, liberação e retenção reais continuam pendentes.

## Critérios de aceite

- [ ] Existem testes Explicit do agente com modelo real, fora do CI.
- [ ] O KapiLab mede o agente com sementes e modos de raciocínio.
- [ ] Qualidade, TTFT, tempo até a primeira resposta, RAM e liberação foram medidos no pacote e hardware fixados e comparados aos limites.
- [ ] Experiência nativa verificada e documentos oficiais atualizados com o alcance real.

## Fora de escopo

- Homologação de GPU e NPU.
- Aceite final do MVP.

## Dependências

- [KS-US03 — Reutilizar o estado de geração entre os passos do turno](../us-03-sessao-de-geracao-por-turno/README.md)
- [KS-US05 — Comprovar tools, recusas e privacidade com modelo real](../us-05-tools-permissoes-e-privacidade/README.md)
- [KS-US06 — Encontrar, baixar e escolher o modelo de cada papel](../us-06-modelos-descoberta-e-download/README.md)
- [KS-US08 — Concluir o autocomplete explícito com o pacote v4](../us-08-autocomplete-explicito/README.md)
- [KS-US09 — Garantir concorrência, liberação e recuperação](../us-09-concorrencia-e-ciclo-de-vida/README.md)

## Tarefas

| Tarefa | Resultado | Depende de |
| --- | --- | --- |
| [KS-US10-T01 — Criar os testes Explicit do agente](task-01-testes-explicit-do-agente.md) | Testes reais opt-in do agente | KS-US02-T01 |
| [KS-US10-T02 — Estender o `kapilab agent run`](task-02-extensoes-do-kapilab.md) | Sementes, raciocínio e bench no KapiLab | KS-US02-T03 |
| [KS-US10-T03 — Executar as medições reais](task-03-medicoes-reais.md) | Resultados comparados aos limites | KS-US10-T01, KS-US10-T02, KS-US01-T01, KS-US03-T04, KT-US09-T03 (modelos) |
| [KS-US10-T04 — Validar a experiência em desktop nativo](task-04-experiencia-nativa.md) | Jornada nativa registrada | KS-US07-T03, KS-US08-T01 |
| [KS-US10-T05 — Consolidar evidências e estado](task-05-consolidacao-documental.md) | Documentos oficiais atualizados | KS-US10-T03, KS-US10-T04 |

A história só fecha quando todos os critérios de aceite têm evidência proporcional. Evidência sintética e real permanecem separadas; preparação não equivale a aceite.
