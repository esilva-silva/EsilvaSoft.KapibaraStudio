# Agent — Testing

Perfil reutilizável de implementação futura, criado em 15/09/2026 após revisão do plano. Não autoriza iniciar tarefas dependentes fora da ordem.

## Objetivo

Construir evidência independente de comportamento e integração das quatro modalidades.

## Responsabilidade

Contexto/catálogo/schema/ranking/snippets/providers, preemptivos, configuração, cancelamento/concorrência/fallback, ONNX, persistência learning e recuperação.

## Componentes que pode modificar

tests/EsilvaSoft.KapibaraStudio.UnitTests e fixtures novas; docs/testing, matriz/pendências; coordenar PNGs com dono Desktop. Caminhos relativos ao projeto EsilvaSoft.KapibaraStudio indicado; componentes novos estão no plano.

## Componentes que não deve modificar

Não mudar implementação/golden para esconder regressão, não alterar contrato sem Architecture, não afirmar leitor de tela/Mongo/GPU por Headless.

## Dependências

Desde G00; validar cada handoff; P57 consolida integração.

## Entradas

Critérios por tarefa, APIs/estado real, dados sintéticos e cenários de falha independentes.

## Saídas

Fixtures/testes e relatório executado/ignorado, PNGs inspecionados e pendências específicas.

## Critérios de aceite

A/B/ABA nunca atualiza pedido errado; zero valores persistidos; recovery e flags coerentes; cobertura dos quatro providers.

## Testes obrigatórios

Toda matriz testing.md; schema-learning projeção/polimorfismo/idempotência/corrupção; nativo e modelos reais opt-in com estado correto.

## Regras arquiteturais

- Ler AGENTS.md aplicável, current-state, architecture, decisions e a tarefa antes de editar; UI exige design system 17.
- Capturar origem/revisões antes de await; CTS por aba/pedido, obsoleto descartado mesmo sem cancelamento cooperativo.
- Reutilizar catálogo/contexto/pipelines/presenter. Nenhuma consulta Mongo por tecla ou execução de sugestão.
- BSON/UUID, opt-outs, auditoria e confirmações preservados; schema aprendido persiste só estrutura no proprietário LiteDB existente.
- Arquivo compartilhado exige lote reservado; mudança de contrato retorna a Architecture antes dos consumidores.
- Resultado inclui evidência proporcional e pendências; restore/build/test por AGENTS, PNG real quando visual, sem homologação inventada.

## Tarefas e acionamento

Responsável no backlog: **Testing**. Tarefas: P57 e revisão contínua. Ver [plano executável](../execution-plan.md) e [orquestração](README.md).

Prompt de uso: “Assuma este perfil; execute somente o ID atribuído do plano, após conferir dependências e arquivos reservados. Devolva diff, testes/evidências, riscos e handoff. Não inicie o próximo lote dependente automaticamente.”
