# Fase 04 — v0.13.0 — Instalação e atualização

[Índice geral](../README.md) · [Fase oficial](../../docs/MVP/fases/fase-04-v0.13.0.md)

**Objetivo:** Instalação e atualização, completando apenas lacunas do escopo vigente.

**Requisitos:** MVP-011, MVP-015. O estado dos requisitos permanece exclusivamente no [catálogo](../../docs/MVP/02-requisitos-e-estado.md).

**Dependência de entrada:** Contratos estabilizados e critérios aplicáveis das fases 01–03 aceitos; ambientes descartáveis disponíveis.

## Sequência e rastreabilidade

As tarefas abaixo começam abertas; consulte cada arquivo para checklist, dependências e evidências. O número determina a ordem padrão; dependências explícitas determinam os gates. Preparação não equivale a aceite da fase.

| Tarefa | Entregas oficiais | Dependências de tarefa |
| --- | --- | --- |
| [F04-T01 — Preparar e auditar artefatos por RID](task-01-artefatos-e-auditoria.md) | F04-E01 | F03-T06 |
| [F04-T02 — Validar atualização e recuperação Windows](task-02-windows.md) | F04-E02 | F04-T01 |
| [F04-T03 — Validar Linux portátil](task-03-linux-portatil.md) | F04-E02 | F04-T01, F04-T02 |
| [F04-T04 — Validar Linux gerenciado](task-04-linux-gerenciado.md) | F04-E03 | F04-T01, F04-T03 |
| [F04-T05 — Comprovar dados, migrações e instruções](task-05-dados-e-instrucoes.md) | F04-E04, F04-E05 | F04-T02, F04-T03, F04-T04 |

## Saída e evidências

Concluir somente quando cada critério obrigatório aplicável da [fase oficial](../../docs/MVP/fases/fase-04-v0.13.0.md) tiver evidência proporcional. Falha, ignorado ou ambiente ausente mantém o critério aberto. Reutilizar evidências históricas apenas no seu alcance; registrar nova execução com snapshot e ambiente próprios.

Seguir as [regras comuns](../README.md#regras-comuns-de-validação) e o [procedimento oficial](../../docs/MVP/06-validacao-e-evidencias.md). Atualizar entregas da fase a partir das evidências das tarefas; não manter cópia do estado dos requisitos neste índice.
