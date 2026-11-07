# Fase 03 — v0.12.0 — GitHub Copilot

[Índice geral](../README.md) · [Fase oficial](../../docs/MVP/fases/fase-03-v0.12.0.md)

**Objetivo:** GitHub Copilot, completando apenas lacunas do escopo vigente.

**Requisitos:** MVP-011, MVP-014. O estado dos requisitos permanece exclusivamente no [catálogo](../../docs/MVP/02-requisitos-e-estado.md).

**Dependência de entrada:** IA local da fase 02 aceita nos critérios aplicáveis; reutilizar registry, sessões, permissões e adapters.

## Sequência e rastreabilidade

As tarefas abaixo começam abertas; consulte cada arquivo para checklist, dependências e evidências. O número determina a ordem padrão; dependências explícitas determinam os gates. Preparação não equivale a aceite da fase.

| Tarefa | Entregas oficiais | Dependências de tarefa |
| --- | --- | --- |
| [F03-T01 — Reconciliar baseline e diagnosticar locks LiteDB](task-01-baseline-e-litedb.md) | F03-E01 | F02-T05 |
| [F03-T02 — Concluir conta e runtime oficial](task-02-conta-e-runtime.md) | F03-E02 | F03-T01 |
| [F03-T03 — Validar conversas e streaming](task-03-conversas-e-streaming.md) | F03-E03 | F03-T02 |
| [F03-T04 — Validar sessões, persistência e recuperação](task-04-sessoes-e-recuperacao.md) | F03-E04 | F03-T03 |
| [F03-T05 — Completar tools, permissões, propostas e arquivos](task-05-tools-e-propostas.md) | F03-E04, F03-E05 | F03-T02, F03-T03, F03-T04 |
| [F03-T06 — Validar experiência e requisitos de distribuição](task-06-experiencia-e-distribuicao.md) | F03-E05 | F03-T02, F03-T03, F03-T04, F03-T05 |

## Saída e evidências

Concluir somente quando cada critério obrigatório aplicável da [fase oficial](../../docs/MVP/fases/fase-03-v0.12.0.md) tiver evidência proporcional. Falha, ignorado ou ambiente ausente mantém o critério aberto. Reutilizar evidências históricas apenas no seu alcance; registrar nova execução com snapshot e ambiente próprios.

Seguir as [regras comuns](../README.md#regras-comuns-de-validação) e o [procedimento oficial](../../docs/MVP/06-validacao-e-evidencias.md). Atualizar entregas da fase a partir das evidências das tarefas; não manter cópia do estado dos requisitos neste índice.
