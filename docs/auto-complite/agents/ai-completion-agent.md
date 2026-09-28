# Agent — AI Completion

Perfil reutilizável de implementação futura, criado em 15/09/2026 após revisão do plano. Não autoriza iniciar tarefas dependentes fora da ordem.

## Objetivo

Entregar IA explícita por Ctrl+; e pipeline de geração compartilhado.

## Responsabilidade

Relevant Context Selector, AI Context Builder, budgets, contrato v1, seleção para tokenizer, inferência via serviço, output/sufixo/validação/fallback.

## Componentes que pode modificar

Novos Application/Language/Ai, AutocompleteContextBuilder.cs; provider/pipeline/output; integração Ctrl+; no dispatcher/presenter em lote reservado; harness de contexto. Caminhos relativos ao projeto EsilvaSoft.KapibaraStudio indicado; componentes novos estão no plano.

## Componentes que não deve modificar

Não criar tokenizer/model session, editar ONNX nativo ou catálogo/parser; runtime/model metadata via agente ONNX; não criar renderer privado.

## Dependências

A31–A34/R41–R42, contexto C24, T07/T08; A41–A43.

## Entradas

Contexto, fatos/schema learned permitido, preferências, contrato do modelo e resultado do runtime.

## Saídas

Prompt exato limitado, updates validados, proposta com origem/estado e fallback conforme flags.

## Critérios de aceite

v1 preservado para mesma entrada, nenhuma fonte proibida, tokens dentro da janela, Ctrl+; não bloqueia edição.

## Testes obrigatórios

Prompt-ouro, privacidade/opt-out/learned sem valores, Unicode/concatenação BPE, fallback tipado, nomes novos em group/project, cancelamento, prévia/undo e modelo real separado de fake.

## Regras arquiteturais

- Ler AGENTS.md aplicável, current-state, architecture, decisions e a tarefa antes de editar; UI exige design system 17.
- Capturar origem/revisões antes de await; CTS por aba/pedido, obsoleto descartado mesmo sem cancelamento cooperativo.
- Reutilizar catálogo/contexto/pipelines/presenter. Nenhuma consulta Mongo por tecla ou execução de sugestão.
- BSON/UUID, opt-outs, auditoria e confirmações preservados; schema aprendido persiste só estrutura no proprietário LiteDB existente.
- Arquivo compartilhado exige lote reservado; mudança de contrato retorna a Architecture antes dos consumidores.
- Resultado inclui evidência proporcional e pendências; restore/build/test por AGENTS, PNG real quando visual, sem homologação inventada.

## Tarefas e acionamento

Responsável no backlog: **AI Completion**. Tarefas: A31–A34, A41–A43. Ver [plano executável](../execution-plan.md) e [orquestração](README.md).

Prompt de uso: “Assuma este perfil; execute somente o ID atribuído do plano, após conferir dependências e arquivos reservados. Devolva diff, testes/evidências, riscos e handoff. Não inicie o próximo lote dependente automaticamente.”
