# KS-US04 — Restringir a saída de tool call quando o runtime permitir

**Estado de execução:** aberta; criação desta pasta não executa nem homologa nenhuma entrega. **Versão-alvo:** v0.11.0.

**Requisitos relacionados:** MVP-013. **Entregas oficiais:** F02-E03.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## História

**Como** pessoa que usa o agente local, **quero** que as chamadas de tool saiam sempre em formato válido, **para** reduzir tentativas perdidas com envelope inválido.

## Contexto verificado

- Não há uso de guidance em `src/`; o KapiLab tem o comando `guidance smoke`.
- O pacote declara `constrainedDecoding` no bloco `agent`.
- História condicional: só segue se o smoke aprovar no runtime fixado; reprovado, registra a limitação e encerra.

## Critérios de aceite

- [ ] Resultado do smoke registrado para o runtime e o pacote de referência.
- [ ] Se aprovado: gramática derivada do registry, ativa apenas por opt-in do pacote, sem restringir o raciocínio nem o texto livre.
- [ ] Se reprovado: limitação registrada no contrato e retry de envelope inválido mantido como única proteção.

## Fora de escopo

- Trocar de runtime para obter guidance.

## Dependências

- [KS-US02 — Tornar o laço do agente local previsível em sucesso e falha](../us-02-robustez-do-laco-do-agente/README.md)

## Tarefas

| Tarefa | Resultado | Depende de |
| --- | --- | --- |
| [KS-US04-T01 — Executar o smoke de guidance e decidir](task-01-smoke-de-guidance.md) | Decisão de seguir ou encerrar | KS-US01-T01 |
| [KS-US04-T02 — Gerar a gramática e ativá-la por opt-in do pacote](task-02-gramatica-e-opt-in.md) | Guidance ativo quando declarado | KS-US04-T01, KS-US02-T05 |
| [KS-US04-T03 — Validar com modelo real ou registrar a limitação](task-03-validar-ou-registrar-limitacao.md) | Evidência real ou limitação no contrato | KS-US04-T01 |

A história só fecha quando todos os critérios de aceite têm evidência proporcional. Evidência sintética e real permanecem separadas; preparação não equivale a aceite.
