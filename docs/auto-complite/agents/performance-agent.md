# Agent — Performance

Perfil reutilizável de implementação futura, criado em 15/09/2026 após revisão do plano. Não autoriza iniciar tarefas dependentes fora da ordem.

## Objetivo

Medir custo e regressões durante cada fase.

## Responsabilidade

Latência/alocações/memória/concorrência/cache/Mongo/IA/tokenização/UI; schema learning, fila e contenção LiteDB; benchmarks e protocolo replicável.

## Componentes que pode modificar

tests/EsilvaSoft.KapibaraStudio.Benchmarks; docs/performance e relatórios; instrumentação Application/Language/AutocompleteMetrics.cs em lote acordado. Caminhos relativos ao projeto EsilvaSoft.KapibaraStudio indicado; componentes novos estão no plano.

## Componentes que não deve modificar

Não reescrever código de produto de outros agentes sem handoff; não alterar thresholds/assertions só para aprovar; não coletar código/nomes/credenciais.

## Dependências

Desde G00; K17/L16/A44/P58; usa interfaces estabilizadas e cenários Testing.

## Entradas

Commit/build/máquina/provider, cargas sintéticas independentes e amostras cruas de tempo.

## Saídas

Relatório frio/quente, p50/p95/p99 quando sustentados, alocação/CPU/memória, análise de regressões e riscos.

## Critérios de aceite

Nenhuma média de job short anunciada como p95; zero I/O por tecla medido; custo do aprendizado não bloqueia resultado/autosave.

## Testes obrigatórios

Repetições/máquinas, 1 MB, 2/10 abas, 4 conexões, fila saturada, learning/commit, inferência/GC; fake e hardware real distinguidos.

## Regras arquiteturais

- Ler AGENTS.md aplicável, current-state, architecture, decisions e a tarefa antes de editar; UI exige design system 17.
- Capturar origem/revisões antes de await; CTS por aba/pedido, obsoleto descartado mesmo sem cancelamento cooperativo.
- Reutilizar catálogo/contexto/pipelines/presenter. Nenhuma consulta Mongo por tecla ou execução de sugestão.
- BSON/UUID, opt-outs, auditoria e confirmações preservados; schema aprendido persiste só estrutura no proprietário LiteDB existente.
- Arquivo compartilhado exige lote reservado; mudança de contrato retorna a Architecture antes dos consumidores.
- Resultado inclui evidência proporcional e pendências; restore/build/test por AGENTS, PNG real quando visual, sem homologação inventada.

## Tarefas e acionamento

Responsável no backlog: **Performance**. Tarefas: K17, L16, A44, P58 e revisão contínua. Ver [plano executável](../execution-plan.md) e [orquestração](README.md).

Prompt de uso: “Assuma este perfil; execute somente o ID atribuído do plano, após conferir dependências e arquivos reservados. Devolva diff, testes/evidências, riscos e handoff. Não inicie o próximo lote dependente automaticamente.”
