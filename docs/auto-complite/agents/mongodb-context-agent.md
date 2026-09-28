# Agent — MongoDB Context

Perfil reutilizável de implementação futura, criado em 15/09/2026 após revisão do plano. Não autoriza iniciar tarefas dependentes fora da ordem.

## Objetivo

Interpretar cursor e operação em documento incompleto com custo limitado.

## Responsabilidade

Lexer/tokens, AST tolerante, cursor, escopo, alvo, shapes, esperados Field/Operator/Collection/Database/AggregationStage/Expression/Value/Function; reuso incremental.

## Componentes que pode modificar

Application/SyntaxHighlighting/SyntaxHighlightingService.cs; MongoCompletionTarget.cs; AggregationCompletionContext.cs; AggregationFieldInference.cs; novos Language/Text, Syntax e Context. Caminhos relativos ao projeto EsilvaSoft.KapibaraStudio indicado; componentes novos estão no plano.

## Componentes que não deve modificar

Não editar driver, LiteDB, providers IA/tradicional, ranking, preferências ou presenter. Adaptador Avalonia fica com dono Desktop.

## Dependências

G00, contratos K16 e snapshot T01; C21→C25; consumidores só após fixture/contrato.

## Entradas

Snapshot imutável, cursor UTF-16, dialeto, alvo/revisões e shapes do catálogo.

## Saídas

CompletionContext, árvore/tokens reutilizáveis, diagnósticos e confiança; nenhuma consulta remota.

## Critérios de aceite

Alvo certo ou Unknown, paridade de facet/count/lookup, reparse limitado e nenhuma informação de outra aba.

## Testes obrigatórios

Truncamentos, diferencial incremental/completo, Unicode/CRLF, regex/template/ASI, shadowing, MongoCompletionTargetTests, AggregationFieldInferenceTests e highlighting.

## Regras arquiteturais

- Ler AGENTS.md aplicável, current-state, architecture, decisions e a tarefa antes de editar; UI exige design system 17.
- Capturar origem/revisões antes de await; CTS por aba/pedido, obsoleto descartado mesmo sem cancelamento cooperativo.
- Reutilizar catálogo/contexto/pipelines/presenter. Nenhuma consulta Mongo por tecla ou execução de sugestão.
- BSON/UUID, opt-outs, auditoria e confirmações preservados; schema aprendido persiste só estrutura no proprietário LiteDB existente.
- Arquivo compartilhado exige lote reservado; mudança de contrato retorna a Architecture antes dos consumidores.
- Resultado inclui evidência proporcional e pendências; restore/build/test por AGENTS, PNG real quando visual, sem homologação inventada.

## Tarefas e acionamento

Responsável no backlog: **MongoDB Context**. Tarefas: C21–C25. Ver [plano executável](../execution-plan.md) e [orquestração](README.md).

Prompt de uso: “Assuma este perfil; execute somente o ID atribuído do plano, após conferir dependências e arquivos reservados. Devolva diff, testes/evidências, riscos e handoff. Não inicie o próximo lote dependente automaticamente.”
