# Fase 01 — v0.10.0 — Conclusão da base

[Índice geral](../README.md) · [Fase oficial](../../docs/MVP/fases/fase-01-v0.10.0.md)

**Objetivo:** Conclusão da base, completando apenas lacunas do escopo vigente.

**Requisitos:** MVP-001..011. O estado dos requisitos permanece exclusivamente no [catálogo](../../docs/MVP/02-requisitos-e-estado.md).

**Dependência de entrada:** Base funcional existente e cortes registrados pela Task-R 1; não repetir a limpeza.

## Sequência e rastreabilidade

As tarefas abaixo começam abertas; consulte cada arquivo para checklist, dependências e evidências. O número determina a ordem padrão; dependências explícitas determinam os gates. Preparação não equivale a aceite da fase.

| Tarefa | Entregas oficiais | Dependências de tarefa |
| --- | --- | --- |
| [F01-T01 — Reconciliar cortes e evidências](task-01-reconciliar-cortes.md) | F01-E01, F01-E02, F01-E03 | Baseline e Task-R 1 |
| [F01-T02 — Validar jornadas afetadas com MongoDB real](task-02-jornadas-mongodb.md) | F01-E04 | F01-T01 |
| [F01-T03 — Validar recuperação legada e jornadas nativas](task-03-recuperacao-e-desktop.md) | F01-E04 | F01-T01, F01-T02 |
| [F01-T04 — Corrigir regressões e fechar a base](task-04-corrigir-e-fechar-base.md) | F01-E04 | F01-T01, F01-T02, F01-T03 |

## Saída e evidências

Concluir somente quando cada critério obrigatório aplicável da [fase oficial](../../docs/MVP/fases/fase-01-v0.10.0.md) tiver evidência proporcional. Falha, ignorado ou ambiente ausente mantém o critério aberto. Reutilizar evidências históricas apenas no seu alcance; registrar nova execução com snapshot e ambiente próprios.

Seguir as [regras comuns](../README.md#regras-comuns-de-validação) e o [procedimento oficial](../../docs/MVP/06-validacao-e-evidencias.md). Atualizar entregas da fase a partir das evidências das tarefas; não manter cópia do estado dos requisitos neste índice.
