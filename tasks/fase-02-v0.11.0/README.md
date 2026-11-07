# Fase 02 — v0.11.0 — IA local CPU

[Índice geral](../README.md) · [Fase oficial](../../docs/MVP/fases/fase-02-v0.11.0.md)

**Objetivo:** IA local CPU, completando apenas lacunas do escopo vigente.

**Requisitos:** MVP-008, MVP-011..013. O estado dos requisitos permanece exclusivamente no [catálogo](../../docs/MVP/02-requisitos-e-estado.md).

**Dependência de entrada:** Base e contexto seguros comprovados na fase 01; homologações não relacionadas podem continuar abertas.

## Sequência e rastreabilidade

As tarefas abaixo começam abertas; consulte cada arquivo para checklist, dependências e evidências. O número determina a ordem padrão; dependências explícitas determinam os gates. Preparação não equivale a aceite da fase.

| Tarefa | Entregas oficiais | Dependências de tarefa |
| --- | --- | --- |
| [F02-T01 — Fixar referência e orçamentos CPU](task-01-referencia-cpu.md) | F02-E01 | F01-T01 |
| [F02-T02 — Concluir autocomplete explícito](task-02-autocomplete-explicito.md) | F02-E02 | F02-T01 |
| [F02-T03 — Concluir ciclo do agente local](task-03-ciclo-agente.md) | F02-E03 | F02-T01, F02-T02 |
| [F02-T04 — Validar concorrência e ciclo de vida](task-04-ciclo-de-vida.md) | F02-E04 | F02-T02, F02-T03 |
| [F02-T05 — Avaliar modelo real e experiência nativa](task-05-avaliacao-real.md) | F02-E05 | F02-T01, F02-T02, F02-T03, F02-T04 |

## Saída e evidências

Concluir somente quando cada critério obrigatório aplicável da [fase oficial](../../docs/MVP/fases/fase-02-v0.11.0.md) tiver evidência proporcional. Falha, ignorado ou ambiente ausente mantém o critério aberto. Reutilizar evidências históricas apenas no seu alcance; registrar nova execução com snapshot e ambiente próprios.

Seguir as [regras comuns](../README.md#regras-comuns-de-validação) e o [procedimento oficial](../../docs/MVP/06-validacao-e-evidencias.md). Atualizar entregas da fase a partir das evidências das tarefas; não manter cópia do estado dos requisitos neste índice.
